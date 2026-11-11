Modern Voices for Windows 0.5.1
===============================

Modern, offline neural voices for Windows' speech API (SAPI 5): for programs that speak with
Windows' installed voices but can't use Narrator's natural voices. The voices are Piper voices
(English for now), each running on your own PC: no internet needed once installed, and nothing
sent anywhere.

They show up in Windows' Speech settings (Control Panel > Speech) and in programs that use
Windows' installed voices: text readers like Balabolka, screen readers in their SAPI 5 mode (NVDA,
JAWS), games and apps with text to speech (World of Warcraft among them), Chromium-based browsers'
speech, and anything built on System.Speech or SAPI. 64-bit and 32-bit programs both.

They do not appear in Narrator, Edge's Read Aloud or other apps that use Windows' newer voice system,
which has no way in for voices from anyone but Microsoft.


Install
-------
1. Double-click Install.cmd and say yes to the administrator prompt.
2. Tick the voices you want. Select one and press Play sample to hear it first. Voices with several
   speakers (VCTK, LibriTTS, ...) let you pick which speakers to install; each becomes a voice.
3. Press Install. It downloads the chosen voices from Hugging Face (rhasspy/piper-voices, version
   1.0.0), prepares them, and tests each one through Windows' speech API.

Restart programs that were open to see the new voices. This folder can then be deleted.

Afterwards, from the Start menu (Modern Voices for Windows):
  Choose voices   add or remove voices (the same list, with what's installed ticked)
  Test voices     hear each voice, with each word shown as the engine reports it
  Uninstall       removes everything (also in Windows' Installed apps)


What programs get
-----------------
* Speech that starts quickly: a sentence at a time, each about a tenth of its length to make.
* Rate, pitch and volume as the program sets them.
* Word and sentence positions, for programs that highlight the word being said or follow along
  (screen readers, reading tools). Piper doesn't say when each word starts, so that's estimated
  across each sentence: close, not exact.
* Bookmarks, for programs that mark places in their text.


Loudness
--------
Each voice is turned up inside its model, as Piper's own program does (it plays the model's output
louder than the model makes it):

  peak    each sentence is scaled so its loudest moment is this. 0.95 = just under full volume.
          0 = off (the model as it was), up to 1.
  boost   lifts the quieter parts more than the loud ones, so the voice is louder overall without
          clipping: 1 = none, 1.5 = mild (the default), 2 = strong, 3 = very strong. Up to 5.

To change it: edit "loudness" in
  C:\Program Files\Modern Voices for Windows\voices\<voice>\voice.json
then run Repair.cmd in C:\Program Files\Modern Voices for Windows. Each voice has its own. A
program's own volume still turns them down from there.

Speaking speed: "length_scale" in the same file (higher = slower), then restart the program.


Good to know
------------
* Language: British voices are listed as British English, American as US English. A program that
  only shows US English voices won't list the British ones; tick "Also list British voices as US
  English" in Choose voices for it.
* Some programs take longer to start the more voices Windows has (World of Warcraft looks at every
  voice several times over when it loads). Install the ones you use.
* The engine is signed by the build that made it, and the installer has Windows trust that signature:
  programs that only load signed speech engines (World of Warcraft) need that. It can sign nothing
  else: its private key was deleted straight after signing. Uninstall removes the trust.
* Memory: each voice in use takes about 110 MB in the program using it, loaded on its first line.
* Something not working: %LOCALAPPDATA%\ModernVoices\engine.log. With a file named verbose in
  that folder, the log has every call and every line spoken too.


Credits and licences
--------------------
Engine: built on the Spoken Players voicepack's engine (https://github.com/f-rolf/spoken-players-voicepack),
with sherpa-onnx, ONNX Runtime and espeak-ng built in. GPL-3.0-or-later: see LICENSE and
THIRD-PARTY-NOTICES.md.

Voices: Piper (https://huggingface.co/rhasspy/piper-voices). Each voice has its own licence and
dataset, given in its MODEL_CARD there. Many are CC BY or CC BY-SA (credit the dataset; CC BY-SA
voices keep that licence when changed, which turning them up here does).
