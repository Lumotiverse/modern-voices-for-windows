<#
Modern Voices for Windows - tries the installed voices through Windows' speech API, the way other
programs use them (System.Speech, which sits on SAPI 5).

  Test.ps1                  says a line aloud with each voice, showing each word as the engine
                            reports it (what programs use to highlight the word being said)
  Test.ps1 -Quiet           speaks into a file instead, and prints one line of JSON per voice: how
                            much audio came out, how many words and bookmarks were reported. Used by
                            the installer, which also runs it in 32-bit PowerShell for 32-bit programs.

Only voices of this program (token names starting ModernVoices_) are tried.
#>
param(
    [switch]$Quiet,
    [string[]]$Only,
    [switch]$NoPause
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

if ($Quiet) {
    # 9 words, and a bookmark between the two sentences.
    $body = "Pulling in three. <mark name='7'/>Watch the patrol on the left."
    $format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(22050,
        [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
    foreach ($v in $ours) {
        $info = $v.VoiceInfo
        $wav = Join-Path $env:TEMP ("modernvoices_test_{0}_{1}.wav" -f $bits, [guid]::NewGuid().ToString('N'))
        $result = [ordered]@{ name = $info.Name; bits = $bits; seconds = 0; words = 0; bookmark = ''; ms = 0; error = '' }
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
$text = 'Pulling in three. Watch the patrol on the left, it has a skull!'
Write-Host "Modern Voices for Windows: $($ours.Count) voice(s) for $bits-bit programs`n" -ForegroundColor Cyan
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
