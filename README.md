# Modern Voices for Windows

Modern, natural-sounding voices for any Windows program that can speak, running entirely on your own
PC. Free, offline, and open source.

Windows has had a standard way for programs to speak since the early 2000s (called SAPI 5). Screen
readers, e-book readers, games and many other programs use it. The voices Windows supplies for it
are old and robotic, and the newer "natural" voices in Narrator can't be used by those programs.
Modern Voices for Windows adds modern neural voices that they can use: 64-bit and 32-bit programs
alike. English voices are offered first, and voices in over 25 other European languages can be
added (see [Other languages](#other-languages)).

It comes with its own Reader, which reads aloud whatever you paste or copy, documents (Word, PDF, web
pages and plain text) and text on the screen, and can save what it reads as an MP3 or WAV file.

Modern Voices for Windows is free to use and open source.

**Not every voice is as good as the best.** The Piper voices vary a lot in quality. The best are
excellent: natural-sounding neural voices that are easy to listen to for hours. Others sound robotic
and unnatural, mostly down to how each one was trained (how many hours of recordings it learned from,
and how clear they were). Modern Voices can adjust a voice's loudness, speed and pauses, but it can't
turn a robotic voice into a natural one. So listen before you choose: in Modern Voices Config, on
**Add or remove voices**, select a voice and press **Play sample** (for a voice with several speakers,
click each speaker's name), and install only the ones you like.

**Better voices for Balabolka, and other text to speech programs.** If you are looking for more natural
voices for [Balabolka](https://www.cross-plus-a.com/balabolka.htm), or any other reader that lists
Windows' SAPI 5 voices, these appear in its voice list as soon as they are installed, in place of the
old Microsoft David, Zira and Hazel.

## Who wrote the code

All work (including uploading to GitHub) was done by Claude.ai
Code provided by Fable5.2, assisted by Opus5.5

To be plain about it: every line of code in this repository beyond the engine it is based on was
written by Claude, Anthropic's AI model, which is why Claude appears as the main contributor. The
person behind the project (Lumoti) is not a programmer. He decided what the program should do, tried
every build on his own computer, and said what was wrong with it; Claude wrote, built and tested
the code.

The user experience was largely directed by Lumoti, a graphic designer with some experience of UX and
UI design: what each program does, how its windows are laid out, what the buttons and icons are and
where they sit. Claude carried those decisions out, and suggested some of its own.

That is only possible because of the programmers named below, and many more besides, who learned
this craft properly and gave their work away. An AI model can assemble a program like this because
people wrote the engines, the tools, the documentation and the answers it learned from. Respect is
due to them. If you are one of them and see something here done badly, issues and pull requests
are welcome.

## How it was made, and what to expect

Lumoti paid £18 for one month of Claude Pro, and used all of the Fable model's allowance he could within
that month to make the program and get it to this point. He can't afford to keep paying for a Claude
subscription, so the project can't be maintained continuously.

It appears to be stable and working, and hopefully that will stay the case. The only real bug found
so far: sometimes the first start of a program after installing hangs for a while before its window
appears (Windows checking a new program; later starts are quick).

**Testing:** all testing was done on Windows 10, apart from one older version that was tried on
Windows 11, where everything worked as normal. Each build is also tested automatically on a clean
Windows Server machine (see [Open and transparent](#open-and-transparent)).

**Other programs:** the voices work in any program that uses Windows' speech voices (SAPI 5). The
program has its own Reader, but the voices also show up automatically in programs such as
[Balabolka](https://www.cross-plus-a.com/balabolka.htm). That is largely untested and may run into
issues, though the little testing done in Balabolka has worked exactly as intended, including
Balabolka's own pitch control.

## Standing on other people's work

This program is a thin layer over years of work by people who actually know how to do this.

- **The engine is based on the engine of the
  [Spoken Players voicepack](https://github.com/f-rolf/spoken-players-voicepack) by f-rolf.** That
  project solved the hard problem: a real Windows speech engine, in Rust, that runs Piper voices
  and reports words and bookmarks as Windows expects. Modern Voices for Windows exists because that
  work was published under a free licence. If you give this repository a star, please give that one
  a star as well: it is where the hard part was done.
- **Piper** (Michael Hansen and contributors) for the voices and the way of making them.
- **eSpeak NG** (Jonathan Duddington, Reece H. Dunn and contributors) for pronunciation.
- **sherpa-onnx** (the k2-fsa project) and **ONNX Runtime** (Microsoft) for running the voices.
- **Claude** (Anthropic) translated the parts written in other languages: each voice's preview text
  and the installer's note.
- **[PdfPig](https://github.com/UglyToad/PdfPig)** (UglyToad and contributors) for reading the
  text of PDFs in the Reader.
- **[LAME](https://lame.sourceforge.io)** (the LAME Project) for saving MP3 files; the Windows builds
  are [RareWares'](https://www.rarewares.org/mp3-lame-libraries.php), as kept by the
  [NAudio.Lame](https://github.com/Corey-M/NAudio.Lame) project.
- Everyone who recorded their voice for a research collection or audiobook, and allowed it to be used for free in software such as this.

The full list, with licences, is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## What you get

| Program | What it is for |
|---|---|
| **Modern Voices Config** | Choose and download voices, set how each one sounds (volume, speed, pauses), teach it how to pronounce names and words, and pick Windows' default voice. Give a voice a nickname or a gender (Windows is told both), and sort and rearrange the list of voices to suit you. |
| **Modern Voices Reader** | Reads text aloud: pasted or copied text, documents (Word, PDF, rich text, web pages, plain text), and text on the screen. It can save the reading as an MP3 or WAV file. See [The Reader](#the-reader). |
| **Modern Voices Repair** | Registers the voices with Windows again, if a large Windows update makes them vanish from programs. |
| **Modern Voices Test** | Speaks with each voice and shows what the engine reports, to find out whether a problem is with the voices or with the program using them. |

**It is a simple install.** No manual registry editing or locating voice packs online. Just install,
select the voices you want to add from the list, and everything should work.

To install, download `ModernVoicesForWindows<version>.zip` from
[Releases](../../releases), unzip it and run `Install.cmd`. The installer's first window asks where to
install and which language's voices you want to choose from. Modern Voices Config then opens on
**Add or remove voices** with a short note saying where to start (in the language you chose too):
tick the voices you want and press **Apply changes**. To update, do the same with the new version: your voices and settings stay, and
nothing is asked. `README.txt` in the zip is the user's guide.

## The Reader

Modern Voices Reader is a small window for reading text aloud with any of the installed voices
(or any other Windows voice).

- **Paste and read**, or tick **Instantly read copy** to have it read whatever you copy, in any
  program. **Don't read links** skips web addresses, code and stray symbols.
- **Open a file:** plain text, Markdown, Word documents (.docx), **PDFs**, rich text (.rtf) and web
  pages (.html). Dropping a file on the window opens it too. A file opened again carries on from
  where you stopped.
- **The magnifying glass** reads text on the screen: drag a box around it (it uses Windows' own text
  recognition).
- **Save as audio:** the text as an **MP3** (about half a megabyte a minute) or a **WAV** file, made
  as fast as the voice can, without playing it.
- Pick a voice from the list (or step through the voices with the triangles beside it), give it a nickname, set it as Windows' default voice, and set the
  speed and volume. Undo and redo for the text, and the text can be kept for next time.

**A note on PDFs.** A PDF doesn't store sentences and paragraphs, only where each letter sits on a
page, so the Reader works out the text from that. It joins lines into paragraphs, mends words broken
at a line's end, reads each item of a list without its bullet, and leaves out page numbers and
running headers. **Opening a PDF can load with some inaccuracies in the text:** a paragraph may be
split in two or joined to the next, and tables or pages in columns can come out in an odd order. A
scanned PDF is a picture of the pages and has no text to open (show it on the screen and use the
magnifying glass instead), and a PDF locked with a password can't be opened.

## Other languages

Voices in over 25 European languages (German, French, Spanish, Italian, Dutch, Polish, Russian and
more) can be added from **Modern Voices Config > Add or remove voices > Additional languages**. Tick a
language and its voices appear in the list to install, like the English ones. Windows is told each
voice's language, so programs that choose voices by language find them.

You can also choose the language when installing: the installer's first window has a list (English,
British or American English, or one of the other languages). With another language, only that
language's voices are listed and the English ones are left out; English can be ticked back in
**Additional languages** at any time.

Some things to know:

- **The program itself is in English.** Its windows, buttons and help stay in English whatever voices
  you install.
- **There may be mistakes in the other languages.** Lumoti only speaks English, so the parts written
  in other languages (the preview text each voice reads in Config, the installer's note) were translated by Claude and
  haven't been checked by a native speaker. Corrections are welcome as issues or pull requests.
- **Other languages have been tested far less** than English: a German voice is installed, spoken and
  checked by the automatic test, and the rest are trusted to work the same way.
- **No ready-made pronunciation rules for other languages.** English comes with a list (chat
  abbreviations, smileys); each other language starts with an empty list of its own, which you can
  fill in on the Pronunciation tab. A voice only uses the rules for its own language.
- **Languages written right to left or without spaces between words** (Arabic, Persian, Hebrew,
  Chinese) are not offered, as the engine's handling of sentences and pauses doesn't fit them.

### Sharing your pronunciation rules

Your rules are kept in plain text files, one per language, in:

```
C:\ProgramData\Modern Voices for Windows\
```

`dictionary.txt` holds the English rules, and `dictionary-de.txt` (German), `dictionary-fr.txt`
(French) and so on hold each other language's. To open the folder, paste the path above into File
Explorer's address bar (the ProgramData folder is hidden, so you won't see it by browsing). You can copy
a file to share it, for example by attaching it to an issue here, so a list for a language can be
added to the program for everyone. To use someone else's list, close Modern Voices Config, put their
file in that folder (keep a copy of yours first), and open Config again.

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
| `installer/` | `Install.ps1` (install, update, repair, and adding or removing voices for Modern Voices Config), `VoiceConvert.ps1` (Piper model to engine folder; voice settings), `Nickname.ps1`, `Test.ps1`, `Uninstall.ps1` |
| `config/` | Modern Voices Config (C#, Windows Forms): the settings window |
| `reader/` | Modern Voices Reader (C#, Windows Forms) |
| `kit/` | what goes at the top of the packaged kit (`Install.cmd`, `README.txt`) |
| `third-party/reader/` | the Reader's two outside libraries, unchanged: PdfPig (reads PDFs) and LAME (makes MP3s); `Package.ps1` copies them into the kit after checking each file's SHA-256 |
| `tools/` | `Package.ps1` (sign and package), `Get-SherpaX86.ps1` (32-bit sherpa-onnx), `Test-Kit.ps1` (end-to-end test), `make_icons.py` |
| `.github/workflows/build.yml` | builds 64- and 32-bit engines, signs, packages `ModernVoicesForWindows<version>.zip`, and runs the end-to-end test |
| `HANDOFF.md` | notes for whoever (or whichever AI) picks the work up next |

Push to `main` (or run the workflow by hand) to build. The version is `engine/Cargo.toml`'s, and
`installer/Install.ps1` must say the same.

## Licence

GPL-3.0-or-later (eSpeak NG, built into the engine, is GPL-3.0). See [LICENSE](LICENSE) and
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The voices are downloaded at install time and
keep their own licences. The Reader's two libraries, which lie beside it as separate files, keep
theirs too: PdfPig is Apache-2.0 and LAME is LGPL (a different build of LAME can be put in its
place).
