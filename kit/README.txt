Modern Voices for Windows 0.7.7
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
2. Choose where to install (C:\Program Files\Modern Voices for Windows unless you change it; a folder
   named Modern Voices for Windows is made in the folder you choose) and press Install.
3. The Modern Voices window opens (if you can't see it, it's on the taskbar). On "Add or remove
   voices", tick the voices you want. Select one and press Play sample to hear it first. Voices with
   several speakers (VCTK, LibriTTS, ...) list their speakers in name order: click a name to hear
   that speaker (Play sample plays it again); tick a speaker's box to install it. Each ticked speaker
   becomes a voice.
4. Press Apply changes. It downloads the chosen voices from Hugging Face (rhasspy/piper-voices,
   version 1.0.0), prepares them, and tests each one through Windows' speech API, with the progress
   shown at the bottom of the window.

Restart programs that were open to see the new voices. This folder can then be deleted.

Installed outside Program Files, the folder is set up as Program Files is: only administrators can
change its files (every program that speaks loads the engine from it).

Afterwards, from the Start menu (Modern Voices for Windows):
  Modern Voices   the window: Voice manager (installed voices, how they sound, Windows' default
                  voice), Speed and pauses, and Add or remove voices
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
  Dictation speed  adjusts this voice from the speed for every voice (Speed and pauses): 100% = that
                   speed.

Changes are saved as you move a slider and take effect straight away, also in programs that are already
using the voice (from the next thing they say): no restarting, no repair. Preview says the text in the
"Preview says" box with the voice as it now is (Enter starts a new line, which gets the paragraph
pause); Reset puts back 95%, 1.5x, 100%. Each installed speaker of a voice has its own row (the
voice named on the first): Preview, Make default voice and Nickname act on the row selected. A voice's speakers share its sliders (add or remove speakers on "Add or remove
voices"). A program's own volume and speed still adjust from there.

Nickname gives the selected row's voice a name of your own, shown in brackets in the voice's name in place of
its accent: "VCTK p262 (Irish Female)" for "VCTK p262 (British)". Set nickname renames the voice where
Windows lists it (restart programs that were open to see it); with the box empty, it puts the accent back.

Make default voice sets the selected voice as Windows' default speech voice: the one programs speak
with when they don't choose a voice (what Windows' Speech control panel, sapi.cpl, sets; Windows
speech settings opens that). It is per Windows user, for 64- and 32-bit programs alike. Windows 10's
Settings app has its own voice choice, for Microsoft's newer voice system: these voices can't appear
there, and it doesn't affect programs that use these.

Speed and pauses
----------------
On the Speed and pauses tab, for every voice:

  Dictation speed how fast every voice speaks before a program's own rate. Default 125%, about what
                  Windows' speech speed +2 gives (each voice's own Speed adjusts from here).

  Sentence pause  added after a sentence when another follows (never after the last, so short bits
                  of speech, such as a screen reader's, don't gain dead air). Default 500 ms.
  Paragraph pause at a line break, in place of the sentence pause. Default 1200 ms.
  Comma pause     at each , ; or : (default 0: commas as the voice says them). Above 0 the voice says
                  each part of a sentence separately so the pause can go between them, which can make
                  the intonation a little more broken up.

Each pause is silence added to the voice's own, in milliseconds at 100% dictation speed and a program's
speech rate 0. Faster speech uses a smaller share of it and slower a larger share, in step with the
speech: at the default 125%, a 500 ms pause is 400 ms, 321 ms if a program also sets rate +2, and
498 ms at -2. The tab shows each pause's result at the speed set.

Speed is applied inside the engine, so it works in every program, also those that set their own
rate (World of Warcraft, Balabolka, NVDA): theirs adds to it. So does Windows' own speech speed (its
Speech settings, sapi.cpl), for programs that leave the speed to Windows: when that isn't 0, the tab
says so, with a button to set it to 0. Set the speed here instead, and leave Windows' and the
program's at 0 (TTS Enhanced in World of Warcraft: its speed 0) unless you want faster still.

A voice can have its own: in the Voice manager, tick "Modify pause duration for the selected voice" and
set its three pauses. Changes apply straight away, like volume. Preview on this tab says the Voice
manager's line with the voice chosen beside it.

The window opens no larger than the screen; a tab that doesn't fit in it scrolls.

The settings live in the install folder: settings.json (the Speed and pauses tab: "speed_percent"
and "pauses") and voices\<voice>\voice.json
("loudness": peak and boost; "inference": length_scale, higher = slower; "pauses": its own), if you'd
rather edit them.


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
