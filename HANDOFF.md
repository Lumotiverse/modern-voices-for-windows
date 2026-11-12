# Handoff: Modern Voices for Windows

For a fresh Claude session continuing this project. Read this first, then the code it points to.
Last updated at version 0.9.4 (12 Nov 2026).

## What it is

A private project for Luke (GitHub: Lumotiverse). It is not published for others. It adds modern,
offline Piper voices (English only for now) to Windows as SAPI 5 voices, for any program that speaks
with Windows' voices but can't use Narrator's natural voices. It must keep working in World of Warcraft
(WoW Forever, with Luke's TTS Enhanced addon), though WoW isn't the focus. It has three parts:

| Part | What | Language | Where |
|---|---|---|---|
| Engine | The SAPI 5 TTS engine DLL (x64 and x86) that programs load. It speaks through sherpa-onnx and applies loudness, pauses, speed and the pronunciation dictionary. | Rust | `engine/` |
| Modern Voices Config | The installer and settings window: Voice manager, Speed and pauses, Pronunciation, Add or remove voices. | PowerShell 5.1 + WinForms | `installer/Install.ps1` (plus `VoiceConvert.ps1`, `Test.ps1`, `Speak.ps1`, `Ocr.ps1`, `Uninstall.ps1`) |
| Modern Voices: Reader | A small app that reads pasted or copied text, files and screen regions. It has play/pause/stop, time left, saves as WAV, and remembers its place per file. | C# 5 (built with Windows' own `csc.exe`, .NET Framework 4) | `reader/Reader.cs`, `reader/Build.ps1` |

The user-facing documentation is `kit/README.txt`, and it is accurate for 0.9.4. Read it to see what
each feature does.

## Luke's standing rules

- **Before any build request, say whether it needs Fable (complicated: new features, engine work,
  multi-part changes) or Opus 5.5 (simple and safe changes to working code)**, so he can switch model
  first. If he's already on the right one, say so and proceed.
- The version number goes in the filename of every build he downloads: `ModernVoicesForWindows0.9.4.zip`.
- Builds assume a fresh install: he uninstalls the old version first. Never add upgrade or migration code.
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
- **`tools/make_icons.py`** (Pillow) regenerates `reader/reader.ico` and `installer/manager.ico`.

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
  E708 moon), with word labels as a fallback.

**Other**
- Icons must store their small sizes as BMP, not PNG. Windows ignored the PNG small sizes, so the taskbar
  and Start menu showed no icon. `make_icons.py` does this.
- Piper's output varies slightly between runs, so tests compare with tolerances, and gap tests set
  noise to (almost) 0.

## Current state (0.9.4)

Everything above works and is tested on the CI Windows machine. Luke has been using it and sending UI
tweaks one batch at a time; the last batch (0.9.4) is done and delivered: the moon moved and greyed in
both windows, and the clipped last word fixed in the engine (`with_end_mark` in `text.rs` adds a full
stop to an unmarked sentence end; `TAIL_MS` of silence is written after a Speak call's last word).
Not yet confirmed by Luke:

- the last word now heard whole, both in playback and in the Reader's saved WAV;
- the icon now showing in the taskbar and Start menu;
- the Reader's progress row lining up with the buttons on his screen;
- pasting from real browsers keeping paragraphs and bullets.

## Ideas discussed but not built (only when Luke asks)

- **Accurate time-left countdown:** run the text through the voice silently in the background and
  measure the real length. Also remember each voice's measured pace for the initial estimate. Agreed in
  principle (the estimate said 1:27 where the real audio was 1:22); Opus-sized.
- **MP3 export:** needs a bundled encoder. Save as sound is WAV only for now.
- **PDF and old .doc support** in the Reader.
- **A "save text to a file" button** in the Reader (it replaced the removed read-later list; Luke saves
  files himself for now).
- **More TTS Enhanced features:** the addon's source is at `/home/claude/reference/TTSEnhanced0.36.1-source.txt`
  in the original session; ask Luke for it if needed. Pauses and the pronunciation dictionary are done.
  Raid-icon stripping was left out as WoW-only.
- Ruled out by Luke: a browser extension, double right-click or double Ctrl+C triggers, LM Studio
  integration (for now), and a tray icon.
