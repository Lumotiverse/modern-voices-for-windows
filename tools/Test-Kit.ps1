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
$App = Join-Path $env:ProgramFiles 'Modern Voices for Windows'
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
$code = Invoke-Install @('-Voices', 'en_GB-alba-medium,en_GB-northern_english_male-medium,en_GB-vctk-medium#p239', '-Unattended', '-NoPause')
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
Check ($ratio -gt 1.6) ("length_scale 2: Alba {0:N2}x as long" -f $ratio)
Set-VoiceSettings $albaDir 0.95 1.5 1

Step 'Settings take effect in a program already speaking'
$lines = & $PS64 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $App 'installer\Test.ps1') -Live $albaName -Dir $albaDir 2>&1
$lines | ForEach-Object { Write-Host "  $_" }
$liveResult = ($lines | Where-Object { ([string]$_).StartsWith('{') } | Select-Object -First 1) | ConvertFrom-Json
Check ($liveResult -and [math]::Abs($liveResult.before - 0.95) -lt 0.03 -and [math]::Abs($liveResult.after - 0.5) -lt 0.03) "same process: peak $($liveResult.before), then $($liveResult.after) after the change"

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
