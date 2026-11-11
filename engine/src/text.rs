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

/// A sentence's audio made louder, as Piper's own program does and then some:
///   peak:  scaled so its loudest moment is this (0.95 = just under full). 0 = left as it is.
///   boost: above 1, the quieter parts lifted more than the loud ones (tanh, a soft limiter), so it
///          is louder overall without its peaks clipping. 1 = none.
/// The loudest moment is taken as at least 0.01, as Piper does, so near-silence is not blown up.
pub fn apply_loudness(audio: &mut [f32], peak: f32, boost: f32) {
    if peak <= 0.0 || audio.is_empty() {
        return;
    }
    let loudest = audio.iter().fold(0f32, |m, s| m.max(s.abs())).max(0.01);
    if boost > 1.0001 {
        let level = peak / boost.tanh();
        for s in audio.iter_mut() {
            *s = (*s / loudest * boost).tanh() * level;
        }
    } else {
        let level = peak / loudest;
        for s in audio.iter_mut() {
            *s *= level;
        }
    }
}

/// Pause lengths, in milliseconds at normal speed: what each gap adds to the voice's own pause.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Pauses {
    pub sentence_ms: f32,
    pub comma_ms: f32,
    pub paragraph_ms: f32,
}

/// Before any are set: 0.6 s after a sentence, double that at a paragraph, none added at commas.
pub const DEFAULT_PAUSES: Pauses = Pauses { sentence_ms: 500.0, comma_ms: 0.0, paragraph_ms: 1200.0 };

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

#[cfg(test)]
mod tests {
    use super::*;

    fn utf16(s: &str) -> Vec<u16> { s.encode_utf16().collect() }
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
        let quiet: Vec<f32> = (0..1000).map(|i| (i as f32 * 0.05).sin() * 0.3).collect();
        let loudest = |a: &[f32]| a.iter().fold(0f32, |m, s| m.max(s.abs()));
        let rms = |a: &[f32]| (a.iter().map(|s| s * s).sum::<f32>() / a.len() as f32).sqrt();
        let mut plain = quiet.clone();
        apply_loudness(&mut plain, 0.95, 1.0);
        assert!((loudest(&plain) - 0.95).abs() < 1e-4);
        let mut boosted = quiet.clone();
        apply_loudness(&mut boosted, 0.95, 1.5);
        assert!((loudest(&boosted) - 0.95).abs() < 1e-3);
        assert!(rms(&boosted) > rms(&plain));
        // As the model stage it replaces computed it: tanh(n * k) * peak / tanh(k).
        let n = 0.5f32;
        let mut one = vec![0.3 * n, 0.3];
        apply_loudness(&mut one, 0.8, 3.0);
        assert!((one[0] - (n * 3.0).tanh() * 0.8 / 3f32.tanh()).abs() < 1e-5);
        let mut off = quiet.clone();
        apply_loudness(&mut off, 0.0, 2.0);
        assert_eq!(off, quiet);
        let mut silence = vec![0.001f32; 10];
        apply_loudness(&mut silence, 0.95, 1.0);
        assert!((silence[0] - 0.095).abs() < 1e-6);
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
}
