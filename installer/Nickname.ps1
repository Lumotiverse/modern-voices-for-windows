# Gives a Modern Voice a nickname: the words in brackets at the end of its name ("Alba (British)" becomes
# "Alba (Storyteller)"), for 64- and 32-bit programs. Run as administrator by Modern Voices Reader's
# nickname box (Windows asks for permission); Modern Voices Config does the same itself.
#
# Once:      -Token <the voice's name in Windows' list, ModernVoices_...>  -Nick64 <the nickname>
#            Ends with 0 (done), 2 (not a Modern Voice, or no nickname), 3 (another voice has that name),
#            1 (failed).
# Staying:   -Serve <a pipe's name>  -OwnerPid <the Reader's process>  -OwnerSid <the Reader's user>
#            So that Windows asks once while the Reader is open, not at every nickname: the Reader starts
#            this the first time, and it stays, hidden, until the Reader closes. Over the pipe (which only
#            the Reader's user may open) it takes lines of "<token> <nickname>" and answers each with the
#            number above. That is all it can be asked to do: rename a Modern Voice.
# The nickname is given as base64 of its UTF-8 (so any letters pass on a command line or a pipe).
param(
    [string]$Token,
    [string]$Nick64,
    [string]$Serve,
    [int]$OwnerPid,
    [string]$OwnerSid
)
$ErrorActionPreference = 'Stop'
$Tokens64 = 'HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens'
$Tokens32 = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens'
$Nicknames = 'HKLM:\SOFTWARE\Modern Voices for Windows\Nicknames'

function Set-Nick([string]$Token, [string]$Nick64) {
    try {
        $nick = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Nick64))
        $nick = ($nick -replace '[()\r\n]', ' ' -replace '\s+', ' ').Trim()
        if ($nick.Length -gt 60) { $nick = $nick.Substring(0, 60).Trim() }
        if (-not $nick -or $Token -notmatch '^ModernVoices_[A-Za-z0-9_\-]+$') { return 2 }
        $key = Join-Path $Tokens64 $Token
        if (-not (Test-Path $key)) { return 2 }
        $props = Get-ItemProperty $key
        if ($props.ModernVoice -ne 1 -or -not $props.VoiceId) { return 2 }
        $was = [string]$props.'(default)'
        if ($was -notmatch '^(.*)\(([^()]*)\)\s*$') { return 2 }
        $display = $Matches[1] + "($nick)"
        foreach ($other in Get-ChildItem $Tokens64) {
            if ($other.PSChildName -ne $Token -and $other.PSChildName -like 'ModernVoices_*' -and
                [string](Get-ItemProperty $other.PSPath).'(default)' -eq $display) { return 3 }
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
        return 0
    } catch {
        return 1
    }
}

if (-not $Serve) { exit (Set-Nick $Token $Nick64) }

# Staying: one request at a time, until the Reader is gone (looked for every second) or says "quit".
if ($Serve -notmatch '^ModernVoicesNickname_[A-Za-z0-9]+$' -or $OwnerSid -notmatch '^S-1-[0-9\-]+$' -or $OwnerPid -le 0) { exit 2 }
try {
    $security = New-Object System.IO.Pipes.PipeSecurity
    $owner = New-Object System.Security.Principal.SecurityIdentifier($OwnerSid)
    $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
    $security.AddAccessRule((New-Object System.IO.Pipes.PipeAccessRule($owner, [System.IO.Pipes.PipeAccessRights]::ReadWrite, [System.Security.AccessControl.AccessControlType]::Allow)))
    $security.AddAccessRule((New-Object System.IO.Pipes.PipeAccessRule($me, [System.IO.Pipes.PipeAccessRights]::FullControl, [System.Security.AccessControl.AccessControlType]::Allow)))
    while ($true) {
        $pipe = New-Object System.IO.Pipes.NamedPipeServerStream($Serve, [System.IO.Pipes.PipeDirection]::InOut, 1,
            [System.IO.Pipes.PipeTransmissionMode]::Byte, [System.IO.Pipes.PipeOptions]::Asynchronous, 4096, 4096, $security)
        try {
            $waiting = $pipe.BeginWaitForConnection($null, $null)
            while (-not $waiting.AsyncWaitHandle.WaitOne(1000)) {
                if (-not (Get-Process -Id $OwnerPid -ErrorAction SilentlyContinue)) { exit 0 }
            }
            $pipe.EndWaitForConnection($waiting)
            $reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
            $line = [string]$reader.ReadLine()
            if ($line -eq 'quit') { exit 0 }
            $parts = $line -split ' '
            $code = if ($parts.Count -eq 2) { Set-Nick $parts[0] $parts[1] } else { 2 }
            $answer = [System.Text.Encoding]::UTF8.GetBytes("$code`n")
            $pipe.Write($answer, 0, $answer.Length)
            $pipe.Flush()
            $pipe.WaitForPipeDrain()
        } catch {
            # (A request that broke off: the next one is waited for.)
        } finally {
            $pipe.Dispose()
        }
    }
} catch {
    exit 1
}
