<#
Modern Voices - tries the installed voices through Windows' speech API, the way other
programs use them (System.Speech, which sits on SAPI 5).

  Test.ps1                  says a line aloud with each voice, showing each word as the engine
                            reports it (what programs use to highlight the word being said)
  Test.ps1 -Quiet           speaks into a file instead, and prints one line of JSON per voice: how
                            much audio came out, how loud (its peak, 0 to 1), how many words and
                            bookmarks were reported. Used by the installer, which also runs it in
                            32-bit PowerShell for 32-bit programs.
  Test.ps1 -Live NAME -Dir FOLDER
                            the build's test of live settings: speaks with voice NAME, changes its
                            voice.json (in FOLDER) to peak 0.5, no boost, speaks again in the same
                            process, puts the settings back; prints the two peaks as JSON.
  Test.ps1 -Measure NAME -TextBase64 TEXT [-Rate N]
                            the build's test of pauses: speaks TEXT (base64 of UTF-16) with voice
                            NAME at SAPI rate N into a file; prints its length, the silent gaps
                            inside it and the silence at its end (ms) as JSON.

Only voices of this program (token names starting ModernVoices_) are tried.
#>
param(
    [switch]$Quiet,
    [string[]]$Only,
    [switch]$NoPause,
    [string]$Live,
    [string]$Dir,
    [string]$Measure,
    [string]$TextBase64,
    [int]$Rate = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$bits = if ([Environment]::Is64BitProcess) { 64 } else { 32 }
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$ours = @($synth.GetInstalledVoices() | Where-Object { $_.VoiceInfo.Id -like 'ModernVoices_*' -and $_.Enabled })
if ($Only) { $ours = @($ours | Where-Object { $Only -contains $_.VoiceInfo.Name }) }

if ($ours.Count -eq 0) {
    if ($Quiet) { '{"bits":' + $bits + ',"none":true}' }
    else {
        Write-Host "No Modern Voices are installed for $bits-bit programs." -ForegroundColor Yellow
        if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    }
    exit 0
}

function Get-Ssml([string]$Culture, [string]$Body) {
    $lang = if ($Culture) { $Culture } else { 'en-US' }
    return "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='$lang'>$Body</speak>"
}

# The loudest sample of a 16-bit mono WAV file, from 0 to 1.
function Get-WavPeak([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [int](($bytes.Length - 44) / 2)
    if ($count -le 0) { return 0 }
    $samples = New-Object 'int16[]' $count
    [Buffer]::BlockCopy($bytes, 44, $samples, 0, $count * 2)
    $max = 0
    foreach ($v in $samples) { $a = [math]::Abs([int]$v); if ($a -gt $max) { $max = $a } }
    return [math]::Round($max / 32768.0, 3)
}

# How loud a 16-bit mono WAV file is on the whole (the root of its mean square), from 0 to 1.
function Get-WavLevel([string]$Path) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [int](($bytes.Length - 44) / 2)
    if ($count -le 0) { return 0 }
    $samples = New-Object 'int16[]' $count
    [Buffer]::BlockCopy($bytes, 44, $samples, 0, $count * 2)
    $sum = 0.0
    foreach ($v in $samples) { $sum += [double]$v * $v }
    return [math]::Round([math]::Sqrt($sum / $count) / 32768.0, 4)
}

$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)

# The silences inside a 16-bit mono WAV (not at its start or end): stretches of at least 60 ms below
# 2% of full scale, in ms.
function Get-WavGaps([string]$Path, [int]$SampleRate) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [int](($bytes.Length - 44) / 2)
    $samples = New-Object 'int16[]' $count
    [Buffer]::BlockCopy($bytes, 44, $samples, 0, $count * 2)
    $quiet = 655
    $first = 0; while ($first -lt $count -and [math]::Abs([int]$samples[$first]) -lt $quiet) { $first++ }
    $last = $count - 1; while ($last -gt $first -and [math]::Abs([int]$samples[$last]) -lt $quiet) { $last-- }
    $gaps = @()
    $run = 0
    for ($i = $first; $i -le $last; $i++) {
        if ([math]::Abs([int]$samples[$i]) -lt $quiet) { $run++ }
        else {
            if ($run -ge $SampleRate * 0.06) { $gaps += [math]::Round($run * 1000.0 / $SampleRate) }
            $run = 0
        }
    }
    return ,$gaps
}

# The silence at the end of a 16-bit mono WAV, in ms.
function Get-WavTail([string]$Path, [int]$SampleRate) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $count = [int](($bytes.Length - 44) / 2)
    $samples = New-Object 'int16[]' $count
    [Buffer]::BlockCopy($bytes, 44, $samples, 0, $count * 2)
    $last = $count - 1
    while ($last -ge 0 -and [math]::Abs([int]$samples[$last]) -lt 655) { $last-- }
    return [math]::Round(($count - 1 - $last) * 1000.0 / $SampleRate)
}

if ($Measure) {
    $text = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($TextBase64))
    $synth.SelectVoice($Measure)
    $synth.Rate = $Rate
    $wav = Join-Path $env:TEMP ("modernvoices_measure_{0}.wav" -f [guid]::NewGuid().ToString('N'))
    $synth.SetOutputToWaveFile($wav, $format)
    $synth.Speak($text)
    $synth.SetOutputToNull()
    $gaps = Get-WavGaps $wav 22050
    $tail = Get-WavTail $wav 22050
    $seconds = [math]::Round(((Get-Item $wav).Length - 44) / 44100.0, 3)
    Remove-Item $wav -ErrorAction SilentlyContinue
    ('{"seconds":' + $seconds.ToString([Globalization.CultureInfo]::InvariantCulture) + ',"tail":' + $tail + ',"gaps":[' + ($gaps -join ',') + ']}')
    exit 0
}

if ($Live) {
    . (Join-Path $PSScriptRoot 'VoiceConvert.ps1')
    $was = Get-VoiceSettings $Dir
    $synth.SelectVoice($Live)
    $peaks = @()
    foreach ($round in 1, 2) {
        $wav = Join-Path $env:TEMP ("modernvoices_live_{0}.wav" -f [guid]::NewGuid().ToString('N'))
        $synth.SetOutputToWaveFile($wav, $format)
        $synth.Speak('Pulling in three. Watch the patrol on the left.')
        $synth.SetOutputToNull()
        $peaks += Get-WavPeak $wav
        Remove-Item $wav -ErrorAction SilentlyContinue
        if ($round -eq 1) { Set-VoiceSettings $Dir 0.5 1 $was.LengthScale; Start-Sleep -Milliseconds 1200 }
    }
    Set-VoiceSettings $Dir $was.Peak $was.Boost $was.LengthScale
    ('{"before":' + $peaks[0] + ',"after":' + $peaks[1] + '}')
    exit 0
}

if ($Quiet) {
    # 9 words, and a bookmark between the two sentences.
    $body = "Pulling in three. <mark name='7'/>Watch the patrol on the left."
    foreach ($v in $ours) {
        $info = $v.VoiceInfo
        $wav = Join-Path $env:TEMP ("modernvoices_test_{0}_{1}.wav" -f $bits, [guid]::NewGuid().ToString('N'))
        $result = [ordered]@{ name = $info.Name; bits = $bits; seconds = 0; peak = 0; level = 0; words = 0; bookmark = ''; ms = 0; error = '' }
        $words = 'mvw_words'; $marks = 'mvw_marks'
        try {
            Register-ObjectEvent -InputObject $synth -EventName SpeakProgress -SourceIdentifier $words | Out-Null
            Register-ObjectEvent -InputObject $synth -EventName BookmarkReached -SourceIdentifier $marks | Out-Null
            $synth.SelectVoice($info.Name)
            $synth.SetOutputToWaveFile($wav, $format)
            $started = Get-Date
            $synth.SpeakSsml((Get-Ssml $info.Culture.Name $body))
            $result.ms = [int]((Get-Date) - $started).TotalMilliseconds
            $synth.SetOutputToNull()
            Start-Sleep -Milliseconds 300     # let the queued events arrive
            $result.words = @(Get-Event -SourceIdentifier $words -ErrorAction SilentlyContinue).Count
            $mark = @(Get-Event -SourceIdentifier $marks -ErrorAction SilentlyContinue)
            if ($mark.Count -gt 0) { $result.bookmark = [string]$mark[0].SourceEventArgs.Bookmark }
            $result.seconds = [math]::Round(((Get-Item $wav).Length - 44) / 44100.0, 2)
            $result.peak = Get-WavPeak $wav
            $result.level = Get-WavLevel $wav
        } catch {
            $result.error = $_.Exception.Message
        } finally {
            Unregister-Event -SourceIdentifier $words -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier $marks -ErrorAction SilentlyContinue
            Remove-Event -SourceIdentifier $words -ErrorAction SilentlyContinue
            Remove-Event -SourceIdentifier $marks -ErrorAction SilentlyContinue
            try { $synth.SetOutputToNull() } catch { }
            Remove-Item $wav -ErrorAction SilentlyContinue
        }
        ($result | ConvertTo-Json -Compress)
    }
    exit 0
}

# Aloud, each word shown as the engine reports it.
$text = 'Speaking a preview of this voice, to hear how natural it sounds. Each word shows here as the engine reports it.'
Write-Host "Modern Voices: $($ours.Count) voice(s) for $bits-bit programs`n" -ForegroundColor Cyan
$synth.SetOutputToDefaultAudioDevice()
foreach ($v in $ours) {
    $info = $v.VoiceInfo
    Write-Host ("{0}  [{1}]" -f $info.Name, $info.Culture) -ForegroundColor Green
    Write-Host '  ' -NoNewline
    $words = 'mvw_live_words'; $done = 'mvw_live_done'
    try {
        Register-ObjectEvent -InputObject $synth -EventName SpeakProgress -SourceIdentifier $words | Out-Null
        Register-ObjectEvent -InputObject $synth -EventName SpeakCompleted -SourceIdentifier $done | Out-Null
        $synth.SelectVoice($info.Name)
        [void]$synth.SpeakAsync($text)
        $finished = $false
        $until = (Get-Date).AddSeconds(30)
        while (-not $finished -and (Get-Date) -lt $until) {
            $e = Wait-Event -Timeout 1
            if ($null -eq $e) { continue }
            if ($e.SourceIdentifier -eq $words) { Write-Host ($e.SourceEventArgs.Text + ' ') -NoNewline }
            elseif ($e.SourceIdentifier -eq $done) {
                $finished = $true
                if ($e.SourceEventArgs.Error) { Write-Host "`n  FAILED - $($e.SourceEventArgs.Error.Message)" -ForegroundColor Red }
            }
            Remove-Event -EventIdentifier $e.EventIdentifier
        }
        if (-not $finished) { $synth.SpeakAsyncCancelAll(); Write-Host "`n  (stopped after 30 s)" -ForegroundColor Yellow }
    } catch {
        Write-Host "`n  FAILED - $($_.Exception.Message)" -ForegroundColor Red
    } finally {
        Unregister-Event -SourceIdentifier $words -ErrorAction SilentlyContinue
        Unregister-Event -SourceIdentifier $done -ErrorAction SilentlyContinue
        Get-Event -ErrorAction SilentlyContinue | Remove-Event -ErrorAction SilentlyContinue
    }
    Write-Host "`n"
}
if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
