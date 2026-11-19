# Handoff: Modern Voices for Windows

For a fresh Claude session continuing this project. Read this first, then the code it points to.
Last updated at version 0.18.12 (19 Nov 2026). Model rule: say which model a batch suits BEFORE
starting; Opus-level work while he is on Fable: recommend switching and wait (he often answers
"build on Fable": then go ahead).

**Version:** he asked "0.14 or perhaps v1.0?"; I suggested 1.0.0 be a build with nothing new, once he has
used the current one for a few days. Ask/confirm before calling a build 1.0.

**The repo is public since 16 Nov 2026** (he made it so when private-repo Actions minutes ran out:
run #109 was refused for billing). Builds are free again. Anything pushed is visible to everyone.

## State for a new chat (19 Nov 2026, late morning)

**He confirmed 0.18.11 is good.** 0.18.12 (job A without the MP3s, on Opus 5.5) is built: see its section.
Still to do: the language MP3s (job A's section 1, waiting for his recordings) and job B (per-speaker
settings, Fable). Ask how 0.18.12 went. 0.18.2 to 0.18.11 were all built on Opus 5.5 in one long chat and
delivered (each zip sent); he has been trying them as they came (he hit 0.18.2's start-up crash, fixed in
0.18.3, and corrected 0.18.9's Amplify default in 0.18.10).
What changed across them, newest first, is in the sections below; the main things a new chat should know:
- Voice manager list: Accent and Gender columns, sorting by heading (both lists), columns that can be
  dragged (all kept in HKCU), a right-click menu; gender is written to Windows by the elevated helper.
- Add or remove voices: a voice's name plays its sample (one speaker); ticking a voice of several speakers
  ticks them all (more than two: it asks, "Select all" / "Choose").
- Layout: the section outlines are centred on the page; the default voice box, the preview box, the bottom
  message and (0.18.11) the first tab all line up with things below them; the bottom message fades.
- Loudness: the per-voice slider is "Amplify" ("off (default)" at 0, MAX at 100, the default 100); Boost up
  to 2.5x; output volume 50% on a new install.
Ideas still on the shelf: see "Ideas" (the pronunciation ignore list is dropped: a rule with "Say it as"
empty does it).

## 0.18.12 (19 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5: sections 3 and 4 of "Planned next" below, and the Reader's save question.
1. Voice variation is out of the Voice manager's override section (`OwnVariation` gone); the tick and the
   menu's tooltip read "Override pause duration for the selected voice". Variation is only on Speed and
   pauses. `Store.GetVoiceVariation` / `SetVoiceVariation` and the engine's `own_variation` are left in
   place, unused by Config (he has none saved; no clean-up, his call).
2. **Remove voice** (Voice manager right-click menu, after a separator, `MenuRemove`):
   `RemoveSelectedVoice` asks "Uninstall selected speaker?" (`RemoveWindow`, Yes / No; No is the default:
   Enter, Esc and closing change nothing; test hook `AskRemove`), then `StartApply(installed minus this
   id, UsTags)`. `Apply()` is now `Apply()` (the Add page's ticks) + `StartApply(wanted, us)` (Elevate,
   helper request, Busy). The Add page's ticks are saved as `pendingIds` (PickedIds less the removed id)
   and `pendingUs`, and put back in `ApplyFinished` (`TickAsPending`, which also writes `Speakers`), so
   anything waiting stays waiting and Apply changes is lit for it. `Current` is let go before the ticks
   are put back (else `SaveSpeakers` would write the speaker list's old ticks over them). The row below
   the removed one (else above) is selected afterwards (`selectAfter`).
3. **Reader "Save text on close"** is a dropdown now: label "Save text on close:" + `saveChoice` (Ask /
   Always / Never; `State.SaveText`; an old settings file's `SaveOnClose` false reads as Never, else
   Ask). Ask: the question as before; Yes / No with "Don't ask again" set Always / Never
   (`AnswerSaveText`); the question's X (DialogResult Cancel; its No button now returns No) keeps nothing
   and ignores the tick. Always keeps the text whatever closes the Reader (also in the self-test, which
   tests it), Never keeps nothing. `underPanel` is as tall as the dropdown needs.
4. kit/README.txt: the menu's Remove voice, the override wording, the Reader's dropdown.

Not covered by CI: a real right-click, the UAC prompt (CI is elevated), the dropdown opened by hand.

## Planned next (agreed 19 Nov 2026, NOT built)

**Sections 3 and 4 were built in 0.18.12; section 1 (MP3s) waits for his recordings (it will be 0.18.13 or
later); section 2 is still job B.** He split it: **job A on Opus 5.5** (sections 1, 3, 4: Config only, plus packaging),
**job B on Fable** (section 2: engine, Config and VoiceConvert.ps1). Either may go first,
but A is smaller; if B goes first it takes 0.18.12. Bump the version per build as usual.

**1. (Job A, Opus) Multi-speaker voice's name plays a recorded instruction (Add or remove voices).**
- He will record MP3s of a voice saying "Select the voices you want from the list below", one per
  language (English is 23.8 KB). He has not sent any yet: ask him to attach them.
- Before he records, give him the list of languages that HAVE multi-speaker voices in the catalogue (only
  those need one). Not yet worked out.
- Plan: files `choose-speakers-<lang>.mp3` (lang = `text::language_of`'s letters, e.g. en, de) shipped
  beside `ModernVoicesConfig.exe` (MediaPlayer needs a file path; `tools/Package.ps1` copies them,
  Install copies config\*). `VoiceNameClicked` (Config.cs ~4374) now returns for `Several`: instead
  play the file for the voice's language through `Sample.Play`, else the English one (my proposal, he
  didn't object; confirm). So "Play sample" reads "Stop sample" meanwhile and Apply stops it, as now.
- Told him: the same clip plays for every multi-speaker voice of a language, so it may be taken as how
  VCTK sounds; and if he records with a Piper voice (the Reader's Save as audio, MP3), pick one with a
  permissive licence. In 0.18.6 he chose "nothing" over "a phrase"; this replaces that choice.
- Self-test: VCTK's name now plays (today it checks it is silent: change that check).

**2. (Job B, Fable) Per-speaker settings in the Voice manager.** His words: "currently, dictation speed affects all
speakers in the voice. can this be changed so each speaker can have its own dictation setting", then
"it makes sense to do boost and amp, as well as the override settings, at the same time? the voices
can vary significantly due to the nature of how they were created."
- Today every setting is per MODEL FOLDER: `voices\<folder>.json` (engine `own_file`, Config
  `App.VoiceFile`, VoiceConvert's `Get-VoiceText`), so all of VCTK's speakers share one. The token
  carries `SpeakerId` (Install.ps1 `Register-Voice`), and the engine already has `voice.speaker`.
- Scope: Amplify (peak), Boost, Dictation speed (length_scale), and the override section (own pauses:
  sentence, comma, paragraph; NOT variation). Per speaker, falling back to the voice's value where a speaker has none,
  so existing settings files need no migration and update-in-place stays safe.
- **Voice variation stays PER VOICE (his decision):** it is applied when a model is LOADED (`load_model`,
  cache key `dir|data|variation`), so per speaker would mean a second ~75 MB copy of the model. Job A
  (section 3) takes it out of the override section altogether.
- Live changes must keep working: he checked in Balabolka that a Config change is heard after Stop/Play
  with no restart (the engine re-reads the settings at each Speak by file time, `Model::settings`).
  Per-speaker values must go through the same re-read; note `Model::settings` caches ONE `Settings` per
  model, so it must become per speaker (or the per-speaker part applied on top after the read).
- A possible file shape (not decided): a `"speakers": { "<id>": { "peak": .., "boost": ..,
  "length_scale": .., "own_pauses": .., "sentence_ms": .. } }` block in the voice's own file. Beware: all
  three readers (engine `number()`, Config `Read`, VoiceConvert regexes) find the FIRST match of a key in
  the text, so a nested block with the same key names would be found by the old voice-level reads. Use
  distinct keys, or read the block out first, and keep the three readers in step.
- Config: the sliders follow the selected ROW (each speaker is a row already); "Reset" resets that
  speaker back to the voice's values. Decide with him what a voice of one speaker shows (unchanged) and
  whether a label should say "for this speaker".
- Tests: engine unit tests for the lookup; Test-Kit: two VCTK speakers at different speeds/peaks speak
  differently; Config self-test: a slider moved on one speaker leaves the other alone.

**3. (Job A, Opus) Voice variation out of the override section.** His words: "voice variation slider would
need removing from the 'overide pause' section, and the tickbox label need updating to remove
'variation'. (i think variation is ok just being in the advanced section of speed and pauses ...)"
- Remove `OwnVariation` (Config.cs ~2548, built in `BuildVoiceManager`'s gapGrid, plus wherever it is
  filled/saved/reset and the self-test's checks of it).
- Label: `OwnBox.Text` (~2540) and the menu's `MenuOverride.ToolTipText` (~2282) become "Override pause
  duration for the selected voice". Check kit/README.txt and README.md for the old wording too.
- A saved `own_variation_percent` would still be used by the engine (while `own_pauses` is 1) with no
  slider to see it. **No clean-up needed (his decision, 19 Nov):** he never saved one and no one else has
  the program. Just remove the slider; leave the engine and the files alone.
- The global "Voice variation" on Speed and pauses (Advanced setting) stays as it is.

**4. (Job A, Opus) "Remove voice" on the Voice manager's right-click menu.** His words: "right click a voice
in voice manager list should have 'remove voice' option to uninstall the selected speaker. this should
trigger a pop up: "Uninstall selected speaker?" yes should remove the voice and 'apply changes' (and ask
for admin permish if required and not yet granted) no ofc cancels."
- Menu item "Remove voice" in `BuildVoiceMenu` (`VoiceMenu`). Question window with his exact words
  "Uninstall selected speaker?", Yes / No (No, Esc and closing cancel). Build it like the other
  question windows (`AskWindow` style, themed light/dark), not a MessageBox.
- Yes: untick that speaker (a voice of one speaker: the voice; the LAST installed speaker of a voice of
  several: the voice too, so its model is removed) in `VoiceItems` / `Speakers`, then run `Apply()`, which
  already asks for the administrator's permission through `Elevate()` only if the helper isn't running.
- **Decided (his choice, 19 Nov): the removal applies only itself.** `Apply()` sends ALL ticks
  (`PickedIds`), so don't call it as it is: send the installed set minus this id (same helper request,
  progress, Busy and SetDefaultAfterApply handling), and leave any ticks pending on Add or remove voices
  as they were, still waiting for Apply changes. Make sure the Add page's ticks for OTHER voices aren't
  reset by the refresh after this apply, and that the removed speaker shows unticked there.
- After Apply, `SetDefaultAfterApply` already covers removing Windows' default voice (first voice
  becomes default). Select a neighbouring row afterwards.
- Self-test: the menu item exists; in the `--apply` run, remove the VCTK speaker the test added through
  this path (answer Yes through a test hook, as `AskAll` does) and check it is gone; No changes nothing.

## 0.18.11 (19 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5:
1. The many-speakers question is shorter (his words): "Ticking VCTK (British) will install all 109 voices."
   then, after an empty line, "It is recommended to only select individual voices" / "from the list
   below..." (his line break kept). Buttons as before (Select all / Choose).
2. The tabs: the first tab's left edge is the lists' left edge (`TabFlow`'s left margin, worked out in
   `AlignStatus` on Shown and Resize from where InstalledList and TabButtons[0] are).
3. "Preview says:" half-way between the Preview button and the preview box: the room there was shared out
   equally (`previewGap`; the label's own top and bottom margins 0). Self-test checks both, within 1 px.

## 0.18.10 (19 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5. He corrected 0.18.9: Amplify's DEFAULT stays 100% (MAX); "off (default)" (lowercase)
only names the voice's own level, as Piper made it. `App.DefaultPeak` and `$DefaultPeak` back to 1;
Test-Kit back to expecting peak 1 (its "-1 = as Piper made it" option in Test-Speech stays, unused).
Everything else in 0.18.9 stands (the name Amplify, MAX, Boost up to 2.5x, output 50% on new installs).
Lesson: don't infer a behaviour change from a label; ask if a label seems to contradict the behaviour.

## 0.18.9 (19 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5. Loudness:
1. The per-voice "Volume" slider is now named **Amplify** (still `Sliders["Volume"]` in code and "peak" in
   voice.json). Values: 0 = "off (Default)", 10%..90%, 100% = "MAX"; steps of 10 as before.
2. (Undone in 0.18.10: off was made the default here; he wanted only the label.)
3. Boost goes up to 2.5x (`App.MaxBoost`; was 4). A voice saved higher shows (and saves, if touched) as 2.5.
   The engine and VoiceConvert.ps1 still accept up to 5 (harmless).
4. Output volume starts at 50% on a NEW install (Initialize-SettingsFolder, when settings.json is made).
   An existing settings.json keeps its value; a file without output_percent still means 100.
5. Test-Kit checks the new defaults (peak 0, output 50), then sets every voice to peak 1 and the output to
   100 before its loudness measurements (they measure against a known level).

## 0.18.8 (19 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5. Voice manager and the window's bottom line:
1. "Preview says:" (colon added) stays where it was; the preview box is on its own row under it, its sides
   on the frames' lines (`PreviewBox.Margin` = the frames' margins, set after `FrameSliders`), four lines
   tall. `previewColumn` (where the sliders' names start) is still measured from "Preview says" WITHOUT
   the colon, so the sliders didn't move.
2. English preview text (his): line 1, line 2, an EMPTY line, then "Change the 'Paragraph gap', to adjust
   the wait time before reading the next line." Every translation got the same empty line (their own
   last-line wording kept, not retranslated: told him). An empty line still gives one paragraph pause
   (`line_break_between` is a yes/no). `IsOneOfThem` also knows 0.18.7's English (`App.PreviewText0187`)
   and every text without the empty line.
3. The bottom message (`StatusLabel`): its words start on the frames' line (`AlignStatus`, on Shown and
   Resize); it fades: stays 4 s + 60 ms a character (max 15 s), fades to the bar's background over
   1.5 s (`StatusFadeTick`), then empties. Not while Busy. Self-test turns `StatusFades` off except for
   one check that it fades and goes.
4. He thought "passed its test" a typo for "it's": told him "its" is right (possessive), left it.

## 0.18.7 (19 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5. His answer to the many-speakers warning: a several-speaker voice with MORE THAN TWO
speakers, ticked by hand, asks first (`AskAllSpeakers`, after the click by BeginInvoke; window
`AllSpeakersWindow`, words `AllSpeakersWords`): it says all N speakers become N voices in every program's
list and programs can be slow to open, and recommends choosing speakers below. "Select all" ticks all;
"Choose" (default: Enter, Esc, closing) unticks the voice, selects it and focuses its speaker list. He said
"large and slow": I left out "large", as speakers share one model file (no extra download), and told him.
Self-test answers through `AskAll` (Choose, then Select all) and pictures the window
(all-speakers-question.png). Unticking by hand still unticks all, without asking.

## 0.18.6 (19 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5. Add or remove voices:
1. Clicking a voice's NAME (not its box) plays a one-speaker voice's sample (`VoiceNameClicked`, from
   AddList.MouseClick, which WinForms raises only over an item); a voice of several speakers plays nothing
   (his choice, option 3 of: first speaker's sample / a "Choose one or more voices from the list below"
   phrase / nothing). Told him: samples are Piper's own recordings (speaker_N.mp3 in each voice's folder
   on Hugging Face, cached in %TEMP% as modernvoices_sample_*), the same text per language (English: the
   rainbow passage); a voice not installed can't say anything else.
2. Ticking a several-speaker voice's own box BY HAND ticks all its speakers, unticking unticks all
   (`AllSpeakers`). By hand = `tickByHand` (MouseDown on the state image, or Space) or `PointerOnBox`
   (Windows may toggle the box before WinForms reports the press). Ticks made by code (SpeakerTicking
   ticking the voice, Apply, start) must not: they don't pass either test. Warned him: VCTK has 109
   speakers, LibriTTS ~900 (each a Windows voice): answered in 0.18.7.
3. Self-test: VCTK ticked/unticked by hand (all/none), its name silent; a one-speaker voice's name plays.

## 0.18.5 (19 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5. Text only:
1. "Powered by"'s thanks (`Credit.Thanks`, also README.md's credits): "Thanks to everyone who recorded their
   voice for a research collection or audiobook, and allowed it to be used for free in software such as
   this." (his words).
2. The quality warning on Add or remove voices moved from beside "Additional languages..." (it got lost
   there) to a line of its own in brackets, under "Tick the voices to install..." above the list.
3. The first-voices note (`FirstVoicesWords`) has a second line, "(Voice quality varies. Sample before
   downloading.)", in each of its 29 languages too (Claude's translations, unchecked, like the rest).
   Careful editing that dictionary by language code: the same codes key the preview texts earlier in the
   file (a script appending by `{ "de", "` first hit those; caught and undone).

## 0.18.4 (18 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5. Layout only, Voice manager:
1. The outlines of "How X sounds" and the override section are in the middle of the page (equal gap to
   the page's left and right edges; the page's padding is the same both sides). Only the lines moved
   (left, by ~11 px at 100%): `FrameSliders` gives both margins their average and moves the inner padding
   the other way, so the sliders, names and values stay put. So the words are no longer FrameGap from
   the line on both sides (more on the left now, less on the right): his choice.
2. The Windows default voice box (`DefaultBox`) starts where the frames' lines do; the speaker (mute) and
   the output volume slider moved right with it, the slider still ending where the sliders below end
   (its column fills, so it is that much shorter).
3. Self-test: the old "as far from the words on both sides" check is now "lines in the middle of the
   page" plus "default box starts at the lines".

## 0.18.3 (18 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5. His asks after 0.18.2:
1. **Voices vary in quality**: a notice near the top of README.md and kit/README.txt (some voices are
   excellent, some robotic; Modern Voices can't make a robotic one natural; play samples first), and in
   Config to the right of "Additional languages..." (`QualityNote`, grey): "Some voices sound much more
   natural than others. Play a sample before you install a voice."
2. **Not reading something**: a rule with "Say it as" empty (always worked: Config, VoiceConvert.ps1 and the
   engine all keep an empty replacement, and the engine drops the text). The list shows it as
   "(not read)" (`ConfigForm.NotRead`, `SaidAs`). Self-test adds, checks and removes a "***" rule.
3. **Pronunciation tab instructions**: one per paragraph with an empty line between (one label, \r\n\r\n):
   add (example now his 'Thx as "Thanks"'), change, not read (his placement: after "Save rule"), untick,
   Hear it, Whole words / Match capitals. The self-test still uses Niamh/Neev: "thx" is a default rule
   (chat1), so the test would edit and remove the real one.
4. **Fixed: 0.18.2 crashed at start once a sort was saved** (he hit it: NullReferenceException in the
   lists' HandleCreated handler). It sorted and read `Items[0]` while Windows was still making the list
   (rows half there: see Pitfalls). Now `ListMade` runs by BeginInvoke after the handle exists. Self-test
   opens a second window first with VoiceSort, AddSort and VoiceColumns saved (the case CI never had).
   Workaround for 0.18.2: delete HKCU\Software\Modern Voices for Windows\VoiceSort (and AddSort).

## 0.18.2 (18 Nov 2026), delivered, not yet confirmed by him: the rest of his 18 Nov batch

Built on Opus 5.5. His words in quotes.
1. **Voice manager columns** (logical order, constants `ConfigForm.VoiceColumn` .. `FolderColumn`): Voice,
   Speaker, Nickname, Accent, Gender, On disk, Windows default, Folder. The Voice column now shows the
   voice's name only (`row.Voice` = `Entry.Display`), since Accent has its own column (`Entry.Accent`, the
   same words as Add or remove voices). `InstalledRow` now carries every cell's value (Voice, Nick, Accent,
   Gender, SizeMb, IsDefault, First, Order). **Use the column constants, never a SubItems number.**
2. **Gender** ("the 1st to leave empty, then female then male"; read as: blank, Female, Male): the column
   shows what Windows has (`Voices.Gender(id)`: the 64-bit token's `Attributes\Gender`).
   `Voices.SetGender` writes both token roots and keeps the choice in HKLM `...\Genders` (by voice id; "" =
   neither chosen). Done by the helper's new third command, `gender <id64> <gender64>` (validated: GoodId,
   "", Female or Male); one permission prompt, as for a nickname. Install.ps1's `Get-ChosenGender` is read
   first in `Register-Voice`: a kept choice wins ("" = no Gender value), else the `$Genders` table as before.
   Set from the right-click menu only (no box on the page).
3. **Sorting** both lists (`RowSorter`: a column's key per row, ties keep the order as listed; numbers for
   sizes and speaker counts; Windows default sorts the default first; Quality medium then high). Click a
   heading, again to reverse. Kept in HKCU (`ListLayout`, values `VoiceSort` / `AddSort`: "Name|up").
   Arrows: Windows' own in light (`Native.SortArrows`, HDF_SORTUP/DOWN), drawn in `DrawHeading` when dark.
   Voice manager: while sorted by anything but Voice, every row shows its voice, size and folder
   (`ShowInstalledCells`); as listed or by Voice, a voice of several speakers names itself on its first row
   only, as before. Selection is kept by voice id (`SelectInstalledId`; SetNickname, SetDefaultVoice and
   SetGender use it now, as the row can move). Add or remove voices: `ApplyVoiceFilter` sorts after refilling;
   ticks are saved and put back around a sort as around a filter (`filtering` set). It has no ListView groups.
4. **Column reorder** (Voice manager only, `AllowColumnReorder`): saved on `ColumnReordered` (raised before
   the move, so the new order is worked out) as HKCU `VoiceColumns` (headings joined by "|"),
   `RestoreColumnOrder` at start. The last DISPLAYED column fills the row (`FillLast`; a column dragged
   away from the end gets its made width back, `columnWidths`). Dark rows: Windows gives the first
   column's cell as the whole row, so `DrawCell` works out its x once it has been dragged elsewhere.
5. **Right-click menu** (`VoiceMenu`, `BuildVoiceMenu`, `UpdateVoiceMenu` on Opening): Set nickname (focus to
   `NickBox`, after the menu closes), Set gender > Female / Male (ticked = current; clicking the ticked one
   clears it: "leave both blank"), Override defaults (toggles `OwnBox`, scrolls to the section when ticked),
   Make default voice (ticked and greyed when it already is). Dark look: `DarkMenuLook` /
   `DarkMenuColours` (its own tick, as Windows' is black); light: Windows' professional renderer.
6. Self-test `ListsAndGender` (columns, cells, gender set/clear in both roots and kept, menu items and
   Override, sorting both lists, column order restored), and in the `--apply` run: a chosen gender, and a
   chosen "neither", survive the installer re-registering every voice. Pictures: voice-menu-light/dark.png,
   voice-menu-gender-light/dark.png, voice-manager-sorted.png, voice-manager-nickname-first.png,
   add-sorted.png. `ListLayout.Override` keeps the test's arrangement out of the person's registry.
7. kit/README.txt (the list, the menu) and README.md's table.

Not covered by CI: a real drag of a heading (the test sets the saved order and restores it), a real
right-click (the test drives the menu's items), the UAC prompt for a gender.

**Version numbers (his rule, 18 Nov 2026):** the middle number (0.18 -> 0.19) goes up only for a major
new feature or change; everything else is 0.18.1, 0.18.2, ... Engine Cargo.toml, both AssemblyVersions
(0.18.11.0), Install.ps1 $Version and kit/README.txt's first line carry it.

## 0.18.1 (18 Nov 2026), delivered, not yet confirmed by him: the Opus half of his 18 Nov batch

He split his batch: these on Opus 5.5 first; the rest was built as 0.18.2, apart from
the ignore list, now only an idea (see Ideas discussed but not built).
1. Voice variation default 75% -> 60% (`text::DEFAULT_VARIATION_PERCENT`, `Store.VariationDefault`).
   Only saved once moved, so everyone who never moved it gets 60 with no migration.
2. Apply changes stops a sample that is playing (`Apply` calls `Sample.Stop()` first).
3. "Select voices" note in the language chosen when installing, above the English
   (`ConfigForm.FirstVoicesTranslated`, 29 languages, Claude's, unchecked; "Apply changes" kept English).
   The language is remembered by `Languages.TakeInstallChoice` as HKCU `InstallLanguage` (from 0.18.1
   installs on: earlier installs have none, so English only). Picture: select-voices-es.png.
4. Reader: triangles (U+25C0 / U+25B6, Segoe UI Symbol) left of the voice list, `StepVoice(+-1)`,
   wrapping. The "about 3:20 left" counter is gone (the progress bar stays; `Pace()` kept, now unused).

## 0.18.0 (17 Nov 2026), delivered, not yet confirmed by him

Built on Opus 5.5.
1. **GitHub README**: a new "The Reader" section (features, and his note that opening a PDF can load
   with some inaccuracies in the text), the Reader in the intro and the "What you get" table, the
   install's first windows, PdfPig / LAME (with RareWares and NAudio.Lame) and Claude's translations
   in "Standing on other people's work", `third-party/reader/` in the developers' table, the
   libraries' licences under Licence.
2. **"Select voices" note** (`FirstVoicesWindow`, words in `FirstVoicesWords`, his wording): borderless
   like "Powered by", with OK and an X (top right); Enter / Esc close it. `Program.Main` shows it
   whenever Config opens with no voice installed, after "Powered by" on the first start (both from one
   `BeginInvoke`, one after the other, over the window, which opens on Add or remove voices).
   Picture: select-voices.png.


Built on Opus 5.5 (he switched). His three points after trying 0.16:

1. **PDF bullets were read as "play button"** (an arrowhead bullet read by its sign's name). `Pdf.Tidy`
   now finds a bullet at a line's start (`Pdf.Bullet`): the signs in `BulletSigns` (dots, squares,
   diamonds, arrowheads, arrows, ticks), any Private Use Area letter (U+E000-F8FF: what a symbol
   font's letter often comes out as), a letter drawn in a Wingdings / Webdings / Symbol / Dingbats
   font (`Pdf.Pages(path, out marks)` collects them per page from PdfPig's `page.Letters`,
   `Letter.FontName`), or a dash / star with a space and words after it ("- 3 -" stays a page number).
   The bullet is dropped and the item starts a paragraph of its own (internally marked with `Item`,
   \u0001, so it is never joined to the page before). PUA and symbol-font signs inside a line become
   a space (`Unmarked`). Not handled: Courier "o" sub-bullets unless that "o" is in a symbol font;
   numbered lists are left as they are (they read fine).
2. **"Is there a way so install won't fail if the PDF/MP3 repos change?"** The install never downloaded
   anything (the kit carries the DLLs); only the BUILD did. Now the eight DLLs are in the repo,
   `third-party/reader/` (`*.dll binary` in .gitattributes), and `tools/Package.ps1` copies them,
   checking each one's SHA-256 (a table in the script). No network at build time for them any more.
3. **Frame gaps.** `FrameGap` = twice 0.15's visible gap (2 x (10 px + 3 + NameInset) = 32 px at 100%),
   both sides: the left line that far from the slider names' words, the right line that far from the
   end of the widest value text (`WidestValue` of `ValueSamples`: "none (as said)" at 100%, so for a
   "100%" row the right gap looks bigger; he asked for "the end of the percent numbers", told him).
   Done by `FrameSliders`: the value column is now just `ValueFit` wide and the frame's right margin
   takes up the rest, so the sliders still end where the output volume slider ends (text and sliders
   did not move: he said they are right). Volume's "off (as Piper made it)" is wider than the column and
   wraps onto two lines (value labels' MaximumSize). Config's test checks both gaps.


His words: "add pdf support to reader and mp3 saving". Built on Fable. Both use an outside library,
which lies beside `ModernVoicesReader.exe` (flat: the installer copies `reader\*` without subfolders) and
is in the repo since 0.17 (`third-party/reader/`; see 0.17.0 above).

1. **PDF** (`Pdf` in Reader.cs): PdfPig 0.1.8 (Apache-2.0; the net47 DLLs from the project's GitHub
   release; 0.1.8 because later versions need System.Memory and binding redirects). Loaded by
   reflection (`Assembly.LoadFrom`) so the Reader compiles and starts without it. `Pdf.Pages` gets each
   page's lines from `ContentOrderTextExtractor.GetText(page, true)` (content order; an empty line where
   the gap is over 1.7 x the type size). `Pdf.Tidy` (pure, tested without a PDF) makes paragraphs: joins
   lines, mends words hyphenated at a line's end, ends a paragraph at a sentence-ending line under 3/4
   of the longest, drops page numbers and first/last lines repeated on most pages, joins a paragraph
   over a page break. No text at all = "only pictures of pages (a scan?)"; encrypted = "locked with a
   password". PDFs may be up to 300 MB (other files 30).
   NOT handled: tables, multi-column pages whose content order is odd, footnotes, scanned PDFs (no OCR
   of a whole file; the magnifying glass is the way).
2. **MP3** (`Mp3` in Reader.cs): LAME 3.100, `libmp3lame.64.dll` / `.32.dll` (LGPL; RareWares' builds, the
   copies in Corey-M/NAudio.Lame at tag v2.1.0; they need only kernel32). Functions found with
   LoadLibrary / GetProcAddress and called through cdecl delegates. 64 kbit/s constant, mono, no VBR tag
   (so a player's length = size / rate). Save as audio offers "MP3 | WAV" (`State.AudioKind` keeps the
   last kind, mp3 first). For an MP3 the voice writes a temporary WAV in %TEMP% (`savingWav`), then
   `StartEncoding` packs it on a thread-pool thread (`encoding`, `stopEncoding`; `Saving` covers both;
   the button reads "Stop saving" throughout).
3. Credits: both in `Credit.All` (About window), README, THIRD-PARTY-NOTICES ("Beside the Reader"),
   `licenses/LGPL-2.0-lame.txt`.

**Checked here before CI:** my own code ran under mono (see below): `Pdf.Tidy`, the self-test's PDF
writer (`SelfTest.TestPdf`, read back with pypdf) and `Mp3.Encode` against Linux's libmp3lame (dlopen in
place of LoadLibrary). The container refused to RUN the downloaded PdfPig DLLs, so reading a real PDF is
tested on CI only (the Reader's self-test logs each page "as read").

## 0.15.0 (17 Nov 2026), delivered, not yet confirmed by him: his 16/17 Nov batch

He confirmed 0.14.0 "seems to be working". Built on Fable at his say.

1. **Default voice box:** reads "Default windows voice" (`ConfigForm.NoVoicesDefault`, his words) while no
   Modern Voice is installed (`UpdateDefault`). With voices installed and a Microsoft default it still
   shows the token name "(not one of these)": his words didn't cover that.
2. **Reader "Save text on close"** (`saveBox`, under Undo, placed by `AlignLine`; `State.SaveOnClose`,
   default true). Ticked: closing with text asks; unticked: no question, text not kept. The question has
   "Don't ask again" (`Program.SaveTextWindow(out tick)`), which unticks it (`AnswerSaveText`).
3. **Voice manager layout.** Two framed sections (`Frame()`, tag "frame", border painted; `SoundFrame`,
   `PauseFrame` with 16 px between). No measuring any more (`alignPreview` / `alignSaved` are gone): the
   columns are fixed numbers worked out in `BuildVoiceManager`: `previewColumn` (the "Preview says"
   label's width: the preview box, the sliders' names, the override tick and "Changes are saved" all
   start there), `labelColumn` (the widest slider name) and `valueColumn` (146 px: the value text right
   of a slider; the output volume's too, so every slider ends at the same x). `Centre(grid, inset)` gives
   a slider grid those columns. The notes under the sliders are tooltips now (`Describe`: on the slider
   and on its name; a disabled slider shows none, its name still does).
   `NameInset` (Font.Height / 5, 3 px at 100%): a label's words start that far inside it, so the sections
   sit that much to the left and the WORDS start at the preview box's edge. The CI screen is only 720
   high: `Pictures` now also takes `dark-N-lower.png`, each tab scrolled to its foot (the Voice manager's
   with the override sliders showing, `PeekOwnPauses`). Look at those too.
4. **Speed and pauses:** the same columns (`Centre(globalGrid, 0)`), everything from the sliders down
   indented by `previewColumn`; the title and "Preview with" stay at the left. Its notes stay under the
   sliders (he asked for tooltips on the Voice manager only). "Set Windows' speed to 0" moved under its
   warning (the value column is too narrow for a button now).
5. **Pronunciation:** the rules list is 390 px high (was 260).
6. **Installer language choice.** `New-LocationForm` has a dropdown: English, English (British), English
   (American), then the 29 others as "German - Deutsch" (`installer/Languages.ps1`: names and notes as
   JSON with \u escapes in a here-string, made by `tools/dev/make_languages.py`: edit the words THERE).
   Another language than English shows `New-LanguageNoteForm`: the note in that language, the English
   under it, OK / Back. The choice goes to `%ProgramData%\...\install-language.txt`
   (`Set-LanguageChoice`; also `-InstallLanguage CODE` for unattended installs); Config takes it up once
   at its next start and deletes it (`Languages.TakeInstallChoice` / `Apply`). An UPDATE does not ask
   (the first window only shows on a fresh install).
   - `Languages.English` (HKCU `English`, "0" = off) and `Languages.Show` (HKCU `Show`: the "Show" box's
     words, kept between runs now; that is how British / American sticks. Before 0.15 Show was "All
     voices" at every start: I changed that, told him).
   - `LanguageShown("en")`: on unless switched off; also on while it has a ticked voice, or while no other
     chosen language has voices in the list (so the list is never empty).
   - Additional languages has an English row first (`SetLanguages(families, english)`).
   - The 29 notes are Claude's translations, unchecked, like `PreviewTexts`.
   - The installer lists all 29 `Catalogue.Offered` languages, but the catalogue had voices (medium or
     better) for only 25 of them on 17 Nov: choosing one of the other four lists English after all (the
     never-empty rule). Told him; could be trimmed if he minds.
   - Pictures: `-PickerSelfTest` saves install-location.png and install-note-de/el/ru.png when
     `$env:MV_PICTURES` names a folder (Test-Kit sets it).

**Checking C# here (new):** `apt-get update && apt-get install -y mono-mcs libmono-system-windows-forms4.0-cil
libmono-system-web-extensions4.0-cil libmono-windowsbase4.0-cil libmono-system-xaml4.0-cil
libmono-system-io-compression4.0-cil libmono-system-io-compression-filesystem4.0-cil` gives `mcs`; with a
small file of stubs for System.Speech and `System.Windows.Media.MediaPlayer`, `mcs -langversion:5` type-checks
Config.cs and Reader.cs in seconds (it found nothing csc didn't, but saves a CI round per typo).

## 0.14.0 (16 Nov 2026), delivered, not yet confirmed by him

1. **About window** (`AboutButton`, an i glyph left of the moon; `AboutWindow()`): the projects from
   `Credit.All` (name, author, what it does, link), `Credit.Thanks`, `Credit.Coding` and
   `Credit.FromLumoti`: the last two are HIS WORDS, verbatim; don't reword.
2. **"Powered by" window** (`PoweredByWindow()`), shown once, over Config, the first time it opens
   (`Welcome.Seen`, HKCU `Welcomed`; Uninstall removes that key, so a reinstall shows it again).
   Borderless, centred text, an X top right; a click anywhere, the X or any key closes it. Shown from
   `Program.Main`'s Shown handler only (never in the self-test).
3. **A voice's own variation** (`OwnVariation`, in the override sliders on the Voice manager; the tick
   now reads "Override pause duration and variation for the selected voice"): the voice's own file's
   `own_variation_percent`, used by the engine only while `own_pauses` is 1 (`text::own_variation`,
   `load_model` reads the voice's own file first, then the global). As with the global one, a model is
   loaded with it: open programs need a restart.
4. **Reader: "Save text for next time?"** on closing with text (Yes / No; `Program.SaveTextWindow`,
   `KeepTextForNextTime`, `State.KeepText`, current.txt). Kept for one start (`RestoreKeptText`), with
   its place; closing asks again. Not asked in the self-test (`silent`) or when Windows closes it.
5. Layout: the Defaults text is level with Reset to defaults; "Changes are saved straight away" lines up
   with the sliders' notes (`alignSaved`) with 16 px above.
6. README: his "simple install" sentence (verbatim), a "Better voices for Balabolka" paragraph.

## 0.13.3 (16 Nov 2026), delivered, not yet confirmed by him

1. **Reader reads from the word clicked.** Stopped: a click sets the place and `placeClicked`; Play then
   starts at that word (`Cleaner.WordStart`), not its sentence. Reading or paused: a click (press and
   release on the same character, so a drag still selects) restarts from that word
   (`ClickedWhileReading`). A place the reading stopped at, or kept for a file, still starts at its
   sentence. `TestClick` drives both in the self-test.
2. **Voice variation** (Speed and pauses, "Advanced setting", `VariationBar`): settings.json
   `variation_percent`, 0 to 150, default 75 (`Store.VariationDefault`, engine `text::variation`). The
   engine multiplies the model's noise_scale / noise_w by it when it LOADS a model (`load_model`; the
   model cache key includes it), so a change is heard when a program next takes up a voice: Preview at
   once (new process), open programs after a restart. Only written to the file once it differs.
   Reset to defaults puts it back to 75.
3. README: simple install (no registry edits, no manual downloads), Balabolka's pitch control works.
4. Everything listed under 0.13.2 below (it was never built on its own; 0.13.3 was the first build).

## 0.13.2 (16 Nov 2026): built and delivered as part of 0.13.3

His asks, and what was written:
1. Installer: a yellow note before "testing each voice through Windows' speech API" that the first start
   of Config or the Reader after installing may take a while.
2. Busy pointer while starting: `Application.UseWaitCursor` on from Main until the form's Shown, in
   Config and the Reader. (Can't cover the wait before the program runs at all, Windows' check: told him.)
3. Add or remove voices: Show + Additional languages at the top; the instruction line under them (12 px
   above), then the list.
4. Speed and pauses: full stop after the dictation speed line; Reset to defaults line 18 px lower.
5. Voice manager: one note under each slider (Volume, Boost, Dictation speed, via `AddNote(soundGrid)`);
   only "Changes are saved straight away: press Preview to hear them." at the bottom.
6. Preview text in the voice's language: `PreviewTexts` (English = his new wording, verbatim; one
   translation per `Catalogue.Offered` language, written by Claude, unchecked; 'Paragraph gap' kept in
   English as the window says it). The box switches language when a voice is selected, unless the text
   was typed over (`IsOneOfThem`, which also knows the pre-0.13.2 English text, `App.OldPreviewText`).
   The Speed tab's Preview uses the chosen voice's language the same way (`languageOfVoice`).
7. README: other languages (interface English, translations unchecked, less tested, no ready-made
   rules, how to find and share the dictionary files in C:\ProgramData\Modern Voices for Windows),
   how it was made (£18, one month of Claude Pro, can't afford to maintain), the first-start hang as
   the one known bug, testing (Windows 10; once on Windows 11), Balabolka, free and open source, and
   the star line reworded ("if you star this one, star that one too").

## 0.13.1 (16 Nov 2026), delivered, not yet confirmed by him: the rest of his 16 Nov batch

1. **Preview box** lines up with the sliders: `alignPreview` sets the "Preview says" column to whatever
   width puts the box's left edge on the Volume slider's (measured after layout; the self-test checks the
   x positions are equal). The gap above it is doubled (+Px(9) top margin).
2. **Mute:** the speaker beside the output volume (`MuteIcon`) is clickable: `ToggleMute` sets 0 and
   back to the value before (`unmutedVolume`, not kept between runs: 100 if opened muted). `ShowMute`
   swaps the glyph (E767 speaker / E74F muted). The value reads "0% (muted)".
3. "Override pause duration for the selected voice" (was Modify).
4. **Open Reader** moved to the bottom strip, left of Close (header now tabs + moon only). **Apply
   changes** shows only on Add or remove voices (`ShowPage`), with Done.
5. **Descriptions rewritten plainly** on every tab, in tooltips, the languages window, the speaker list
   label, and kit/README.txt; World of Warcraft removed from Config, installer messages and the README.
   The line under each pause shows only when the speed isn't 100%: "At 150% speed you hear about 500 ms."
6. **Comma pause default 60 ms** (`Store.CommaDefault`, engine `DEFAULT_PAUSES`, VoiceConvert
   `PauseDefaults`). Only applies where nothing is saved: a fresh install, or Reset to defaults.
   Existing installs keep their saved comma value (told him).
7. Dictation speed's line is his exact words.

Self-test note: a test that moves the output slider must `StopTimers()` and delete a settings file it
created, or the installer later finds settings.json and skips writing its defaults (broke run #105).

## 0.13.0 (16 Nov 2026), delivered, not yet confirmed by him: other languages

His ask: Windows' language table for every language; an "Additional languages" button with a tick list
(easy European languages only); ticked languages join Add or remove voices and the "Show" dropdown;
pronunciation lists per language, used by that language's voices; a way to switch a list off.

- **Engine:** a voice's language is the letters before `_` in its model folder (`text::language_of`,
  kept in `Voice.language`). Rules: `dictionary.txt` for English, `dictionary-<lang>.txt` for others
  (`text::dictionary_file`). A line `#off` anywhere in a list switches the whole list off. `money()`
  (pounds/dollars said the English way) runs for English voices only.
- **Installer:** `Get-Catalogue` returns every language (objects have `Family`); `$Lcids` covers all of
  Piper's language codes (unknown: 409); `-UsEnglishTags` only touches English voices; a fresh install's
  default voices are still English. `Set-Dictionary ... $true` writes `#off`; `Add-DefaultRules` keeps it.
- **Accent** (the word in brackets in a voice's name) is worked out the same way in Install.ps1
  (`Get-Catalogue`) and Config (`Catalogue.Parse`): British/American; else the language's English name,
  plus ", Country" when the catalogue has that language from several countries ("Spanish, Mexico").
  Keep the two in step or names will differ between Config and Windows.
- **Config:** `Catalogue.Offered` is the list of languages offered (European, left to right, spaces
  between words; Cyrillic and Greek included; no Arabic/Persian/Hebrew/Chinese etc.). `Languages.Enabled`
  (HKCU `Languages`, space-separated; `Override` for the self-test). `LanguageShown(family)`: English,
  enabled, or has a ticked voice (installed voices are never hidden). `FillLanguageChoices` fills both
  "Show" (`LanguageChoice` items; the first four are as before) and the Pronunciation tab's "Rules for".
  `LanguagesDialog` builds the tick window (`ChooseLanguages` shows it; `SetLanguages` applies).
  Pronunciation: `wordFamily`, `App.DictionaryFor`, "Use these rules" (`WordListOnBox`) writes `#off`.
- **Where I departed from his words (told him):** he asked for a button opening a window of pronunciation
  lists to enable/disable; I put a "Rules for" dropdown and a "Use these rules" tick on the tab instead,
  since a selector is needed anyway to say which list is shown. Default rules exist for English only:
  no invented lists for languages neither of us can check.
- **Tests:** engine unit tests (language_of, #off); Config self-test (German chosen/unchosen, its rules
  file, pictures languages.png and german.png); Test-Kit installs de_DE-thorsten-medium, checks Language
  407, that it speaks, and that a German rule changes German speech only.
- **A flaky Reader test:** run #102 failed "Pause keeps the reading" / "Stop keeps the place (0)" with no
  Reader change; the next run passed. If it recurs, look at the timing around `PauseOrResume` in the
  Reader's self-test before suspecting the engine.
- **Not done / ideas:** Preview text and "Hear it" are English whatever the voice; Reader untouched; no
  default rules for other languages; sentence/pause handling untested by ear in any other language.

## 0.12.7 (16 Nov 2026), delivered, not yet confirmed by him

1. **Header line taken out, spacing kept.** `headerLine` stays as a 1 px spacer painted the page colour
   (no Tag); `Palette.Line` is gone.
2. **Play sample stops a sample playing.** `Sample.Playing` / `Sample.Check()` (a sample that played to
   its end is let go: a 200 ms WinForms timer compares `MediaPlayer.Position` with `NaturalDuration`, so
   no reliance on WPF events). The button reads "Stop sample" while one plays (`PlayButton`, minimum
   width fits both words). Clicking a speaker's name still plays (restarts); only the button toggles.
3. **Add or remove voices columns:** Voice, Speakers, Accent, Quality, Download, Catalogue key.
4. **Sliders level with their labels:** `Lower` drops a slider Px(13) (was 10); Speed and pauses' sliders
   are lowered too now (they weren't at all). Screenshots: label middle and slider line at the same y on
   both tabs.

## 0.12.6 (15 Nov 2026), delivered, not yet confirmed by him

He confirmed the 0.12.5 first-start fix ("it opened quicker"). The Reader had the same fix already.

1. **Header bar (Config).** `tabStrip` (tabs | stretch | Open Reader | moon) has bottom padding equal to
   the old gap (Px(6)+3, less the line), then `headerLine` (1 px Panel; black until 0.12.7, now a page-coloured
   spacer) docked Top under it. Docking order matters: `pageHost`, then `headerLine`, then
   `tabStrip` are added in that order. Pages and their scroll bars start below the line. The Voice
   manager's page has no top padding and `InstalledList` no top margin, so list top = scroll bar top =
   just under the line (self-test checks this; use the list's outer edge, `PointToScreen(Point.Empty)`
   is inside its 2 px border). Other tabs keep their 6 px top padding (text first).
2. **Tabs:** 1 px border. Not selected: `Palette.Tab` fill (dark 64,64,64), `TabEdge` border, `TabText`.
   Selected: `Input` fill, `TabOn` border (near white in dark), bold. Set in `ShowPage`.
3. **Open Reader** (`ReaderButton`, `OpenReader`): starts `reader\ModernVoicesReader.exe`; the Reader's
   own "already running" question handles a second copy. The window's minimum width is now worked out
   from the header (733 px at 100%), so the gap after the last tab never closes.
4. **Bigger lists:** ListViews use `listFont` (10 pt; `listBold` for installed voices) and a
   `StateImageList` from `Boxes.Images(Px(16))`; dark rows (`DrawCell`) draw the same `Boxes.Draw`.
   The speakers' list is `BigCheckList` (a CheckedListBox that draws its own rows: 12 pt, 19 px boxes;
   `TextLeft` is where a name starts, used by `SpeakerMouseDown`); its column width fits the longest
   name shown (`UpdateSelection`).
5. **Reader opens on Windows' default voice** (`StartVoiceIndex`): the default (HKCU DefaultTokenId, else
   `synth.Voice.Id`), else the voice used last time, else the first. This replaces "the voice used last".

## 0.12.5 (15 Nov 2026), delivered, not yet confirmed by him

His words: "config hangs for a long time before opening the 1st time it runs after the install. add a
'done' button next to the 'apply changes' button in add remove voices, which will open the voice manager
tab. reduce the height of the 'list of voices box' in add/remove by 40%. add a dropdown menu above voices
list, so you can select all voices, english only, american only (this would also be used later if/when
adding multi language support). increase the height of the 'speakers preview list box' by 100%."

1. **Slow first start (a likely cause fixed, NOT reproduced).** Config's own start is under a second on
   the build machine (self-test notes "window built in N ms; Windows took N ms to start the program"),
   so the wait is Windows', before the program runs. Likely cause: a kit unzipped from a download has the
   "from the internet" mark (Zone.Identifier) on every file, `Copy-Item` keeps it, and Windows checks a
   marked program (SmartScreen / Defender's cloud check) the first time it starts. `Install-Engine` now
   runs `Unblock-File` over config, reader, engine and installer folders. Test-Kit marks the kit's exes
   (after the kit's own Config test: `Start-Process` on a marked exe waits for ever at Windows' "Open
   file?" box, which hung run #94 for 25 min) and checks the installed ones aren't marked.
   **If he says it's still slow:** ask for `%TEMP%\ModernVoicesConfig-start.log` (`Program.Startup`: one
   line per start, last 30: "Windows to program N ms, voice list read at, window built at, window shown
   at"). A big first number = Windows/antivirus (next idea: nothing we control but Defender's scan of a
   new exe; could try ngen, or not re-signing unchanged exes); big later numbers = our code.
2. **Done button** (`DoneButton`, bottom strip between Apply changes and Close; visible on page 3 only,
   set in `ShowPage`; disabled while applying): `ShowPage(0)`. It does not apply anything.
3. **"Show" dropdown** (`VoiceFilterBox`: All voices / English only / American only / British only; I
   added British only unasked, told him; All voices at every start, not saved). `VoiceItems` is the
   master list of rows; `AddList` shows those `FilterShows` lets through (`ApplyVoiceFilter`; flag
   `filtering` mutes the list's events meanwhile). **Anything that reads what is ticked must use
   `VoiceItems`, never `AddList.Items`** (hidden rows keep their ticks; reading the list would make Apply
   remove hidden voices). `Catalogue.Parse` still keeps English only, so "All voices" = "English only"
   until other languages are let in; `FilterShows` goes by `Entry.Code`.
4. **Heights:** the voice list and the speaker list share the tab's spare height 42% / 58% (both Percent
   rows of `addGrid`). At the default window: voices 266 -> 150 px (-44%), speakers 110 -> 212 px (+93%).
   The intro line was shortened to one line to make the room.

## 0.12.4 (15 Nov 2026), delivered, not yet confirmed by him

His words: "in the add remove voices tab: if a voice only has 1 speaker put the name of that speaker in
the play sample box below. same as the voices with multiple speakers. add a 'only show selected speakers'
toggle for the play sample box ... default this setting to off."

1. **One-speaker voices list their speaker** (`ConfigForm.UpdateSelection`, `OnlySpeaker`). The name is
   `Entry.FirstSpeaker`, else `Catalogue.PrettyName(Entry.Name)` (single-speaker Piper voices have an
   empty `speaker_id_map`, so in practice it's the voice's pretty name, e.g. "Alba"). Its box MIRRORS the
   voice's own tick both ways: `SpeakerTicking` sets the AddList tick for a one-speaker voice and returns;
   `AddList.ItemChecked` re-fills the speaker box (BeginInvoke `UpdateSelection`) when the selected voice
   is one-speaker or the filter is on. Nothing is written to `Speakers` for it (`SaveSpeakers` still returns
   for `!Several`). Clicking the name plays the sample (speaker 0) as before.
2. **"Only show selected speakers"** (`OnlyTickedBox`, between Play sample and the speaker label; off at
   every start, not saved). On: only ticked speakers listed; a ticked multi-speaker voice with none ticked
   shows its `FirstSpeaker` ticked (that's what gets installed; `SaveSpeakers` then stores it explicitly,
   same `PickedIds`); an unticked voice with none ticked shows an empty box and a label saying so.
   Unticking a speaker while filtered leaves it listed (unticked) until the next refresh, so a misclick
   can be undone; ticking/unticking the voice itself refreshes.
3. **Start menu ordering attempts removed** (`Register-Program`): no sleeps, no remake block, no program
   file stamping. Windows' "Recently added" order is not by entry creation time nor by the program's file
   date (three builds tried). Idea dropped; don't try again unless he asks.

SelfTest has new checks for both features (filter off at start; filtered count; one-speaker name, mirror
both ways, nothing in `Speakers`; filter hides an unticked one-speaker voice).

**Confirmed by him on 15 Nov:** the 0.12.2 loudness fix works. Not yet commented on: the 0.12.1 slider
alignment, Reset position, shorter voice list, and the reduced variation (`VARIATION` 0.75).

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

The user-facing documentation is `kit/README.txt`, and it is accurate for 0.12.0. Read it to see what
each feature does.

## Lumoti's standing rules

- **Before any build request, say whether it needs Fable (complicated: new features, engine work,
  multi-part changes) or Opus 5.5 (simple and safe changes to working code)**, so he can switch model
  first. If he's already on the right one, say so and proceed.
- The version number goes in the filename of every build he downloads: `ModernVoicesForWindows0.12.0.zip`.
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
- It runs unelevated (manifest `asInvoker`, since 0.12.0). The administrator's part (nickname, Apply
  changes) is a second copy, `--helper PIPE PID`, started with the `runas` verb at first need and kept
  until the window closes: `Elevated` (window side, owns the pipe) and `Helper` (elevated side). Lines over
  the pipe: `nick <id64> <nick64> <display64>` or `apply <0|1 US tags> <ids64>` one way; `say <line64>` and
  `done <code> [message64]` back. Both ends check the other's process id (GetNamedPipeClient/ServerProcessId),
  the helper also that the window is the same exe, and it validates ids against a pattern. Since 0.18.2 a
  third: `gender <id64> <gender64>`. Keep it to those.
- On CI everything is already elevated with UAC off, so `runas` starts the helper with no prompt: the
  pipe path is tested, the UAC prompt itself never is.
- It is DPI-aware (the PowerShell window was not: Windows stretched it, so `Px()` scaling at 125% and
  above is new and not yet seen by Lumoti).
- `Store` in Config.cs, `VoiceConvert.ps1` and the engine all read the settings folder's files: keep the
  three in step.

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

## Current state (0.13.0)

Everything above works and is tested on the CI Windows machine. Lumoti uses it daily and sends UI tweaks
one batch at a time; every batch so far is done and delivered. He installs each build over the last
(update in place works in practice). Where things live, for the features added since 0.9.4:

**Engine (`engine/src/text.rs`, `lib.rs`)**
- `with_end_mark` adds a full stop to an unmarked sentence end (the voice clipped a last word without
  one); `TAIL_MS` of silence follows a Speak call's last word.
- `money` says sums aloud ("£150" as "150 pounds", "£1.50" as "1 pound 50", "$3.5m" as "3.5 million
  dollars"), applied after the pronunciation dictionary.

**Settings folder (0.12.0)**
- `%ProgramData%\Modern Voices for Windows\`: `settings.json` (speed_percent, pauses, output_percent),
  `dictionary.txt`, `voices\<voice folder name>.json` (a voice's own loudness, length_scale, pauses). The
  installer makes it and grants Users Modify (`Initialize-SettingsFolder`), and moves settings.json and
  dictionary.txt there from the program folder of an older install. Uninstall deletes it.
- A voice's own file is read IN FRONT of its voice.json (engine `Model::settings`, `Store.VoiceText`,
  `Get-VoiceText`: the texts are joined, and the key search finds the first). voice.json keeps the
  defaults from conversion, and the settings of an install from before 0.12.0, so nothing per-voice
  needed migrating. Removing a voice deletes its own file.
- The engine finds all of it from the token's `SettingsFile` (now the ProgramData path): dictionary.txt
  beside it, `voices\` beside it (`own_file`).
- Why ProgramData and not per-user: the engine runs in any process and user, and the installer runs
  elevated (possibly as another account). The cost: any user of the PC can change them. They are only
  numbers and pronunciation text, parsed in Rust and clamped; voice.json (which feeds sherpa/espeak) is
  NOT user-writable.

**Volume and output volume (0.12.0)**
- Per-voice Volume slider is 0 to 100% again (0.12.1). 0.12.0 let it go to 200% (`peak` to 2, a soft
  limiter above full): Lumoti said it "sounds horrid", so it is gone, engine and all. A voice file left
  with a peak above 1 is read as 1. Don't offer it again.
- Output volume: `output_percent` 0 to 100 in settings.json, for every voice. The engine applies it in
  `Output::write`, which now sends audio in 1024-sample pieces and re-reads the setting (a file stat)
  for each, ramping between levels: it changes mid-sentence, after whatever SAPI has buffered. Config's
  slider (Voice manager, beside the default-voice box) saves 60 ms after the last movement. Programs
  that take the audio to a buffer and play it later only get it per utterance.
- SAPI's own volume (`GetVolume`, SPVES_VOLUME) is still read once per Speak call.

**Config (0.12.0)**
- `config/ModernVoicesConfig.exe`, installed to `<app>\config\`. The Start menu entry, `Modern Voices
  Config.cmd`, Installed apps' Modify and the Reader's cog all start the exe. One instance (mutex): a
  second start brings the first to the front. No permission prompt to open; one prompt at the first
  nickname or Apply changes, then none until the window is closed (see the helper, under Pitfalls).
- It opens from `<app>\catalogue.json` (the installer downloads it at install, update and Apply), so
  opening needs no internet; without that file it downloads the list itself (to %TEMP%).
- Apply changes: the helper runs `Install.ps1 -ExactVoices -Unattended -NoPause -Progress [-Voices a,b]
  [-UsEnglishTags]` hidden and relays its lines (status line; `##progress N` moves the bar). Exit 0 =
  done, 2 = a voice failed its test, else the last `Stopped:` line is shown.
- After Apply (`SetDefaultAfterApply`, Lumoti's rule): if Windows' default voice is not an installed
  Modern Voice, and voices were added (or the default was a Modern Voice just removed), the first voice
  of the list becomes the default (HKCU, done by the unelevated window, so it is the right user's).
  The unattended installer does not do this.
- Preview buttons toggle (`TogglePreview`): pressed while their voice speaks they stop it; with another
  speaking, that stops and theirs starts. The label reads Stop meanwhile (`UpdatePreviewButtons`; width
  kept by `KeepWidth`). All three Stop buttons are gone (Voice manager, Speed and pauses, Hear it).
  Lumoti asked for the Voice manager's; the other two were changed to match.
- Voice manager row order: Preview, Make default voice, nickname box, Set nickname; Windows speech
  settings at the right. Under "Preview says": the default voice in a bordered box (`defaultBox`, tag
  "box", border drawn in Paint), then the speaker symbol (E767) and the output volume slider.
- Install.cmd (fresh install or update) runs Install.ps1 in a console, then starts the installed Config
  through explorer.exe (so that it is NOT elevated) and the console closes. The console is no longer
  moved or resized.
- Window 900 x 1000 at 100%, centred, capped to the screen.

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

**Reader (0.12.0)**
- Read screen asks first (`Program.ScreenWarningWindow`, built by `AskWindow` like the already-running
  prompt): Yes, Cancel, "Do not display this warning again" (`State.ScreenWarningOff`, saved on Yes).

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

**Start menu "Recently added" (0.12.1 to 0.12.3, dropped in 0.12.4):** Windows 10 listed Test, Repair,
Config, Reader whatever order or dates the installer gave. All the ordering code is gone.

**0.12.2 (his feedback on 0.12.1): "every now and again it says one of the lines really loud".**
Cause, measured (sherpa-onnx in Python here: `pip install sherpa-onnx==1.12.40`, models from the
k2-fsa/sherpa-onnx `tts-models` GitHub release; 2.x wheels have no espeak): the per-line PEAK
normalisation that `apply_loudness` had from the start (as Piper's own program does). The models' raw
output is even line to line (sd about 1 dB), but a line's one loudest moment is not: a line without a
stray peak ("No.", "Hello there.", many short ones) came out up to 8 dB louder than the rest (Northern
English Male: -13.5 to -5.2 dB). Now `apply_loudness` scales by `text::speech_level` (rms of the louder
half of 1024-sample stretches) times `CREST` 4.2, and `ease`s what would pass the peak under it: the
spread is under 1.5 dB on three voices, typical loudness as before or 1 dB more. A line's peak is no
longer exactly the Volume (Test-Kit checks 0.6 to 1 of it). Not confirmed by ear.

**0.12.1 (his feedback on 0.12.0):** Volume back to 100% max. Voice manager: slider bars lowered by
`Px(10)` (`ConfigForm.Lower`: the TrackBar is made that much shorter and given that top margin, so rows
keep their height and labels don't move; the output bar's line is level with the middle of the default
voice box), Reset sits right of the "How <voice> sounds" label, voice list 240 high (was 300; he wrote
"font list box", taken as the voice list). Engine: `VARIATION` 0.75 multiplies noise_scale and noise_w at
model load (he found Piper's variation "a little bit strong"; 0.75 is a guess, unheard). Not confirmed by him: all of it.

**Not yet confirmed on his machine (0.12.0):** the UAC prompt appearing once (first nickname or Apply) and
not again; Config opening unelevated after Install.cmd (the explorer.exe hand-off); the output volume
changing mid-sentence and how long the lag is; the new Voice manager layout;
the Reader's Read screen warning; a second Windows account using the settings folder.

**Not yet confirmed on his machine (0.11.0):** everything about the C# Config on a real screen: its look
next to the old window (text is drawn by GDI now, and it is DPI-aware), Play sample and Preview (CI has no
sound device), a real voice download through Apply changes with the progress bar moving (CI only added a
speaker of a model already there), the update from 0.10.11 closing nothing it shouldn't, Windows 11.

**Not yet confirmed on his machine (older):** the cog opening Config; the icon in the taskbar and Start menu;
pasting from real browsers keeping paragraphs and bullets; the volume slider changing live (0.10.6); the greyed Apply button's
look in dark mode.

## Ideas discussed but not built (only when Lumoti asks)

- **Pronunciation "ignore list": not wanted** (18 Nov 2026). He uses an ordinary rule instead: "***" said
  as "," (or with "Say it as" left empty, which drops it; the engine allows an empty replacement). Note,
  told him: rules are applied AFTER the text is split at commas (`clauses` in lib.rs runs on the original
  text, `Dictionary::apply` per piece in `say_piece`), so a comma a rule puts in gets only Piper's own
  short pause, not the Comma pause setting. Making it honour the setting is engine work (Fable-sized).

- **Accurate time-left countdown:** run the text through the voice silently in the background and
  measure the real length. Also remember each voice's measured pace for the initial estimate. Agreed in
  principle (the estimate said 1:27 where the real audio was 1:22); Opus-sized.
- **Old .doc support** in the Reader (PDF and MP3 were done in 0.16.0).
- **Repair and Test as buttons inside Config** instead of Start menu entries (offered; he kept them).
- **A "save text to a file" button** in the Reader (it replaced the removed read-later list; Lumoti saves
  files himself for now).
- **More TTS Enhanced features:** ask Lumoti for the addon's source (TTSEnhanced0.36.1) if needed. Pauses and the pronunciation dictionary are done.
  Raid-icon stripping was left out as WoW-only.
- Ruled out by Lumoti: a browser extension, double right-click or double Ctrl+C triggers, LM Studio
  integration (for now), and a tray icon.
