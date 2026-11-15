# Turns a stock Piper voice (NAME.onnx + NAME.onnx.json) into the folder the engine reads:
# model.onnx (the model as Piper made it, with the metadata sherpa-onnx wants added), tokens.txt and
# voice.json. Dot-sourced by Install.ps1. Plain ASCII, Windows PowerShell 5.1 and 7.
#
# How a voice sounds is set in its voice.json, which the engine reads while speaking (a change takes
# effect from the next thing said, in programs already using the voice):
#   "loudness": { "peak": 1, "boost": 1.5 }
#     peak:  each sentence is made this loud on the whole (1 = its loudest moments at full; 0 = off,
#            as Piper made it)
#     boost: above 1, the quieter parts are lifted more than the loud ones (a soft limiter, tanh), so
#            the voice is louder overall without its peaks clipping. 1 = none, 1.5 mild, 2-3 strong.
#   "inference": { "length_scale": 1 }   speed: higher is slower
#   "pauses": { "own_pauses": 1, "sentence_ms": 500, "comma_ms": 0, "paragraph_ms": 1200 }
#     this voice's own pauses (with own_pauses 1; else the program's settings.json, same keys, applies)
#     the silence added to the voice's own pause after a sentence, at a comma (0 = commas untouched)
#     and at a line break, in milliseconds at rate 0, 100% speed and the voice's own speed: faster
#     speech, shorter gaps, in proportion
# The program's settings.json also has "speed_percent": the speed every voice speaks at before a
# program's own rate (100 unless set; each voice's length_scale adjusts from there).

$script:PevUtf8 = New-Object System.Text.UTF8Encoding $false

function Read-PbVarint {
    param([byte[]]$Bytes, [long]$Pos)
    [long]$value = 0
    $shift = 0
    do {
        if ($Pos -ge $Bytes.Length) { throw 'The model file is cut short (incomplete download?).' }
        $byte = $Bytes[$Pos]
        $Pos++
        $value = $value -bor (([long]($byte -band 0x7F)) -shl $shift)
        $shift += 7
    } while ($byte -band 0x80)
    return @($value, $Pos)
}

function Write-PbVarint {
    param([System.IO.Stream]$Stream, [long]$Number)
    while ($Number -ge 0x80) {
        $Stream.WriteByte([byte](($Number -band 0x7F) -bor 0x80))
        $Number = $Number -shr 7
    }
    $Stream.WriteByte([byte]$Number)
}

# The fields of one protobuf message in $Bytes[$From..$To): field number, wire type, where the whole
# field starts and ends, and where its payload starts and ends (for length-delimited fields).
function Get-PbFields {
    param([byte[]]$Bytes, [long]$From, [long]$To)
    $fields = New-Object System.Collections.Generic.List[object]
    [long]$pos = $From
    while ($pos -lt $To) {
        $start = $pos
        $r = Read-PbVarint -Bytes $Bytes -Pos $pos
        [long]$tag = $r[0]; $pos = $r[1]
        $field = $tag -shr 3
        $wire = $tag -band 7
        $payload = $pos
        if ($wire -eq 0) { $r = Read-PbVarint -Bytes $Bytes -Pos $pos; $pos = $r[1] }
        elseif ($wire -eq 1) { $pos += 8 }
        elseif ($wire -eq 2) { $r = Read-PbVarint -Bytes $Bytes -Pos $pos; $payload = $r[1]; $pos = $r[1] + $r[0] }
        elseif ($wire -eq 5) { $pos += 4 }
        else { throw "This does not look like an ONNX model (wire type $wire at byte $start)." }
        if ($pos -gt $To -or $field -lt 1) { throw 'This does not look like a complete ONNX model.' }
        $fields.Add([pscustomobject]@{ Field = $field; Wire = $wire; Start = $start; End = $pos; From = $payload })
    }
    return ,$fields
}

function Get-PbString([byte[]]$Bytes, $F) { return $script:PevUtf8.GetString($Bytes, [int]$F.From, [int]($F.End - $F.From)) }

function Add-PbBytes([System.IO.Stream]$Stream, [int]$Field, [byte[]]$Payload) {
    Write-PbVarint -Stream $Stream -Number (($Field -shl 3) -bor 2)
    Write-PbVarint -Stream $Stream -Number $Payload.Length
    $Stream.Write($Payload, 0, $Payload.Length)
}
function Add-PbString([System.IO.Stream]$Stream, [int]$Field, [string]$Text) { Add-PbBytes $Stream $Field $script:PevUtf8.GetBytes($Text) }
function Copy-PbField([System.IO.Stream]$Stream, [byte[]]$Bytes, $F) { $Stream.Write($Bytes, [int]$F.Start, [int]($F.End - $F.Start)) }

# Copies an ONNX model with $Meta as its metadata (ModelProto field 14, metadata_props), in place of
# any it had. Every other field is copied byte for byte: the network itself is never parsed or changed.
function Write-PiperModel {
    param([string]$InPath, [string]$OutPath, [System.Collections.Specialized.OrderedDictionary]$Meta)
    $data = [System.IO.File]::ReadAllBytes($InPath)
    $fields = Get-PbFields -Bytes $data -From 0 -To $data.Length
    if ($fields.Count -lt 3 -or -not ($fields | Where-Object { $_.Field -eq 7 })) { throw 'This does not look like an ONNX model.' }
    $temp = "$OutPath.writing"
    $out = [System.IO.File]::Create($temp)
    try {
        foreach ($f in $fields) { if ($f.Field -ne 14) { Copy-PbField $out $data $f } }
        foreach ($key in $Meta.Keys) {                 # StringStringEntryProto: key 1, value 2
            $entry = New-Object System.IO.MemoryStream
            Add-PbString $entry 1 ([string]$key)
            Add-PbString $entry 2 ([string]$Meta[$key])
            Add-PbBytes $out 14 $entry.ToArray()
        }
    } finally {
        $out.Dispose()
    }
    Move-Item $temp $OutPath -Force
}

# tokens.txt from the config's phoneme_id_map: one "symbol id" per line. Read with a pattern, not a
# JSON parser: Windows PowerShell's parser refuses maps whose keys differ only by case.
function Get-PiperTokens {
    param([string]$Json)
    $at = $Json.IndexOf('"phoneme_id_map"')
    if ($at -lt 0) { throw 'The .onnx.json has no phoneme_id_map: is it a Piper voice config?' }
    $found = [regex]::Matches($Json.Substring($at), '"((?:[^"\\]|\\.)+)"\s*:\s*\[\s*(\d+)\s*\]')
    $lines = New-Object System.Text.StringBuilder
    $ids = @{}
    foreach ($m in $found) {
        $symbol = [regex]::Unescape($m.Groups[1].Value)
        $id = $m.Groups[2].Value
        $ids[$id] = $true
        [void]$lines.Append($symbol).Append(' ').Append($id).Append("`n")
    }
    if ($found.Count -lt 50 -or $ids.Count -ne $found.Count -or -not $ids.ContainsKey('0')) {
        throw "Could not read the phoneme table from the .onnx.json ($($found.Count) symbols found)."
    }
    return $lines.ToString()
}

function Get-PiperSetting {
    param([string]$Text, [string]$Pattern, $Default)
    $m = [regex]::Match($Text, $Pattern)
    if ($m.Success) { return $m.Groups[1].Value }
    if ($null -ne $Default) { return $Default }
    throw "A setting is missing ($Pattern)."
}

$script:PevNumber = '(-?\d+(?:\.\d+)?)'

function Format-Number([double]$Number) { return $Number.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture) }

# Where the settings people change are kept (since 0.12.0): a folder of the PC's own that every user of
# it may write to, so that changing them needs no administrator. In it: settings.json (speed, pauses and
# output volume for every voice), dictionary.txt (pronunciation) and voices\<voice folder's name>.json,
# a voice's own settings (loudness, speed, its own pauses). A voice's voice.json, in its folder, has them
# as the voice was installed (and, from before 0.12.0, as they were set then): what its own file sets
# comes first. The engine reads them the same way (it finds the folder by the settings file the voice's
# token names); so does Modern Voices Config (Store, in config\Config.cs): keep the three in step.
$script:UserData = Join-Path $env:ProgramData 'Modern Voices for Windows'
function Get-VoiceFile([string]$Dir) {
    return (Join-Path $script:UserData ('voices\' + (Split-Path $Dir -Leaf) + '.json'))
}
# A voice's settings as text: its own file's, then its voice.json's (a search finds the first).
function Get-VoiceText([string]$Dir) {
    $own = Get-VoiceFile $Dir
    $made = Join-Path $Dir 'voice.json'
    $text = if (Test-Path $own) { [System.IO.File]::ReadAllText($own, $script:PevUtf8) } else { '' }
    if (Test-Path $made) { $text += "`n" + [System.IO.File]::ReadAllText($made, $script:PevUtf8) }
    return $text
}
# A settings file's text to change (LF line ends; an empty one when there is none yet, its folder made).
function Get-SettingsText([string]$Path) {
    if (Test-Path $Path) { return [System.IO.File]::ReadAllText($Path, $script:PevUtf8).Replace("`r`n", "`n") }
    $folder = Split-Path $Path -Parent
    if ($folder -and -not (Test-Path $folder)) { New-Item $folder -ItemType Directory -Force | Out-Null }
    return "{`n}`n"
}
# Text with a block added before its last brace.
function Add-SettingsBlock([string]$Text, [string]$Block) {
    $body = $Text.Substring(0, $Text.LastIndexOf('}')).TrimEnd()
    return $body + $(if ($body.EndsWith('{')) { "`n  " } else { ",`n  " }) + $Block + "`n}`n"
}
# Written to a new file that then replaces the old, so the engine never reads half a file.
function Set-SettingsText([string]$Path, [string]$Text) {
    $temp = "$Path.writing"
    [System.IO.File]::WriteAllText($temp, $Text, $script:PevUtf8)
    Move-Item $temp $Path -Force
}

function Test-VoiceSettings([double]$Peak, [double]$Boost, [double]$LengthScale) {
    if ($Peak -lt 0 -or $Peak -gt 1) { throw "peak must be from 0 to 1 (it is $(Format-Number $Peak))." }
    if ($Boost -lt 1 -or $Boost -gt 5) { throw "boost must be from 1 to 5 (it is $(Format-Number $Boost))." }
    if ($LengthScale -lt 0.3 -or $LengthScale -gt 4) { throw "length_scale must be from 0.3 to 4 (it is $(Format-Number $LengthScale))." }
}

# What is set for a voice (by its folder): Peak, Boost, LengthScale ($null where nothing says). $null
# when it has no voice.json.
function Get-VoiceSettings([string]$Dir) {
    if (-not (Test-Path (Join-Path $Dir 'voice.json'))) { return $null }
    $text = Get-VoiceText $Dir
    $c = [Globalization.CultureInfo]::InvariantCulture
    $read = {
        param($key)
        $m = [regex]::Match($text, '"' + $key + '"\s*:\s*' + $script:PevNumber)
        if ($m.Success) { [double]::Parse($m.Groups[1].Value, $c) } else { $null }
    }
    return [pscustomobject]@{ Peak = (& $read 'peak'); Boost = (& $read 'boost'); LengthScale = (& $read 'length_scale') }
}

# Records a voice's loudness and speed in its own settings file, for the engine.
function Set-VoiceSettings([string]$Dir, [double]$Peak, [double]$Boost, [double]$LengthScale) {
    Test-VoiceSettings $Peak $Boost $LengthScale
    $path = Get-VoiceFile $Dir
    $text = Get-SettingsText $path
    $block = "`"loudness`": {`n    `"peak`": $(Format-Number $Peak),`n    `"boost`": $(Format-Number $Boost)`n  }"
    $existing = [regex]::Match($text, '"loudness"\s*:\s*\{[^}]*\}')
    if ($existing.Success) {
        $text = $text.Substring(0, $existing.Index) + $block + $text.Substring($existing.Index + $existing.Length)
    } else {
        $text = Add-SettingsBlock $text $block
    }
    $speed = [regex]::Match($text, '"length_scale"\s*:\s*' + $script:PevNumber)
    if ($speed.Success) {
        $g = $speed.Groups[1]
        $text = $text.Substring(0, $g.Index) + (Format-Number $LengthScale) + $text.Substring($g.Index + $g.Length)
    } else {
        $text = Add-SettingsBlock $text "`"inference`": {`n    `"length_scale`": $(Format-Number $LengthScale)`n  }"
    }
    Set-SettingsText $path $text
}

# --- pauses -------------------------------------------------------------------------------------------

$script:PauseDefaults = @{ Sentence = 500; Comma = 0; Paragraph = 1200 }
$script:PauseLimits = @{ Sentence = 2000; Comma = 1000; Paragraph = 3000 }

function Test-Pauses([double]$Sentence, [double]$Comma, [double]$Paragraph) {
    foreach ($pair in @(@('sentence', $Sentence, $script:PauseLimits.Sentence), @('comma', $Comma, $script:PauseLimits.Comma),
                        @('paragraph', $Paragraph, $script:PauseLimits.Paragraph))) {
        if ($pair[1] -lt 0 -or $pair[1] -gt $pair[2]) { throw "The $($pair[0]) gap must be from 0 to $($pair[2]) ms (it is $(Format-Number $pair[1]))." }
    }
}

# The pauses a settings file sets, or (-From) a settings text: Own (its own_pauses), Stored (it has
# any), Sentence, Comma, Paragraph (ms; the defaults where it says nothing).
function Get-Pauses([string]$Path, [string]$From = '') {
    $text = if ($From) { $From } elseif ($Path -and (Test-Path $Path)) { [System.IO.File]::ReadAllText($Path, $script:PevUtf8) } else { '' }
    $c = [Globalization.CultureInfo]::InvariantCulture
    $read = {
        param($key, $default)
        $m = [regex]::Match($text, '"' + $key + '"\s*:\s*' + $script:PevNumber)
        if ($m.Success) { [double]::Parse($m.Groups[1].Value, $c) } else { $default }
    }
    return [pscustomobject]@{ Own = ((& $read 'own_pauses' 0) -ge 1); Stored = ($text -match '"pauses"\s*:')
        Sentence = (& $read 'sentence_ms' $script:PauseDefaults.Sentence); Comma = (& $read 'comma_ms' $script:PauseDefaults.Comma)
        Paragraph = (& $read 'paragraph_ms' $script:PauseDefaults.Paragraph) }
}

# Puts a "pauses" block in a settings file (made if missing), in place of the one there.
function Set-PausesBlock([string]$Path, [string]$Block) {
    $text = Get-SettingsText $Path
    $existing = [regex]::Match($text, '"pauses"\s*:\s*\{[^}]*\}')
    if ($existing.Success) {
        $text = $text.Substring(0, $existing.Index) + $Block + $text.Substring($existing.Index + $existing.Length)
    } else {
        $text = Add-SettingsBlock $text $Block
    }
    Set-SettingsText $Path $text
}

function Format-PausesBlock([double]$Sentence, [double]$Comma, [double]$Paragraph, $Own) {
    $ownLine = if ($null -ne $Own) { "`n    `"own_pauses`": $([int][bool]$Own)," } else { '' }
    return "`"pauses`": {$ownLine`n    `"sentence_ms`": $(Format-Number $Sentence),`n    `"comma_ms`": $(Format-Number $Comma),`n    `"paragraph_ms`": $(Format-Number $Paragraph)`n  }"
}

# The pauses for every voice without its own: the program's settings.json.
function Set-GlobalPauses([string]$Path, [double]$Sentence, [double]$Comma, [double]$Paragraph) {
    Test-Pauses $Sentence $Comma $Paragraph
    Set-PausesBlock $Path (Format-PausesBlock $Sentence $Comma $Paragraph $null)
}

# The speed every voice speaks at before a program's rate, in % (settings.json's speed_percent).
$script:SpeedDefault = 100
function Get-GlobalSpeed([string]$Path) {
    $text = if (Test-Path $Path) { [System.IO.File]::ReadAllText($Path, $script:PevUtf8) } else { '' }
    $m = [regex]::Match($text, '"speed_percent"\s*:\s*' + $script:PevNumber)
    if (-not $m.Success) { return $script:SpeedDefault }
    return [math]::Max(25, [math]::Min(200, [double]::Parse($m.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)))
}
# A number of the settings file's own, by its key (speed_percent, output_percent): set, or added first.
function Set-GlobalNumber([string]$Path, [string]$Key, [double]$Number) {
    $text = Get-SettingsText $Path
    $line = '"' + $Key + '": ' + (Format-Number $Number)
    $existing = [regex]::Match($text, '"' + $Key + '"\s*:\s*' + $script:PevNumber)
    if ($existing.Success) {
        $text = $text.Substring(0, $existing.Index) + $line + $text.Substring($existing.Index + $existing.Length)
    } else {
        $start = $text.IndexOf('{') + 1
        $rest = $text.Substring($start).TrimStart()
        $text = $text.Substring(0, $start) + "`n  " + $line + $(if ($rest.StartsWith('}')) { "`n" } else { ",`n  " }) + $rest
    }
    Set-SettingsText $Path $text
}
function Set-GlobalSpeed([string]$Path, [double]$Percent) {
    if ($Percent -lt 25 -or $Percent -gt 200) { throw "speed must be 25 to 200% (got $Percent)" }
    Set-GlobalNumber $Path 'speed_percent' $Percent
}

# The output volume: how loud every voice comes out, of what its own settings make, in % (settings.json's
# output_percent; 100 unless turned down, 0 for silence). The engine looks at it as speech goes out.
function Get-OutputVolume([string]$Path) {
    $text = if (Test-Path $Path) { [System.IO.File]::ReadAllText($Path, $script:PevUtf8) } else { '' }
    $m = [regex]::Match($text, '"output_percent"\s*:\s*' + $script:PevNumber)
    if (-not $m.Success) { return 100 }
    return [math]::Max(0, [math]::Min(100, [double]::Parse($m.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)))
}
function Set-OutputVolume([string]$Path, [double]$Percent) {
    if ($Percent -lt 0 -or $Percent -gt 100) { throw "the output volume must be 0 to 100% (got $Percent)" }
    Set-GlobalNumber $Path 'output_percent' $Percent
}

# ---- the pronunciation dictionary for every voice (dictionary.txt beside settings.json): a rule a
# line, tab-separated: on (1 or 0), whole words only (w) or parts of words too (p), capitals matched
# (c) or not (i), the word or phrase, what it is said as. The engine reads it again whenever it changes.
function Get-Dictionary([string]$Path) {
    $rules = @()
    if (Test-Path $Path) {
        foreach ($line in [System.IO.File]::ReadAllLines($Path, $script:PevUtf8)) {
            $parts = $line.Split("`t")
            if ($parts.Count -lt 5 -or @('0', '1') -notcontains $parts[0]) { continue }
            $rules += [pscustomobject]@{ On = ($parts[0] -eq '1'); Whole = ($parts[1] -ne 'p'); Case = ($parts[2] -eq 'c'); From = $parts[3]; To = $parts[4] }
        }
    }
    return $rules
}
function Set-Dictionary([string]$Path, $Rules) {
    $lines = @('# Modern Voices for Windows - pronunciation dictionary: on, whole words (w) or parts too (p), capitals matched (c) or not (i), said, as')
    foreach ($rule in @($Rules)) {
        $from = ([string]$rule.From -replace '[\t\r\n]+', ' ').Trim()
        $to = ([string]$rule.To -replace '[\t\r\n]+', ' ').Trim()
        if (-not $from) { continue }
        $lines += (@($(if ($rule.On) { '1' } else { '0' }), $(if ($rule.Whole) { 'w' } else { 'p' }), $(if ($rule.Case) { 'c' } else { 'i' }), $from, $to) -join "`t")
    }
    $folder = Split-Path $Path -Parent
    if ($folder -and -not (Test-Path $folder)) { New-Item $folder -ItemType Directory -Force | Out-Null }
    $temp = "$Path.writing"
    [System.IO.File]::WriteAllLines($temp, [string[]]$lines, $script:PevUtf8)
    Move-Item $temp $Path -Force
}

# A voice's own pauses ($Own true) or not (the global ones apply; its values are kept for next time).
function Set-VoicePauses([string]$Dir, [bool]$Own, [double]$Sentence, [double]$Comma, [double]$Paragraph) {
    Test-Pauses $Sentence $Comma $Paragraph
    Set-PausesBlock (Get-VoiceFile $Dir) (Format-PausesBlock $Sentence $Comma $Paragraph $Own)
}
# The pauses set for a voice (by its folder): as Get-Pauses.
function Get-VoicePauses([string]$Dir) {
    return (Get-Pauses -From (Get-VoiceText $Dir))
}

# The whole job for one voice. $Name is the voice's short id (folder name), $Label what people see.
function Convert-PiperVoice {
    param([string]$Onnx, [string]$Config, [string]$OutDir, [string]$Name, [string]$Label, [double]$Peak, [double]$Boost)
    Test-VoiceSettings $Peak $Boost 1
    $json = [System.IO.File]::ReadAllText($Config, $script:PevUtf8)
    $num = $script:PevNumber
    $rate = Get-PiperSetting $json '"sample_rate"\s*:\s*(\d+)' $null
    $espeak = Get-PiperSetting $json '"espeak"\s*:\s*\{\s*"voice"\s*:\s*"([^"]+)"' $null
    $speakers = Get-PiperSetting $json '"num_speakers"\s*:\s*(\d+)' '1'
    $language = Get-PiperSetting $json '"name_english"\s*:\s*"([^"]+)"' 'English'
    $noise = Get-PiperSetting $json ('"noise_scale"\s*:\s*' + $num) '0.667'
    $length = Get-PiperSetting $json ('"length_scale"\s*:\s*' + $num) '1'
    $noiseW = Get-PiperSetting $json ('"noise_w"\s*:\s*' + $num) '0.8'
    $tokens = Get-PiperTokens $json

    if (-not (Test-Path $OutDir)) { New-Item $OutDir -ItemType Directory -Force | Out-Null }
    $meta = [ordered]@{ model_type = 'vits'; comment = 'piper'; language = $language; voice = $espeak
                        has_espeak = '1'; n_speakers = $speakers; sample_rate = $rate }
    Write-PiperModel -InPath $Onnx -OutPath (Join-Path $OutDir 'model.onnx') -Meta $meta
    [System.IO.File]::WriteAllText((Join-Path $OutDir 'tokens.txt'), $tokens, $script:PevUtf8)
    $voiceJson = @"
{
  "name": "$Name",
  "model_type": "vits",
  "comment": "piper",
  "language": "$language",
  "voice": "$espeak",
  "has_espeak": 1,
  "n_speakers": $speakers,
  "sample_rate": $rate,
  "speakers": ["$Name"],
  "labels": ["$Label"],
  "inference": {
    "noise_scale": $noise,
    "length_scale": $length,
    "noise_w": $noiseW
  },
  "loudness": {
    "peak": $(Format-Number $Peak),
    "boost": $(Format-Number $Boost)
  }
}
"@
    [System.IO.File]::WriteAllText((Join-Path $OutDir 'voice.json'), $voiceJson, $script:PevUtf8)
}
