# Handoff: Modern Voices for Windows

For a fresh Claude session continuing this project. Read this first, then the code it points to.
Last updated at version 0.10.6 (13 Nov 2026).

## What it is

A private project for Lumoti (GitHub: Lumotiverse). It is not published for others. It adds modern,
offline Piper voices (English only for now) to Windows as SAPI 5 voices, for any program that speaks
with Windows' voices but can't use Narrator's natural voices. It must keep working in World of Warcraft
(WoW Forever, with Lumoti's TTS Enhanced addon), though WoW isn't the focus. It has three parts:

| Part | What | Language | Where |
|---|---|---|---|
| Engine | The SAPI 5 TTS engine DLL (x64 and x86) that programs load. It speaks through sherpa-onnx and applies loudness, pauses, speed and the pronunciation dictionary. | Rust | `engine/` |
| Modern Voices Config | The installer and settings window: Voice manager, Speed and pauses, Pronunciation, Add or remove voices. | PowerShell 5.1 + WinForms | `installer/Install.ps1` (plus `VoiceConvert.ps1`, `Test.ps1`, `Speak.ps1`, `Ocr.ps1`, `Uninstall.ps1`) |
| Modern Voices: Reader | A small app that reads pasted or copied text, files and screen regions. It has play/pause/stop, time left, saves as WAV, and remembers its place per file. | C# 5 (built with Windows' own `csc.exe`, .NET Framework 4) | `reader/Reader.cs`, `reader/Build.ps1` |

The user-facing documentation is `kit/README.txt`, and it is accurate for 0.10.6. Read it to see what
each feature does.

## Lumoti's standing rules

- **Before any build request, say whether it needs Fable (complicated: new features, engine work,
  multi-part changes) or Opus 5.5 (simple and safe changes to working code)**, so he can switch model
  first. If he's already on the right one, say so and proceed.
- The version number goes in the filename of every build he downloads: `ModernVoicesForWindows0.10.6.zip`.
- Every build must update in place (since 0.9.6): the new kit's Install.cmd run over the installed version
  keeps voices, settings, nicknames and pronunciations, with no uninstall and no re-download. If a build
  changes where or how any of those are stored, it must carry the old ones over. Test-Kit has an 'Update' step.
- The GitHub `README.md` must not mention World of Warcraft; it credits the Spoken Players voicepack engine,
  says Claude wrote all the code, and keeps Lumoti's two lines ("All work ... Claude.ai" / "Code provided by ...").
- Icons: the speech bubble with the speaker (the Reader's exe icon) is the program's default icon
  (Installed apps, the Reader); the bubble with the cog (`manager.ico`) is Modern Voices Config's only;
  the bubble with the double-ended wrench (`repair.ico`) is for Repair, Test and Uninstall in the Start
  menu. All three are drawn by `tools/make_icons.py` (`python3 tools/make_icons.py preview.png` also
  writes a preview sheet).
- Discuss before executing when he asks for that; otherwise build straight away.
- Bump the version in all three places: `engine/Cargo.toml`, `$Version` in `installer/Install.ps1`, and
  the first line of `kit/README.txt`. The Reader's `AssemblyVersion` in `reader/Reader.cs` is stamped
  at build from Cargo.toml, but keep it in step.
- He likes direct pushback and honesty about what wasn't tested. Report what the test machine did NOT
  cover (real mouse, real audio, real clipboard, taskbar).
- He's British. He's a trained graphic designer, so he notices alignment and spacing.

## How a build works

There's no way to build for Windows locally. Everything goes through GitHub Actions
(`.github/workflows/build.yml`), triggered by pushing to `main`:

1. `engine-x64` builds and runs `cargo test`. `engine-x86` builds the 32-bit DLL (continue-on-error;
   its sherpa libraries come from `tools/Get-SherpaX86.ps1`). `reader` compiles the Reader with
   `reader/Build.ps1`.
2. `package` signs the DLLs and the Reader exe with a throwaway self-signed certificate (the private
   key is deleted after signing; the installer trusts the `.cer`, because WoW only loads signed
   engines). It then zips the kit (`tools/Package.ps1`).
3. It then runs `tools/Test-Kit.ps1` end to end on the Windows runner, which:
   - installs to `C:\MV Test` with real voices from Hugging Face;
   - speaks through System.Speech and checks loudness, live settings and speed;
   - measures pause gaps to the millisecond;
   - checks the pronunciation dictionary changes speech;
   - drives the Config window (`Install.ps1 -PickerSelfTest -ScreenshotDir`);
   - runs the Reader's own test (`ModernVoicesReader.exe --selftest DIR`);
   - repairs, then uninstalls.
4. If everything passes, it publishes GitHub release `v<version>` with `ModernVoicesForWindows<version>.zip`
   and `ui-screens.zip` (PNG screenshots of every window, light and dark).

A run takes about 8 minutes. Always look at the screenshots before handing a build over: they have
caught several layout bugs.

To deliver a build: download the release asset through the API
(`Accept: application/octet-stream`; artifact blobs are blocked here), then send it with SendUserFile.

## Tools for working in this container (`tools/dev/`)

The repo is cloned at `/home/claude/modern-voices-for-windows`, with the remote `origin` on `main`. In a
new session, attach it with add_repo (Lumotiverse/modern-voices-for-windows) and clone it.

- **`ci.sh status | log JOBID [N] | wait`**: GitHub Actions status, logs and wait. It unsets `GH_TOKEN`,
  because the proxy injects auth for api.github.com.
- **`setup-check.sh DIR`**: builds a Linux type-check of the engine as if for Windows (patched
  windows-rs crates plus `rustc-wrap.sh`). Copy `engine/src/*.rs` into `DIR/src/`, then run
  `RUSTC_WRAPPER=$PWD/rustc-wrap.sh DOCS_RS=1 cargo check --release --all-targets` there. It catches
  type errors before a 5-minute CI round.
- **Engine unit tests:** run `rustc --edition 2024 --test engine/src/text.rs -o /tmp/t && /tmp/t`. All
  pure logic lives in `text.rs` (sentences, pauses, loudness, tidy-up, dictionary).
- **PowerShell checks:** these need pwsh. Download the linux-x64 tar of PowerShell 7 from its GitHub
  releases and unpack it.
  - Parse-check each script with `[System.Management.Automation.Language.Parser]::ParseFile`.
  - **Always** run `scopescan.ps1 FILE` after editing PowerShell. It finds variable names that differ
    only by case in one scope. PowerShell treats those as the same variable, and this has caused real
    bugs several times (`$here`/`$Here`, `$own`/`$Own`, ...).
- **`cscheck.ps1 reader/Reader.cs`**: a syntax check of the Reader at C# 5 level (uses the Roslyn DLLs
  that ship with pwsh). It does not catch type errors; the CI `reader` job does, in about a minute.
- **`tools/make_icons.py`** (Pillow) regenerates `reader/reader.ico`, `installer/manager.ico` and `installer/repair.ico`.

## Pitfalls learned the hard way

**PowerShell and WinForms**
- Event handlers run after the function that made them has returned, so they can't see its locals. Keep
  window state in `$script:Ui`.
- `@(@(a,b))` flattens; use `@(,@(...))`. Single-quoted strings can't contain `\"`; use `` `" `` in
  double-quoted ones.
- In ListView event handlers `$sender` can be null; fall back to `$this`.
- A blocking dialog freezes a headless CI run, so tests run under time limits and
  `Application.ThreadException` is wired to exit with the error in self-test mode.
- `SetWindowTheme(..., $null)` passes an empty string, not null. Use `[NullString]::Value`.

**The engine**
- sherpa-onnx's C API treats a value of exactly 0 as "not set" (noise 0 gave its 0.667 default). The
  engine clamps noise to a minimum of 0.001.
- Speed has three layers: the program's SAPI rate (3^(rate/10)), times the global `speed_percent` in
  `settings.json`, divided by each voice's `length_scale` in `voice.json`. Pause gaps are divided by the
  same combined speed.
- Settings files (`settings.json`, `voice.json`, `dictionary.txt`) are re-read whenever their
  modification time changes, so changes apply live.
- Pronunciation rules change only what is spoken. Word-boundary events still report the original text.

**The Reader (C#)**
- It is compiled by Windows' csc 4.x, so only C# 5: no `$""`, `?.`, `=>` members or `nameof`.
- Keep the source pure ASCII. Write symbols as `\uXXXX` escapes. A literal `▶` or `•` in the file was
  garbled once at build.
- Button symbols come from the Segoe MDL2 Assets font (E768 play, E769 pause, E71A stop, E892 restart,
  E721 magnifier, E767 volume, E7A7/E7A6 undo/redo, E73E tick, E713 cog; Config's moon is E708), with
  word labels as a fallback.
- Local variable names inside a lambda must not repeat one in the enclosing method (CS0136): this broke
  a build once.
- System.Speech caches voice names per process; read names from the registry when they can change.
- A WinForms ComboBox's dropped list is a separate window: reach it with `GetComboBoxInfo`.

**Other**
- Icons must store their small sizes as BMP, not PNG. Windows ignored the PNG small sizes, so the taskbar
  and Start menu showed no icon. `make_icons.py` does this.
- Piper's output varies slightly between runs, so tests compare with tolerances, and gap tests set
  noise to (almost) 0.

## Current state (0.10.6)

Everything above works and is tested on the CI Windows machine. Lumoti uses it daily and sends UI tweaks
one batch at a time; every batch so far is done and delivered. He installs each build over the last
(update in place works in practice). Where things live, for the features added since 0.9.4:

**Engine (`engine/src/text.rs`, `lib.rs`)**
- `with_end_mark` adds a full stop to an unmarked sentence end (the voice clipped a last word without
  one); `TAIL_MS` of silence follows a Speak call's last word.
- `money` says sums aloud ("£150" as "150 pounds", "£1.50" as "1 pound 50", "$3.5m" as "3.5 million
  dollars"), applied after the pronunciation dictionary.

**Installer and Config (`installer/Install.ps1`)**
- Update in place: `$updating` in the main flow, `Close-OurPrograms`, `Remove-Leftovers`.
- Icons: `Get-AppIcon` (the speaker) for Installed apps; Config has the cog; Repair, Test and Uninstall
  in the Start menu have the double-ended wrench (`repair.ico`).
- Start menu: Modern Voices Config, Modern Voices Repair, Modern Voices Test, Uninstall (plus the Reader).
- `Loading program...` is printed by Install.cmd before PowerShell starts (env `MV_LOADING_SAID`).
- Default pronunciation rules: `$DefaultRuleSets` / `Add-DefaultRules` add each named set once (registry
  value `RuleSets`), never overwriting, re-adding or re-ticking a rule the person changed or removed.
  Sets: `smileys1`, `chat1` (rn, np, wp, lf installed unticked, at Lumoti's say), `trade1`, all from his
  old TTS Enhanced addon. Game-instance abbreviations deliberately left out. A changed or extra default
  rule needs a NEW set name.
- Apply changes is greyed out while the ticks match what is installed (`Update-UiApply`, 300 ms timer).
- The Pronunciation list keeps each row's rule as its own hashtable in `Tag`, and saves a tick only when
  it differs from the rule (Windows reports every ticked row as the list is first drawn).

**Nicknames from the Reader (`installer/Nickname.ps1`)**
- One-shot: `-Token <ModernVoices_...> -Nick64 <base64 UTF-8>`; exit 0 done, 2 refused, 3 name taken.
- Staying: `-Serve <pipe> -OwnerPid -OwnerSid`. Started elevated (one UAC prompt) at the Reader's first
  nickname; serves `"<token> <nick64>"` lines on a named pipe whose ACL admits only the Reader's user;
  can only rename a Modern Voice; exits when the Reader's process is gone or on `quit`. The Reader
  (`AskNickHelper`) falls back to the one-shot script if the helper doesn't answer.

**Reader (`reader/Reader.cs`)**
- Top row: voice list, Voice label, nickname box ("Change nickname" cue banner) and square tick, Speed.
  The box and tick are as tall as the list.
- Voice list: owner-drawn with a radio dot per row (filled = Windows' default voice). Clicking a dot sets
  the default and keeps the list open; clicking a name chooses the voice. Two routes in: `DotList` (a
  NativeWindow on the dropped list) and `VoiceChosen` (a row chosen with the pointer on its dot counts as
  a dot click). The "Set Windows default voice" tooltip is a `TipWindow` driven by a timer watching the
  pointer. Lumoti confirmed the dots work.
- Voice names are read from the registry (`NameNow`), because System.Speech keeps the names it saw at
  start-up for the life of the process; System.Speech's own name is kept as `VoiceChoice.Spoken` for
  `SelectVoice`. The chosen voice is saved by token id. `RefreshVoices` runs after a nickname and when the
  window is activated and `VoicesNow` differs.
- Button row: restart, play, stop | Open a file, Paste and read, magnifying glass (E721, read a screen
  area), Save as audio | volume (E767 speaker + slider, `State.Volume`) | undo, redo (E7A7, E7A6), Clear.
- Volume (0.10.6): the slider sets the Reader process's audio-session volume with `waveOutSetVolume(0, ...)`
  (`ApplyVolume`), heard at once; `synth.Volume` stays 100 for playback. Setting `synth.Volume` mid-read
  did nothing because the engine reads `GetVolume` once per Speak call (`lib.rs`, `speak`). Save as audio
  still uses `saver.Volume`. Engine-side fix (honour `SPVES_VOLUME` mid-Speak, for all programs) not done.
- Play after the end (0.10.6): `Play` starts from 0 when `finished`; `StartAt` with nothing readable left
  from the place (blank lines after the last sentence) starts from 0 instead of saying "end of the text".
- Row under the buttons is a Panel placed by `AlignLine`: progress bar as wide as restart+play, the time
  after it, "Instantly read copy" under Paste and read, "Don't read links" under Save as audio (it still
  leaves out code and symbols; the tooltip says so).
- Progress bar shows what is LEFT: mirrored ProgressBar, full at the start, emptying towards the right,
  empty once finished (`finished` flag). It and the time left follow the reading position, which a click
  in the text moves (see below).
- Status line: messages fade five seconds after they appear (`Status`, `FadeStatus`).
- Undo/redo: ten steps (`NoteChange`, `StepBack`); Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z, the right-click menu.
- The box starts empty each run. A second Reader asks first (`AnotherWanted`; Cancel focuses the open one).
- Copied HTML is read as UTF-8 bytes (`CopiedHtml`), not via `Clipboard.GetText(Html)`, which decoded it in
  the ANSI code page and turned curly quotes and ellipses into mojibake.
- The cog (E713, bottom right) opens Config; the Reader follows Config's moon (registry `Theme`).
- Least window width 950.

**Known behaviour Lumoti noticed (0.10.5), not yet decided:** clicking in the text moves the reading
position (by design: Play starts from the caret), so the bar and the time left jump to show what's left
from there, even when he only clicked to type. He called it an "unintended feature". Ask whether he wants
it kept, or the bar and time to change only while reading.

**Not yet confirmed on his machine:** the cog opening Config; the icon in the taskbar and Start menu;
pasting from real browsers keeping paragraphs and bullets; the volume slider changing live (0.10.6); the greyed Apply button's
look in dark mode.

## Ideas discussed but not built (only when Lumoti asks)

- **Accurate time-left countdown:** run the text through the voice silently in the background and
  measure the real length. Also remember each voice's measured pace for the initial estimate. Agreed in
  principle (the estimate said 1:27 where the real audio was 1:22); Opus-sized.
- **MP3 export:** needs a bundled encoder. Save as sound is WAV only for now.
- **PDF and old .doc support** in the Reader.
- **Repair and Test as buttons inside Config** instead of Start menu entries (offered; he kept them).
- **A "save text to a file" button** in the Reader (it replaced the removed read-later list; Lumoti saves
  files himself for now).
- **More TTS Enhanced features:** ask Lumoti for the addon's source (TTSEnhanced0.36.1) if needed. Pauses and the pronunciation dictionary are done.
  Raid-icon stripping was left out as WoW-only.
- Ruled out by Lumoti: a browser extension, double right-click or double Ctrl+C triggers, LM Studio
  integration (for now), and a tray icon.
