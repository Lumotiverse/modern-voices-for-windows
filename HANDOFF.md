# Handoff: Modern Voices for Windows

For a fresh Claude session continuing this project. Read this first, then the code it points to.
Last updated at version 0.10.3 (12 Nov 2026).

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

The user-facing documentation is `kit/README.txt`, and it is accurate for 0.10.3. Read it to see what
each feature does.

## Lumoti's standing rules

- **Before any build request, say whether it needs Fable (complicated: new features, engine work,
  multi-part changes) or Opus 5.5 (simple and safe changes to working code)**, so he can switch model
  first. If he's already on the right one, say so and proceed.
- The version number goes in the filename of every build he downloads: `ModernVoicesForWindows0.10.3.zip`.
- Every build must update in place (since 0.9.6): the new kit's Install.cmd run over the installed version
  keeps voices, settings, nicknames and pronunciations, with no uninstall and no re-download. If a build
  changes where or how any of those are stored, it must carry the old ones over. Test-Kit has an 'Update' step.
- The GitHub `README.md` must not mention World of Warcraft; it credits the Spoken Players voicepack engine,
  says Claude wrote all the code, and keeps Lumoti's two lines ("All work ... Claude.ai" / "Code provided by ...").
- Icons: the speech bubble with the speaker (the Reader's exe icon) is the program's default icon (Installed
  apps, Start menu entries); the bubble with the cog (`manager.ico`) is for Modern Voices Config only; the bubble with the wrench
  (`repair.ico`) is Modern Voices Repair's. All three are drawn by `tools/make_icons.py`.
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
  E713 cog; Config's moon is E708), with word labels as a fallback.

**Other**
- Icons must store their small sizes as BMP, not PNG. Windows ignored the PNG small sizes, so the taskbar
  and Start menu showed no icon. `make_icons.py` does this.
- Piper's output varies slightly between runs, so tests compare with tolerances, and gap tests set
  noise to (almost) 0.

## Current state (0.10.3)

Everything above works and is tested on the CI Windows machine. Lumoti has been using it and sending UI
tweaks one batch at a time; the last batch (0.9.4) is done and delivered: the moon moved and greyed in
both windows, and the clipped last word fixed in the engine (`with_end_mark` in `text.rs` adds a full
stop to an unmarked sentence end; `TAIL_MS` of silence is written after a Speak call's last word).
0.9.5 then: the Reader reads the
clipboard's "HTML Format" bytes itself as UTF-8 (`CopiedHtml`; .NET's `Clipboard.GetText(Html)` decodes
them in the ANSI code page, which turned ' and ... into mojibake); the engine says sums of money aloud
(`money` in `text.rs`, applied after the dictionary); the Reader's moon became a cog that opens Config,
and the Reader follows Config's moon by re-reading the registry `Theme` value on its 500 ms tick.
Lumoti confirmed 0.9.5 works. 0.9.6: update in place (`$updating` in Install.ps1's main flow,
`Close-OurPrograms`, `Remove-Leftovers`) and the default icon (`Get-AppIcon`).
0.9.7: Start menu entries renamed Modern Voices Repair (wrench icon) and Modern Voices Test; the
Reader asks before a second instance (`AnotherWanted` in `Program`) and no longer keeps the box's text
between runs; `Loading program...` is said by Install.cmd and Install.ps1; the README was rewritten.
Default pronunciation rules: `$DefaultRuleSets` / `Add-DefaultRules` in Install.ps1 add each named set
once (registry `RuleSets`), never overwriting or re-adding. Sets: `smileys1`, `chat1` (rn, np, wp, lf
installed switched off, at Lumoti's say) and `trade1`, from his old TTS Enhanced addon. He said to leave
the game-instance abbreviations out. A changed or extra default rule needs a NEW set name.
0.9.8: the Reader's undo and redo (`NoteChange`, `StepBack` in `MainForm`; ten steps; Ctrl+Z, Ctrl+Y,
the two buttons left of Clear); the window's least width is now 920.
0.9.9: the Reader's button row is start, play, stop, a wider gap, Open a file, Read clipboard, a
magnifying glass (E721: read an area of the screen), Save as audio.
0.10.0, in the Reader: the voice list is owner-drawn with a dot per row (filled: Windows' default voice);
`DotList` subclasses the dropped list's window to take clicks on the dots (the list stays open) and to
show the tooltip. A nickname box and tick beside Voice run `installer/Nickname.ps1` elevated (UAC), which
renames the token and writes the Nicknames key without needing the catalogue. The row under the buttons
is a Panel placed by `AlignLine` against the buttons above. Read clipboard is now Paste and read. The
wrench is double-ended and is also Test's and Uninstall's Start menu icon.
0.10.1: on Lumoti's machine the dots took no clicks and showed no tooltip in 0.10.0 (cause not found:
the `DotList` hook passes the build's simulated click). Now there are two ways in: the hook, and
`VoiceChosen` (a row chosen with the pointer on its dot counts as a click on the dot; the list is
dropped again). The tooltip is a `TipWindow` (own topmost no-activate window) driven by a timer that
watches the pointer, not by the hook. `RefreshVoices` makes a new SpeechSynthesizer (the old one keeps
the voice list it was made with), after a nickname and when the window is activated and `VoicesNow`
differs. Under the buttons: the bar is as wide as the first two buttons; "Instantly read copy" and
"Don't read links" (it still leaves out code and symbols) start where the buttons above them do.
0.10.2: Lumoti confirmed the dots work. A new SpeechSynthesizer still gave the old voice names after a
rename (System.Speech keeps them for the life of the process), so `FillVoices` reads each voice's name
from the registry (`NameNow`) and keeps System.Speech's own as `VoiceChoice.Spoken` for `SelectVoice`;
the chosen voice is saved by token id. A volume slider (the Reader's own `synth.Volume`, saved as
`State.Volume`) sits between Save as audio and undo; the window's least width is 950.
0.10.3: Lumoti confirmed the list updates after a rename. The Reader's status line fades out five
seconds after a message (`Status`, `FadeStatus`); Config's Apply changes is greyed out while the ticks
match what is installed (`Update-UiApply`, on a 300 ms timer).
The owner (GitHub: Lumotiverse) is to be called Lumoti in anything public, never by any other name.
Not yet confirmed by Lumoti:

- the 0.9.7 and 0.9.8 items above on his machine;

- updating 0.9.5 to 0.9.6 on his machine without uninstalling;

- curly quotes and ellipses copied from his browser now read cleanly (CI has no real clipboard);
- the cog opening Config (UAC prompt, then the window) on his machine;

- the last word now heard whole, both in playback and in the Reader's saved WAV;
- the icon now showing in the taskbar and Start menu;
- the Reader's progress row lining up with the buttons on his screen;
- pasting from real browsers keeping paragraphs and bullets.

## Ideas discussed but not built (only when Lumoti asks)

- **Accurate time-left countdown:** run the text through the voice silently in the background and
  measure the real length. Also remember each voice's measured pace for the initial estimate. Agreed in
  principle (the estimate said 1:27 where the real audio was 1:22); Opus-sized.
- **MP3 export:** needs a bundled encoder. Save as sound is WAV only for now.
- **PDF and old .doc support** in the Reader.
- **A "save text to a file" button** in the Reader (it replaced the removed read-later list; Lumoti saves
  files himself for now).
- **More TTS Enhanced features:** the addon's source is at `/home/claude/reference/TTSEnhanced0.36.1-source.txt`
  in the original session; ask Lumoti for it if needed. Pauses and the pronunciation dictionary are done.
  Raid-icon stripping was left out as WoW-only.
- Ruled out by Lumoti: a browser extension, double right-click or double Ctrl+C triggers, LM Studio
  integration (for now), and a tray icon.
