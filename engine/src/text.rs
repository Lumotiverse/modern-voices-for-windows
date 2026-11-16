//! Text and audio helpers with no Windows in them (tested on their own): where sentences and words
//! are, about when each word is heard, bookmark numbers, pitch shifting by resampling, loudness, and
//! numbers from a voice.json.

pub const SPACE: u16 = b' ' as u16;

pub fn char_of(unit: u16) -> char {
    char::from_u32(unit as u32).unwrap_or('\u{fffd}')
}

pub fn is_space(unit: u16) -> bool {
    char_of(unit).is_whitespace()
}

pub fn is_wordy(unit: u16) -> bool {
    char_of(unit).is_alphanumeric()
}

/// A sentence longer than this (UTF-16 units) is cut at a comma or a space, so speech starts soon.
pub const LONGEST_SENTENCE: usize = 400;

/// The sentences of `text`, as [start, end) ranges without the space around them: each ends after
/// . ! ? or … followed by a space (and any closing quotes or brackets), at a line break, or where a
/// long one is cut. Ranges with nothing to say (no letters or digits) are left out.
pub fn sentences(text: &[u16]) -> Vec<(usize, usize)> {
    let closing = |u: u16| matches!(char_of(u), '"' | '\'' | '\u{201d}' | '\u{2019}' | ')' | ']' | '}' | '\u{bb}');
    let mut found = Vec::new();
    let mut start = 0;
    let mut i = 0;
    let push = |from: usize, to: usize, found: &mut Vec<(usize, usize)>| {
        let mut a = from;
        let mut b = to;
        while a < b && is_space(text[a]) { a += 1; }
        while b > a && is_space(text[b - 1]) { b -= 1; }
        if text[a..b].iter().any(|&u| is_wordy(u)) {
            found.push((a, b));
        }
    };
    while i < text.len() {
        let c = char_of(text[i]);
        let mut end = None;
        if c == '\n' || c == '\r' {
            end = Some(i + 1);
        } else if matches!(c, '.' | '!' | '?' | '\u{2026}') {
            let mut j = i + 1;
            while j < text.len() && (matches!(char_of(text[j]), '.' | '!' | '?') || closing(text[j])) { j += 1; }
            if j >= text.len() || is_space(text[j]) {
                end = Some(j);
            }
        }
        if end.is_none() && i - start >= LONGEST_SENTENCE {
            // Cut at the last comma-like mark, else the last space, in the second half.
            let window = start + LONGEST_SENTENCE / 2..i;
            let cut = window.clone().rev().find(|&k| matches!(char_of(text[k]), ',' | ';' | ':'))
                .or_else(|| window.rev().find(|&k| is_space(text[k])))
                .map(|k| k + 1)
                .unwrap_or(i);
            end = Some(cut);
        }
        if let Some(e) = end {
            push(start, e, &mut found);
            start = e;
            i = e;
        } else {
            i += 1;
        }
    }
    if start < text.len() {
        push(start, text.len(), &mut found);
    }
    found
}

/// The words of text[from..to): maximal stretches without spaces, as [start, end) ranges.
pub fn words(text: &[u16], from: usize, to: usize) -> Vec<(usize, usize)> {
    let mut found = Vec::new();
    let mut i = from;
    while i < to {
        while i < to && is_space(text[i]) { i += 1; }
        let start = i;
        while i < to && !is_space(text[i]) { i += 1; }
        if i > start {
            found.push((start, i));
        }
    }
    found
}

/// How long a word takes to say, roughly, against the others in its sentence: its letters and digits,
/// one for the word itself, and the pause its punctuation makes.
pub fn weight(word: &[u16]) -> f32 {
    let letters = word.iter().filter(|&&u| is_wordy(u)).count() as f32;
    let pause = match word.last().map(|&u| char_of(u)) {
        Some(',' | ';' | ':' | '\u{2014}' | '\u{2013}') => 2.0,
        Some('.' | '!' | '?' | '\u{2026}') => 3.0,
        _ => 0.0,
    };
    letters + 1.0 + pause
}

/// A bookmark's number, as SAPI gives it to programs beside its name: the number its name starts with
/// (what C's _wtol makes of it), else 0.
pub fn bookmark_number(name: &[u16]) -> i64 {
    let text = String::from_utf16_lossy(name);
    let text = text.trim_start();
    let (sign, digits) = match text.strip_prefix('-') {
        Some(rest) => (-1, rest),
        None => (1, text.strip_prefix('+').unwrap_or(text)),
    };
    let end = digits.find(|c: char| !c.is_ascii_digit()).unwrap_or(digits.len());
    digits[..end].parse::<i64>().map(|n| sign * n).unwrap_or(0)
}

/// `samples` played `factor` times as fast (linear interpolation): higher by that factor, and
/// shorter; the caller made them that much slower to begin with.
pub fn resample(samples: &[f32], factor: f32) -> Vec<f32> {
    if (factor - 1.0).abs() < 1e-3 || samples.len() < 2 {
        return samples.to_vec();
    }
    let length = (samples.len() as f32 / factor) as usize;
    (0..length)
        .map(|i| {
            let position = i as f32 * factor;
            let index = position as usize;
            let next = (index + 1).min(samples.len() - 1);
            let weight = position - index as f32;
            samples[index] * (1.0 - weight) + samples[next] * weight
        })
        .collect()
}

/// Where each word's sound starts in a sentence's audio (one position per word): the voiced part (from
/// the first to the last sound above a fiftieth of the loudest) shared out by the words' weights.
pub fn word_starts(audio: &[f32], weights: &[f32]) -> Vec<usize> {
    let loudest = audio.iter().fold(0f32, |m, s| m.max(s.abs()));
    let threshold = loudest / 50.0;
    let first = audio.iter().position(|s| s.abs() > threshold).unwrap_or(0);
    let last = audio.iter().rposition(|s| s.abs() > threshold).map(|p| p + 1).unwrap_or(audio.len());
    let span = last.saturating_sub(first) as f32;
    let total: f32 = weights.iter().sum::<f32>().max(1.0);
    let mut before = 0f32;
    weights.iter().map(|w| {
        let at = first + (span * before / total) as usize;
        before += w;
        at.min(audio.len())
    }).collect()
}

/// A number from a voice.json (`"length_scale": 1.2`, wherever it is), else `default`. The keys
/// used are unique in that file, so a plain search does; no JSON parser for a few numbers.
pub fn number(info: &str, key: &str, default: f32) -> f32 {
    let Some(at) = info.find(&format!("\"{key}\"")) else { return default };
    let rest = info[at + key.len() + 2..].trim_start().strip_prefix(':').unwrap_or("").trim_start();
    let end = rest.find(|c: char| !(c.is_ascii_digit() || c == '.' || c == '-')).unwrap_or(rest.len());
    rest[..end].parse().ok().filter(|v: &f32| v.is_finite() && *v >= 0.0).unwrap_or(default)
}

/// How loud a piece of speech is on the whole: the level (rms) of the louder half of its 1024-sample
/// stretches (about 50 ms each), which leaves out the silence around and between its words.
pub fn speech_level(audio: &[f32]) -> f32 {
    let mut levels: Vec<f32> = audio.chunks(1024).map(|c| c.iter().map(|s| s * s).sum::<f32>() / c.len() as f32).collect();
    if levels.is_empty() {
        return 0.0;
    }
    levels.sort_by(|a, b| b.total_cmp(a));
    let half = (levels.len() + 1) / 2;
    (levels[..half].iter().sum::<f32>() / half as f32).sqrt()
}

/// A voice's loudest moments are about this many times its level on the whole (measured: 4.2 to 5 for
/// Piper's voices, line by line from 2.4 to 7).
const CREST: f32 = 4.2;

/// A sentence's audio made as loud as the voice is set:
///   peak:  1 = as loud as sound goes for speech (its loudest moments at full), 0.5 half that. 0 = left
///          as the model made it.
///   boost: above 1, the quieter parts lifted more than the loud ones (tanh, a soft limiter), so it
///          is louder overall without its peaks clipping. 1 = none.
/// Every line comes out as loud as the next. It is scaled by how loud it is on the whole
/// (`speech_level`), not by its one loudest moment as before 0.12.2 (and as Piper's own program does):
/// that way a line with a stray loud moment came out quiet, and a line without one (many short ones,
/// "No.", "Hello there.") up to 8 dB louder than the rest. The moments that would now pass full are
/// eased under it (the same soft limiter).
/// The level is taken as at least what goes with a loudest moment of 0.01, so near-silence is not blown up.
pub fn apply_loudness(audio: &mut [f32], peak: f32, boost: f32) {
    if peak <= 0.0 || audio.is_empty() {
        return;
    }
    let full = (speech_level(audio) * CREST).max(0.01);
    if boost > 1.0001 {
        // (What is at `full` comes out at the peak, as it always did; the little above it is eased under.)
        let level = peak / boost.tanh();
        for s in audio.iter_mut() {
            *s = ease((*s / full * boost).tanh() * level, peak, 0.85);
        }
    } else {
        for s in audio.iter_mut() {
            *s = ease(*s / full * peak, peak, 0.7);
        }
    }
}

/// A sample kept under `peak`: unchanged up to `knee` of it, above that eased towards the peak (tanh),
/// never past it.
fn ease(sample: f32, peak: f32, knee: f32) -> f32 {
    let (knee, room) = (knee * peak, (1.0 - knee) * peak);
    let size = sample.abs();
    if size <= knee {
        sample
    } else {
        (knee + room * ((size - knee) / room).tanh()).copysign(sample)
    }
}

/// How loud everything comes out, of what the voices' own settings make (a settings file's
/// `output_percent`, 0 to 100): 1 unless set, 0 for silence. Read while speaking, so it can be turned
/// down in the middle of a sentence.
pub fn output_level(info: &str) -> f32 {
    number(info, "output_percent", 100.0).clamp(0.0, 100.0) / 100.0
}

/// A piece of speech on its way out, at the output level: from `from` at its first sample to `to` at
/// its end (a change of level spread over the piece, so it makes no click).
pub fn scale_into(piece: &[i16], from: f32, to: f32, out: &mut [i16]) {
    let n = piece.len().max(1) as f32;
    for (i, (s, o)) in piece.iter().zip(out.iter_mut()).enumerate() {
        let level = from + (to - from) * (i as f32 / n);
        *o = (*s as f32 * level) as i16;
    }
}

/// Pause lengths, in milliseconds at normal speed: what each gap adds to the voice's own pause.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Pauses {
    pub sentence_ms: f32,
    pub comma_ms: f32,
    pub paragraph_ms: f32,
}

/// Before any are set: 0.5 s after a sentence, 1.2 s at a paragraph, 60 ms at commas.
pub const DEFAULT_PAUSES: Pauses = Pauses { sentence_ms: 500.0, comma_ms: 60.0, paragraph_ms: 1200.0 };

impl Pauses {
    /// From a settings file's or voice.json's text (keys sentence_ms, comma_ms, paragraph_ms); the
    /// default for any it does not set.
    pub fn read(info: &str) -> Pauses {
        Pauses {
            sentence_ms: number(info, "sentence_ms", DEFAULT_PAUSES.sentence_ms).min(10_000.0),
            comma_ms: number(info, "comma_ms", DEFAULT_PAUSES.comma_ms).min(10_000.0),
            paragraph_ms: number(info, "paragraph_ms", DEFAULT_PAUSES.paragraph_ms).min(10_000.0),
        }
    }
}

/// The speed every voice speaks at before a program's rate, unless set: 100%, the voices' own pace.
pub const DEFAULT_SPEED_PERCENT: f32 = 100.0;

/// The speed for every voice a settings file sets (`speed_percent`, 25 to 200) as a multiple of the
/// voices' own: 1.25 for 125%.
pub fn global_speed(info: &str) -> f32 {
    number(info, "speed_percent", DEFAULT_SPEED_PERCENT).clamp(25.0, 200.0) / 100.0
}

/// Samples of silence for a pause of `ms` (at normal speed) when speaking `speed` times as fast:
/// faster speech, shorter pauses, in proportion.
pub fn gap_samples(ms: f32, speed: f32, sample_rate: u32) -> usize {
    if ms <= 0.0 || speed <= 0.0 {
        return 0;
    }
    (ms / 1000.0 * sample_rate as f32 / speed).round() as usize
}

/// Whether a line break lies in text[from..to): between two sentences, a new paragraph.
pub fn line_break_between(text: &[u16], from: usize, to: usize) -> bool {
    text[from.min(to)..to].iter().any(|&u| u == b'\n' as u16 || u == b'\r' as u16)
}

/// The clauses of the sentence text[from..to): it split after each , ; or : that a space follows
/// (not "3,000" or "10:30"), each with its mark. Pieces with nothing to say join the one before.
pub fn clauses(text: &[u16], from: usize, to: usize) -> Vec<(usize, usize)> {
    let mut found: Vec<(usize, usize)> = Vec::new();
    let mut start = from;
    for i in from..to {
        let c = char_of(text[i]);
        if matches!(c, ',' | ';' | ':') && i + 1 < to && is_space(text[i + 1]) {
            if text[start..=i].iter().any(|&u| is_wordy(u)) {
                found.push((start, i + 1));
                start = i + 1;
            }
        }
    }
    if start < to {
        if text[start..to].iter().any(|&u| is_wordy(u)) || found.is_empty() {
            found.push((start, to));
        } else if let Some(last) = found.last_mut() {
            last.1 = to;
        }
    }
    // Without the spaces around them.
    found.into_iter().map(|(mut a, mut b)| {
        while a < b && is_space(text[a]) { a += 1; }
        while b > a && is_space(text[b - 1]) { b -= 1; }
        (a, b)
    }).collect()
}

/// Silence written after the last thing a Speak call says, in milliseconds: the voice's audio ends on
/// the final phoneme's last sample, and audio devices and format converters often swallow the last few
/// ms of a stream, taking a quiet final "s" or "t" with them. Piper's own program adds 200 ms after
/// every sentence; this is added once, at the end.
pub const TAIL_MS: u32 = 150;

/// The text as the voice should be given it: ending in a full stop when it ends in a letter or digit
/// (or a closing quote or bracket after one). A Piper voice says a sentence with no end mark more
/// abruptly, clipping its last sound, since the end mark is part of what it learned a sentence is.
/// Pieces ending in a comma, colon or the like are left alone: those marks give the voice its ending.
pub fn with_end_mark(text: &str) -> String {
    let trimmed = text.trim_end();
    let last = trimmed.chars().rev().find(|c| !matches!(c, '"' | '\'' | '\u{201d}' | '\u{2019}' | ')' | ']' | '}' | '\u{bb}'));
    match last {
        Some(c) if c.is_alphanumeric() => format!("{trimmed}."),
        _ => text.to_string(),
    }
}

/// Sums of money said as they are read aloud: "£150" as "150 pounds" (the voice, left to itself, says
/// "pound 150"). A £, $ or € with a number straight after it: "£1" is "1 pound", "£1.50" "1 pound 50",
/// "£0.99" "99 pence", "£2.05" "2 pounds and 5 pence", "$3.5m" and "$3.5 million" "3.5 million dollars".
pub fn money(text: &str) -> String {
    let chars: Vec<char> = text.chars().collect();
    let mut made = String::with_capacity(text.len() + 16);
    let mut i = 0;
    while i < chars.len() {
        let (one, many, small_one, small_many) = match chars[i] {
            '£' => ("pound", "pounds", "penny", "pence"),
            '$' => ("dollar", "dollars", "cent", "cents"),
            '€' => ("euro", "euros", "cent", "cents"),
            c => { made.push(c); i += 1; continue; }
        };
        let digit = |at: usize| chars.get(at).is_some_and(|c| c.is_ascii_digit());
        if !digit(i + 1) {
            made.push(chars[i]);
            i += 1;
            continue;
        }
        // The whole part (with its commas: "1,500"), then the part after a decimal point.
        let mut at = i + 1;
        let mut whole = String::new();
        while digit(at) || (chars.get(at) == Some(&',') && digit(at + 1)) {
            whole.push(chars[at]);
            at += 1;
        }
        let mut part = String::new();
        if chars.get(at) == Some(&'.') && digit(at + 1) {
            at += 1;
            while digit(at) {
                part.push(chars[at]);
                at += 1;
            }
        }
        // Thousands and millions: "k", "m", "bn" straight after, or the word after a space.
        let letters = |from: usize| -> String {
            chars[from.min(chars.len())..].iter().take_while(|c| c.is_alphabetic()).collect::<String>()
        };
        let short = letters(at);
        let mut scale = match short.to_lowercase().as_str() {
            "k" => "thousand", "m" | "mn" => "million", "b" | "bn" => "billion", "tn" => "trillion", _ => "",
        }.to_string();
        if !scale.is_empty() {
            at += short.chars().count();
        } else if chars.get(at) == Some(&' ') {
            let word = letters(at + 1);
            if matches!(word.to_lowercase().as_str(), "thousand" | "million" | "billion" | "trillion") {
                at += 1 + word.chars().count();
                scale = word;
            }
        }
        let is_one = whole.trim_start_matches('0') == "1";
        if !scale.is_empty() {
            made.push_str(&whole);
            if !part.is_empty() { made.push('.'); made.push_str(&part); }
            made.push_str(&format!(" {scale} {many}"));
        } else if part.len() == 2 {
            let small: u32 = part.parse().unwrap_or(0);
            let nothing = whole.chars().all(|c| c == '0' || c == ',');
            if nothing && small > 0 {
                made.push_str(&format!("{small} {}", if small == 1 { small_one } else { small_many }));
            } else {
                made.push_str(&format!("{whole} {}", if is_one { one } else { many }));
                if small >= 10 {
                    made.push_str(&format!(" {small}"));
                } else if small > 0 {
                    made.push_str(&format!(" and {small} {}", if small == 1 { small_one } else { small_many }));
                }
            }
        } else if !part.is_empty() {
            made.push_str(&format!("{whole}.{part} {many}"));
        } else {
            made.push_str(&format!("{whole} {}", if is_one { one } else { many }));
        }
        if chars.get(at).is_some_and(|c| c.is_alphanumeric()) { made.push(' '); }
        i = at;
    }
    made
}

/// The part of `audio` from just before its first sound to just after its last (sound: above a fiftieth
/// of its loudest), keeping `margin` samples of the silence on each side that is trimmed: where a
/// sentence is said in parts (a gap at its commas), the silence each part starts and ends with comes
/// off, so only the gap is added to the voice's own short pause. `head`/`tail`: which ends to trim.
pub fn trim_silence(audio: &[f32], head: bool, tail: bool, margin: usize) -> &[f32] {
    let loudest = audio.iter().fold(0f32, |m, s| m.max(s.abs()));
    if loudest <= 0.0 {
        return audio;
    }
    let threshold = loudest / 50.0;
    let first = audio.iter().position(|s| s.abs() > threshold).unwrap_or(0);
    let last = audio.iter().rposition(|s| s.abs() > threshold).map(|p| p + 1).unwrap_or(audio.len());
    let from = if head { first.saturating_sub(margin) } else { 0 };
    let to = if tail { (last + margin).min(audio.len()) } else { audio.len() };
    &audio[from..to.max(from)]
}

/// A rule of the pronunciation dictionary: `from`, where it is found in text to be spoken, is said as
/// `to`. `whole`: only as whole words ("cat" not in "catalogue"); `case`: only with these capitals.
#[derive(Clone, Debug, PartialEq)]
pub struct Rule {
    pub from: Vec<char>,
    pub to: String,
    pub whole: bool,
    pub case: bool,
}

/// The pronunciation dictionary for every voice: the program's `dictionary.txt`, a rule a line as
/// `on<TAB>whole<TAB>case<TAB>from<TAB>to` (on: 1 or 0; whole: w, or p for parts of words too; case: c
/// to match capitals, or i). Lines that aren't that are passed over.
#[derive(Clone, Debug, Default, PartialEq)]
pub struct Dictionary {
    /// The rules that are on, longest `from` first (so that the longest that fits is the one used).
    pub rules: Vec<Rule>,
}

/// A voice's language, from its model folder (named as Piper names voices: `en_GB-alba-medium`, or a
/// path ending in that): the letters before the first `_` or `-`, in small letters ("en", "de").
/// "en" when the name does not start with two or three letters and one of those marks.
pub fn language_of(model_dir: &str) -> String {
    let name = model_dir.trim_end_matches(['\\', '/']).rsplit(['\\', '/']).next().unwrap_or("");
    let letters: String = name.chars().take_while(|c| c.is_ascii_alphabetic()).collect();
    let marked = name[letters.len()..].starts_with(['_', '-']);
    if marked && (2..=3).contains(&letters.len()) { letters.to_ascii_lowercase() } else { "en".to_string() }
}

/// The file of a language's pronunciation rules: `dictionary.txt` for English (as before there were
/// other languages), `dictionary-de.txt` for German.
pub fn dictionary_file(language: &str) -> String {
    if language == "en" { "dictionary.txt".to_string() } else { format!("dictionary-{language}.txt") }
}

/// A character that is part of a word (for "whole words only").
fn in_word(c: char) -> bool {
    c.is_alphanumeric() || c == '_'
}

/// A character without its capital (one character for one, so that places in the text stay put).
fn lower(c: char) -> char {
    c.to_lowercase().next().unwrap_or(c)
}

impl Dictionary {
    pub fn read(text: &str) -> Dictionary {
        let mut rules = Vec::new();
        for line in text.lines() {
            let line = line.trim_start_matches('\u{feff}').trim_end_matches('\r');
            // A line `#off`: the whole list is switched off (its rules kept in the file).
            if line.trim() == "#off" {
                return Dictionary::default();
            }
            let parts: Vec<&str> = line.split('\t').collect();
            if parts.len() < 5 || parts[0] != "1" {
                continue;
            }
            let case = parts[2] == "c";
            let from: Vec<char> = parts[3].trim().chars().map(|c| if case { c } else { lower(c) }).collect();
            if from.is_empty() {
                continue;
            }
            rules.push(Rule { from, to: parts[4].trim().to_string(), whole: parts[1] != "p", case });
        }
        rules.sort_by(|a, b| b.from.len().cmp(&a.from.len()));     // (stable: equal lengths stay in the file's order)
        Dictionary { rules }
    }

    /// The text as it is to be said: in one pass, so a rule's result is not changed by another rule.
    pub fn apply(&self, text: &str) -> String {
        if self.rules.is_empty() {
            return text.to_string();
        }
        let chars: Vec<char> = text.chars().collect();
        let lowered: Vec<char> = chars.iter().map(|&c| lower(c)).collect();
        let mut out = String::with_capacity(text.len());
        let mut i = 0;
        'text: while i < chars.len() {
            for rule in &self.rules {
                let end = i + rule.from.len();
                if end > chars.len() {
                    continue;
                }
                let here = if rule.case { &chars[i..end] } else { &lowered[i..end] };
                if here != rule.from.as_slice() {
                    continue;
                }
                if rule.whole {
                    // An end of the rule that is a letter or digit must not be in the middle of a word.
                    let starts_in_word = in_word(chars[i]) && i > 0 && in_word(chars[i - 1]);
                    let ends_in_word = in_word(chars[end - 1]) && end < chars.len() && in_word(chars[end]);
                    if starts_in_word || ends_in_word {
                        continue;
                    }
                }
                out.push_str(&rule.to);
                i = end;
                continue 'text;
            }
            out.push(chars[i]);
            i += 1;
        }
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn utf16(s: &str) -> Vec<u16> { s.encode_utf16().collect() }

    #[test]
    fn sums_of_money() {
        assert_eq!(money("It costs £150 a session."), "It costs 150 pounds a session.");
        assert_eq!(money("£1"), "1 pound");
        assert_eq!(money("£1,500"), "1,500 pounds");
        assert_eq!(money("£1.50"), "1 pound 50");
        assert_eq!(money("£12.99, or $20.00."), "12 pounds 99, or 20 dollars.");
        assert_eq!(money("£0.99"), "99 pence");
        assert_eq!(money("$0.01"), "1 cent");
        assert_eq!(money("£2.05"), "2 pounds and 5 pence");
        assert_eq!(money("€3.5"), "3.5 euros");
        assert_eq!(money("$3.5m"), "3.5 million dollars");
        assert_eq!(money("£2bn"), "2 billion pounds");
        assert_eq!(money("£40k."), "40 thousand pounds.");
        assert_eq!(money("$5 million more"), "5 million dollars more");
        assert_eq!(money("£5 more"), "5 pounds more");
        assert_eq!(money("£150."), "150 pounds.");
        assert_eq!(money("£5, then"), "5 pounds, then");
        assert_eq!(money("£5 Million"), "5 Million pounds");
        assert_eq!(money("£5 per month"), "5 pounds per month");
        assert_eq!(money("£5members"), "5 pounds members");
        assert_eq!(money("the £ sign, $x and 150"), "the £ sign, $x and 150");
        assert_eq!(money(""), "");
        // (A sum with pence is not cut in two as sentences.)
        let t = utf16("It was £1.50 then. Now £2.");
        assert_eq!(sentences(&t).len(), 2);
    }

    #[test]
    fn end_marks() {
        assert_eq!(with_end_mark("this is a test"), "this is a test.");
        assert_eq!(with_end_mark("a test "), "a test.");
        assert_eq!(with_end_mark("in 2026"), "in 2026.");
        assert_eq!(with_end_mark("he said \"test\""), "he said \"test\".");
        assert_eq!(with_end_mark("(a test)"), "(a test).");
        assert_eq!(with_end_mark("this is a test."), "this is a test.");
        assert_eq!(with_end_mark("Really?"), "Really?");
        assert_eq!(with_end_mark("first,"), "first,");
        assert_eq!(with_end_mark("note:"), "note:");
        assert_eq!(with_end_mark("Hello!\""), "Hello!\"");
        assert_eq!(with_end_mark("100%"), "100%");
        assert_eq!(with_end_mark(""), "");
    }
    fn texts(s: &str, ranges: &[(usize, usize)]) -> Vec<String> {
        let t = utf16(s);
        ranges.iter().map(|&(a, b)| String::from_utf16_lossy(&t[a..b])).collect()
    }

    #[test]
    fn splits_sentences() {
        let s = "  Hello there. \"Is it?\" Yes!  Mr. Smith said 3.5 is fine...\nNew line  ";
        assert_eq!(texts(s, &sentences(&utf16(s))),
            ["Hello there.", "\"Is it?\"", "Yes!", "Mr.", "Smith said 3.5 is fine...", "New line"]);
        assert!(sentences(&utf16(" ... ?! ")).is_empty());
        assert_eq!(texts("no end", &sentences(&utf16("no end"))), ["no end"]);
    }

    #[test]
    fn cuts_long_sentences() {
        let long = "word, ".repeat(150);
        let found = sentences(&utf16(&long));
        assert!(found.len() > 1);
        assert!(found.iter().all(|&(a, b)| b - a <= LONGEST_SENTENCE + 1));
        let t = utf16(&long);
        assert!(found[..found.len() - 1].iter().all(|&(_, b)| t[b - 1] == b',' as u16));
    }

    #[test]
    fn finds_words() {
        let s = utf16("  Hi,  there  you. ");
        assert_eq!(texts("  Hi,  there  you. ", &words(&s, 0, s.len())), ["Hi,", "there", "you."]);
    }

    #[test]
    fn word_starts_cover_voiced_audio() {
        let mut audio = vec![0f32; 1000];
        for s in &mut audio[200..800] { *s = 0.5; }
        let starts = word_starts(&audio, &[1.0, 1.0, 2.0]);
        assert_eq!(starts, vec![200, 350, 500]);
        assert_eq!(word_starts(&[], &[1.0]), vec![0]);
    }

    #[test]
    fn bookmark_numbers() {
        assert_eq!(bookmark_number(&utf16("42")), 42);
        assert_eq!(bookmark_number(&utf16(" -7x")), -7);
        assert_eq!(bookmark_number(&utf16("Start")), 0);
    }

    #[test]
    fn loudness() {
        // A tone at 0.3: its level on the whole is 0.3 / sqrt(2), so its tops come to sqrt(2) / CREST of full.
        let quiet: Vec<f32> = (0..4096).map(|i| (i as f32 * 0.05).sin() * 0.3).collect();
        let loudest = |a: &[f32]| a.iter().fold(0f32, |m, s| m.max(s.abs()));
        let rms = |a: &[f32]| (a.iter().map(|s| s * s).sum::<f32>() / a.len() as f32).sqrt();
        assert!((speech_level(&quiet) - 0.3 / 2f32.sqrt()).abs() < 0.003);
        let tops = 2f32.sqrt() / CREST;
        let mut plain = quiet.clone();
        apply_loudness(&mut plain, 0.95, 1.0);
        assert!((loudest(&plain) - 0.95 * tops).abs() < 0.01, "{}", loudest(&plain));
        let mut boosted = quiet.clone();
        apply_loudness(&mut boosted, 0.95, 1.5);
        assert!((loudest(&boosted) - (tops * 1.5).tanh() * 0.95 / 1.5f32.tanh()).abs() < 0.01 && loudest(&boosted) < 0.85 * 0.95);
        assert!(rms(&boosted) > rms(&plain));
        // Half the Volume: half as loud.
        let mut half = quiet.clone();
        apply_loudness(&mut half, 0.475, 1.0);
        assert!((rms(&half) / rms(&plain) - 0.5).abs() < 1e-3);
        // As loud as each other, whatever the model made them: the same line at a tenth, and one with
        // a stray loud moment in it (which is eased under full, not let through or clipped).
        for boost in [1.0f32, 1.5] {
            let mut same = quiet.clone();
            apply_loudness(&mut same, 0.95, boost);
            let mut faint: Vec<f32> = quiet.iter().map(|s| s * 0.1).collect();
            apply_loudness(&mut faint, 0.95, boost);
            assert!((rms(&faint) / rms(&same) - 1.0).abs() < 1e-3);
            let mut spiked = quiet.clone();
            spiked[2000] = 0.9;
            spiked[2001] = -1.2;
            apply_loudness(&mut spiked, 0.95, boost);
            assert!((rms(&spiked[..1024]) / rms(&same[..1024]) - 1.0).abs() < 0.02, "{}", rms(&spiked[..1024]) / rms(&same[..1024]));
            assert!(loudest(&spiked) <= 0.95 + 1e-4 && loudest(&spiked) > 0.9);
            assert!(spiked[2000] > 0.6 && spiked[2001] < -0.9);
        }
        // Off, silence, nothing.
        let mut off = quiet.clone();
        apply_loudness(&mut off, 0.0, 2.0);
        assert_eq!(off, quiet);
        let mut silence = vec![0.001f32; 10];
        apply_loudness(&mut silence, 0.95, 1.0);
        assert!((silence[0] - 0.095).abs() < 1e-6);
        apply_loudness(&mut [], 0.95, 1.0);
        assert_eq!(speech_level(&[]), 0.0);
        // The louder half only: silence around the words does not count.
        let mut padded = vec![0f32; 4096];
        padded.extend(&quiet);
        assert!((speech_level(&padded) - speech_level(&quiet)).abs() < 0.003);
    }

    #[test]
    fn output() {
        assert_eq!(output_level(""), 1.0);
        assert_eq!(output_level(r#"{ "speed_percent": 100, "output_percent": 40 }"#), 0.4);
        assert_eq!(output_level(r#"{"output_percent": 0}"#), 0.0);
        assert_eq!(output_level(r#"{"output_percent": 250}"#), 1.0);
        let piece = [1000i16; 4];
        let mut out = [0i16; 4];
        scale_into(&piece, 1.0, 0.0, &mut out);
        assert_eq!(out, [1000, 750, 500, 250]);
        scale_into(&piece, 0.5, 0.5, &mut out);
        assert_eq!(out, [500; 4]);
        scale_into(&piece, 0.0, 0.0, &mut out);
        assert_eq!(out, [0; 4]);
    }

    #[test]
    fn own_settings_come_first() {
        // A voice's own settings file is read in front of its voice.json: what it sets wins, the rest
        // is the voice.json's.
        let own = r#"{ "loudness": { "peak": 0.5, "boost": 1 } }"#;
        let made = r#"{ "inference": { "length_scale": 1.1 }, "loudness": { "peak": 1, "boost": 1.5 } }"#;
        let both = format!("{own}\n{made}");
        assert_eq!(number(&both, "peak", 0.0), 0.5);
        assert_eq!(number(&both, "boost", 0.0), 1.0);
        assert_eq!(number(&both, "length_scale", 1.0), 1.1);
    }

    #[test]
    fn numbers() {
        let j = r#"{ "inference": { "noise_scale": 0.667, "length_scale": 1.25 }, "loudness": { "peak": 0.9 } }"#;
        assert_eq!(number(j, "length_scale", 1.0), 1.25);
        assert_eq!(number(j, "peak", 0.0), 0.9);
        assert_eq!(number(j, "boost", 1.5), 1.5);
        assert_eq!(number(r#"{"peak": -1}"#, "peak", 0.5), 0.5);
    }

    #[test]
    fn pauses() {
        assert_eq!(Pauses::read(""), DEFAULT_PAUSES);
        let p = Pauses::read(r#"{ "pauses": { "sentence_ms": 750, "comma_ms": 200, "paragraph_ms": 1500 } }"#);
        assert_eq!(p, Pauses { sentence_ms: 750.0, comma_ms: 200.0, paragraph_ms: 1500.0 });
        assert_eq!(Pauses::read(r#"{"sentence_ms": 300}"#).paragraph_ms, 1200.0);
        // 600 ms at 22050 Hz: rate 0 all of it; faster, less; slower, more.
        assert_eq!(gap_samples(600.0, 1.0, 22050), 13230);
        let near = |got: usize, ms: f32| (got as f32 - ms * 22.05).abs() <= 2.0;
        assert!(near(gap_samples(600.0, 3f32.powf(0.2), 22050), 481.7));   // rate +2: 482 ms
        assert!(near(gap_samples(600.0, 3f32.powf(-0.2), 22050), 747.4));  // rate -2: 747 ms
        assert_eq!(gap_samples(0.0, 1.0, 22050), 0);
    }

    #[test]
    fn speed_for_every_voice() {
        assert_eq!(global_speed(""), 1.0);
        assert_eq!(global_speed("{\n  \"speed_percent\": 100,\n  \"pauses\": { \"sentence_ms\": 600 }\n}"), 1.0);
        assert_eq!(global_speed(r#"{"speed_percent": 900}"#), 2.0);
        assert_eq!(global_speed(r#"{"speed_percent": 10}"#), 0.25);
        // 600 ms at 120% and rate 0: 500 ms.
        assert_eq!(gap_samples(600.0, global_speed(r#"{"speed_percent": 120}"#), 22050), 11025);
    }

    #[test]
    fn paragraphs_and_clauses() {
        let s = utf16("One. Two.\nThree.");
        assert!(!line_break_between(&s, 4, 5));
        assert!(line_break_between(&s, 9, 10));
        let c = "Pulling in three, watch the patrol; it has 3,000 hit points: run.";
        let t = utf16(c);
        assert_eq!(texts(c, &clauses(&t, 0, t.len())),
            ["Pulling in three,", "watch the patrol;", "it has 3,000 hit points:", "run."]);
        let d = "Well, , then.";
        let u = utf16(d);
        assert_eq!(texts(d, &clauses(&u, 0, u.len())), ["Well,", ", then."]);
        let e = "No commas here.";
        let v = utf16(e);
        assert_eq!(texts(e, &clauses(&v, 0, v.len())), ["No commas here."]);
    }

    #[test]
    fn trims_silence() {
        let mut audio = vec![0f32; 1000];
        for s in &mut audio[300..600] { *s = 0.5; }
        assert_eq!(trim_silence(&audio, true, true, 40).len(), 380);      // 260..640
        assert_eq!(trim_silence(&audio, false, true, 40).len(), 640);     // 0..640
        assert_eq!(trim_silence(&audio, true, false, 40).len(), 740);     // 260..1000
        assert_eq!(trim_silence(&audio, false, false, 40).len(), 1000);
        assert_eq!(trim_silence(&[0f32; 10], true, true, 2).len(), 10);
    }

    #[test]
    fn dictionary_rules() {
        let d = Dictionary::read("1\tw\ti\tGIF\tjif\r\n1\tw\ti\tcat\tfeline\n0\tw\ti\tdog\thound\n1\tp\ti\tcolour\tcolor\n1\tw\tc\tUS\tU S\n\
            1\tw\ti\tNew York\tthe big apple\n1\tw\ti\tyork\tyork city\n# a note\n1\tw\ti\tC++\tC plus plus\n1\tw\ti\tNiamh\tNeev\n");
        assert_eq!(d.rules.len(), 8);
        // Whole words, any capitals; not inside other words; a rule that is off does nothing.
        assert_eq!(d.apply("A gif, a GIF and gifts for the Cat's catalogue dog."), "A jif, a jif and gifts for the feline's catalogue dog.");
        // Parts of words too.
        assert_eq!(d.apply("Colourful colours"), "colorful colors");
        // Capitals matched: "US" but not "us".
        assert_eq!(d.apply("US and us"), "U S and us");
        // The longest rule that fits is used, and a result is not changed again.
        assert_eq!(d.apply("new york, York."), "the big apple, york city.");
        // Rules that end in symbols; names.
        assert_eq!(d.apply("C++ and c++14, Niamh's."), "C plus plus and C plus plus14, Neev's.");
        assert_eq!(Dictionary::read("").apply("unchanged"), "unchanged");
        // A list switched off: none of its rules, wherever the line is.
        assert!(Dictionary::read("# comment\n#off\n1\tw\ti\tcat\tfeline\n").rules.is_empty());
        assert!(Dictionary::read("1\tw\ti\tcat\tfeline\n#off\n").rules.is_empty());
        assert_eq!(Dictionary::read("# off\n1\tw\ti\tcat\tfeline\n").rules.len(), 1);
    }

    #[test]
    fn languages_of_voices() {
        assert_eq!(language_of(r"C:\Program Files\Modern Voices for Windows\voices\en_GB-alba-medium"), "en");
        assert_eq!(language_of(r"C:\voices\de_DE-thorsten-medium\"), "de");
        assert_eq!(language_of("es_MX-claude-high"), "es");
        assert_eq!(language_of("PT_br-faber-medium"), "pt");
        assert_eq!(language_of("voices/something"), "en");
        assert_eq!(language_of(""), "en");
        assert_eq!(dictionary_file("en"), "dictionary.txt");
        assert_eq!(dictionary_file("de"), "dictionary-de.txt");
        assert!(Dictionary::read("not a rule\n1\tw\ti\t\tx\n").rules.is_empty());
    }
}
