Modern Voices for Windows 0.6.1
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
2. The Modern Voices window opens (if you can't see it, it's on the taskbar). On "Add or remove
   voices", tick the voices you want. Select one and press Play sample to hear it first. Voices with
   several speakers (VCTK, LibriTTS, ...) list their speakers to tick; each becomes a voice.
3. Press Apply changes. It downloads the chosen voices from Hugging Face (rhasspy/piper-voices,
   version 1.0.0), prepares them, and tests each one through Windows' speech API, with the progress
   shown at the bottom of the window.

Restart programs that were open to see the new voices. This folder can then be deleted.

Afterwards, from the Start menu (Modern Voices for Windows):
  Modern Voices   the window: Voice manager (installed voices, how they sound, Windows' default
                  voice) and Add or remove voices
  Test voices     hear each voice, with each word shown as the engine reports it
  Repair          registers everything again (if a voice went missing from programs)
  Uninstall       removes everything (also in Windows' Installed apps)


How each voice sounds
---------------------
In the Modern Voices window, on "Voice manager", select a voice:

  Volume   how loud each sentence's loudest moment is: 95% = just under full volume. 0 = as Piper
           made it (quieter: the model's own level).
  Boost    lifts the quieter parts more than the loud ones, so the voice sounds louder overall without
           clipping. None, 1.5x (the default), up to 4x; higher sounds more compressed, like radio.
  Speed    100% is the voice's own pace.

Changes are saved as you move a slider and take effect straight away, also in programs that are already
using the voice (from the next thing they say): no restarting, no repair. Preview says a line with the
voice as it now is; Reset puts back 95%, 1.5x, 100%. A voice's speakers share its settings. A program's
own volume and speed still adjust from there.

Make default voice sets the selected voice as Windows' default speech voice: the one programs speak
with when they don't choose a voice (what Windows' Speech control panel, sapi.cpl, sets; Windows
speech settings opens that). It is per Windows user, for 64- and 32-bit programs alike. Windows 10's
Settings app has its own voice choice, for Microsoft's newer voice system: these voices can't appear
there, and it doesn't affect programs that use these.

The settings live in C:\Program Files\Modern Voices for Windows\voices\<voice>\voice.json
("loudness": peak and boost; "inference": length_scale, higher = slower), if you'd rather edit them.


Disk space
----------
Each voice is one model of 60 to 110 MB (the window shows each one's size and the total ticked).
Speakers of one voice share its model: more speakers take no more space. Two qualities of one voice
(Lessac and Lessac HQ, say) are two separate models; the window warns when both are ticked.


What programs get
-----------------
* Speech that starts quickly: a sentence at a time, each about a tenth of its length to make.
* Rate, pitch and volume as the program sets them.
* Word and sentence positions, for programs that highlight the word being said or follow along
  (screen readers, reading tools). Piper doesn't say when each word starts, so that's estimated
  across each sentence: close, not exact.
* Bookmarks, for programs that mark places in their text.


Good to know
------------
* Language: British voices are listed as British English, American as US English. A program that
  only shows US English voices won't list the British ones; tick "Also list British voices as US
  English" on the window's "Add or remove voices" tab for it.
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
dataset, given in its MODEL_CARD there. Many are CC BY or CC BY-SA (credit the dataset). The models
are installed as published; the loudness is applied by the engine as it speaks.
