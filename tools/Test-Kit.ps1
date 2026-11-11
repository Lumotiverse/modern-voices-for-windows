<#
The build's end-to-end test of a packaged kit, on a Windows machine, in Windows PowerShell as users run
it: the manager window builds and its controls work (speakers, duplicate warning, sliders saving to
voice.json); install three voices (one a speaker of a multi-speaker voice); each speaks through
System.Speech (SAPI 5) in 64-bit and, when built, 32-bit PowerShell, reporting words and a bookmark, at
the loudness its voice.json sets (measured); a settings change takes effect, also in a process already
speaking; speed works; repair; change the voices (removed ones go); uninstall (nothing left).
#>
param([Parameter(Mandatory)][string]$Kit)

$ErrorActionPreference = 'Stop'
$Kit = (Resolve-Path $Kit).Path
# Installed somewhere other than Program Files, chosen as a person would choose a folder: the program
# makes its own folder inside it.
$InstallParent = 'C:\MV Test'
$App = Join-Path $InstallParent 'Modern Voices for Windows'
$Tokens64 = 'HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens'
$Tokens32 = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens'
$has32 = Test-Path (Join-Path $Kit 'engine\x86\modern_voices.dll')
$failures = New-Object System.Collections.Generic.List[string]

function Step([string]$Title) { Write-Host "`n=== $Title ===" -ForegroundColor Cyan }
function Check([bool]$Condition, [string]$What) {
    if ($Condition) { Write-Host "  ok   $What" -ForegroundColor Green }
    else { Write-Host "  FAIL $What" -ForegroundColor Red; $failures.Add($What) }
}
function Get-OurTokens([string]$Root) {
    if (-not (Test-Path $Root)) { return @() }
    return @(Get-ChildItem $Root | Where-Object { (Get-ItemProperty $_.PSPath).ModernVoice -eq 1 } | ForEach-Object { $_.PSChildName })
}
function Invoke-Install([string[]]$Arguments) {
    $global:LASTEXITCODE = 0
    $started = Get-Date
    # Out-Host: the installer's output is shown, not returned along with the exit code.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Kit 'installer\Install.ps1') @Arguments | Out-Host
    $code = $LASTEXITCODE
    Write-Host ("  (exit code {0}, {1:N0} s)" -f $code, ((Get-Date) - $started).TotalSeconds)
    return $code
}
# Speaks with every voice (Test.ps1 -Quiet), checks words and bookmark, and returns the results by
# name; $Peaks maps a voice name to the peak it should reach (else 0.95).
function Test-Speech([string]$PowerShell, [int]$Bits, [int]$Expected, [hashtable]$Peaks = @{}) {
    $lines = & $PowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $App 'installer\Test.ps1') -Quiet 2>&1
    $results = @{}
    foreach ($line in $lines) {
        $text = [string]$line
        Write-Host "  $text"
        if ($text.StartsWith('{')) { $r = $text | ConvertFrom-Json; if ($r.name) { $results[$r.name] = $r } }
    }
    Check ($results.Count -eq $Expected) "$Bits-bit: $Expected voices listed (got $($results.Count))"
    foreach ($r in $results.Values) {
        Check (-not $r.error -and $r.seconds -gt 0.5) "$Bits-bit $($r.name): speaks ($($r.seconds) s of audio in $($r.ms) ms) $($r.error)"
        Check ($r.words -ge 7) "$Bits-bit $($r.name): word events ($($r.words), 9 words said)"
        Check ($r.bookmark -eq '7') "$Bits-bit $($r.name): bookmark event ('$($r.bookmark)')"
        $want = if ($Peaks.ContainsKey($r.name)) { $Peaks[$r.name] } else { 0.95 }
        Check ([math]::Abs($r.peak - $want) -lt 0.03) "$Bits-bit $($r.name): loudness, peak $($r.peak) (set: $want)"
    }
    return $results
}
# The engine's verbose log (every call, fragment and sentence), shown at the end.
$logFolder = Join-Path $env:LOCALAPPDATA 'ModernVoices'
New-Item $logFolder -ItemType Directory -Force | Out-Null
New-Item (Join-Path $logFolder 'verbose') -ItemType File -Force | Out-Null

$PS64 = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$PS32 = "$env:SystemRoot\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
$albaDir = Join-Path $App 'voices\en_GB-alba-medium'
$albaName = 'Alba (British)'

Step 'Manager window (before installing)'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Kit 'installer\Install.ps1') -PickerSelfTest
Check ($LASTEXITCODE -eq 0) 'the window builds from the real catalogue; speakers and duplicate warning work'

Step 'Install: Alba, Northern English Male, VCTK speaker p239'
$code = Invoke-Install @('-Voices', 'en_GB-alba-medium,en_GB-northern_english_male-medium,en_GB-vctk-medium#p239', '-Unattended', '-NoPause',
    '-InstallDir', $InstallParent)
Check ((Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ModernVoicesForWindows').InstallLocation -eq $App) "installed in the chosen folder ($App)"
$users = (New-Object Security.Principal.SecurityIdentifier 'S-1-5-32-545').Translate([Security.Principal.NTAccount]).Value
$rules = @((Get-Acl $App).Access)
$userWrite = @($rules | Where-Object { $_.IdentityReference.Value -eq $users -and
    ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Write) })
Write-Host ("  folder permissions: " + (($rules | ForEach-Object { "$($_.IdentityReference.Value): $($_.FileSystemRights)" }) -join '; '))
Check ($userWrite.Count -eq 0 -and @($rules | Where-Object { $_.IdentityReference.Value -eq $users }).Count -gt 0) 'outside Program Files: ordinary users can read the files, not change them'
$token = Get-ItemProperty (Join-Path $Tokens64 'ModernVoices_en_GB-alba-medium')
Check ($token.SettingsFile -eq (Join-Path $App 'settings.json') -and (Test-Path $token.SettingsFile)) 'voices point at the settings file'
Check ($code -eq 0) 'installer finished without errors'
Check ((Get-OurTokens $Tokens64).Count -eq 3) "3 voices for 64-bit programs ($((Get-OurTokens $Tokens64) -join ', '))"
if ($has32) { Check ((Get-OurTokens $Tokens32).Count -eq 3) '3 voices for 32-bit programs' }
$signature = Get-AuthenticodeSignature (Join-Path $App 'engine\x64\modern_voices.dll')
Check ($signature.Status -eq 'Valid') "64-bit engine signature trusted ($($signature.Status))"
$menu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Modern Voices for Windows'
Check ((Test-Path "$menu\Modern Voices.lnk") -and (Test-Path "$menu\Repair.lnk")) 'Start menu: Modern Voices, Repair'
Check (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ModernVoicesForWindows') 'Installed apps entry'
$alba = Get-ItemProperty (Join-Path $Tokens64 'ModernVoices_en_GB-alba-medium\Attributes')
Check ($alba.Language -eq '809' -and $alba.Gender -eq 'Female') "Alba tagged British, female ($($alba.Language), $($alba.Gender))"
Check ((Get-Content (Join-Path $albaDir 'voice.json') -Raw) -match '"peak": 0.95') 'voice.json: loudness set (95%, 1.5x)'
$settingsText = Get-Content (Join-Path $App 'settings.json') -Raw
Check ($settingsText -match '"speed_percent": 120' -and $settingsText -match '"sentence_ms": 500' -and $settingsText -match '"paragraph_ms": 1200') 'settings.json: speed 120%, sentence 500 ms, paragraph 1200 ms'

Step 'Speaking through System.Speech (SAPI 5), at the set loudness'
$normal = Test-Speech $PS64 64 3
if ($has32) { [void](Test-Speech $PS32 32 3) }

Step 'Manager window (installed voices: sliders save to voice.json)'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Kit 'installer\Install.ps1') -PickerSelfTest
Check ($LASTEXITCODE -eq 0) 'sliders save the settings'

Step 'Settings take effect: volume, speed'
. (Join-Path $App 'installer\VoiceConvert.ps1')
Set-VoiceSettings $albaDir 0.5 1 1
[void](Test-Speech $PS64 64 3 @{ $albaName = 0.5 })
Set-VoiceSettings $albaDir 0.95 1.5 2
$slow = Test-Speech $PS64 64 3
$ratio = $slow[$albaName].seconds / $normal[$albaName].seconds
Check ($ratio -gt 1.45) ("length_scale 2: Alba {0:N2}x as long" -f $ratio)
Set-VoiceSettings $albaDir 0.95 1.5 1

Step 'Settings take effect in a program already speaking'
$lines = & $PS64 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $App 'installer\Test.ps1') -Live $albaName -Dir $albaDir 2>&1
$lines | ForEach-Object { Write-Host "  $_" }
$liveResult = ($lines | Where-Object { ([string]$_).StartsWith('{') } | Select-Object -First 1) | ConvertFrom-Json
Check ($liveResult -and [math]::Abs($liveResult.before - 0.95) -lt 0.03 -and [math]::Abs($liveResult.after - 0.5) -lt 0.03) "same process: peak $($liveResult.before), then $($liveResult.after) after the change"

Step 'Pauses: measured gaps'
$settingsFile = Join-Path $App 'settings.json'
$maleName = 'Northern English Male (British)'
function Measure-Speech([string]$Voice, [string]$Text, [int]$Rate = 0) {
    $b64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Text))
    $line = & $PS64 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $App 'installer\Test.ps1') -Measure $Voice -TextBase64 $b64 -Rate $Rate 2>&1 |
        Where-Object { ([string]$_).StartsWith('{') } | Select-Object -First 1
    Write-Host "    measured ($Voice, rate $Rate): $line" -ForegroundColor DarkGray
    $r = $line | ConvertFrom-Json
    $longest = if (@($r.gaps).Count) { (@($r.gaps) | Measure-Object -Maximum).Maximum } else { 0 }
    return [pscustomobject]@{ Seconds = [double]$r.seconds; Longest = [double]$longest; Tail = [double]$r.tail }
}
function Check-Added([string]$What, $Without, $With, [double]$Expected, [double]$Slack) {
    $added = $With.Longest - $Without.Longest
    Check ([math]::Abs($added - $Expected) -le $Slack) ("{0}: +{1:N0} ms (expected {2:N0} +/- {3:N0}; longest gap {4:N0} -> {5:N0} ms)" -f $What, $added, $Expected, $Slack, $Without.Longest, $With.Longest)
}
# Piper varies its timing (noise_w) and its sound (noise_scale, which can put a faint breath inside a
# pause) a little each time it speaks: both off for these measurements, so that each "with" and
# "without" differ only by the gap. (Read when a voice loads: each measurement is a new process.)
function Set-Noise([string]$Dir, [string[]]$Values) {
    $path = Join-Path $Dir 'voice.json'
    $text = [System.IO.File]::ReadAllText($path)
    $was = @()
    $keys = @('noise_scale', 'noise_w')
    for ($i = 0; $i -lt 2; $i++) {
        $m = [regex]::Match($text, '"' + $keys[$i] + '"\s*:\s*(-?[0-9.]+)')
        $was += $m.Groups[1].Value
        $text = $text.Substring(0, $m.Groups[1].Index) + $Values[$i] + $text.Substring($m.Groups[1].Index + $m.Groups[1].Length)
    }
    [System.IO.File]::WriteAllText($path, $text)
    return ,$was
}
$maleDir = Join-Path $App 'voices\en_GB-northern_english_male-medium'
$albaNoise = Set-Noise $albaDir @('0', '0')
$maleNoise = Set-Noise $maleDir @('0', '0')
$two = 'Pulling in three. Watch the patrol on the left.'
$para = "Pulling in three.`nWatch the patrol on the left."
$comma = 'Pulling in three, watch the patrol on the left.'
$one = 'Watch the patrol on the left.'

# The gaps below are measured at 100% speed for every voice; 125% (as fast as rate +2) after.
Set-GlobalSpeed $settingsFile 100
Set-GlobalPauses $settingsFile 0 0 0
$base = Measure-Speech $albaName $two
$baseFast = Measure-Speech $albaName $two 5
$baseRate2 = Measure-Speech $albaName $two 2
$basePara = Measure-Speech $albaName $para
$baseComma = Measure-Speech $albaName $comma
$baseOne = Measure-Speech $albaName $one
$baseMale = Measure-Speech $maleName $two
Set-GlobalPauses $settingsFile 1000 0 2000
Check-Added 'sentence gap 1000 ms, rate 0' $base (Measure-Speech $albaName $two) 1000 30
Check-Added 'sentence gap 1000 ms, rate +5 (1.73x as fast: 577 ms)' $baseFast (Measure-Speech $albaName $two 5) 577 30
Check-Added 'paragraph gap 2000 ms at a line break' $basePara (Measure-Speech $albaName $para) 2000 30
$withOne = Measure-Speech $albaName $one
# (Not by total length: Piper varies its timing a little each time it speaks.)
Check ($withOne.Tail -lt $baseOne.Tail + 150) ("no gap after the last sentence: silence at the end {0:N0} ms with a 1000 ms sentence gap, {1:N0} ms without" -f $withOne.Tail, $baseOne.Tail)
Set-GlobalPauses $settingsFile 0 600 0
Check-Added 'comma gap 600 ms (each part said separately)' $baseComma (Measure-Speech $albaName $comma) 600 80
# A voice's own pauses: Alba 300 ms while every other voice has 1000.
Set-GlobalPauses $settingsFile 1000 0 2000
Set-VoicePauses $albaDir $true 300 0 600
Check-Added "Alba's own sentence gap 300 ms" $base (Measure-Speech $albaName $two) 300 30
Check-Added 'Northern English Male still the global 1000 ms' $baseMale (Measure-Speech $maleName $two) 1000 30
Set-VoicePauses $albaDir $false 300 0 600
Check-Added 'Alba back on the global 1000 ms' $base (Measure-Speech $albaName $two) 1000 30
# Speed for every voice 125%: speech and gaps 1.25x as fast (sentence gap 1000 ms -> 800 ms).
Set-GlobalSpeed $settingsFile 125
Set-GlobalPauses $settingsFile 0 0 0
$fastBase = Measure-Speech $albaName $two
$ratio = $fastBase.Seconds / $baseRate2.Seconds
Check ([math]::Abs($ratio - 1) -lt 0.05) ("speed 125% at rate 0 as long as 100% at rate +2: {0:N2} s and {1:N2} s" -f $fastBase.Seconds, $baseRate2.Seconds)
Set-GlobalPauses $settingsFile 1000 0 2000
Check-Added 'speed 125%, sentence gap 1000 ms (800 ms)' $fastBase (Measure-Speech $albaName $two) 800 30
Set-GlobalPauses $settingsFile 500 0 1200
Set-GlobalSpeed $settingsFile 120
[void](Set-Noise $albaDir $albaNoise)
[void](Set-Noise $maleDir $maleNoise)
$screens = Join-Path (Split-Path (Resolve-Path $Kit).Path -Parent) 'screens'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Kit 'installer\Install.ps1') -PickerSelfTest -ScreenshotDir $screens | Out-Host
Check ($LASTEXITCODE -eq 0) 'window: pause sliders save (Speed and pauses tab and a voice of its own), preview voices, pictures'

Step 'Repair (registers everything again; settings kept)'
Set-VoiceSettings $albaDir 0.8 2 1
$code = Invoke-Install @('-Repair', '-Unattended', '-NoPause')
Check ($code -eq 0) 'repair finished without errors'
[void](Test-Speech $PS64 64 3 @{ $albaName = 0.8 })
Set-VoiceSettings $albaDir 0.95 1.5 1

Step 'Change voices: Alba only'
$code = Invoke-Install @('-Voices', 'en_GB-alba-medium', '-Unattended', '-NoPause')
Check ($code -eq 0) 'change finished without errors'
Check ((Get-OurTokens $Tokens64).Count -eq 1) 'one voice left'
Check (-not (Test-Path (Join-Path $App 'voices\en_GB-vctk-medium'))) 'removed voice''s files gone'

Step 'Uninstall'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $App 'installer\Uninstall.ps1') -NoPause
Check ((Get-OurTokens $Tokens64).Count -eq 0 -and (Get-OurTokens $Tokens32).Count -eq 0) 'no voices left'
Check (-not (Test-Path $App)) 'program folder gone'
Check (-not (Test-Path 'HKLM:\SOFTWARE\Classes\CLSID\{F9857585-FF01-467B-84C0-D6CA4E0572D2}')) 'engine registration gone'

Step 'Engine log (last 150 lines)'
Get-ChildItem $logFolder -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "--- $($_.FullName)"
    Get-Content $_.FullName -Tail 150 | ForEach-Object { Write-Host "  $_" }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) check(s) failed:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'All checks passed.' -ForegroundColor Green
