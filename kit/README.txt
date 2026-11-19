Modern Voices for Windows 0.18.12
===============================

Modern, offline neural voices for Windows' speech API (SAPI 5): for programs that speak with
Windows' installed voices but can't use Narrator's natural voices. The voices are Piper voices
(English for now), each running on your own PC: no internet needed once installed, and nothing
sent anywhere.

Not every voice is as good as the best. The Piper voices vary a lot in quality: the best are
excellent, natural-sounding neural voices; others sound robotic and unnatural, mostly down to how
each one was trained. The settings here change a voice's loudness, speed and pauses, but they can't
make a robotic voice natural. So listen before you choose: on "Add or remove voices", select a voice
and press Play sample (for a voice with several speakers, click each speaker's name), and install
only the ones you like.

They show up in Windows' Speech settings (Control Panel > Speech) and in programs that use
Windows' installed voices: text readers like Balabolka, screen readers in their SAPI 5 mode (NVDA,
JAWS), games and apps with text to speech, Chromium-based browsers'
speech, and anything built on System.Speech or SAPI. 64-bit and 32-bit programs both.

They do not appear in Narrator, Edge's Read Aloud or other apps that use Windows' newer voice system,
which has no way in for voices from anyone but Microsoft.


Install
-------
1. Double-click Install.cmd and say yes to the administrator prompt.
2. Choose where to install (C:\Program Files\Modern Voices for Windows unless you change it; a folder
   named Modern Voices for Windows is made in the folder you choose), and the language of the voices
   you want to choose from: English, English (British), English (American), or one of the other
   languages there are voices for. Then press Install. With British or American, "Show" (step 3)
   starts on that; with another language, only that language's voices are listed, not the English
   ones, and a note says what to expect (in that language, with the English under it). Nothing is
   installed by the choice itself, and it can be changed later ("Additional languages...", step 3).
   Updating an installed version does not ask.
3. The Modern Voices Config window opens (if you can't see it, it's on the taskbar). The first time,
   a "Powered by" window over it names what the program is made from: click it to close it. While no
   voice is installed, a note then says where to start (OK or its X closes it), in the language
   chosen when installing too. On "Add or remove
   voices", tick the voices you want. Click a voice's name to hear its sample first (some voices
   sound much more natural than others, so it is worth it); Play sample plays it again (press it
   again, "Stop sample" meanwhile, to stop). Voices with
   several speakers (VCTK, LibriTTS, ...) play nothing when their name is clicked: they list their
   speakers in name order below, and clicking a speaker's name plays that speaker; tick a speaker's
   box to install it. Each ticked speaker becomes a voice. Ticking such a voice's own box ticks all
   its speakers (unticking it, none). With more than two speakers it asks first, as each speaker
   becomes a voice of its own: Select all, or Choose (recommended) to tick the ones you want below. A voice with one speaker lists that one: its box is the voice's own tick.
   "Only show selected speakers" (beside Play sample, off each time the window opens) lists only
   the ticked speakers of the voice selected, to find the ones installed.
   "Show" above the list narrows it (All voices, English only, American only, British only);
   voices not shown keep their ticks, and it opens on what it was last on.
   "Additional languages..." beside it lists English and the other languages there are voices for
   (German, Spanish, French...: European languages written left to right). Tick a language and its
   voices join the list, with a choice of their own in "Show"; untick one (English too) and they
   leave it. A language with a voice ticked stays listed, and with nothing ticked English is listed.
   The program's own words stay in English.
4. Press Apply changes. It downloads the chosen voices from Hugging Face (rhasspy/piper-voices,
   version 1.0.0), prepares them, and tests each one through Windows' speech API, with the progress
   shown at the bottom of the window. Done goes back to the Voice manager.

Restart programs that were open to see the new voices. This folder can then be deleted.

Updating: run Install.cmd from the new version's folder, with the old version still installed. The
program is replaced where it is; the voices (nothing is downloaded again), their sound settings,
speed and pauses, nicknames and pronunciation rules all stay (rules you added, changed or removed
are left exactly as they are). An open Reader or
Modern Voices Config is closed first; other programs that are speaking need closing by hand.

Installed outside Program Files, the folder is set up as Program Files is: only administrators can
change its files (every program that speaks loads the engine from it).

Afterwards, from the Start menu (Modern Voices for Windows):
  Modern Voices Config
                  the window: Voice manager (installed voices, how they sound, Windows'
                  default voice), Speed and pauses, Pronunciation, and Add or remove voices.
                  A program of its own. It opens without asking for permission and without
                  the internet; Windows asks for permission once, the first time you set a
                  nickname or press Apply changes (not again until you close the window)
  Modern Voices Reader
                  reads text aloud with the voices: pasted or copied text, files, text on the
                  screen (see "The Reader" below)
  Modern Voices Repair
                  registers everything again (if a voice went missing from programs, as can
                  happen after a large Windows update)
  Modern Voices Test
                  hear each voice, with each word shown as the engine reports it (to find out
                  why a program is not speaking: whether it is the voices or the program)
  Uninstall       removes everything (also in Windows' Installed apps)


How each voice sounds
---------------------
In the Modern Voices Config window, on "Voice manager", select a voice:

  Amplify  makes the voice louder: 10% to MAX (the setting a voice starts with) raise its loudest
           moments to that level (MAX = as loud as speech goes), every line as loud as the next.
           Off (default) leaves it at its own level, as Piper made it. It moves in steps of 10%.
  Boost    lifts the quieter parts more than the loud ones, so the voice sounds louder overall without
           clipping. None, 1.5x (the default), up to 2.5x; higher sounds more compressed, like radio.
  Dictation speed  adjusts this voice from the speed for every voice (Speed and pauses): 100% = that
                   speed.

Output volume (the slider with the speaker, beside Windows' default voice) is for every voice, in
every program: how loud they come out, of what each voice's own settings make. 50% when first
installed; 0 = silent (the speech still runs, without sound). Click the speaker to mute, and again to
unmute. Unlike the other settings it works while a
voice is speaking, within a moment. It turns down what the voices hand to a program: a program's own
volume control still works on top. (settings.json's "output_percent", 0 to 100, in the settings folder:
anything allowed to write that file can set it.)

Rest the pointer on a slider, or on its name, to read what it does. The Windows default voice box
reads "Default windows voice" while none of these voices is installed.

Changes are saved as you move a slider and take effect straight away, also in programs that are already
using the voice (from the next thing they say): no restarting, no repair. Preview says the text in the
"Preview says" box with the voice as it now is (Enter starts a new line, which gets the paragraph
gap); pressed while that voice is speaking, it stops it, and pressed with another voice selected it
stops the one speaking and starts that one. Reset puts back 100%, 1.5x, 100%. Each installed speaker of a voice has its own row (the
voice named on the first): Preview, Make default voice and Nickname act on the row selected. A voice's speakers share its sliders (add or remove speakers on "Add or remove
voices"). A program's own volume and speed still adjust from there.

The list of installed voices shows each voice's name, speaker, nickname, accent (or language), gender,
size on disk, whether it is Windows' default and its folder. Click a column's heading to sort the list
by it, and again to sort it the other way (sizes sort as numbers). Drag a heading left or right to move
its column: put Nickname first, say. The order and the sorting are kept for next time.

Right-click a voice in the list for:
  Set nickname        puts the cursor in the nickname box (see below).
  Set gender          Female or Male (click the ticked one again to leave both blank). This tells
                      Windows too, so programs that choose a voice by gender find it. Windows asks for
                      permission, as for a nickname, and programs already open see it after a restart.
                      Repair and updates keep it.
  Override defaults   ticks or unticks "Override pause duration for the selected voice".
  Make default voice  as the button (see below).
  Remove voice        uninstalls the selected speaker (a voice of one speaker: the voice), after
                      asking "Uninstall selected speaker?". Only that is changed: anything ticked on
                      Add or remove voices but not yet applied stays ticked, waiting for Apply changes.
                      Windows asks for permission, as for Apply changes.

Set nickname gives the selected row's voice the name typed in the box beside it, shown in brackets in
the voice's name in place of its accent: "VCTK p262 (Irish Female)" for "VCTK p262 (British)". It
renames the voice where Windows lists it (restart programs that were open to see it); with the box
empty, it puts the accent back. Windows asks for permission for this (a voice's name is Windows' to
keep).

Make default voice sets the selected voice as Windows' default speech voice: the one programs speak
with when they don't choose a voice (what Windows' Speech control panel, sapi.cpl, sets; Windows
speech settings opens that). It is per Windows user, for 64- and 32-bit programs alike. Windows 10's
Settings app has its own voice choice, for Microsoft's newer voice system: these voices can't appear
there, and it doesn't affect programs that use these. When voices are installed and Windows'
default is not one of these (it is one of Windows' own, such as Hazel), the first voice of the list
becomes the default; a Modern Voice that is the default stays so.

Speed and pauses
----------------
On the Speed and pauses tab, for every voice:

  Dictation speed how fast every voice speaks before a program's own rate. Default 100%, the
                  voices' own pace (Windows' speech speed +2 is about 125%). Each voice's own Dictation speed, on
                  the Voice manager, adjusts from here.

  Sentence pause  added after a sentence when another follows (never after the last, so short bits
                  of speech, such as a screen reader's, don't gain dead air). Default 500 ms.
  Paragraph gap   at a line break, in place of the sentence pause. Default 1200 ms.
  Comma pause     at each , ; or : (default 60 ms; 0: commas as the voice says them). Above 0 the voice says
                  each part of a sentence separately so the pause can go between them, which can make
                  the intonation a little more broken up.

Advanced setting, at the bottom of the tab: Voice variation. How much a voice changes its tone and
rhythm each time it speaks: lower is steadier, higher is livelier. It is the same for every voice. 100% is how the voices were made,
60% is the default (75% before 0.18.1). Programs already open use a new value after they are restarted.

Each pause is silence added to the voice's own, in milliseconds at 100% dictation speed and a program's
speech rate 0. Faster speech uses a smaller share of it and slower a larger share, in step with the
speech: at the default 100%, a 500 ms pause is 500 ms, 401 ms if a program also sets rate +2, and
623 ms at -2. The tab shows each pause's result at the speed set.

Speed is applied inside the engine, so it works in every program, also those that set their own
rate (Balabolka, NVDA): theirs adds to it. So does Windows' own speech speed (its
Speech settings, sapi.cpl), for programs that leave the speed to Windows: when that isn't 0, the tab
says so, with a button to set it to 0. Set the speed here instead, and leave Windows' and the
program's at 0 unless you want faster still.

A voice can have its own: in the Voice manager, tick "Override pause duration for the selected voice" and
set its three pauses. Changes apply straight away, like volume. Preview on this tab says the Voice
manager's line with the voice chosen beside it.

The window opens no larger than the screen; a tab that doesn't fit in it scrolls. The mouse wheel
scrolls the tab even over a slider (sliders move by dragging, clicking or the arrow keys). Each
Preview (and Hear it) stops what it is saying when pressed again. Dictation speed goes from 25% to 200%.

The settings live in the settings folder, C:\ProgramData\Modern Voices for Windows (every user of the
PC may change them, which is why Modern Voices Config needs no administrator): settings.json (the Speed
and pauses tab's "speed_percent" and "pauses", and "output_percent") and, for each voice you have
changed, voices\<voice>.json ("loudness": peak and boost; "inference": length_scale, higher = slower;
"pauses": its own), if you'd rather edit them. A voice you haven't changed is as its voice.json in the
install folder has it. (Before 0.12.0 all of these were in the install folder: updating moves them.)


Pronunciation
-------------
On the Pronunciation tab: words and names said your way, by every voice, in every program that uses
these voices (the Reader, a screen reader...).

  * Each language has its own rules ("Rules for", at the top: English, and each additional
    language chosen). A voice uses the rules of its own language only. "Use these rules" unticked
    switches the whole list off without removing anything. Other languages' lists start empty.
  * Type the word or phrase and how to say it, spelt as it sounds (Thx as "Thanks"; GIF as jif), then
    Add rule. Hear it says the "Say it as" text with the voice selected on the Voice manager, to try
    a spelling before saving.
  * The list starts with rules for smileys (:) is said as "smiley face") and for chat and trade
    abbreviations (ty as "thank you", lol as "haha", wts as "want to sell"): change, untick or
    remove them as you like. Four are there but unticked, as they are ordinary abbreviations too:
    rn, np, wp and lf.
  * To change a rule, select it, edit, and Save rule. Untick a rule to switch it off; Remove deletes it.
  * To have something not read at all, add it with "Say it as" left empty: *** is then skipped, for
    example, instead of being read as "star star star". The list shows such a rule as "(not read)".
  * Whole words only (the usual): "cat" changes "the cat's" but not "catalogue". Unticked, parts of
    words change too. Match capitals: "US" but not "us".
  * Where two rules fit, the longer is used; a rule never changes another rule's result.

Rules apply from the next thing a program says. Programs still see the original words (what they
highlight or show is unchanged): only what is said differs. The rules are in dictionary.txt in the
settings folder (a rule a line, tab-separated), if you'd rather edit or copy them.


The Reader
----------
Modern Voices: Reader (Start menu > Modern Voices for Windows > Modern Voices Reader) reads text aloud:

  * Paste text into the box (or press Paste and read) and press the play button. While it reads,
    that button is pause. Stop keeps the place, and play goes on from the start of the sentence it
    was in. Click a word to read from that word, also while it is reading; the first button reads
    from the start.
    Once it has read to the end, play reads the whole text again from the start.
    The word being read is marked.
  * What is pasted (or read from the clipboard) is laid out as it was where it was copied: an empty
    line between paragraphs, lists with their bullets and numbers.
  * In the Voice list each voice has a dot before its name; the filled one is Windows' default
    voice (what programs that don't choose a voice speak with). Click another voice's dot to make
    it the default (the list stays open); click a name to read with that voice.
    The Reader opens with Windows' default voice selected.
  * Change nickname: type a name in the box beside Voice and press the tick (or Enter) to give the
    voice chosen that nickname, in every program and in Modern Voices Config. Windows asks for
    permission the first time (voices' names are kept where only an administrator may write), and
    not again until the Reader has been closed and opened: a small hidden helper, which can do
    nothing but rename a Modern Voice, stays until the Reader closes. A voice renamed,
    added or removed in Modern Voices Config shows in the Reader's list when you come back to it. (To remove a
    nickname, use Modern Voices Config.)
  * Voice lists the Modern Voices (by nickname, where one has a nickname), then Windows' other
    voices. The two triangles left of it go to the voice before or after the one chosen (round from
    the last to the first). Speed is the program's own rate (as other programs have), on top of the Dictation speed.
  * The slider with the small speaker (between Save as audio and undo) is how loud the Reader
    speaks: the Reader's own volume, on top of each voice's settings in Modern Voices Config. It
    changes the sound at once, while reading too (it is the Reader's level in Windows' Volume
    Mixer), and applies to Save as audio too.
  * The bar under the play buttons is what is left to read: full as reading starts, emptying
    towards the right, empty when the reading has finished.
  * "Instantly read copy": while the Reader is open (minimised is fine), anything copied in
    another program is read straight away.
  * "Don't read links" (which also leaves out code and symbols): web addresses are read as "link", blocks of code are
    skipped, and bullets and the marks of formatted text (#, **, `, table lines) aren't read out.
  * Open a file: plain text (.txt, .md), Word documents (.docx), PDFs, rich text (.rtf) and web
    pages (.html). A file dropped on the text is opened too. A file opened again goes on from where
    you stopped in it (while its text is unchanged). (Not the older .doc: copy the text from those
    and press Paste and read.)
    A PDF's text is put back into paragraphs: lines are joined, words broken at a line's end are
    made whole, and page numbers and the line that heads or foots every page are left out. Each item
    of a list is read as a paragraph of its own, without its bullet (dots, arrows, ticks, dashes). That is
    worked out from where the words stand on the page, so now and then a paragraph is split or two
    are joined; tables and pages in columns can come out in an odd order. A scanned PDF is pictures
    of pages and has no text to open: show a page on the screen and use the magnifying glass. A PDF
    locked with a password can't be opened.
  * Read the screen (the magnifying glass): for text that can't be selected or copied (a
    picture, a game, some PDFs).
    It first says what it is for and that it can misread (tick "Do not display this warning again"
    to skip that next time). Then the screen dims; drag a box around the text, and Windows' own text
    recognition reads it into the box and starts reading. Recognition is not perfect, small or fancy print least of all. It works
    for the languages Windows has text recognition for (Settings > Time & language > Language).
  * Save as audio: the text as an MP3 or a WAV file (choose which under "Save as type"; the Reader
    offers the one you chose last time), in the voice and at the speed chosen, made as fast as the
    voice can (not played). An MP3 is about half a megabyte a minute, a WAV about 2.6 MB. For an MP3
    the voice's sound is made first and then packed: "making the MP3" shows while it is.
  * Undo (Ctrl+Z) and redo (Ctrl+Y), or the two arrow buttons beside Clear, take back and put
    back the last ten changes to the text: typing, a paste, a file opened, Clear.
  * Closed with text in the box, the Reader asks "Save text for next time?": Yes and the text is
    there, at its place, the next time it opens; No and it opens empty. (A file's place is kept
    either way: open the file again and Play goes on from where you stopped.) "Save text on close"
    (under Undo) chooses: Ask (to begin with) asks that question; Always keeps the text without
    asking; Never closes without asking and the text is not kept. Ticking "Don't ask again" in the
    question keeps your answer: Yes makes it Always, No makes it Never. Closing the question with
    its X keeps nothing and changes nothing.
  * Opened while it is already running, the Reader asks whether to start a second one; Cancel
    shows the one that is open.
  * Sums of money are said the way they are read aloud, by every program that uses the voices:
    "$150" is "150 dollars", "$1.50" "1 dollar 50", "$3.5m" "3.5 million dollars"; the same for
    pounds and euros.

Its settings and files' places are kept in %APPDATA%\Modern Voices Reader (removed by Uninstall).


Light and dark
--------------
The moon (top right of Modern Voices Config) switches between dark and light, for Config and the
Reader both (an open Reader follows within a second); the choice is kept. The cog at the bottom
right of the Reader opens Modern Voices Config.
They are dark until you choose light. A few parts are Windows' own and stay as Windows draws them (the ticks of tick boxes, the
sliders' tracks).


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
* Some programs take longer to start the more voices Windows has. Install the ones you use.
* The engine is signed by the build that made it, and the installer has Windows trust that signature:
  programs that only load signed speech engines need that. It can sign nothing
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
