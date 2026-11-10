//! Text and audio helpers with no Windows in them (tested on their own): where sentences and words
//! are, about when each word is heard, bookmark numbers, and pitch shifting by resampling.

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

}
