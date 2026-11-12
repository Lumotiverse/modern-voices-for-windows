# Gives one Modern Voice a nickname: the words in brackets at the end of its name ("Alba (British)" becomes
# "Alba (Storyteller)"), for 64- and 32-bit programs. Run as administrator by Modern Voices Reader's
# nickname box (Windows asks for permission); Modern Voices Config does the same itself.
#   -Token   the voice's name in Windows' list of voices (ModernVoices_...)
#   -Nick64  the nickname, as base64 of its UTF-8 (so any letters pass on a command line)
# Ends with 0 (done), 2 (not a Modern Voice, or no nickname), 3 (another voice has that name), 1 (failed).
param(
    [string]$Token,
    [string]$Nick64
)
$ErrorActionPreference = 'Stop'
$Tokens64 = 'HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens'
$Tokens32 = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens'
$Nicknames = 'HKLM:\SOFTWARE\Modern Voices for Windows\Nicknames'
try {
    $nick = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Nick64))
    $nick = ($nick -replace '[()\r\n]', ' ' -replace '\s+', ' ').Trim()
    if (-not $nick -or $Token -notmatch '^ModernVoices_[A-Za-z0-9_\-]+$') { exit 2 }
    $key = Join-Path $Tokens64 $Token
    if (-not (Test-Path $key)) { exit 2 }
    $props = Get-ItemProperty $key
    if ($props.ModernVoice -ne 1 -or -not $props.VoiceId) { exit 2 }
    $was = [string]$props.'(default)'
    if ($was -notmatch '^(.*)\(([^()]*)\)\s*$') { exit 2 }
    $display = $Matches[1] + "($nick)"
    foreach ($other in Get-ChildItem $Tokens64) {
        if ($other.PSChildName -ne $Token -and $other.PSChildName -like 'ModernVoices_*' -and
            [string](Get-ItemProperty $other.PSPath).'(default)' -eq $display) { exit 3 }
    }
    if (-not (Test-Path $Nicknames)) { New-Item $Nicknames -Force | Out-Null }
    Set-ItemProperty $Nicknames -Name ([string]$props.VoiceId) -Value $nick
    foreach ($root in $Tokens64, $Tokens32) {
        $at = Join-Path $root $Token
        if (Test-Path $at) {
            Set-ItemProperty $at -Name '(default)' -Value $display
            Set-ItemProperty "$at\Attributes" -Name 'Name' -Value $display
        }
    }
    exit 0
} catch {
    exit 1
}
