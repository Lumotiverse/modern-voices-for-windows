# Modern Voices for Windows

Modern, natural-sounding voices for any Windows program that can speak, running entirely on your own
PC. Free, offline, and open source.

Windows has had a standard way for programs to speak since the early 2000s (called SAPI 5). Screen
readers, e-book readers, games and many other programs use it. The voices Windows supplies for it
are old and robotic, and the newer "natural" voices in Narrator can't be used by those programs.
Modern Voices for Windows adds modern neural voices that they can use: 64-bit and 32-bit programs
alike. English voices for now.

## Who wrote the code

All work (including uploading to GitHub) was done by Claude.ai
Code provided by Fable5.2, assisted by Opus5.5

To be plain about it: every line of code in this repository beyond the engine it is based on was
written by Claude, Anthropic's AI model, which is why Claude appears as the main contributor. The
person behind the project (Lumoti) is not a programmer. He decided what the program should do, tried
every build on his own computer, and said what was wrong with it; Claude wrote, built and tested
the code. The user experience was refined by Lumoti. (Currently tested on Windows 10 only)

That is only possible because of the programmers named below, and many more besides, who learned
this craft properly and gave their work away. An AI model can assemble a program like this because
people wrote the engines, the tools, the documentation and the answers it learned from. Respect is
due to them. If you are one of them and see something here done badly, issues and pull requests
are welcome.

## Standing on other people's work

This program is a thin layer over years of work by people who actually know how to do this.

- **The engine is based on the engine of the
  [Spoken Players voicepack](https://github.com/f-rolf/spoken-players-voicepack) by f-rolf.** That
  project solved the hard problem: a real Windows speech engine, in Rust, that runs Piper voices
  and reports words and bookmarks as Windows expects. Modern Voices for Windows exists because that
  work was published under a free licence. If you find this program useful, that repository
  deserves the star.
- **Piper** (Michael Hansen and contributors) for the voices and the way of making them.
- **eSpeak NG** (Jonathan Duddington, Reece H. Dunn and contributors) for pronunciation.
- **sherpa-onnx** (the k2-fsa project) and **ONNX Runtime** (Microsoft) for running the voices.
- Everyone who recorded their voice for a research collection or an audiobook and let it be used.

The full list, with licences, is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## What you get

| Program | What it is for |
|---|---|
| **Modern Voices Config** | Choose and download voices, set how each one sounds (volume, speed, pauses), teach it how to pronounce names and words, and pick Windows' default voice. |
| **Modern Voices Reader** | Reads text aloud: pasted or copied text, documents, web pages you copy, and text on the screen. It can save the reading as a sound file. |
| **Modern Voices Repair** | Registers the voices with Windows again, if a large Windows update makes them vanish from programs. |
| **Modern Voices Test** | Speaks with each voice and shows what the engine reports, to find out whether a problem is with the voices or with the program using them. |

To install, download `ModernVoicesForWindows<version>.zip` from
[Releases](../../releases), unzip it and run `Install.cmd`. To update, do the same with the new
version: your voices and settings stay. `README.txt` in the zip is the user's guide.

## How it works

Think of it as an adapter between Windows and a modern AI voice.

1. **A program asks Windows to speak.** It says "here is some text, say it with this voice".
   Windows hands the text to whichever *engine* owns that voice.
2. **The engine tidies the text.** Ours is one file, `modern_voices.dll`. It splits the text into
   sentences and clauses, applies your pronunciation rules, and reads things like sums of money
   the way a person would.
3. **Letters become sounds.** [eSpeak NG](https://github.com/espeak-ng/espeak-ng) turns spelling into
   phonemes, the individual sounds of speech, so that "though" and "tough" come out differently.
4. **Sounds become audio.** The phonemes go into the voice: a neural network, in a file of about
   60 MB, that produces the sound wave. [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) and
   [ONNX Runtime](https://github.com/microsoft/onnxruntime) run it.
5. **The audio goes back to Windows**, with your speed, pauses and volume applied, along with which
   word is being spoken, so that programs can follow along.

All of this happens on your computer. Nothing you have read aloud is sent anywhere. The program only
goes online to fetch the list of available voices and to download the ones you choose.

## Where the voices come from

The voices are [Piper](https://github.com/rhasspy/piper) voices, made by Michael Hansen and the
Rhasspy project's contributors. Each one was made by training a neural network on many hours of a
person reading sentences aloud, until it could speak text it had never seen in that person's voice.
The recordings come from university research collections, public-domain audiobooks and volunteers;
each voice's page in the [Piper voice collection](https://huggingface.co/rhasspy/piper-voices) says
whose it is and under what licence. Modern Voices Config downloads the voices you tick from that
collection (version 1.0.0). They are not part of this project and keep their own licences.

## Open and transparent

- **Everything is here.** The complete source of what you install is in this repository, under
  the GPL-3.0-or-later licence. There is no closed part, no account, no telemetry and no adverts.
- **The download is built in the open.** Each release is built from this source by GitHub Actions
  (`.github/workflows/build.yml`), which also runs an end-to-end test on a clean Windows machine:
  install, speak with each voice, update, repair, uninstall. The build logs are public.
- **The signature is honest about what it is.** The engine is signed with a certificate made
  during each build, whose private key is deleted straight after. That lets programs which insist
  on signed engines load it; it is not a statement from a certificate authority about who we are.
  Uninstalling removes the trust.
- **It only touches what it says.** The program folder, its Start menu folder, its registry entries
  for the voices, and the Reader's settings. Uninstall removes all of it.

## For developers

| Path | What it is |
|---|---|
| `engine/` | the SAPI 5 engine (Rust, sherpa-onnx built in), with its own class ID: text handling, pauses, pronunciation rules, and the events programs follow speech by (bookmarks, sentence and word boundaries) |
| `engine/espeak-ng-data/` | eSpeak NG's data, read at run time |
| `installer/` | `Install.ps1` (install, update, the Modern Voices Config window, repair), `VoiceConvert.ps1` (Piper model to engine folder; voice settings), `Speak.ps1` (Preview), `Test.ps1`, `Uninstall.ps1` |
| `reader/` | Modern Voices Reader (C#, Windows Forms) |
| `kit/` | what goes at the top of the packaged kit (`Install.cmd`, `README.txt`) |
| `tools/` | `Package.ps1` (sign and package), `Get-SherpaX86.ps1` (32-bit sherpa-onnx), `Test-Kit.ps1` (end-to-end test), `make_icons.py` |
| `.github/workflows/build.yml` | builds 64- and 32-bit engines, signs, packages `ModernVoicesForWindows<version>.zip`, and runs the end-to-end test |
| `HANDOFF.md` | notes for whoever (or whichever AI) picks the work up next |

Push to `main` (or run the workflow by hand) to build. The version is `engine/Cargo.toml`'s, and
`installer/Install.ps1` must say the same.

## Licence

GPL-3.0-or-later (eSpeak NG, built into the engine, is GPL-3.0). See [LICENSE](LICENSE) and
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The voices are downloaded at install time and
keep their own licences.
