<#
The build's end-to-end test of a packaged kit, on a Windows machine, in Windows PowerShell as users run
it: the voice list window builds; install three voices (one a speaker of a multi-speaker voice); each
voice speaks through System.Speech (SAPI 5) in 64-bit and, when built, 32-bit PowerShell, reporting
words and a bookmark; the engine's signature is trusted; repair (re-applies loudness to the real
models); change the voices (removed ones go); uninstall (nothing left).
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
function Test-Speech([string]$PowerShell, [int]$Bits, [int]$Expected) {
    $lines = & $PowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $App 'installer\Test.ps1') -Quiet 2>&1
    $results = @()
    foreach ($line in $lines) {
        $text = [string]$line
        Write-Host "  $text"
        if ($text.StartsWith('{')) { $results += ($text | ConvertFrom-Json) }
    }
    Check ($results.Count -eq $Expected) "$Bits-bit: $Expected voices listed (got $($results.Count))"
    foreach ($r in $results) {
        if ($r.none) { continue }
        Check (-not $r.error -and $r.seconds -gt 0.5) "$Bits-bit $($r.name): speaks ($($r.seconds) s of audio in $($r.ms) ms) $($r.error)"
        Check ($r.words -ge 7) "$Bits-bit $($r.name): word events ($($r.words), 9 words said)"
        Check ($r.bookmark -eq '7') "$Bits-bit $($r.name): bookmark event ('$($r.bookmark)')"
    }
}

# The engine's verbose log (every call, fragment and sentence), shown at the end.
$logFolder = Join-Path $env:LOCALAPPDATA 'ModernVoices'
New-Item $logFolder -ItemType Directory -Force | Out-Null
New-Item (Join-Path $logFolder 'verbose') -ItemType File -Force | Out-Null

Step 'Voice list window'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Kit 'installer\Install.ps1') -PickerSelfTest
Check ($LASTEXITCODE -eq 0) 'the window builds from the real catalogue'

Step 'Install: Alba, Northern English Male, VCTK speaker p239'
$code = Invoke-Install @('-Voices', 'en_GB-alba-medium,en_GB-northern_english_male-medium,en_GB-vctk-medium#p239', '-Unattended', '-NoPause')
Check ($code -eq 0) 'installer finished without errors'
Check ((Get-OurTokens $Tokens64).Count -eq 3) "3 voices for 64-bit programs ($((Get-OurTokens $Tokens64) -join ', '))"
if ($has32) { Check ((Get-OurTokens $Tokens32).Count -eq 3) '3 voices for 32-bit programs' }
$signature = Get-AuthenticodeSignature (Join-Path $App 'engine\x64\modern_voices.dll')
Check ($signature.Status -eq 'Valid') "64-bit engine signature trusted ($($signature.Status))"
Check (Test-Path (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Modern Voices for Windows\Choose voices.lnk')) 'Start menu shortcut'
Check (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ModernVoicesForWindows') 'Installed apps entry'
$alba = Get-ItemProperty (Join-Path $Tokens64 'ModernVoices_en_GB-alba-medium\Attributes')
Check ($alba.Language -eq '809' -and $alba.Gender -eq 'Female') "Alba tagged British, female ($($alba.Language), $($alba.Gender))"

Step 'Speaking through System.Speech (SAPI 5)'
Test-Speech "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" 64 3
if ($has32) { Test-Speech "$env:SystemRoot\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" 32 3 }

Step 'Repair (re-applies loudness to the installed models)'
$voiceJson = Join-Path $App 'voices\en_GB-alba-medium\voice.json'
(Get-Content $voiceJson -Raw) -replace '"boost": 1.5', '"boost": 2' | Set-Content $voiceJson -Encoding ASCII
$code = Invoke-Install @('-Repair', '-Unattended', '-NoPause')
Check ($code -eq 0) 'repair finished without errors'
Check ((Get-Content $voiceJson -Raw) -match '"boost": 2\b') 'edited loudness kept and applied'
Test-Speech "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" 64 3

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
