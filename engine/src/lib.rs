//! Modern Voices for Windows: a Windows speech (SAPI 5) engine that speaks with offline Piper voices,
//! so any program that uses Windows' speech API can use them like any installed voice.
//!
//! Based on the Spoken Players voice engine (https://github.com/f-rolf/spoken-players-voicepack),
//! GPL-3.0-or-later. Changed from it: its own class ID (so the two never share a registration), and
//! the events programs follow speech by: bookmarks, sentence and word boundaries. Speech is made a
//! sentence at a time (sentences split here, so each one's text and audio are known), and each
//! sentence's audio is written word by word, every word's event just before its sound. Piper does not
//! say when each word starts, so that is estimated from the words' lengths across the sentence's
//! voiced audio; close enough for a program to highlight the word being said.
//!
//! Each voice is a SAPI voice token in the registry whose CLSID is this engine's and which names
//! its model folder (`ModelDir`: model.onnx, tokens.txt, voice.json), espeak-ng's data (`DataDir`)
//! and, for a model with several speakers, the speaker (`SpeakerId`). Models are loaded once per
//! process and shared by every utterance that uses them.
//!
//! Model loads and anything that went wrong are always logged. With the log's `verbose` switch on,
//! every call the program makes into the engine is too, with how long it took, on which thread, and
//! from where (the modules on the calling thread's stack, as module+offset).

mod log;
mod text;

use text::*;

use std::collections::HashMap;
use std::ffi::c_void;
use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicU32, AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::{Instant, SystemTime};

use sherpa_onnx::{GenerationConfig, OfflineTts, OfflineTtsConfig, OfflineTtsModelConfig, OfflineTtsVitsModelConfig};
use windows::Win32::Foundation::{CLASS_E_CLASSNOTAVAILABLE, CLASS_E_NOAGGREGATION, E_FAIL, E_POINTER, HMODULE, LPARAM,
    S_FALSE, WPARAM};
use windows::Win32::Media::Audio::{WAVE_FORMAT_PCM, WAVEFORMATEX};
use windows::Win32::Media::Speech::*;
use windows::Win32::System::Com::{CoTaskMemAlloc, CoTaskMemFree, IClassFactory, IClassFactory_Impl};
use windows::Win32::System::Diagnostics::Debug::RtlCaptureStackBackTrace;
use windows::Win32::System::LibraryLoader::{GetModuleFileNameW, GetModuleHandleExW};
use windows::Win32::System::ProcessStatus::{GetProcessMemoryInfo, PROCESS_MEMORY_COUNTERS};
use windows::Win32::System::Registry::{HKEY_LOCAL_MACHINE, RRF_RT_REG_DWORD, RegGetValueW};
use windows::Win32::System::SystemInformation::{GlobalMemoryStatusEx, MEMORYSTATUSEX};
use windows::Win32::Foundation::{CloseHandle, HANDLE};
use windows::Win32::Security::{GetSidSubAuthority, GetSidSubAuthorityCount, GetTokenInformation, TOKEN_MANDATORY_LABEL,
    TOKEN_QUERY, TokenIntegrityLevel};
use windows::Win32::System::Threading::{GetCurrentProcess, OpenProcessToken};
use windows::core::{BOOL, GUID, HRESULT, IUnknown, Interface, PCWSTR, Ref, Result, implement, w};

/// This DLL, set when it is loaded: its frames are left out of the callers logged.
static MODULE: AtomicUsize = AtomicUsize::new(0);
static FACTORIES: AtomicU32 = AtomicU32::new(0);
static INSTANCES: AtomicU32 = AtomicU32::new(0);
static TOKENS: AtomicU32 = AtomicU32::new(0);

/// Who called: the modules on this thread's stack outside the engine, nearest first, as
/// module+offset (Wow.exe+0x1a2b3c < sapi.dll+0x4d5e). Only addresses, read from the engine's own
/// call; nothing of the program's is changed.
fn caller() -> String {
    let mut frames = [std::ptr::null_mut(); 32];
    let count = unsafe { RtlCaptureStackBackTrace(1, &mut frames, None) } as usize;
    let ours = MODULE.load(Ordering::Relaxed);
    let mut chain: Vec<String> = Vec::new();
    for &address in &frames[..count] {
        let mut module = HMODULE::default();
        // GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT
        if unsafe { GetModuleHandleExW(4 | 2, PCWSTR(address as *const u16), &mut module) }.is_err() {
            chain.push(format!("{address:p}"));
            continue;
        }
        if module.0 as usize == ours {
            continue;
        }
        let mut name = [0u16; 260];
        let length = unsafe { GetModuleFileNameW(Some(module), &mut name) } as usize;
        let path = String::from_utf16_lossy(&name[..length]);
        let file = path.rsplit('\\').next().unwrap_or(&path).to_string();
        chain.push(format!("{file}+{:#x}", address as usize - module.0 as usize));
        if chain.len() >= 10 {
            break;
        }
    }
    chain.join(" < ")
}

/// The program's memory: what it has in RAM and what it has committed, in MB.
fn memory() -> String {
    let mut counters = PROCESS_MEMORY_COUNTERS { cb: size_of::<PROCESS_MEMORY_COUNTERS>() as u32, ..Default::default() };
    match unsafe { GetProcessMemoryInfo(GetCurrentProcess(), &mut counters, counters.cb) } {
        Ok(()) => format!("{} MB in RAM, {} MB committed", counters.WorkingSetSize >> 20, counters.PagefileUsage >> 20),
        Err(_) => "memory unknown".into(),
    }
}

/// The program's integrity level (low means sandboxed: it may write to AppData\LocalLow only).
fn integrity() -> String {
    unsafe {
        let mut token = HANDLE::default();
        if OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &mut token).is_err() {
            return "unknown".into();
        }
        let mut buffer = [0u8; 256];
        let mut length = 0u32;
        let read = GetTokenInformation(token, TokenIntegrityLevel, Some(buffer.as_mut_ptr() as *mut c_void),
            buffer.len() as u32, &mut length);
        let _ = CloseHandle(token);
        if read.is_err() {
            return "unknown".into();
        }
        let label = &*(buffer.as_ptr() as *const TOKEN_MANDATORY_LABEL);
        let count = *GetSidSubAuthorityCount(label.Label.Sid) as u32;
        let level = *GetSidSubAuthority(label.Label.Sid, count.saturating_sub(1));
        let name = match level {
            0..=0x0fff => "untrusted",
            0x1000..=0x1fff => "low",
            0x2000..=0x2fff => "medium",
            0x3000..=0x3fff => "high",
            _ => "system",
        };
        format!("{name} ({level:#x})")
    }
}

/// Logs, once, which program loaded the engine: the first sign it is in use there at all.
fn announce() {
    static DONE: OnceLock<()> = OnceLock::new();
    DONE.get_or_init(|| {
        let exe = std::env::current_exe().map(|p| p.display().to_string()).unwrap_or_default();
        log::write(&format!("engine {} ({}-bit) loaded into {exe} ({:?} before its first use; {}; integrity {}; log in {:?})",
            env!("CARGO_PKG_VERSION"), usize::BITS, log::start().elapsed(), memory(), integrity(), log::folder()));
    });
}

/// This engine's class: the CLSID the voice tokens name. Not the Spoken Players voicepack's
/// ({055BF9E8-68B3-4CB2-8090-50895F4BCC68}): the two can be installed side by side, and installing or
/// removing one never touches the other's registration.
pub const CLSID_MODERN_VOICES: GUID = GUID::from_u128(0xf9857585_ff01_467b_84c0_d6ca4e0572d2);
/// SAPI's format ID for "a WAVEFORMATEX follows" (SPDFID_WaveFormatEx).
const SPDFID_WAVEFORMATEX: GUID = GUID::from_u128(0xc31adbae_527f_4ff5_a230_f62bb61ff70c);
const DEFAULT_RATE: u32 = 22050;

/// A loaded model, shared by every engine instance (every SpVoice) that speaks with it. One
/// sentence at a time per model: each takes a fraction of its length to make, so programs speaking at
/// once only wait a moment for each other. Up to max_models of them stay loaded.
struct Model {
    tts: OfflineTts,
    sample_rate: u32,
    busy: Mutex<()>,
    /// The model's folder, for its voice.json.
    dir: String,
    /// The voice's settings, and when its voice.json was last changed when they were read.
    settings: Mutex<(Option<SystemTime>, Settings)>,
}

/// What a voice's voice.json sets for how it sounds, read while speaking (so a change takes effect
/// from the next thing said, in programs that are already using the voice): its loudness (see
/// text::apply_loudness) and its speed (`length_scale`, Piper's: higher is slower).
#[derive(Clone, Copy, Debug, PartialEq)]
struct Settings {
    peak: f32,
    boost: f32,
    length_scale: f32,
    /// The voice's own pauses (`"own_pauses": 1` in its voice.json), else the global ones.
    own_pauses: Option<Pauses>,
}

impl Settings {
    /// From voice.json's text; a voice.json without them leaves the voice as Piper made it.
    fn from(info: &str) -> Settings {
        Settings {
            peak: number(info, "peak", 0.0).min(1.0),
            boost: number(info, "boost", 1.0).clamp(1.0, 5.0),
            length_scale: number(info, "length_scale", 1.0).clamp(0.3, 4.0),
            own_pauses: (number(info, "own_pauses", 0.0) >= 1.0).then(|| Pauses::read(info)),
        }
    }
}

/// What the program's settings file sets for every voice: the pauses (for a voice without its own)
/// and the speed every voice speaks at before the program's own rate.
#[derive(Clone, Copy, Debug, PartialEq)]
struct Global {
    pauses: Pauses,
    speed: f32,
}

impl Global {
    fn from(info: &str) -> Global {
        Global { pauses: Pauses::read(info), speed: global_speed(info) }
    }
}

/// The settings file the voice token names (`SettingsFile`), read again whenever it changes. The
/// defaults without one.
fn global_settings(path: Option<&str>) -> Global {
    let fallback = Global::from("");
    let Some(path) = path else { return fallback };
    static CACHE: OnceLock<Mutex<HashMap<String, (Option<SystemTime>, Global)>>> = OnceLock::new();
    let changed = std::fs::metadata(path).and_then(|m| m.modified()).ok();
    let Ok(mut cache) = CACHE.get_or_init(|| Mutex::new(HashMap::new())).lock() else { return fallback };
    if let Some((when, global)) = cache.get(path) {
        if *when == changed {
            return *global;
        }
    }
    let global = Global::from(&std::fs::read_to_string(path).unwrap_or_default());
    if cache.contains_key(path) || log::enabled() {
        log::write(&format!("settings for every voice: {global:?} ({path})"));
    }
    cache.insert(path.to_string(), (changed, global));
    global
}

impl Model {
    /// The voice's settings now: read again when its voice.json has changed since last time.
    fn settings(&self) -> Settings {
        let path = format!("{}\\voice.json", self.dir);
        let changed = std::fs::metadata(&path).and_then(|m| m.modified()).ok();
        let Ok(mut cached) = self.settings.lock() else { return Settings::from("") };
        if cached.0 != changed {
            let info = std::fs::read_to_string(&path).unwrap_or_default();
            let settings = Settings::from(&info);
            if cached.0.is_some() || log::enabled() {
                log::write(&format!("{}: settings {settings:?}", self.dir));
            }
            *cached = (changed, settings);
        }
        cached.1
    }
}

/// One model's place in the cache: the model once loaded (None before, or after it was unloaded to
/// make room), and when it was last asked for. Each model loads under its own lock, so a model being
/// loaded holds up only the lines waiting for that model, never a line of one already loaded.
struct Slot {
    model: Mutex<Option<Arc<Model>>>,
    used: AtomicU64,
}

fn slots() -> &'static Mutex<HashMap<String, Arc<Slot>>> {
    static SLOTS: OnceLock<Mutex<HashMap<String, Arc<Slot>>>> = OnceLock::new();
    SLOTS.get_or_init(|| Mutex::new(HashMap::new()))
}

/// Ticks for "last asked for": a count, not a time.
static CLOCK: AtomicU64 = AtomicU64::new(0);

/// What a loaded model takes in RAM, about: a medium Piper model's 63 MB of weights and onnxruntime's
/// graph and buffers around them.
const MODEL_MB: u64 = 110;

/// How many models stay loaded at once: as many as fit in a tenth of the PC's RAM (8 GB: 7, 16 GB:
/// 14, 32 GB: 29), at least 2, and at most half that in a 32-bit program. Past that, the one asked
/// for longest ago is unloaded. `MaxModels` (DWORD) under HKLM\SOFTWARE\Modern Voices for Windows sets
/// it instead, as does the environment variable MODERN_VOICES_MAX_MODELS (for trying it out).
fn max_models() -> usize {
    static MAX: OnceLock<usize> = OnceLock::new();
    *MAX.get_or_init(|| {
        let mut chosen = 0u32;
        let mut size = size_of::<u32>() as u32;
        let set = unsafe {
            RegGetValueW(HKEY_LOCAL_MACHINE, w!("SOFTWARE\\Modern Voices for Windows"), w!("MaxModels"), RRF_RT_REG_DWORD,
                None, Some(&mut chosen as *mut u32 as *mut c_void), Some(&mut size))
        };
        let (count, why) = if set.is_ok() && chosen > 0 {
            (chosen as usize, "MaxModels".to_string())
        } else if let Some(count) = std::env::var("MODERN_VOICES_MAX_MODELS").ok().and_then(|v| v.parse::<usize>().ok()) {
            (count.max(1), "MODERN_VOICES_MAX_MODELS".to_string())
        } else {
            let mut status = MEMORYSTATUSEX { dwLength: size_of::<MEMORYSTATUSEX>() as u32, ..Default::default() };
            let total = if unsafe { GlobalMemoryStatusEx(&mut status) }.is_ok() { status.ullTotalPhys } else { 8 << 30 };
            // A 32-bit program has 2 to 4 GB of address space, whatever the PC has.
            let budget = if usize::BITS == 32 { (total >> 20).min(1600) } else { (total >> 20) / 10 };
            ((budget / MODEL_MB).max(2) as usize, format!("{} GB of RAM, {}-bit", (total + (1 << 29)) >> 30, usize::BITS))
        };
        log::write(&format!("keeps up to {count} model(s) loaded ({why}, ~{} MB)", count as u64 * MODEL_MB));
        count
    })
}

/// Over the limit (max_models), the models asked for longest ago are unloaded, but never `keep` (the
/// one just loaded), one being loaded or one speaking. A model an engine instance still holds stays
/// in memory until that instance lets go of it; it is only out of the cache.
fn make_room(keep: &str) {
    let Ok(map) = slots().lock() else { return };
    let loaded = |slot: &Slot| slot.model.try_lock().map(|model| model.is_some()).unwrap_or(true);
    let mut count = map.values().filter(|slot| loaded(slot)).count();
    let limit = max_models();
    while count > limit {
        let oldest = map.iter()
            .filter(|(key, slot)| key.as_str() != keep && slot.model.try_lock().is_ok_and(|model| {
                model.as_ref().is_some_and(|m| m.busy.try_lock().is_ok())
            }))
            .min_by_key(|(_, slot)| slot.used.load(Ordering::Relaxed));
        let Some((key, slot)) = oldest else { break };
        // Taken meanwhile (loading again): left as it is.
        let Ok(mut model) = slot.model.try_lock() else { break };
        *model = None;
        drop(model);
        count -= 1;
        log::write(&format!("unloaded {} to make room ({count} model(s) now, up to {limit}; {})",
            key.split('|').next().unwrap_or(key), memory()));
    }
}

/// Piper's sampling for a model whose voice.json does not set it: the noise of the sound itself
/// (`noise_scale`) and of each sound's length (`noise_w`), below Piper's own 0.667 and 0.8.
const NOISE_SCALE: f32 = 0.6;
const NOISE_W: f32 = 0.65;
fn load_model(model_dir: &str, data_dir: &str) -> Option<Arc<Model>> {
    let key = format!("{model_dir}|{data_dir}");
    let slot = slots().lock().ok()?.entry(key.clone())
        .or_insert_with(|| Arc::new(Slot { model: Mutex::new(None), used: AtomicU64::new(0) })).clone();
    slot.used.store(CLOCK.fetch_add(1, Ordering::Relaxed) + 1, Ordering::Relaxed);
    let waited = Instant::now();
    let mut cell = slot.model.lock().ok()?;
    if waited.elapsed().as_millis() >= 10 {
        log::write(&format!("waited {:?} for {model_dir} to finish loading", waited.elapsed()));
    }
    if let Some(model) = cell.as_ref() {
        return Some(model.clone());
    }
    log::write(&format!("loading {model_dir} ..."));
    let started = Instant::now();
    let info = std::fs::read_to_string(format!("{model_dir}\\voice.json")).unwrap_or_default();
    // sherpa-onnx takes 0 as "not set" and uses its own 0.667 and 0.8: 0 here is near enough 0 instead.
    let (noise_scale, noise_w) = (number(&info, "noise_scale", NOISE_SCALE).max(0.001), number(&info, "noise_w", NOISE_W).max(0.001));
    let config = OfflineTtsConfig {
        model: OfflineTtsModelConfig {
            vits: OfflineTtsVitsModelConfig {
                model: Some(format!("{model_dir}\\model.onnx")),
                tokens: Some(format!("{model_dir}\\tokens.txt")),
                data_dir: Some(data_dir.to_string()),
                noise_scale,
                noise_scale_w: noise_w,
                // Speed is applied per line instead, from the voice's settings as they are then.
                length_scale: 1.0,
                ..Default::default()
            },
            num_threads: 2,
            debug: false,
            provider: Some("cpu".into()),
            ..Default::default()
        },
        // The engine splits sentences itself and asks for one at a time.
        max_num_sentences: 1,
        ..Default::default()
    };
    let Some(tts) = OfflineTts::create(&config) else {
        log::write(&format!("could not load the model in {model_dir} (espeak data: {data_dir})"));
        return None;
    };
    let sample_rate = u32::try_from(tts.sample_rate()).unwrap_or(DEFAULT_RATE);
    let speakers = tts.num_speakers().max(1);
    let changed = std::fs::metadata(format!("{model_dir}\\voice.json")).and_then(|m| m.modified()).ok();
    let model = Arc::new(Model { tts, sample_rate, busy: Mutex::new(()), dir: model_dir.to_string(),
        settings: Mutex::new((changed, Settings::from(&info))) });
    *cell = Some(model.clone());
    drop(cell);
    log::write(&format!("loaded {model_dir}: {sample_rate} Hz, {speakers} speaker(s), noise {noise_scale}/{noise_w}, {:?} ({})",
        started.elapsed(), memory()));
    make_room(&key);
    Some(model)
}

/// The voice a token names: its model and speaker.
#[derive(Clone)]
struct Voice {
    model: Arc<Model>,
    speaker: i32,
    name: String,
    /// The program's settings file (pauses for every voice), from the token's `SettingsFile`.
    settings_file: Option<String>,
}

#[implement(ISpTTSEngine, ISpObjectWithToken)]
struct Engine {
    token: Mutex<Option<ISpObjectToken>>,
    voice: Mutex<Option<Voice>>,
}

fn token_string(token: &ISpObjectToken, name: PCWSTR) -> Option<String> {
    unsafe {
        let value = token.GetStringValue(name).ok()?;
        let text = value.to_string().ok();
        CoTaskMemFree(Some(value.0 as *const c_void));
        text
    }
}

fn token_dword(token: &ISpObjectToken, name: PCWSTR) -> Option<u32> {
    let mut value = 0u32;
    unsafe { token.GetDWORD(name, &mut value).ok()? };
    Some(value)
}

impl ISpObjectWithToken_Impl for Engine_Impl {
    fn SetObjectToken(&self, ptoken: Ref<ISpObjectToken>) -> Result<()> {
        let token = ptoken.ok()?.clone();
        let started = Instant::now();
        let number = TOKENS.fetch_add(1, Ordering::Relaxed) + 1;
        let result = catch_unwind(AssertUnwindSafe(|| {
            let name = token_string(&token, PCWSTR::null()).unwrap_or_else(|| "Modern voice".into());
            let model_dir = token_string(&token, w!("ModelDir"));
            let data_dir = token_string(&token, w!("DataDir"));
            let (Some(model_dir), Some(data_dir)) = (model_dir, data_dir) else {
                log::write(&format!("{name}: the voice token has no ModelDir or DataDir"));
                return None;
            };
            let speaker = token_dword(&token, w!("SpeakerId")).unwrap_or(0) as i32;
            let settings_file = token_string(&token, w!("SettingsFile"));
            let model = load_model(&model_dir, &data_dir)?;
            if log::enabled() {
                log::write(&format!("voice #{number} {name}: {:?} (from {})", started.elapsed(), caller()));
            }
            Some(Voice { model, speaker, name, settings_file })
        }));
        match result {
            Ok(Some(voice)) => {
                *self.voice.lock().map_err(|_| E_FAIL)? = Some(voice);
                *self.token.lock().map_err(|_| E_FAIL)? = Some(token);
                Ok(())
            }
            Ok(None) => Err(E_FAIL.into()),
            Err(_) => {
                log::write("panic while loading a voice");
                Err(E_FAIL.into())
            }
        }
    }

    fn GetObjectToken(&self) -> Result<ISpObjectToken> {
        self.token.lock().map_err(|_| E_FAIL)?.clone().ok_or_else(|| E_POINTER.into())
    }
}

// --- What to say: SAPI's text fragments, as runs of text that keep where each character came from ---

/// A stretch of text to say, with SAPI's adjustments as they stood for it. `text` is UTF-16, as the
/// program gave it; `offsets[i]` is where `text[i]` is in the program's own text (what the events
/// report back, so it can find the word being said).
struct Run {
    text: Vec<u16>,
    offsets: Vec<u32>,
    rate: i32,
    pitch: i32,
    volume: u32,
}

enum Part {
    Say(Run),
    Silence(u32),
    /// A bookmark's name, to report when speech reaches it.
    Bookmark(Vec<u16>),
}


/// The text fragments SAPI hands over (it has already parsed any XML tags into their state), joined
/// into runs that share their rate, pitch and volume. Text to spell out becomes its characters with
/// spaces between, each still pointing at its own place in the program's text.
unsafe fn parts(mut fragment: *const SPVTEXTFRAG) -> Vec<Part> {
    let mut parts: Vec<Part> = Vec::new();
    while let Some(frag) = unsafe { fragment.as_ref() } {
        let state = &frag.State;
        let action = state.eAction;
        let units: &[u16] = if frag.pTextStart.is_null() || frag.ulTextLen == 0 {
            &[]
        } else {
            unsafe { std::slice::from_raw_parts(frag.pTextStart.0, frag.ulTextLen as usize) }
        };
        if (action == SPVA_Speak || action == SPVA_SpellOut || action == SPVA_Pronounce) && !units.is_empty() {
            let start = frag.ulTextSrcOffset;
            let (text, offsets): (Vec<u16>, Vec<u32>) = if action == SPVA_SpellOut {
                let mut text = Vec::with_capacity(units.len() * 2);
                let mut offsets = Vec::with_capacity(units.len() * 2);
                for (i, &unit) in units.iter().enumerate() {
                    if i > 0 {
                        text.push(SPACE);
                        offsets.push(start + i as u32);
                    }
                    text.push(unit);
                    offsets.push(start + i as u32);
                }
                (text, offsets)
            } else {
                (units.to_vec(), (0..units.len() as u32).map(|i| start + i).collect())
            };
            let (rate, pitch, volume) = (state.RateAdj, state.PitchAdj.MiddleAdj, state.Volume);
            match parts.last_mut() {
                Some(Part::Say(run)) if run.rate == rate && run.pitch == pitch && run.volume == volume => {
                    run.text.push(SPACE);
                    run.offsets.push(offsets[0]);
                    run.text.extend_from_slice(&text);
                    run.offsets.extend_from_slice(&offsets);
                }
                _ => parts.push(Part::Say(Run { text, offsets, rate, pitch, volume })),
            }
        } else if action == SPVA_Silence {
            parts.push(Part::Silence(state.SilenceMSecs));
        } else if action == SPVA_Bookmark {
            parts.push(Part::Bookmark(units.to_vec()));
        }
        fragment = frag.pNext;
    }
    parts
}

// --- Speaking ---

/// Where speech goes: SAPI's site, how much has been written to it (the events' positions, in bytes
/// of the output format) and which events the program wants.
struct Output<'a> {
    site: &'a ISpTTSEngineSite,
    written: u64,
    interest: u64,
    /// A sentence has ended and nothing since: the next one starts with the pause between them.
    /// (Written then, not after the sentence, so speech never ends on an added pause.)
    after_sentence: bool,
}

fn event(id: SPEVENTENUM, kind: SPEVENTLPARAMTYPE, offset: u64, wparam: usize, lparam: isize) -> SPEVENT {
    SPEVENT {
        // eEventId : 16, elParamType : 16
        _bitfield: (id.0 & 0xffff) | (kind.0 << 16),
        ulStreamNum: 0,
        ullAudioStreamOffset: offset,
        wParam: WPARAM(wparam),
        lParam: LPARAM(lparam),
    }
}

impl Output<'_> {
    fn wants(&self, id: SPEVENTENUM) -> bool {
        self.interest & (1u64 << id.0) != 0
    }

    /// Writes 16-bit samples to SAPI. False when SAPI will take no more (the write failed).
    ///
    /// A successful write took all of it, as in Microsoft's sample engine, which never reads the
    /// count written: not every site fills it in (System.Speech's says 0 and has taken the lot),
    /// and treating that 0 as "full" stopped speech after its first piece.
    fn write(&mut self, samples: &[i16]) -> bool {
        if samples.is_empty() {
            return true;
        }
        let bytes: &[u8] = unsafe { std::slice::from_raw_parts(samples.as_ptr() as *const u8, samples.len() * 2) };
        match unsafe { self.site.Write(bytes.as_ptr() as *const c_void, bytes.len() as u32) } {
            Ok(_) => {
                self.written += bytes.len() as u64;
                true
            }
            Err(e) => {
                log::write(&format!("SAPI refused {} bytes of audio: {e}", bytes.len()));
                false
            }
        }
    }

    /// Silence, written in pieces of about 50 ms so that stopping cuts it short at once. False when
    /// stopped or SAPI took no more.
    fn silence(&mut self, mut count: usize) -> bool {
        let piece = [0i16; 1024];
        while count > 0 {
            if self.aborted() {
                return false;
            }
            let n = count.min(piece.len());
            if !self.write(&piece[..n]) {
                return false;
            }
            count -= n;
        }
        true
    }

    fn aborted(&self) -> bool {
        unsafe { self.site.GetActions() as i32 & SPVES_ABORT.0 != 0 }
    }

    /// A word or sentence boundary here: where it is in the program's text, and how long it is there.
    fn boundary(&self, id: SPEVENTENUM, offsets: &[u32], from: usize, to: usize) {
        if !self.wants(id) || from >= to {
            return;
        }
        let start = offsets[from];
        let length = offsets[to - 1].saturating_sub(start) + 1;
        let e = event(id, SPET_LPARAM_IS_UNDEFINED, self.written, length as usize, start as isize);
        let _ = unsafe { self.site.AddEvents(&e, 1) };
    }

    fn bookmark(&self, name: &[u16]) {
        if !self.wants(SPEI_TTS_BOOKMARK) {
            return;
        }
        // SAPI copies the string while it queues the event.
        let mut text: Vec<u16> = name.to_vec();
        text.push(0);
        let e = event(SPEI_TTS_BOOKMARK, SPET_LPARAM_IS_STRING, self.written, bookmark_number(name) as isize as usize,
            text.as_ptr() as isize);
        let _ = unsafe { self.site.AddEvents(&e, 1) };
    }
}

impl Engine_Impl {
    fn speak(&self, parts: Vec<Part>, out: &mut Output) -> Result<()> {
        let voice = self.voice.lock().map_err(|_| E_FAIL)?.clone().ok_or(E_FAIL)?;
        let base_rate = unsafe { out.site.GetRate() }.unwrap_or(0);
        let base_volume = unsafe { out.site.GetVolume() }.unwrap_or(100) as f32 / 100.0;
        for part in parts {
            if out.aborted() {
                return Ok(());
            }
            match part {
                Part::Silence(ms) => {
                    // The program's own pause, in place of the one between sentences.
                    out.after_sentence = false;
                    let count = (voice.model.sample_rate as u64 * ms as u64 / 1000) as usize;
                    if !out.silence(count) {
                        return Ok(());
                    }
                }
                Part::Bookmark(name) => out.bookmark(&name),
                Part::Say(run) => {
                    if !self.say(&voice, &run, base_rate, base_volume, out) {
                        return Ok(());
                    }
                }
            }
        }
        Ok(())
    }

    /// One run, sentence by sentence, each sentence word by word. Between sentences, the voice's
    /// pause plus the set gap (a paragraph's at a line break); within one, when commas have a gap, a
    /// clause at a time with that gap between. Gaps shrink as speech speeds up: at rate 0, 100% for
    /// every voice and the voice's own speed, the full gap. False when it was stopped or SAPI took no more.
    fn say(&self, voice: &Voice, run: &Run, base_rate: i32, base_volume: f32, out: &mut Output) -> bool {
        // SAPI's rate is -10 to 10, a third to three times as fast; its pitch is taken as
        // semitones, a few at most, by which the speech is shifted.
        let rate = (base_rate + run.rate).clamp(-10, 10) as f32;
        let semitones = run.pitch.clamp(-6, 6) as f32;
        let shift = 2f32.powf(semitones / 12.0);
        let gain = base_volume * (run.volume.min(100) as f32 / 100.0);
        let settings = voice.model.settings();
        let global = global_settings(voice.settings_file.as_deref());
        let pauses = settings.own_pauses.unwrap_or(global.pauses);
        // The program's rate on top of the speed set for every voice (the window's Speed and pauses
        // tab): 100% unless set; 125% is about as fast as rate +2.
        let speed = (3f32.powf(rate / 10.0).clamp(0.4, 2.5) * global.speed).clamp(0.1, 3.0);
        let pace = speed / settings.length_scale;
        let rate_hz = voice.model.sample_rate;
        let config = GenerationConfig { speed: speed / shift / settings.length_scale, sid: voice.speaker, ..Default::default() };

        let found = sentences(&run.text);
        for (i, &(from, to)) in found.iter().enumerate() {
            if out.aborted() {
                return false;
            }
            if out.after_sentence {
                let before = if i > 0 { found[i - 1].1 } else { 0 };
                let ms = if line_break_between(&run.text, before, from) { pauses.paragraph_ms } else { pauses.sentence_ms };
                if !out.silence(gap_samples(ms, pace, rate_hz)) {
                    return false;
                }
                out.after_sentence = false;
            }
            out.boundary(SPEI_SENTENCE_BOUNDARY, &run.offsets, from, to);
            let pieces = if pauses.comma_ms > 0.0 { clauses(&run.text, from, to) } else { vec![(from, to)] };
            for (k, &(a, b)) in pieces.iter().enumerate() {
                if k > 0 && !out.silence(gap_samples(pauses.comma_ms, pace, rate_hz)) {
                    return false;
                }
                // Said in parts: the silence where a part meets the next comes off (the gap replaces it).
                let (head, tail) = (k > 0, k + 1 < pieces.len());
                if !self.say_piece(voice, run, (a, b), (head, tail), &config, &settings, (shift, gain), out) {
                    return false;
                }
            }
            out.after_sentence = true;
        }
        true
    }

    /// Makes text[from..to) of a run and writes it word by word, each word's event just before it.
    /// `trim`: whether to take the silence off its start and end (a part of a sentence said in parts).
    #[allow(clippy::too_many_arguments)]
    fn say_piece(&self, voice: &Voice, run: &Run, (from, to): (usize, usize), (head, tail): (bool, bool),
                 config: &GenerationConfig, settings: &Settings, (shift, gain): (f32, f32), out: &mut Output) -> bool {
        let text = String::from_utf16_lossy(&run.text[from..to]);
        let started = Instant::now();
        let made = {
            let _turn = voice.model.busy.lock();
            voice.model.tts.generate_with_config(&text, config, None::<fn(&[f32], f32) -> bool>)
        };
        let audio = match made {
            Some(made) => {
                let mut raw = made.samples().to_vec();
                apply_loudness(&mut raw, settings.peak, settings.boost);
                let margin = (voice.model.sample_rate as usize) * 40 / 1000;
                resample(trim_silence(&raw, head, tail, margin), shift)
            }
            None => {
                log::write(&format!("{}: nothing made for {text:?}", voice.name));
                Vec::new()
            }
        };
        if log::enabled() {
            log::write(&format!("{} (speaker {}): {text:?}, {} samples in {:?} (speed {:.2}, shift {shift:.2})",
                voice.name, voice.speaker, audio.len(), started.elapsed(), config.speed));
        }
        if out.aborted() {
            return false;
        }
        let pcm: Vec<i16> = audio.iter().map(|s| (s * gain * 32767.0).clamp(-32768.0, 32767.0) as i16).collect();
        let spoken = words(&run.text, from, to);
        let weights: Vec<f32> = spoken.iter().map(|&(a, b)| weight(&run.text[a..b])).collect();
        let starts = word_starts(&audio, &weights);
        let mut at = 0usize;
        for (k, &(a, b)) in spoken.iter().enumerate() {
            let next = starts[k].max(at);
            if !out.write(&pcm[at..next]) || out.aborted() {
                return false;
            }
            at = next;
            out.boundary(SPEI_WORD_BOUNDARY, &run.offsets, a, b);
        }
        out.write(&pcm[at..])
    }

    fn sample_rate(&self) -> u32 {
        self.voice.lock().ok().and_then(|v| v.as_ref().map(|v| v.model.sample_rate)).unwrap_or(DEFAULT_RATE)
    }
}

impl ISpTTSEngine_Impl for Engine_Impl {
    fn Speak(&self, flags: u32, format: *const GUID, wave: *const WAVEFORMATEX, fragments: *const SPVTEXTFRAG,
             site: Ref<ISpTTSEngineSite>) -> Result<()> {
        let rate = unsafe { wave.as_ref() }.map(|w| (w.nSamplesPerSec, w.wBitsPerSample, w.nChannels));
        if log::enabled() {
            log::write(&format!("speak: flags {flags:#x}, format {:?}, wave {rate:?} (from {})", unsafe { format.as_ref() },
                caller()));
        }
        let started = Instant::now();
        let site = site.ok()?.clone();
        if log::enabled() {
            // Each fragment as SAPI hands it over: what the program's text became on the way.
            let mut fragment = fragments;
            while let Some(frag) = unsafe { fragment.as_ref() } {
                let text = if frag.pTextStart.is_null() { String::new() } else {
                    String::from_utf16_lossy(unsafe { std::slice::from_raw_parts(frag.pTextStart.0, frag.ulTextLen as usize) })
                };
                log::write(&format!("  fragment: action {}, offset {}, {text:?}", frag.State.eAction.0, frag.ulTextSrcOffset));
                fragment = frag.pNext;
            }
        }
        let mut interest = 0u64;
        let _ = unsafe { site.GetEventInterest(&mut interest) };
        let result = match catch_unwind(AssertUnwindSafe(|| {
            let parts = unsafe { parts(fragments) };
            let mut out = Output { site: &site, written: 0, interest, after_sentence: false };
            self.speak(parts, &mut out)
        })) {
            Ok(result) => result,
            Err(_) => {
                log::write("panic while speaking");
                Err(E_FAIL.into())
            }
        };
        if log::enabled() {
            log::write(&format!("speak done in {:?} (events wanted {interest:#x})", started.elapsed()));
        }
        result
    }

    fn GetOutputFormat(&self, _target_format: *const GUID, _target_wave: *const WAVEFORMATEX,
                       output_format: *mut GUID, output_wave: *mut *mut WAVEFORMATEX) -> Result<()> {
        if output_format.is_null() || output_wave.is_null() {
            return Err(E_POINTER.into());
        }
        let rate = self.sample_rate();
        if log::enabled() {
            log::write(&format!("output format asked (target {:?}): giving PCM {rate} Hz 16-bit mono",
                unsafe { _target_format.as_ref() }));
        }
        unsafe {
            let wave = CoTaskMemAlloc(size_of::<WAVEFORMATEX>()) as *mut WAVEFORMATEX;
            if wave.is_null() {
                return Err(E_FAIL.into());
            }
            wave.write(WAVEFORMATEX {
                wFormatTag: WAVE_FORMAT_PCM as u16,
                nChannels: 1,
                nSamplesPerSec: rate,
                nAvgBytesPerSec: rate * 2,
                nBlockAlign: 2,
                wBitsPerSample: 16,
                cbSize: 0,
            });
            *output_format = SPDFID_WAVEFORMATEX;
            *output_wave = wave;
        }
        Ok(())
    }
}

#[implement(IClassFactory)]
struct Factory;

impl IClassFactory_Impl for Factory_Impl {
    fn CreateInstance(&self, outer: Ref<IUnknown>, iid: *const GUID, object: *mut *mut c_void) -> Result<()> {
        if object.is_null() {
            return Err(E_POINTER.into());
        }
        unsafe { *object = std::ptr::null_mut() };
        if outer.is_some() {
            return Err(CLASS_E_NOAGGREGATION.into());
        }
        let number = INSTANCES.fetch_add(1, Ordering::Relaxed) + 1;
        if log::enabled() {
            log::write(&format!("engine instance #{number} (from {})", caller()));
        }
        let engine: IUnknown = Engine { token: Mutex::new(None), voice: Mutex::new(None) }.into();
        unsafe { engine.query(iid, object).ok() }
    }

    fn LockServer(&self, _lock: BOOL) -> Result<()> {
        Ok(())
    }
}

/// Notes when and where the engine was loaded, nothing more: the loader lock is held here, so the
/// log line waits for the first call (`announce`).
#[unsafe(no_mangle)]
pub extern "system" fn DllMain(module: *mut c_void, reason: u32, _reserved: *mut c_void) -> BOOL {
    const DLL_PROCESS_ATTACH: u32 = 1;
    if reason == DLL_PROCESS_ATTACH {
        MODULE.store(module as usize, Ordering::Relaxed);
        log::start();
    }
    true.into()
}

/// COM's way in: the class factory for this engine's CLSID.
#[unsafe(no_mangle)]
pub unsafe extern "system" fn DllGetClassObject(clsid: *const GUID, iid: *const GUID, object: *mut *mut c_void) -> HRESULT {
    if clsid.is_null() || iid.is_null() || object.is_null() {
        return E_POINTER;
    }
    announce();
    unsafe {
        *object = std::ptr::null_mut();
        if *clsid != CLSID_MODERN_VOICES {
            log::write(&format!("asked for an unknown class {:?}", *clsid));
            return CLASS_E_CLASSNOTAVAILABLE;
        }
        let factory: IClassFactory = Factory.into();
        let result = factory.query(iid, object);
        let number = FACTORIES.fetch_add(1, Ordering::Relaxed) + 1;
        if log::enabled() {
            log::write(&format!("class factory #{number} requested (interface {:?}): {result:?} (from {})", *iid, caller()));
        }
        result
    }
}

/// Never unloaded once loaded: the models it holds are what makes the next line quick.
#[unsafe(no_mangle)]
pub extern "system" fn DllCanUnloadNow() -> HRESULT {
    S_FALSE
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn settings_from_voice_json() {
        let set = r#"{ "inference": { "noise_scale": 0.667, "length_scale": 1.2, "noise_w": 0.8 },
            "loudness": { "peak": 0.9, "boost": 2 } }"#;
        assert_eq!(Settings::from(set), Settings { peak: 0.9, boost: 2.0, length_scale: 1.2, own_pauses: None });
        let own = r#"{ "pauses": { "own_pauses": 1, "sentence_ms": 300, "comma_ms": 0, "paragraph_ms": 900 } }"#;
        assert_eq!(Settings::from(own).own_pauses, Some(Pauses { sentence_ms: 300.0, comma_ms: 0.0, paragraph_ms: 900.0 }));
        assert_eq!(Settings::from(""), Settings { peak: 0.0, boost: 1.0, length_scale: 1.0, own_pauses: None });
    }

    #[test]
    fn event_bitfield() {
        let e = event(SPEI_WORD_BOUNDARY, SPET_LPARAM_IS_STRING, 10, 3, 5);
        assert_eq!(e._bitfield & 0xffff, 5);
        assert_eq!(e._bitfield >> 16, 4);
    }
}
