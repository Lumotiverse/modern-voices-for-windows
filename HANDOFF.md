# Handoff: Modern Voices for Windows

For a fresh Claude session continuing this project. Read this first, then the code it points to.
Last updated at version 0.11.0 (14 Nov 2026).

## What it is

A private project for Lumoti (GitHub: Lumotiverse). It is not published for others. It adds modern,
offline Piper voices (English only for now) to Windows as SAPI 5 voices, for any program that speaks
with Windows' voices but can't use Narrator's natural voices. It must keep working in World of Warcraft
(WoW Forever, with Lumoti's TTS Enhanced addon), though WoW isn't the focus. It has four parts:

| Part | What | Language | Where |
|---|---|---|---|
| Engine | The SAPI 5 TTS engine DLL (x64 and x86) that programs load. It speaks through sherpa-onnx and applies loudness, pauses, speed and the pronunciation dictionary. | Rust | `engine/` |
| Installer | Installs, updates in place, repairs, registers voices, downloads and converts them. Also does the work behind Config's Apply changes. | PowerShell 5.1 | `installer/Install.ps1` (plus `VoiceConvert.ps1`, `Test.ps1`, `Nickname.ps1`, `Ocr.ps1`, `Uninstall.ps1`) |
| Modern Voices Config | The settings window: Voice manager, Speed and pauses, Pronunciation, Add or remove voices. A program of its own since 0.11.0 (it was a PowerShell window inside Install.ps1 before). | C# 5 + WinForms (csc.exe, .NET Framework 4) | `config/Config.cs`, `config/Build.ps1`, `config/app.manifest` |
| Modern Voices: Reader | A small app that reads pasted or copied text, files and screen regions. It has play/pause/stop, time left, saves as WAV, and remembers its place per file. | C# 5 (built with Windows' own `csc.exe`, .NET Framework 4) | `reader/Reader.cs`, `reader/Build.ps1` |

The user-facing documentation is `kit/README.txt`, and it is accurate for 0.11.0. Read it to see what
each feature does.

## Lumoti's standing rules

- **Before any build request, say whether it needs Fable (complicated: new features, engine work,
  multi-part changes) or Opus 5.5 (simple and safe changes to working code)**, so he can switch model
  first. If he's already on the right one, say so and proceed.
- The version number goes in the filename of every build he downloads: `ModernVoicesForWindows0.11.0.zip`.
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
  the first line of `kit/README.txt`. The `AssemblyVersion` in `reader/Reader.cs` and `config/Config.cs`
  is stamped at build from Cargo.toml, but keep both in step.
- He likes direct pushback and honesty about what wasn't tested. Report what the test machine did NOT
  cover (real mouse, real audio, real clipboard, taskbar).
- He's British. He's a trained graphic designer, so he notices alignment and spacing.

## How a build works

There's no way to build for Windows locally. Everything goes through GitHub Actions
(`.github/workflows/build.yml`), triggered by pushing to `main`:

1. `engine-x64` builds and runs `cargo test`. `engine-x86` builds the 32-bit DLL (continue-on-error;
   its sherpa libraries come from `tools/Get-SherpaX86.ps1`). `reader` compiles the Reader with
   `reader/Build.ps1`. `config` first parse-checks every `.ps1` and runs `scopescan.ps1` over
   `installer/` (either failing stops the build), then compiles Config with `config/Build.ps1`. It is the
   quickest job (about a minute): look at its log first for compile errors.
2. `package` signs the DLLs and the Reader and Config exes with a throwaway self-signed certificate (the private
   key is deleted after signing; the installer trusts the `.cer`, because WoW only loads signed
   engines). It then zips the kit (`tools/Package.ps1`).
3. It then runs `tools/Test-Kit.ps1` end to end on the Windows runner, which:
   - installs to `C:\MV Test` with real voices from Hugging Face;
   - speaks through System.Speech and checks loudness, live settings and speed;
   - measures pause gaps to the millisecond;
   - checks the pronunciation dictionary changes speech;
   - runs Config's own test three times (`ModernVoicesConfig.exe --selftest DIR [--pictures] [--apply]`:
     from the kit before installing, installed, and with pictures plus a real Apply changes through the
     window that adds a VCTK speaker and downloads en_GB-alan-medium, then removes both; it also speaks
     with a voice from a second copy of the exe to a memory stream, `--speak-check`, as CI has no sound
     device); `Install.ps1 -PickerSelfTest` now only tests the
     "where to install" screen;
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
- **PowerShell checks:** the CI `config` job does both of these on every push (since 0.11.0), so pwsh
  here is optional. (In the 0.11.0 session the container refused the pwsh download; CI was enough.)
  - Parse-check each script with `[System.Management.Automation.Language.Parser]::ParseFile`.
  - `scopescan.ps1 FILE` finds variable names that differ only by case in one scope. PowerShell treats
    those as the same variable, and this has caused real bugs several times (`$here`/`$Here`,
    `$own`/`$Own`, ...).
- **`cscheck.ps1 FILE.cs`** (needs pwsh): a syntax check at C# 5 level (uses the Roslyn DLLs that ship
  with pwsh). It does not catch type errors; the CI `reader` and `config` jobs do, in about a minute.
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

**Config (C#, `config/Config.cs`)**
- Same compiler and rules as the Reader (C# 5, pure ASCII, `\uXXXX` for symbols).
- While Windows makes a ListView's own window (when its tab is first shown), it raises ItemChecked for
  every ticked row with the rows half there (`Items` can hold nulls, `Checked` reads wrong). Ticks count
  only once the list is in `listsReady` (set by a BeginInvoke from HandleCreated). The pronunciation
  rules live in `wordRules`, so saving never reads the ListView.
- A ListView has no selection until its window exists: `SelectedItems` is empty before that, and
  SelectedIndexChanged fires when the window is made. Lists on tabs not yet opened have no window (the
  self-test touches `.Handle`).
- `Control.Visible` and `Enabled` read false while a parent is hidden or disabled: test them with the
  right tab showing.
- Preview speaks in a second copy of the exe (`--speak <base64 voice> <base64 text>`), so the engine is
  never loaded into the window's process (Apply changes may delete a voice's files).
- The exe's manifest asks for administrator (it writes voice.json, settings.json, dictionary.txt under
  the program folder, and HKLM for nicknames). It is DPI-aware (the PowerShell window was not: Windows
  stretched it, so `Px()` scaling at 125% and above is new and not yet seen by Lumoti).
- `Store` in Config.cs and `VoiceConvert.ps1` both read and write voice.json, settings.json and
  dictionary.txt: keep the two in step.

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

## Current state (0.11.0)

Everything above works and is tested on the CI Windows machine. Lumoti uses it daily and sends UI tweaks
one batch at a time; every batch so far is done and delivered. He installs each build over the last
(update in place works in practice). Where things live, for the features added since 0.9.4:

**Engine (`engine/src/text.rs`, `lib.rs`)**
- `with_end_mark` adds a full stop to an unmarked sentence end (the voice clipped a last word without
  one); `TAIL_MS` of silence follows a Speak call's last word.
- `money` says sums aloud ("£150" as "150 pounds", "£1.50" as "1 pound 50", "$3.5m" as "3.5 million
  dollars"), applied after the pronunciation dictionary.

**Config as a program (0.11.0)**
- `config/ModernVoicesConfig.exe`, installed to `<app>\config\`. The Start menu entry, `Modern Voices
  Config.cmd`, Installed apps' Modify and the Reader's cog all start the exe. One instance (mutex): a
  second start brings the first to the front. Windows asks for permission at each start (manifest).
- It opens from `<app>\catalogue.json` (the installer downloads it at install, update and Apply), so
  opening needs no internet; without that file it downloads the list itself.
- Apply changes runs `Install.ps1 -ExactVoices -Unattended -NoPause -Progress [-Voices a,b] [-UsEnglishTags]`
  hidden, and shows its lines in the status line and `##progress N` in the bar. Exit 0 = done, 2 = a
  voice failed its test, else the last `Stopped:` line is shown. `-ExactVoices` means exactly the voices
  given, also none; the US English option is then exactly `-UsEnglishTags`.
- Install.cmd (fresh install or update) still runs Install.ps1 in a console: "where to install" (fresh
  only), files copied, voices registered again, then it starts the installed Config exe and the console
  closes. The console is still moved to the window's bounds first (`Move-ConsoleToUiWindow`;
  `Get-UiWindowBounds` must match `ConfigForm.WindowBounds`).
- Window 900 x 1000 at 100%, centred, capped to the screen. Per-voice Volume slider moves in tens.
- The startup timer (0.10.9 to 0.10.11) is gone. On CI the C# window builds in about 0.6 s (the
  PowerShell one took 1 to 2.3 s after about 1.5 s of PowerShell starting and installer steps).
- `installer/Speak.ps1` is gone (Preview is the exe itself). The update removes it from old installs.
- NOT done, offered to Lumoti as the next step: opening Config without the administrator prompt. Needs
  the settings files moved somewhere writable without admin (and read by the engine from there, with
  existing settings carried over), nicknames through the elevated helper (`Nickname.ps1`, which refuses
  an empty nickname today), and Apply started elevated (then its output can't be redirected: use a pipe
  or a file). Config.cs keeps all privileged writes in `Store`, `Voices.SetNickname` and `Apply` to make
  that change local.

**Installer (`installer/Install.ps1`)**
- Update in place: `$updating` in the main flow, `Close-OurPrograms` (closes the Reader, Config, and a
  pre-0.11 PowerShell Config window), `Remove-Leftovers`.
- Icons: `Get-AppIcon` (the speaker) for Installed apps; Config's exe carries the cog (`manager.ico`,
  embedded at build); Repair, Test and Uninstall in the Start menu have the double-ended wrench (`repair.ico`).
- Start menu: Modern Voices Config, Modern Voices Repair, Modern Voices Test, Uninstall (plus the Reader).
- `Loading program...` is printed by Install.cmd before PowerShell starts (env `MV_LOADING_SAID`).
- Default pronunciation rules: `$DefaultRuleSets` / `Add-DefaultRules` add each named set once (registry
  value `RuleSets`), never overwriting, re-adding or re-ticking a rule the person changed or removed.
  Sets: `smileys1`, `chat1` (rn, np, wp, lf installed unticked, at Lumoti's say), `trade1`, all from his
  old TTS Enhanced addon. Game-instance abbreviations deliberately left out. A changed or extra default
  rule needs a NEW set name.
- Apply changes is greyed out while the ticks match what is installed (`UpdateApply` in Config.cs, 300 ms timer).

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

**Not yet confirmed on his machine (0.11.0):** everything about the C# Config on a real screen: its look
next to the old window (text is drawn by GDI now, and it is DPI-aware), Play sample and Preview (CI has no
sound device), a real voice download through Apply changes with the progress bar moving (CI only added a
speaker of a model already there), the update from 0.10.11 closing nothing it shouldn't, Windows 11.

**Not yet confirmed on his machine (older):** the cog opening Config; the icon in the taskbar and Start menu;
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
