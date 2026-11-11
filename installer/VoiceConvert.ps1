# Turns a stock Piper voice (NAME.onnx + NAME.onnx.json) into the folder the engine reads:
# model.onnx (the model as Piper made it, with the metadata sherpa-onnx wants added), tokens.txt and
# voice.json. Dot-sourced by Install.ps1. Plain ASCII, Windows PowerShell 5.1 and 7.
#
# How a voice sounds is set in its voice.json, which the engine reads while speaking (a change takes
# effect from the next thing said, in programs already using the voice):
#   "loudness": { "peak": 0.95, "boost": 1.5 }
#     peak:  each sentence is scaled so its loudest moment is this (0.95 = just under full; 0 = off,
#            as Piper made it)
#     boost: above 1, the quieter parts are lifted more than the loud ones (a soft limiter, tanh), so
#            the voice is louder overall without its peaks clipping. 1 = none, 1.5 mild, 2-3 strong.
#   "inference": { "length_scale": 1 }   speed: higher is slower
#   "engine_loudness": 1                 the engine applies the loudness (since 0.6)
#
# Up to 0.5 the loudness was put inside the model, as a few nodes named pev_* at its end. The code for
# that stays, to take them out again (Convert-ToEngineLoudness), and is tested both ways.

$script:PevUtf8 = New-Object System.Text.UTF8Encoding $false
$script:PevMark = 'pev_'
$script:PevRaw = 'pev_raw'

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

# NodeProto: input 1, output 2, name 3, op_type 4.
function New-OnnxNode([string]$Name, [string]$Op, [string[]]$Inputs, [string[]]$Outputs) {
    $m = New-Object System.IO.MemoryStream
    foreach ($i in $Inputs) { Add-PbString $m 1 $i }
    foreach ($o in $Outputs) { Add-PbString $m 2 $o }
    Add-PbString $m 3 $Name
    Add-PbString $m 4 $Op
    return ,$m.ToArray()
}

# TensorProto, a float32 scalar: data_type 2 (1 = FLOAT), name 8, raw_data 9 (little-endian).
function New-OnnxScalar([string]$Name, [double]$Number) {
    $m = New-Object System.IO.MemoryStream
    Write-PbVarint -Stream $m -Number (2 -shl 3); Write-PbVarint -Stream $m -Number 1
    Add-PbString $m 8 $Name
    $raw = [BitConverter]::GetBytes([single]$Number)
    if (-not [BitConverter]::IsLittleEndian) { [Array]::Reverse($raw) }
    Add-PbBytes $m 9 $raw
    return ,$m.ToArray()
}

# The graph with the loudness stage taken out (and the name of its output given back to the node that
# made it), then, unless $Peak is 0, put in anew. Everything else is copied byte for byte.
function Convert-OnnxGraph {
    param([byte[]]$Bytes, [long]$From, [long]$To, [double]$Peak, [double]$Boost)
    $fields = Get-PbFields -Bytes $Bytes -From $From -To $To
    $outputName = $null
    foreach ($f in $fields) {
        if ($f.Field -eq 12 -and $f.Wire -eq 2) {       # GraphProto.output: ValueInfoProto, name 1
            foreach ($g in (Get-PbFields -Bytes $Bytes -From $f.From -To $f.End)) {
                if ($g.Field -eq 1) { $outputName = Get-PbString $Bytes $g; break }
            }
            break
        }
    }
    if (-not $outputName) { throw 'The model has no output.' }
    $on = $Peak -gt 0
    # Wherever the graph's output is named, it becomes pev_raw (with the stage on) or stays itself.
    $oldName = if ($on) { $outputName } else { $script:PevRaw }
    $newName = if ($on) { $script:PevRaw } else { $outputName }

    $m = New-Object System.IO.MemoryStream
    $renamed = 0
    foreach ($f in $fields) {
        if ($f.Field -eq 1 -and $f.Wire -eq 2) {        # NodeProto
            $parts = Get-PbFields -Bytes $Bytes -From $f.From -To $f.End
            $name = ''
            foreach ($p in $parts) { if ($p.Field -eq 3) { $name = Get-PbString $Bytes $p } }
            if ($name.StartsWith($script:PevMark)) { continue }
            $node = New-Object System.IO.MemoryStream
            $changed = $false
            foreach ($p in $parts) {
                $text = if (($p.Field -eq 1 -or $p.Field -eq 2) -and $p.Wire -eq 2) { Get-PbString $Bytes $p } else { $null }
                if ($null -ne $text -and $text -ceq $oldName) {
                    Add-PbString $node $p.Field $newName
                    $changed = $true
                } else {
                    Copy-PbField $node $Bytes $p
                }
                # The node that makes the output, whether renamed now or on an earlier run.
                if ($p.Field -eq 2 -and ($text -ceq $oldName -or $text -ceq $newName)) { $renamed++ }
            }
            if ($changed) { Add-PbBytes $m 1 $node.ToArray() } else { Copy-PbField $m $Bytes $f }
        } elseif ($f.Field -eq 5 -and $f.Wire -eq 2) {  # TensorProto (initializer), name 8
            $name = ''
            foreach ($p in (Get-PbFields -Bytes $Bytes -From $f.From -To $f.End)) {
                if ($p.Field -eq 8) { $name = Get-PbString $Bytes $p; break }
            }
            if (-not $name.StartsWith($script:PevMark)) { Copy-PbField $m $Bytes $f }
        } else {
            Copy-PbField $m $Bytes $f
        }
    }
    if ($on) {
        if ($renamed -ne 1) { throw "Could not find what makes the model's output ($renamed found)." }
        # out = n * g, or tanh(n * k) * g; n = raw / max(|raw| loudest, 0.01), as Piper scales it.
        $mk = $script:PevMark
        Add-PbBytes $m 1 (New-OnnxNode "${mk}abs" 'Abs' @($script:PevRaw) @("${mk}a"))
        Add-PbBytes $m 1 (New-OnnxNode "${mk}loudest" 'ReduceMax' @("${mk}a") @("${mk}l"))
        Add-PbBytes $m 1 (New-OnnxNode "${mk}floor" 'Max' @("${mk}l", "${mk}eps") @("${mk}d"))
        Add-PbBytes $m 1 (New-OnnxNode "${mk}normalize" 'Div' @($script:PevRaw, "${mk}d") @("${mk}n"))
        Add-PbBytes $m 5 (New-OnnxScalar "${mk}eps" 0.01)
        if ($Boost -gt 1.0001) {
            Add-PbBytes $m 1 (New-OnnxNode "${mk}drive" 'Mul' @("${mk}n", "${mk}k") @("${mk}nk"))
            Add-PbBytes $m 1 (New-OnnxNode "${mk}limit" 'Tanh' @("${mk}nk") @("${mk}t"))
            Add-PbBytes $m 1 (New-OnnxNode "${mk}level" 'Mul' @("${mk}t", "${mk}g") @($outputName))
            Add-PbBytes $m 5 (New-OnnxScalar "${mk}k" $Boost)
            Add-PbBytes $m 5 (New-OnnxScalar "${mk}g" ($Peak / [Math]::Tanh($Boost)))
        } else {
            Add-PbBytes $m 1 (New-OnnxNode "${mk}level" 'Mul' @("${mk}n", "${mk}g") @($outputName))
            Add-PbBytes $m 5 (New-OnnxScalar "${mk}g" $Peak)
        }
    }
    return ,$m.ToArray()
}

# Writes the model with the loudness stage (see the top), and with $Meta as its metadata when given
# (else its metadata as it is). $InPath and $OutPath may be the same file.
function Write-PiperModel {
    param([string]$InPath, [string]$OutPath, $Meta, [double]$Peak, [double]$Boost)
    $data = [System.IO.File]::ReadAllBytes($InPath)
    $fields = Get-PbFields -Bytes $data -From 0 -To $data.Length
    if ($fields.Count -lt 3 -or -not ($fields | Where-Object { $_.Field -eq 7 })) { throw 'This does not look like an ONNX model.' }
    $temp = "$OutPath.writing"
    $out = [System.IO.File]::Create($temp)
    try {
        foreach ($f in $fields) {
            if ($f.Field -eq 7 -and $f.Wire -eq 2) {
                Add-PbBytes $out 7 (Convert-OnnxGraph -Bytes $data -From $f.From -To $f.End -Peak $Peak -Boost $Boost)
            } elseif ($f.Field -eq 14 -and $null -ne $Meta) {
                continue
            } else {
                Copy-PbField $out $data $f
            }
        }
        if ($null -ne $Meta) {
            foreach ($key in $Meta.Keys) {             # metadata_props: StringStringEntryProto, key 1, value 2
                $entry = New-Object System.IO.MemoryStream
                Add-PbString $entry 1 ([string]$key)
                Add-PbString $entry 2 ([string]$Meta[$key])
                Add-PbBytes $out 14 $entry.ToArray()
            }
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

function Test-VoiceSettings([double]$Peak, [double]$Boost, [double]$LengthScale) {
    if ($Peak -lt 0 -or $Peak -gt 1) { throw "peak must be from 0 to 1 (it is $(Format-Number $Peak))." }
    if ($Boost -lt 1 -or $Boost -gt 5) { throw "boost must be from 1 to 5 (it is $(Format-Number $Boost))." }
    if ($LengthScale -lt 0.3 -or $LengthScale -gt 3) { throw "length_scale must be from 0.3 to 3 (it is $(Format-Number $LengthScale))." }
}

# What a voice folder's voice.json sets: Peak, Boost, LengthScale ($null where it says nothing) and
# Engine (whether the engine applies the loudness). $null when there is no voice.json.
function Get-VoiceSettings([string]$Dir) {
    $path = Join-Path $Dir 'voice.json'
    if (-not (Test-Path $path)) { return $null }
    $text = [System.IO.File]::ReadAllText($path, $script:PevUtf8)
    $c = [Globalization.CultureInfo]::InvariantCulture
    $read = {
        param($key)
        $m = [regex]::Match($text, '"' + $key + '"\s*:\s*' + $script:PevNumber)
        if ($m.Success) { [double]::Parse($m.Groups[1].Value, $c) } else { $null }
    }
    $engineMark = & $read 'engine_loudness'
    return [pscustomobject]@{ Peak = (& $read 'peak'); Boost = (& $read 'boost'); LengthScale = (& $read 'length_scale')
                              Engine = ($null -ne $engineMark -and $engineMark -ge 1) }
}

# Records a voice's loudness and speed in its voice.json, for the engine. Written to a new file that
# then replaces the old, so the engine never reads half a file.
function Set-VoiceSettings([string]$Dir, [double]$Peak, [double]$Boost, [double]$LengthScale) {
    Test-VoiceSettings $Peak $Boost $LengthScale
    $path = Join-Path $Dir 'voice.json'
    $text = [System.IO.File]::ReadAllText($path, $script:PevUtf8).Replace("`r`n", "`n")
    $block = "`"loudness`": {`n    `"peak`": $(Format-Number $Peak),`n    `"boost`": $(Format-Number $Boost)`n  }"
    $existing = [regex]::Match($text, '"loudness"\s*:\s*\{[^}]*\}')
    if ($existing.Success) {
        $text = $text.Substring(0, $existing.Index) + $block + $text.Substring($existing.Index + $existing.Length)
    } else {
        $end = $text.LastIndexOf('}')
        $text = $text.Substring(0, $end).TrimEnd() + ",`n  $block`n}`n"
    }
    $speed = [regex]::Match($text, '"length_scale"\s*:\s*' + $script:PevNumber)
    if ($speed.Success) {
        $g = $speed.Groups[1]
        $text = $text.Substring(0, $g.Index) + (Format-Number $LengthScale) + $text.Substring($g.Index + $g.Length)
    } else {
        $end = $text.LastIndexOf('}')
        $text = $text.Substring(0, $end).TrimEnd() + ",`n  `"inference`": {`n    `"length_scale`": $(Format-Number $LengthScale)`n  }`n}`n"
    }
    if ($text -notmatch '"engine_loudness"\s*:') {
        $end = $text.LastIndexOf('}')
        $text = $text.Substring(0, $end).TrimEnd() + ",`n  `"engine_loudness`": 1`n}`n"
    }
    $temp = "$path.writing"
    [System.IO.File]::WriteAllText($temp, $text, $script:PevUtf8)
    Move-Item $temp $path -Force
}

# A voice installed before 0.6 has its loudness inside its model: takes it out (the model becomes
# Piper's own again) and has the engine apply the same loudness instead. False when there was nothing
# to do. Takes a few seconds per voice, once.
function Convert-ToEngineLoudness([string]$Dir, [double]$DefaultPeak, [double]$DefaultBoost) {
    $settings = Get-VoiceSettings $Dir
    if ($null -eq $settings -or $settings.Engine) { return $false }
    $model = Join-Path $Dir 'model.onnx'
    Write-PiperModel -InPath $model -OutPath $model -Meta $null -Peak 0 -Boost 1
    $peak = if ($null -ne $settings.Peak) { $settings.Peak } else { $DefaultPeak }
    $boost = if ($null -ne $settings.Boost) { $settings.Boost } else { $DefaultBoost }
    $speed = if ($null -ne $settings.LengthScale) { $settings.LengthScale } else { 1 }
    Set-VoiceSettings $Dir $peak $boost $speed
    return $true
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
    # The model as Piper made it (no loudness stage: the engine applies the loudness).
    Write-PiperModel -InPath $Onnx -OutPath (Join-Path $OutDir 'model.onnx') -Meta $meta -Peak 0 -Boost 1
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
  },
  "engine_loudness": 1
}
"@
    [System.IO.File]::WriteAllText((Join-Path $OutDir 'voice.json'), $voiceJson, $script:PevUtf8)
}
