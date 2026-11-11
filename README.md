# Modern Voices for Windows

Offline neural voices (Piper, English for now) as ordinary Windows speech voices (SAPI 5), for any
program that uses Windows' installed voices but can't use Narrator's natural voices. 64-bit and
32-bit programs.

## Layout

| Path | What it is |
|---|---|
| `engine/` | the SAPI 5 engine (Rust, sherpa-onnx built in): based on the [Spoken Players voicepack](https://github.com/f-rolf/spoken-players-voicepack)'s engine, with its own class ID and the events programs follow speech by (bookmarks, sentence and word boundaries) |
| `engine/espeak-ng-data/` | espeak-ng's data, read at run time |
| `installer/` | `Install.ps1` (install, the Modern Voices window, repair), `VoiceConvert.ps1` (Piper model → engine folder; voice settings), `Speak.ps1` (Preview), `Test.ps1`, `Uninstall.ps1` |
| `kit/` | what goes at the top of the packaged kit (`Install.cmd`, `README.txt`) |
| `tools/` | `Package.ps1` (sign and package), `Get-SherpaX86.ps1` (32-bit sherpa-onnx), `Test-Kit.ps1` (end-to-end test) |
| `.github/workflows/build.yml` | builds 64- and 32-bit engines, signs, packages `ModernVoicesForWindows<version>.zip`, and runs the end-to-end test |

## Building

Push to `main` (or run the workflow by hand). The kit is the run's `ModernVoicesForWindows<version>`
artifact. The version is `engine/Cargo.toml`'s, and `installer/Install.ps1` must say the same.

## Licence

GPL-3.0-or-later (espeak-ng, built into the engine, is GPL-3.0). See `LICENSE` and
`THIRD-PARTY-NOTICES.md`. The voices are downloaded at install time and keep their own licences.
