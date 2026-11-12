<#
Modern Voices for Windows - install, choose voices, repair.

Adds modern, offline voices (Piper, English) to Windows' speech API (SAPI 5), for programs that use
Windows' installed voices but cannot use Narrator's natural voices. 64-bit and 32-bit programs both.

  Install.ps1                       install, or change which voices are installed (a list to tick)
  Install.ps1 -Repair               register everything again as it is installed (and apply loudness
                                    changes made in a voice's voice.json)
  Install.ps1 -Voices a,b -Unattended
                                    no list, no questions: exactly these voices (catalogue keys, such as
                                    en_GB-alba-medium; a speaker of a multi-speaker voice as key#speaker)

Everything goes in C:\Program Files\Modern Voices for Windows (engine, voices, these scripts), with
an entry in Installed apps and a Start menu folder.
#>
[CmdletBinding()]
param(
    [string[]]$Voices,
    [switch]$Repair,
    [switch]$Unattended,
    [switch]$NoPause,
    [switch]$UsEnglishTags,
    [string]$InstallDir,      # where to install (a folder named Modern Voices for Windows is made in it); else asked
    [switch]$PickerSelfTest,  # builds the voice list window without showing it (for the build's tests)
    [string]$ScreenshotDir,   # with -PickerSelfTest: pictures of the window's tabs are saved here
    [switch]$FunctionsOnly    # defines the functions and stops (for tests)
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $Here                       # the kit, or the installed program's folder

# --- administrator, 64-bit (the arguments go along) ------------------------------------------------
$isAdmin = $false
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $isAdmin = (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} catch { }
if (-not $isAdmin -and -not $PickerSelfTest -and -not $FunctionsOnly) {
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$($MyInvocation.MyCommand.Path)`"")
    if ($Voices) { $forward += '-Voices'; $forward += "`"$($Voices -join ',')`"" }
    if ($Repair) { $forward += '-Repair' }
    if ($Unattended) { $forward += '-Unattended' }
    if ($NoPause) { $forward += '-NoPause' }
    if ($UsEnglishTags) { $forward += '-UsEnglishTags' }
    if ($InstallDir) { $forward += '-InstallDir'; $forward += "`"$InstallDir`"" }
    Start-Process powershell.exe -Verb RunAs -ArgumentList $forward
    exit
}
if (-not [Environment]::Is64BitProcess -and -not $FunctionsOnly) { throw 'Run this from 64-bit PowerShell (use Install.cmd).' }
# -File passes "a,b" as one string: split it.
$Voices = @($Voices | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

. (Join-Path $Here 'VoiceConvert.ps1')

$Version = '0.9.0'
$AppName = 'Modern Voices for Windows'
$Clsid = '{F9857585-FF01-467B-84C0-D6CA4E0572D2}'
$DefaultApp = Join-Path $env:ProgramFiles $AppName

# Where the program is, and everything in it. Set again once the install folder is chosen.
function Set-AppPaths([string]$Path) {
    $script:App = $Path.TrimEnd('\')
    $script:Engine = Join-Path $script:App 'engine'
    $script:Dll64 = Join-Path $script:Engine 'x64\modern_voices.dll'
    $script:Dll32 = Join-Path $script:Engine 'x86\modern_voices.dll'
    $script:Cer = Join-Path $script:Engine 'modern_voices.cer'
    $script:Data = Join-Path $script:Engine 'espeak-ng-data'
    $script:VoiceRoot = Join-Path $script:App 'voices'
    $script:SettingsFile = Join-Path $script:App 'settings.json'
    $script:DictionaryFile = Join-Path $script:App 'dictionary.txt'
}

# The folder the installer puts the program in, for the folder chosen: a folder named
# Modern Voices for Windows inside it (unless that is the one chosen), so that uninstalling only ever
# deletes the program's own folder.
function Resolve-AppFolder([string]$Chosen) {
    $full = [System.IO.Path]::GetFullPath($Chosen.Trim().Trim('"'))
    if ((Split-Path $full -Leaf) -eq $AppName) { return $full.TrimEnd('\') }
    return (Join-Path $full $AppName)
}

# Where it is installed now (Installed apps' entry), or '' when it is not.
function Get-InstalledFolder {
    if (-not (Test-Path $OurKey)) { return '' }
    $location = (Get-ItemProperty $OurKey -ErrorAction SilentlyContinue).InstallLocation
    if ($location -and (Test-Path (Join-Path $location 'engine'))) { return $location.TrimEnd('\') }
    return ''
}

$Settings = 'HKLM:\SOFTWARE\Modern Voices for Windows'
$OurKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ModernVoicesForWindows'
$Tokens64 = 'HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens'
$Tokens32 = 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens'
$Class64 = "HKLM:\SOFTWARE\Classes\CLSID\$Clsid"
$Class32 = "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID\$Clsid"
$StartMenu = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\$AppName"
$PS64 = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
$PS32 = "$env:SystemRoot\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
$Base = 'https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0'   # v1.0.0: the voices as tested
$DefaultPeak = 1
$DefaultBoost = 1.5
Set-AppPaths $DefaultApp
$InPlace = $false

# Names for the catalogue's voices (else its own name, in capitals), and the voices known to be
# male or female (SAPI's Gender attribute; left out where not known or several speakers).
$PrettyNames = @{ hfc_female = 'HFC Female'; hfc_male = 'HFC Male'; l2arctic = 'L2 Arctic'; libritts = 'LibriTTS'
    libritts_r = 'LibriTTS-R'; ljspeech = 'LJ Speech'; vctk = 'VCTK'; jenny_dioco = 'Jenny' }
$Genders = @{ alba = 'Female'; alan = 'Male'; cori = 'Female'; jenny_dioco = 'Female'; northern_english_male = 'Male'
    southern_english_female = 'Female'; amy = 'Female'; bryce = 'Male'; danny = 'Male'; hfc_female = 'Female'
    hfc_male = 'Male'; joe = 'Male'; john = 'Male'; kathleen = 'Female'; kristin = 'Female'; kusal = 'Male'
    lessac = 'Female'; ljspeech = 'Female'; norman = 'Male'; reza_ibrahim = 'Male'; ryan = 'Male' }
$Accents = @{ en_GB = 'British'; en_US = 'American' }
$Lcids = @{ en_GB = '809'; en_US = '409' }

function Stop-Here([string]$Message) {
    Write-Host ''
    Write-Host $Message -ForegroundColor Red
    if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
    exit 1
}

# --- the catalogue ---------------------------------------------------------------------------------

# JSON as dictionaries: Windows PowerShell's ConvertFrom-Json refuses big files and keys that differ
# only by case; JavaScriptSerializer does neither.
function Read-Json([string]$Text) {
    if ($PSVersionTable.PSVersion.Major -ge 6) { return ($Text | ConvertFrom-Json -AsHashtable) }
    Add-Type -AssemblyName System.Web.Extensions
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $serializer.MaxJsonLength = [int]::MaxValue
    $serializer.RecursionLimit = 100
    return $serializer.DeserializeObject($Text)
}

# A line of progress: to the console, and to the window's status line while it is applying changes.
function Write-Status([string]$Text, [string]$Color = 'Gray') {
    Write-Host $Text -ForegroundColor $Color
    if ($script:Ui -and $script:Ui.Busy) {
        $script:Ui.Status.Text = $Text.Trim()
        [System.Windows.Forms.Application]::DoEvents()
    }
}

# Downloads to $Path (through a .part file). While the window is applying changes, in the background
# with its progress shown ($Bytes: the size to expect, when known), so the window keeps responding.
function Get-Download([string]$Url, [string]$Path, [double]$Bytes = 0) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $part = "$Path.part"
    $client = New-Object Net.WebClient
    $client.Headers.Add('User-Agent', "ModernVoicesForWindows/$Version")
    try {
        if ($script:Ui -and $script:Ui.Busy) {
            $client.DownloadFileAsync([uri]$Url, $part)
            while ($client.IsBusy) {
                [System.Windows.Forms.Application]::DoEvents()
                Start-Sleep -Milliseconds 50
                if ($Bytes -gt 0 -and (Test-Path $part)) {
                    $done = (Get-Item $part).Length
                    $script:Ui.Progress.Value = [int][math]::Min(100, 100 * $done / $Bytes)
                }
            }
            $got = if (Test-Path $part) { (Get-Item $part).Length } else { 0 }
            if ($got -eq 0 -or ($Bytes -gt 0 -and $got -ne $Bytes)) { throw "The download of $Url stopped part way (check the internet connection)." }
        } else {
            $client.DownloadFile($Url, $part)
        }
    } finally {
        $client.Dispose()
    }
    Move-Item $part $Path -Force
}

# A dictionary's value, or $null when it has no such key (or is not there at all).
function Get-Value($Dictionary, [string]$Key) {
    if ($null -eq $Dictionary) { return $null }
    if ($Dictionary.ContainsKey($Key)) { return $Dictionary[$Key] }
    return $null
}

function Get-PrettyName([string]$Name) {
    if ($PrettyNames.ContainsKey($Name)) { return $PrettyNames[$Name] }
    return (($Name -split '_') | ForEach-Object { if ($_) { $_.Substring(0, 1).ToUpper() + $_.Substring(1) } }) -join ' '
}

# The English voices of medium or high quality, as objects: Key, Name, Display, Code, Accent, Quality,
# Speakers (name -> id, in id order), the model's and config's paths, sizes and MD5s.
function Get-Catalogue {
    $cache = if ($isAdmin) { Join-Path $App 'catalogue.json' } else { Join-Path $env:TEMP 'modernvoices_catalogue.json' }
    New-Item (Split-Path $cache -Parent) -ItemType Directory -Force | Out-Null
    try {
        Get-Download "$Base/voices.json" $cache
    } catch {
        if (-not (Test-Path $cache)) { throw "Could not download the voice list from Hugging Face: $($_.Exception.Message)" }
        Write-Host "  (could not refresh the voice list; using the one from last time)" -ForegroundColor Yellow
    }
    $all = Read-Json ([System.IO.File]::ReadAllText($cache, (New-Object System.Text.UTF8Encoding $false)))
    $list = New-Object System.Collections.Generic.List[object]
    foreach ($key in $all.Keys) {
        $entry = $all[$key]
        $language = Get-Value $entry 'language'
        if ((Get-Value $language 'family') -ne 'en') { continue }
        $quality = [string](Get-Value $entry 'quality')
        if ($quality -ne 'medium' -and $quality -ne 'high') { continue }
        $code = [string](Get-Value $language 'code')
        $files = Get-Value $entry 'files'
        if ($null -eq $files) { continue }
        $onnx = $null; $config = $null
        foreach ($path in $files.Keys) {
            if ($path.EndsWith('.onnx')) { $onnx = $path }
            elseif ($path.EndsWith('.onnx.json')) { $config = $path }
        }
        if (-not $onnx -or -not $config) { continue }
        $speakers = [ordered]@{}
        $map = Get-Value $entry 'speaker_id_map'
        if ($map -and $map.Count -gt 0) {
            foreach ($pair in ($map.GetEnumerator() | Sort-Object { [int]$_.Value })) { $speakers[[string]$pair.Key] = [int]$pair.Value }
        }
        $name = [string](Get-Value $entry 'name')
        $pretty = Get-PrettyName $name
        if ($quality -eq 'high') { $pretty += ' HQ' }
        $accent = if ($Accents.ContainsKey($code)) { $Accents[$code] } else { $code }
        $onnxInfo = Get-Value $files $onnx
        $configInfo = Get-Value $files $config
        $list.Add([pscustomobject]@{
            Key = [string]$key; Name = $name; Display = $pretty; Code = $code; Accent = $accent; Quality = $quality
            Speakers = $speakers; Onnx = $onnx; Config = $config
            OnnxMd5 = [string](Get-Value $onnxInfo 'md5_digest'); ConfigMd5 = [string](Get-Value $configInfo 'md5_digest')
            SizeMb = [math]::Round([double](Get-Value $onnxInfo 'size_bytes') / 1MB)
            OnnxBytes = [double](Get-Value $onnxInfo 'size_bytes'); ConfigBytes = [double](Get-Value $configInfo 'size_bytes')
        })
    }
    return ,($list | Sort-Object Accent, Display)
}

# A voice id is a catalogue key, or key#speaker for one speaker of a multi-speaker voice.
function Split-VoiceId([string]$Id) {
    $at = $Id.IndexOf('#')
    if ($at -lt 0) { return @($Id, $null) }
    return @($Id.Substring(0, $at), $Id.Substring($at + 1))
}

function Get-TokenName([string]$Id) { return 'ModernVoices_' + ($Id -replace '#', '_s') -replace '[^A-Za-z0-9_\-]', '_' }

# The ids installed now (from the 64-bit tokens).
function Get-InstalledIds {
    $ids = @()
    if (Test-Path $Tokens64) {
        foreach ($key in Get-ChildItem $Tokens64) {
            $props = Get-ItemProperty $key.PSPath
            if ($props.ModernVoice -eq 1 -and $props.VoiceId) { $ids += [string]$props.VoiceId }
        }
    }
    return $ids
}

# --- where to install -------------------------------------------------------------------------------

# The first screen of a fresh install: where the program goes, with Change... to choose another folder.
# Returns the program's folder, or '' when cancelled.
function New-LocationForm([string]$Default) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName $Version - install"
    $form.Size = New-Object System.Drawing.Size(640, 300)
    $form.FormBorderStyle = 'FixedDialog'; $form.MaximizeBox = $false; $form.MinimizeBox = $false
    $form.StartPosition = 'CenterScreen'
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
    $form.TopMost = $true
    $form.add_Shown({ $script:Where.Form.Activate(); $script:Where.Form.TopMost = $false })
    $title = New-Control 'Label' $form 16 14 600 24 "Install $AppName" ''
    $title.Font = New-Object System.Drawing.Font('Segoe UI', 12, [System.Drawing.FontStyle]::Bold)
    [void](New-Control 'Label' $form 16 50 600 20 'Install to:' '')
    $path = New-Control 'TextBox' $form 16 72 480 24 $Default ''
    $change = New-Control 'Button' $form 506 70 100 27 'Change...' ''
    $note = New-Control 'Label' $form 16 106 590 90 '' ''
    $note.ForeColor = [System.Drawing.Color]::DimGray
    $install = New-Control 'Button' $form 400 216 100 30 'Install' ''
    $cancel = New-Control 'Button' $form 506 216 100 30 'Cancel' ''
    $install.DialogResult = 'OK'; $cancel.DialogResult = 'Cancel'
    $form.AcceptButton = $install; $form.CancelButton = $cancel
    $script:Where = @{ Form = $form; Path = $path; Note = $note }
    $change.add_Click({ Select-InstallFolder })
    $path.add_TextChanged({ Update-LocationNote })
    Update-LocationNote
    return $script:Where
}

function Test-UnderProgramFiles([string]$Folder) {
    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($base -and $Folder.StartsWith($base.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Update-LocationNote {
    $text = "Everything goes in this folder: the voice engine (about 25 MB), the voices (60 to 110 MB each) and their " +
        "settings. Uninstalling removes it."
    try {
        $folder = Resolve-AppFolder $script:Where.Path.Text
        if ($folder -ne $script:Where.Path.Text.Trim().TrimEnd('\')) { $text += "`nA folder named $AppName is made in the folder you choose: $folder" }
        if (-not (Test-UnderProgramFiles $folder)) {
            $text += "`nOutside Program Files: only administrators will be able to change the files in it (every program that " +
                "speaks loads the engine from there)."
        }
    } catch {
        $text = 'That is not a folder path.'
    }
    $script:Where.Note.Text = $text
}

function Select-InstallFolder {
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Choose where to install $AppName (a folder named $AppName is made in it)"
    $current = $script:Where.Path.Text.Trim()
    $parent = if ((Split-Path $current -Leaf) -eq $AppName) { Split-Path $current -Parent } else { $current }
    if ($parent -and (Test-Path $parent)) { $dialog.SelectedPath = $parent }
    if ($dialog.ShowDialog($script:Where.Form) -eq 'OK') { $script:Where.Path.Text = Resolve-AppFolder $dialog.SelectedPath }
}

# Asks where to install; the program's folder, or '' when cancelled.
function Read-InstallFolder {
    while ($true) {
        $where = New-LocationForm $DefaultApp
        $answer = $where.Form.ShowDialog()
        $text = $where.Path.Text
        $where.Form.Dispose()
        if ($answer -ne 'OK') { return '' }
        try {
            $folder = Resolve-AppFolder $text
            $drive = [System.IO.Path]::GetPathRoot($folder)
            if (-not $drive -or -not (Test-Path $drive)) { throw "There is no drive $drive." }
            return $folder
        } catch {
            [void][System.Windows.Forms.MessageBox]::Show("Can't install there: $($_.Exception.Message)", $AppName)
        }
    }
}

# Outside Program Files, the folder is made as Program Files is: administrators and Windows may change
# it, everyone else (store apps and sandboxed programs too) may only read and run it. (Every program
# that speaks loads the engine from here, so a folder anyone could write to would let anyone put code
# into them.)
function Protect-AppFolder {
    if (Test-UnderProgramFiles $App) { return }
    & "$env:SystemRoot\System32\icacls.exe" $App /inheritance:r /grant:r '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-18:(OI)(CI)F' `
        '*S-1-5-32-545:(OI)(CI)RX' '*S-1-5-11:(OI)(CI)RX' '*S-1-15-2-1:(OI)(CI)RX' '*S-1-15-2-2:(OI)(CI)RX' | Out-Null
    if ($LASTEXITCODE) { throw "Could not set who may change $App (icacls: $LASTEXITCODE)." }
}

# --- the manager window ------------------------------------------------------------------------------
# Two tabs: Voice manager (the installed voices: how each sounds, sliders saved to its voice.json as
# they move, which the engine reads while speaking; Preview; Windows' default voice) and Add or remove
# voices (the catalogue to tick, with samples, sizes and a warning about duplicate qualities; changes
# applied in the window, with progress). The window's controls and state live in $script:Ui: its event
# handlers run later (while it is shown), in the script's scope.

# Three lines, so that Preview also has paragraph gaps in it.
$PreviewText = ("Speaking a preview of the selected voice, to hear how natural it sounds.`r`n" +
    "Change the dictation speed, and 'Comma pause' values, to adjust the sound to your liking.`r`n" +
    "Change the 'Paragraph gap', to adjust the wait before a new paragraph.")

# A nickname of your own for a voice, shown in brackets in its name in place of its accent ("VCTK p262
# (Irish Female)"): kept by voice id under the program's settings key.
$Nicknames = "$Settings\Nicknames"
function Get-Nickname([string]$Id) {
    if (-not (Test-Path $Nicknames)) { return '' }
    $found = Get-ItemProperty $Nicknames -Name $Id -ErrorAction SilentlyContinue
    if ($null -eq $found) { return '' }
    return [string]$found.$Id
}

# The name a voice would have with a nickname (none: its accent).
function Format-DisplayName($Entry, [string]$SpeakerName, [string]$Nick) {
    $what = if ($Nick) { $Nick } else { $Entry.Accent }
    if ($SpeakerName) { return "$($Entry.Display) $SpeakerName ($what)" }
    return "$($Entry.Display) ($what)"
}

# The name programs see for a voice.
function Get-DisplayName($Entry, [string]$SpeakerName) {
    $id = if ($SpeakerName) { "$($Entry.Key)#$SpeakerName" } else { $Entry.Key }
    return (Format-DisplayName $Entry $SpeakerName (Get-Nickname $id))
}

# Sets (or, empty, removes) a voice's nickname and renames it where Windows lists it, for 64- and 32-bit
# programs. Returns its name now.
function Set-VoiceNickname($Entry, [string]$SpeakerName, [string]$Nick) {
    $id = if ($SpeakerName) { "$($Entry.Key)#$SpeakerName" } else { $Entry.Key }
    if (-not (Test-Path $Nicknames)) { New-Item $Nicknames -Force | Out-Null }
    if ($Nick) { Set-ItemProperty $Nicknames -Name $id -Value $Nick }
    else { Remove-ItemProperty $Nicknames -Name $id -ErrorAction SilentlyContinue }
    $display = Get-DisplayName $Entry $SpeakerName
    foreach ($root in $Tokens64, $Tokens32) {
        $token = Join-Path $root (Get-TokenName $id)
        if (Test-Path $token) {
            Set-ItemProperty $token -Name '(default)' -Value $display
            Set-ItemProperty "$token\Attributes" -Name 'Name' -Value $display
        }
    }
    return $display
}

function Get-FolderMb([string]$Path) {
    if (-not (Test-Path $Path)) { return 0 }
    return [math]::Round(((Get-ChildItem $Path -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB)
}

function New-Control([string]$Type, $Parent, [int]$X, [int]$Y, [int]$Width, [int]$Height, [string]$Text, [string]$Anchor) {
    $c = New-Object "System.Windows.Forms.$Type"
    $c.Location = New-Object System.Drawing.Point($X, $Y)
    $c.Size = New-Object System.Drawing.Size($Width, $Height)
    if ($Text) { $c.Text = $Text }
    if ($Anchor) { $c.Anchor = $Anchor }
    $Parent.Controls.Add($c)
    return $c
}

# SAPI's default voice (what sapi.cpl sets): a token id under the current user. The same id serves 64-
# and 32-bit programs (each reads it in its own view of the registry, where this program registers the
# voice under the same name).
$DefaultVoiceKey = 'HKCU:\Software\Microsoft\Speech\Voices'
function Get-DefaultVoiceToken {
    if (-not (Test-Path $DefaultVoiceKey)) { return '' }
    $id = (Get-ItemProperty $DefaultVoiceKey -ErrorAction SilentlyContinue).DefaultTokenId
    if (-not $id) { return '' }
    return ($id -split '\\')[-1]
}
function Set-DefaultVoiceToken([string]$TokenName) {
    New-Item $DefaultVoiceKey -Force -ErrorAction SilentlyContinue | Out-Null
    Set-ItemProperty $DefaultVoiceKey -Name 'DefaultTokenId' -Value "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\$TokenName"
}

# ---- the window's building blocks. Rows are laid out by what is in them (grids that size themselves),
# so text and sliders can't overlap whatever the display's text size.
function Get-Px([double]$N) { return [int][math]::Round($N * $script:UiScale) }

# Text that wraps to the width it is given.
function New-UiLabel([string]$Text, [switch]$Grey, [switch]$Bold) {
    $l = New-Object System.Windows.Forms.Label
    $l.AutoSize = $true; $l.Dock = 'Fill'; $l.Text = $Text
    $l.Margin = New-Object System.Windows.Forms.Padding(3, (Get-Px 3), 3, (Get-Px 3))
    if ($Grey) { $l.ForeColor = [System.Drawing.Color]::DimGray; $l.Tag = 'muted' }
    if ($Bold) { $l.Font = $script:UiBold }
    return $l
}

function New-UiButton([string]$Text) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $Text; $b.AutoSize = $true; $b.AutoSizeMode = 'GrowAndShrink'
    $b.Padding = New-Object System.Windows.Forms.Padding((Get-Px 8), (Get-Px 2), (Get-Px 8), (Get-Px 2))
    $b.Anchor = 'Left'
    return $b
}

# A tab's rows, each the tab's width, top to bottom; the tab scrolls when they don't fit.
function New-UiPage($Tab) {
    $g = New-Object System.Windows.Forms.TableLayoutPanel
    $g.ColumnCount = 1
    [void]$g.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 100)))
    $g.AutoSize = $true; $g.AutoSizeMode = 'GrowAndShrink'; $g.Dock = 'Top'
    $g.Padding = New-Object System.Windows.Forms.Padding (Get-Px 6)
    $Tab.Controls.Add($g)
    return $g
}

# The next row of a page: as tall as the control needs, or (-Fill) taking the room left over.
function Add-UiRow($Grid, $Control, [switch]$Fill) {
    $Grid.RowCount++
    $style = if ($Fill) { New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent, 100) }
             else { New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize) }
    [void]$Grid.RowStyles.Add($style)
    $Grid.Controls.Add($Control, 0, ($Grid.RowCount - 1))
}

# One row of controls side by side: each column fits its control ('auto') or stretches ('fill').
function New-UiLine([string[]]$Columns) {
    $g = New-Object System.Windows.Forms.TableLayoutPanel
    $g.ColumnCount = $Columns.Count; $g.RowCount = 1
    foreach ($kind in $Columns) {
        $style = if ($kind -eq 'fill') { New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 100) }
                 else { New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::AutoSize) }
        [void]$g.ColumnStyles.Add($style)
    }
    [void]$g.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize)))
    $g.AutoSize = $true; $g.AutoSizeMode = 'GrowAndShrink'; $g.Dock = 'Fill'
    $g.Margin = New-Object System.Windows.Forms.Padding 0
    return $g
}

# Controls one after another, onto the next line when they don't fit.
function New-UiFlow {
    $f = New-Object System.Windows.Forms.FlowLayoutPanel
    $f.AutoSize = $true; $f.AutoSizeMode = 'GrowAndShrink'; $f.Dock = 'Fill'; $f.WrapContents = $true
    $f.Margin = New-Object System.Windows.Forms.Padding 0
    return $f
}

# Sliders: name | slider | value, a row each (and notes under a slider, in its column).
function New-UiSliderGrid { return (New-UiLine @('auto', 'fill', 'auto')) }
function Add-UiGridRow($Grid) {
    if ($Grid.Controls.Count -gt 0) { $Grid.RowCount++; [void]$Grid.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize))) }
    return ($Grid.RowCount - 1)
}
# What the sliders are called in the window (their names in the code stay as they are).
$script:SliderNames = @{ 'Speed' = 'Dictation speed'; 'Sentence gap' = 'Sentence pause'; 'Comma gap' = 'Comma pause'; 'Paragraph gap' = 'Paragraph gap' }
# Scrolls the tab a control is on, as the mouse wheel would.
function Send-UiWheelToPage($Control, [int]$Delta) {
    $tab = $Control.Parent
    while ($tab -and -not ($tab.Tag -is [string] -and $tab.Tag -eq 'page')) { $tab = $tab.Parent }
    if (-not $tab -or -not $tab.VerticalScroll.Visible) { return }
    $lines = [System.Windows.Forms.SystemInformation]::MouseWheelScrollLines
    if ($lines -le 0) { $lines = 3 }
    $step = [int]($Delta / 120 * $lines * $tab.Font.Height)
    $y = [math]::Max(0, [math]::Min($tab.VerticalScroll.Maximum, -$tab.AutoScrollPosition.Y - $step))
    $tab.AutoScrollPosition = New-Object System.Drawing.Point(0, $y)
}

function Add-UiSlider($Grid, [string]$Name, [int]$Min, [int]$Max, [int]$Small, [int]$Large, [int]$Tick) {
    $row = Add-UiGridRow $Grid
    $label = New-UiLabel $(if ($script:SliderNames.ContainsKey($Name)) { $script:SliderNames[$Name] } else { $Name })
    $label.Dock = 'None'; $label.Anchor = 'Left'
    $bar = New-Object System.Windows.Forms.TrackBar
    $bar.Maximum = $Max; $bar.Minimum = $Min; $bar.SmallChange = $Small; $bar.LargeChange = $Large; $bar.TickFrequency = $Tick
    $bar.Dock = 'Fill'
    $bar.Margin = New-Object System.Windows.Forms.Padding 0
    # The mouse wheel over a slider scrolls the tab, not the slider (it moves by dragging, clicking or the arrow keys).
    $bar.add_MouseWheel({ param($sender, $e) if ($e -is [System.Windows.Forms.HandledMouseEventArgs]) { $e.Handled = $true }; Send-UiWheelToPage $sender $e.Delta })
    $value = New-UiLabel ''
    $value.Dock = 'None'; $value.Anchor = 'Left'
    $value.MinimumSize = New-Object System.Drawing.Size((Get-Px 150), 0)
    $Grid.Controls.Add($label, 0, $row); $Grid.Controls.Add($bar, 1, $row); $Grid.Controls.Add($value, 2, $row)
    return @{ Bar = $bar; Value = $value }
}
function Add-UiNote($Grid, $Label) {
    $row = Add-UiGridRow $Grid
    $Grid.Controls.Add($Label, 1, $row)
}

# ---- light and dark. The choice (As Windows, Light, Dark) is kept for the Windows user and shared
# with the Reader; "As Windows" follows Settings > Personalisation > Colours (the mode for apps).
$ThemeKey = 'HKCU:\Software\Modern Voices for Windows'
function Get-ThemeChoice {
    $found = Get-ItemProperty $ThemeKey -Name Theme -ErrorAction SilentlyContinue
    if ($found -and @('light', 'dark') -contains $found.Theme) { return [string]$found.Theme }
    return 'auto'
}
function Set-ThemeChoice([string]$Choice) {
    if (-not (Test-Path $ThemeKey)) { New-Item $ThemeKey -Force | Out-Null }
    Set-ItemProperty $ThemeKey -Name Theme -Value $Choice
}
function Test-DarkTheme {
    $choice = if ($script:UiThemeOverride) { $script:UiThemeOverride } else { Get-ThemeChoice }
    if ($choice -eq 'dark') { return $true }
    if ($choice -eq 'light') { return $false }
    $windows = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Name AppsUseLightTheme -ErrorAction SilentlyContinue
    return ($null -ne $windows) -and ($windows.AppsUseLightTheme -eq 0)
}
function Get-UiPalette([bool]$Dark) {
    $rgb = { param($r, $g, $b) [System.Drawing.Color]::FromArgb($r, $g, $b) }
    if ($Dark) {
        return @{ Dark = $true; Back = (& $rgb 32 32 32); Raised = (& $rgb 51 51 51); Input = (& $rgb 24 24 24); Text = (& $rgb 240 240 240)
                  Muted = (& $rgb 160 160 160); Warn = (& $rgb 255 128 128); Border = (& $rgb 90 90 90); Tab = (& $rgb 20 20 20)
                  Selected = (& $rgb 0 84 153) }
    }
    return @{ Dark = $false; Back = [System.Drawing.SystemColors]::Control; Raised = [System.Drawing.SystemColors]::Control
              Input = [System.Drawing.SystemColors]::Window; Text = [System.Drawing.SystemColors]::ControlText; Muted = [System.Drawing.Color]::DimGray
              Warn = [System.Drawing.Color]::Firebrick; Border = [System.Drawing.SystemColors]::ControlDark; Tab = [System.Drawing.SystemColors]::ControlLight
              Selected = [System.Drawing.SystemColors]::Highlight }
}

function Set-UiThemeOn($Control, $P) {
    if ($Control -is [System.Windows.Forms.Button]) {
        if ($Control.Tag -is [int]) { }   # a tab's button: Show-UiPage colours those
        elseif ($P.Dark) {
            $Control.FlatStyle = 'Flat'; $Control.BackColor = $P.Raised; $Control.ForeColor = $P.Text
            $Control.FlatAppearance.BorderColor = $P.Border
        } else { $Control.FlatStyle = 'Standard'; $Control.ForeColor = $P.Text; $Control.UseVisualStyleBackColor = $true }
    }
    elseif ($Control -is [System.Windows.Forms.TextBox] -or $Control -is [System.Windows.Forms.ListView] -or $Control -is [System.Windows.Forms.ListBox]) {
        $Control.BackColor = $P.Input; $Control.ForeColor = $P.Text
        if ($Control -is [System.Windows.Forms.ListView]) { $Control.OwnerDraw = $P.Dark }
        try { [void][ModernVoices.Look]::SetWindowTheme($Control.Handle, $(if ($P.Dark) { 'DarkMode_Explorer' } else { 'Explorer' }), [NullString]::Value) } catch { }
    }
    elseif ($Control -is [System.Windows.Forms.ComboBox]) {
        $Control.FlatStyle = $(if ($P.Dark) { 'Flat' } else { 'Standard' }); $Control.BackColor = $P.Input; $Control.ForeColor = $P.Text
    }
    elseif ($Control -is [System.Windows.Forms.Label]) {
        $Control.ForeColor = $(if ($Control.Tag -is [string] -and $Control.Tag -eq 'muted') { $P.Muted } elseif ($Control.Tag -is [string] -and $Control.Tag -eq 'warn') { $P.Warn } else { $P.Text })
    }
    elseif ($Control -is [System.Windows.Forms.CheckBox]) { $Control.ForeColor = $P.Text }
    elseif ($Control -is [System.Windows.Forms.TrackBar]) { $Control.BackColor = $P.Back }
    elseif ($Control -is [System.Windows.Forms.ProgressBar]) { }
    else {
        $Control.BackColor = $P.Back; $Control.ForeColor = $P.Text
        if ($Control.Tag -is [string] -and $Control.Tag -eq 'page') {   # a page's own scroll bar
            try { [void][ModernVoices.Look]::SetWindowTheme($Control.Handle, $(if ($P.Dark) { 'DarkMode_Explorer' } else { 'Explorer' }), [NullString]::Value) } catch { }
        }
    }
    foreach ($child in $Control.Controls) { Set-UiThemeOn $child $P }
}

# Makes the window light or dark as chosen (nothing to do when it already is).
function Set-UiTheme {
    if (-not $script:Ui) { return }
    $dark = Test-DarkTheme
    if ($null -ne $script:UiDark -and $script:UiDark -eq $dark) { return }
    $script:UiDark = $dark
    $p = Get-UiPalette $dark
    $script:UiPalette = $p
    if (-not ('ModernVoices.Look' -as [type])) {
        Add-Type -Namespace ModernVoices -Name Look -MemberDefinition @'
[DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(System.IntPtr window, int attribute, ref int value, int size);
[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(System.IntPtr window, string app, string ids);
'@
    }
    $form = $script:Ui.Form
    $form.SuspendLayout()
    $form.BackColor = $p.Back; $form.ForeColor = $p.Text
    try {
        # The title bar (Windows 10 version 2004 and later: 20; before that: 19).
        $on = [int]$dark
        if ([ModernVoices.Look]::DwmSetWindowAttribute($form.Handle, 20, [ref]$on, 4) -ne 0) { [void][ModernVoices.Look]::DwmSetWindowAttribute($form.Handle, 19, [ref]$on, 4) }
    } catch { }
    foreach ($child in $form.Controls) { Set-UiThemeOn $child $p }
    Show-UiPage $script:Ui.PageIndex
    $form.ResumeLayout()
    $form.Refresh()
}

# Shows one of the tabs' pages; its button stands out.
function Show-UiPage([int]$Index) {
    $ui = $script:Ui
    $p = $script:UiPalette
    $ui.PageIndex = $Index
    for ($i = 0; $i -lt $ui.Pages.Count; $i++) {
        $ui.Pages[$i].Visible = ($i -eq $Index)
        $b = $ui.TabButtons[$i]
        $b.Font = $(if ($i -eq $Index) { $script:UiBold } else { $ui.Form.Font })
        if ($p) {
            $b.BackColor = $(if ($i -eq $Index) { $p.Input } else { $p.Tab })
            $b.ForeColor = $(if ($i -eq $Index) { $p.Text } else { $p.Muted })
        }
    }
}

# A list's column headings are drawn by the program when dark (Windows draws them light only).
function Enable-UiDarkHeader($View) {
    $View.add_DrawColumnHeader({
        param($sender, $e)
        $p = $script:UiPalette
        $fill = New-Object System.Drawing.SolidBrush $p.Raised
        $e.Graphics.FillRectangle($fill, $e.Bounds)
        $fill.Dispose()
        $line = New-Object System.Drawing.Pen $p.Border
        $e.Graphics.DrawLine($line, $e.Bounds.Right - 1, $e.Bounds.Top + 3, $e.Bounds.Right - 1, $e.Bounds.Bottom - 4)
        $line.Dispose()
        $flags = [System.Windows.Forms.TextFormatFlags]::VerticalCenter -bor [System.Windows.Forms.TextFormatFlags]::EndEllipsis
        if ($e.Header.TextAlign -eq 'Center') { $flags = $flags -bor [System.Windows.Forms.TextFormatFlags]::HorizontalCenter }
        $inside = New-Object System.Drawing.Rectangle(($e.Bounds.X + 4), $e.Bounds.Y, ($e.Bounds.Width - 8), $e.Bounds.Height)
        [System.Windows.Forms.TextRenderer]::DrawText($e.Graphics, $e.Header.Text, $sender.Font, $inside, $p.Text, $flags)
    })
    # ...and so are its rows (Windows would draw the selected one unreadably on a dark list).
    $View.add_DrawItem({ param($sender, $e) })
    $View.add_DrawSubItem({
        param($sender, $e)
        $p = $script:UiPalette
        $column = $sender.Columns[$e.ColumnIndex]
        $cell = New-Object System.Drawing.Rectangle($e.Bounds.X, $e.Bounds.Y, $column.Width, $e.Bounds.Height)
        $fill = New-Object System.Drawing.SolidBrush $(if ($e.Item.Selected) { $p.Selected } else { $p.Input })
        $e.Graphics.FillRectangle($fill, $cell)
        $fill.Dispose()
        $left = $cell.X + 4
        if ($e.ColumnIndex -eq 0 -and $sender.CheckBoxes) {
            $state = if ($e.Item.Checked) { [System.Windows.Forms.VisualStyles.CheckBoxState]::CheckedNormal } else { [System.Windows.Forms.VisualStyles.CheckBoxState]::UncheckedNormal }
            $size = [System.Windows.Forms.CheckBoxRenderer]::GetGlyphSize($e.Graphics, $state)
            $at = New-Object System.Drawing.Point($left, ($cell.Y + [int](($cell.Height - $size.Height) / 2)))
            [System.Windows.Forms.CheckBoxRenderer]::DrawCheckBox($e.Graphics, $at, $state)
            $left += $size.Width + 4
        }
        $flags = [System.Windows.Forms.TextFormatFlags]::VerticalCenter -bor [System.Windows.Forms.TextFormatFlags]::EndEllipsis -bor [System.Windows.Forms.TextFormatFlags]::NoPrefix
        if ($column.TextAlign -eq 'Center') { $flags = $flags -bor [System.Windows.Forms.TextFormatFlags]::HorizontalCenter }
        $inside = New-Object System.Drawing.Rectangle($left, $cell.Y, [math]::Max(0, $cell.Right - $left - 4), $cell.Height)
        [System.Windows.Forms.TextRenderer]::DrawText($e.Graphics, $e.SubItem.Text, $e.Item.Font, $inside, $p.Text, $flags)
    })
}

# The names Windows has for this program's voices (what Preview asks for).
function Get-OurVoiceNames {
    $tokens = 'HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens'
    return @(Get-ChildItem $tokens | Where-Object { $_.PSChildName -like 'ModernVoices_*' } | ForEach-Object { (Get-ItemProperty $_.PSPath).'(default)' })
}

# Pictures of the window's tabs (the build's test looks at them): as it opens, and made small.
function Save-UiPictures([string]$Dir) {
    $ui = $script:Ui
    New-Item $Dir -ItemType Directory -Force | Out-Null
    $ui.Form.Show()
    foreach ($shape in @(@('open', 0, 0, 'light'), @('dark', 0, 0, 'dark'), @('small', 640, 440, 'light'))) {
        if ($shape[1]) { $ui.Form.Size = New-Object System.Drawing.Size($shape[1], $shape[2]) }
        $script:UiThemeOverride = $shape[3]
        Set-UiTheme
        for ($i = 0; $i -lt $ui.Pages.Count; $i++) {
            Show-UiPage $i
            [System.Windows.Forms.Application]::DoEvents()
            Start-Sleep -Milliseconds 150
            [System.Windows.Forms.Application]::DoEvents()
            $picture = New-Object System.Drawing.Bitmap($ui.Form.Width, $ui.Form.Height)
            $ui.Form.DrawToBitmap($picture, (New-Object System.Drawing.Rectangle(0, 0, $ui.Form.Width, $ui.Form.Height)))
            $picture.Save((Join-Path $Dir ("{0}-{1}.png" -f $shape[0], ($i + 1))), [System.Drawing.Imaging.ImageFormat]::Png)
            $picture.Dispose()
        }
    }
    $script:UiThemeOverride = $null
    Set-UiTheme
    Show-UiPage 0
    $ui.Form.Hide()
    Write-Output "pictures of the window: $Dir"
}

# The Add or remove tab's rows: the tab's size, but no shorter than they need (the tab then scrolls).
function Update-UiAddLayout {
    if (-not $script:Ui -or -not $script:Ui.AddGrid) { return }
    $tab = $script:Ui.AddTab
    $wide = $tab.ClientSize.Width
    $high = [math]::Max($tab.ClientSize.Height, (Get-Px 520))
    $grid = $script:Ui.AddGrid
    if ($grid.Width -ne $wide -or $grid.Height -ne $high) { $grid.Size = New-Object System.Drawing.Size($wide, $high) }
}

function New-ManagerForm($Catalogue, [string[]]$Installed, [bool]$UsTags, $ByKey) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName $Version"
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
    # Sizes here are for a display at 100%; one scaled up (125%, 150%, ...) gets them scaled with its
    # text. Rows are laid out by what is in them, so nothing overlaps at any text size; the window never
    # opens larger than the screen, and a tab that doesn't fit scrolls.
    $screenGraphics = [System.Drawing.Graphics]::FromHwnd([IntPtr]::Zero)
    $script:UiScale = [math]::Max(1.0, $screenGraphics.DpiX / 96.0)
    $screenGraphics.Dispose()
    $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $form.Size = New-Object System.Drawing.Size([math]::Min((Get-Px 900), $area.Width), [math]::Min((Get-Px 800), $area.Height))
    $form.MinimumSize = New-Object System.Drawing.Size([math]::Min((Get-Px 600), $area.Width), [math]::Min((Get-Px 400), $area.Height))
    $form.StartPosition = 'CenterScreen'
    $form.Padding = New-Object System.Windows.Forms.Padding (Get-Px 8)
    # In front of the console it was started from (else it can open behind it, unseen).
    $form.TopMost = $true
    $form.add_Shown({ $script:Ui.Form.Activate(); $script:Ui.Form.TopMost = $false })
    $bold = New-Object System.Drawing.Font($form.Font, [System.Drawing.FontStyle]::Bold)
    $script:UiBold = $bold
    $red = [System.Drawing.Color]::Firebrick

    # The tabs: a button each and a page each (drawn by the program, so that they can be dark too).
    $pageHost = New-Object System.Windows.Forms.Panel
    $pageHost.Dock = 'Fill'
    $form.Controls.Add($pageHost)
    $tabStrip = New-UiFlow
    $tabStrip.Dock = 'Top'; $tabStrip.WrapContents = $false
    $form.Controls.Add($tabStrip)
    $pages = @(); $tabButtons = @()
    foreach ($title in 'Voice manager', 'Speed and pauses', 'Pronunciation', 'Add or remove voices') {
        $tab = New-Object System.Windows.Forms.Panel
        $tab.Dock = 'Fill'; $tab.AutoScroll = $true; $tab.Tag = 'page'; $tab.Visible = $false
        $pageHost.Controls.Add($tab)
        $pages += $tab
        $tabButton = New-UiButton $title
        $tabButton.FlatStyle = 'Flat'; $tabButton.FlatAppearance.BorderSize = 0; $tabButton.TabStop = $false
        $tabButton.Margin = New-Object System.Windows.Forms.Padding(0, 0, (Get-Px 2), 0)
        $tabButton.Tag = $pages.Count - 1
        $tabButton.add_Click({ param($sender, $e) Show-UiPage ([int]$sender.Tag) })
        $tabStrip.Controls.Add($tabButton)
        $tabButtons += $tabButton
    }
    $tabInstalled = $pages[0]; $tabPauses = $pages[1]; $tabWords = $pages[2]; $tabAdd = $pages[3]

    # ---- bottom of the window: progress while applying changes
    $bottom = New-UiLine @('auto', 'auto', 'fill', 'auto', 'auto')
    $bottom.Dock = 'Bottom'
    $bottom.RowStyles.Clear()
    [void]$bottom.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Absolute, (Get-Px 20))))
    [void]$bottom.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize)))
    $bottom.RowCount = 2
    $progress = New-Object System.Windows.Forms.ProgressBar
    $progress.Dock = 'Fill'; $progress.Visible = $false
    $progress.Margin = New-Object System.Windows.Forms.Padding(3, (Get-Px 6), 3, 0)
    $bottom.Controls.Add($progress, 0, 0); $bottom.SetColumnSpan($progress, 5)
    $status = New-UiLabel '' -Grey
    $status.MinimumSize = New-Object System.Drawing.Size(0, (Get-Px 34))
    $apply = New-UiButton 'Apply changes'
    $close = New-UiButton 'Close'
    $close.DialogResult = 'Cancel'
    $themeLabel = New-UiLabel 'Theme'
    $themeLabel.Dock = 'None'; $themeLabel.Anchor = 'Left'
    $themeBox = New-Object System.Windows.Forms.ComboBox
    $themeBox.DropDownStyle = 'DropDownList'; $themeBox.Width = Get-Px 110; $themeBox.Anchor = 'Left'
    foreach ($name in 'As Windows', 'Light', 'Dark') { [void]$themeBox.Items.Add($name) }
    $themeBox.SelectedIndex = [math]::Max(0, [array]::IndexOf(@('auto', 'light', 'dark'), (Get-ThemeChoice)))
    $themeBox.add_SelectedIndexChanged({ Set-ThemeChoice (@('auto', 'light', 'dark')[$script:Ui.ThemeBox.SelectedIndex]); Set-UiTheme })
    $bottom.Controls.Add($themeLabel, 0, 1); $bottom.Controls.Add($themeBox, 1, 1)
    $bottom.Controls.Add($status, 2, 1); $bottom.Controls.Add($apply, 3, 1); $bottom.Controls.Add($close, 4, 1)
    $form.Controls.Add($bottom)
    $form.CancelButton = $close

    # ---- Voice manager
    $page = New-UiPage $tabInstalled
    $installedList = New-Object System.Windows.Forms.ListView
    $installedList.View = 'Details'; $installedList.FullRowSelect = $true; $installedList.HideSelection = $false
    $installedList.MultiSelect = $false
    $installedList.Dock = 'Fill'; $installedList.Height = Get-Px 300
    $installedList.MinimumSize = New-Object System.Drawing.Size(0, (Get-Px 300))
    [void]$installedList.Columns.Add('Voice', (Get-Px 210))
    [void]$installedList.Columns.Add('Speaker', (Get-Px 80))
    $installedList.Columns[1].TextAlign = 'Center'
    [void]$installedList.Columns.Add('Nickname', (Get-Px 190))
    [void]$installedList.Columns.Add('On disk', (Get-Px 70))
    [void]$installedList.Columns.Add('Windows default', (Get-Px 110))
    [void]$installedList.Columns.Add('Folder', (Get-Px 170))
    Add-UiRow $page $installedList
    $speakersOf = [ordered]@{}
    foreach ($id in $Installed) {
        $idParts = Split-VoiceId $id
        if (-not $speakersOf.Contains($idParts[0])) { $speakersOf[$idParts[0]] = @() }
        if ($null -ne $idParts[1]) { $speakersOf[$idParts[0]] += $idParts[1] }
    }

    # The selected voice (a row for each installed speaker): hear it, make it Windows' default.
    $voiceLine = New-UiFlow
    $previewButton = New-UiButton 'Preview'
    $stopButton = New-UiButton 'Stop'
    $defaultButton = New-UiButton 'Make default voice'
    $sapiButton = New-UiButton 'Windows speech settings'
    $previewButton.Enabled = $false; $defaultButton.Enabled = $false
    foreach ($c in $previewButton, $stopButton, $defaultButton, $sapiButton) { $voiceLine.Controls.Add($c) }
    Add-UiRow $page $voiceLine
    $nickLine = New-UiFlow
    $nickLabel = New-UiLabel 'Nickname'
    $nickLabel.Dock = 'None'; $nickLabel.Anchor = 'Left'
    $nickBox = New-Object System.Windows.Forms.TextBox
    $nickBox.Width = Get-Px 170; $nickBox.MaxLength = 40; $nickBox.Anchor = 'Left'; $nickBox.Enabled = $false
    $nickButton = New-UiButton 'Set nickname'
    $nickButton.Enabled = $false
    $nickHint = New-UiLabel "shown in brackets in the voice's name: VCTK p262 (Irish Female). Empty: its accent." -Grey
    $nickHint.Dock = 'None'; $nickHint.Anchor = 'Left'
    foreach ($c in $nickLabel, $nickBox, $nickButton, $nickHint) { $nickLine.Controls.Add($c) }
    Add-UiRow $page $nickLine
    $previewLine = New-UiLine @('auto', 'fill')
    $previewLabel = New-UiLabel 'Preview says'
    $previewLabel.Dock = 'None'; $previewLabel.Anchor = 'Top, Left'
    $previewBox = New-Object System.Windows.Forms.TextBox
    # Several lines (Enter starts a new one: a paragraph pause), three showing.
    $previewBox.Multiline = $true; $previewBox.AcceptsReturn = $true; $previewBox.WordWrap = $true; $previewBox.ScrollBars = 'Vertical'
    $previewBox.Text = $PreviewText; $previewBox.Dock = 'Fill'
    $previewBox.MinimumSize = New-Object System.Drawing.Size(0, ($previewBox.Font.Height * 3 + (Get-Px 18)))
    $previewBox.Height = $previewBox.MinimumSize.Height
    $previewLine.Controls.Add($previewLabel, 0, 0); $previewLine.Controls.Add($previewBox, 1, 0)
    Add-UiRow $page $previewLine
    $defaultLabel = New-UiLabel ''
    Add-UiRow $page $defaultLabel

    $soundLine = New-UiLine @('fill', 'auto')
    $soundLabel = New-UiLabel 'Select a voice above to set how it sounds.' -Bold
    $soundLabel.Dock = 'None'; $soundLabel.Anchor = 'Left'
    $resetButton = New-UiButton 'Reset'
    $resetButton.Enabled = $false
    $soundLine.Controls.Add($soundLabel, 0, 0); $soundLine.Controls.Add($resetButton, 1, 0)
    Add-UiRow $page $soundLine
    $sliders = @{}
    $soundGrid = New-UiSliderGrid
    foreach ($spec in @(@('Volume', 0, 100, 1, 5, 10), @('Boost', 10, 40, 1, 5, 5), @('Speed', 25, 200, 1, 5, 25))) {
        $sliders[$spec[0]] = Add-UiSlider $soundGrid $spec[0] $spec[1] $spec[2] $spec[3] $spec[4] $spec[5]
    }
    Add-UiRow $page $soundGrid
    $ownBox = New-Object System.Windows.Forms.CheckBox
    $ownBox.Text = 'Modify pause duration for the selected voice'
    $ownBox.AutoSize = $true; $ownBox.Enabled = $false; $ownBox.Checked = $false
    Add-UiRow $page $ownBox
    $gapGrid = New-UiSliderGrid
    foreach ($spec in @(@('Sentence gap', $PauseLimits.Sentence), @('Comma gap', $PauseLimits.Comma), @('Paragraph gap', $PauseLimits.Paragraph))) {
        $sliders[$spec[0]] = Add-UiSlider $gapGrid $spec[0] 0 $spec[1] 50 250 250
    }
    $gapGrid.Visible = $false
    Add-UiRow $page $gapGrid
    foreach ($k in @($sliders.Keys)) {
        $sliders[$k].Bar.Enabled = $false
        $sliders[$k].Bar.add_ValueChanged({ Update-UiSliderLabels; if (-not $script:Ui.Filling) { $script:Ui.SaveTimer.Stop(); $script:Ui.SaveTimer.Start() } })
    }
    Add-UiRow $page (New-UiLabel ("Volume: each sentence's loudest moment (0 = as Piper made it). Boost: lifts quiet parts more than loud ones " +
        "(higher sounds more compressed). Dictation speed: 100% = the speed for every voice (Speed and pauses tab). Unticked, the voice uses the pauses " +
        "for every voice; ticked, its own.") -Grey)
    Add-UiRow $page (New-UiLabel ("Changes are saved as you move a slider and take effect straight away, also in programs already using the " +
        "voice (from the next thing they say): press Preview to hear them. A voice's speakers share its settings.") -Grey)

    # ---- Speed and pauses (every voice; pauses for those without their own)
    $page = New-UiPage $tabPauses
    Add-UiRow $page (New-UiLabel 'Speed and pauses for every voice (a voice can adjust its speed and have its own pauses: Voice manager)' -Bold)
    $hearLine = New-UiFlow
    $hearLabel = New-UiLabel 'Preview with'
    $hearLabel.Dock = 'None'; $hearLabel.Anchor = 'Left'
    $previewVoiceBox = New-Object System.Windows.Forms.ComboBox
    $previewVoiceBox.DropDownStyle = 'DropDownList'; $previewVoiceBox.Width = Get-Px 280; $previewVoiceBox.Anchor = 'Left'
    $globalPreview = New-UiButton 'Preview'
    $globalStop = New-UiButton 'Stop'
    $globalPreview.Enabled = $false
    foreach ($c in $hearLabel, $previewVoiceBox, $globalPreview, $globalStop) { $hearLine.Controls.Add($c) }
    Add-UiRow $page $hearLine
    $globalGrid = New-UiSliderGrid
    $speedSlider = Add-UiSlider $globalGrid 'Speed' 25 200 5 25 25
    $speedBar = $speedSlider.Bar; $speedValue = $speedSlider.Value
    $speedValue.Font = $bold
    Add-UiNote $globalGrid (New-UiLabel ("how fast every voice speaks before a program's own rate: 100% by default, the voices' own pace (rate +2 is about 125%) " +
        "(each voice's own Dictation speed, on the Voice manager, adjusts from here)") -Grey)
    $rateWarn = New-UiLabel ''
    $rateWarn.ForeColor = $red; $rateWarn.Tag = 'warn'
    Add-UiNote $globalGrid $rateWarn
    $rateReset = New-UiButton "Set Windows' speed to 0"
    $rateReset.Visible = $false
    $globalGrid.Controls.Add($rateReset, 2, ($globalGrid.RowCount - 1))
    $speedBar.add_ValueChanged({ Update-UiSliderLabels; if (-not $script:Ui.Filling) { $script:Ui.GlobalTimer.Stop(); $script:Ui.GlobalTimer.Start() } })
    $globalBars = @{}
    foreach ($spec in @(@('Sentence gap', $PauseLimits.Sentence, 'after each sentence, when another follows (none after the last)'),
                        @('Comma gap', $PauseLimits.Comma, 'at each , ; or : (0 = commas left as the voice says them)'),
                        @('Paragraph gap', $PauseLimits.Paragraph, 'at a line break, in place of the sentence pause (Enter in Preview says starts a new line)'))) {
        $slider = Add-UiSlider $globalGrid $spec[0] 0 $spec[1] 50 250 250
        $slider.Value.Font = $bold
        Add-UiNote $globalGrid (New-UiLabel $spec[2] -Grey)
        $example = New-UiLabel ''
        Add-UiNote $globalGrid $example
        $globalBars[$spec[0]] = @{ Bar = $slider.Bar; Value = $slider.Value; Example = $example }
        $slider.Bar.add_ValueChanged({ Update-UiSliderLabels; if (-not $script:Ui.Filling) { $script:Ui.GlobalTimer.Stop(); $script:Ui.GlobalTimer.Start() } })
    }
    Add-UiRow $page $globalGrid
    Add-UiRow $page (New-UiLabel ("Each pause is silence added to the voice's own, in milliseconds at 100% dictation speed and a program's rate 0. " +
        "Faster speech uses a smaller share of it and slower a larger share, in step with the speech (the line under each shows what you get). " +
        "A comma pause above 0 has the voice say each part of a sentence separately, so the intonation can sound a little more broken up.") -Grey)
    $resetLine = New-UiLine @('auto', 'fill')
    $pausesReset = New-UiButton 'Reset to defaults'
    $pausesNote = New-UiLabel 'Defaults: dictation speed 100%, sentence 500 ms, comma 0, paragraph 1200 ms. Saved as you move a slider; applies straight away.' -Grey
    $resetLine.Controls.Add($pausesReset, 0, 0); $resetLine.Controls.Add($pausesNote, 1, 0)
    Add-UiRow $page $resetLine

    # ---- Pronunciation: words and names said another way, by every voice in every program
    $page = New-UiPage $tabWords
    Add-UiRow $page (New-UiLabel 'Words and names said your way: by every voice, in every program that uses these voices' -Bold)
    $wordList = New-Object System.Windows.Forms.ListView
    $wordList.View = 'Details'; $wordList.CheckBoxes = $true; $wordList.FullRowSelect = $true; $wordList.HideSelection = $false
    $wordList.MultiSelect = $false; $wordList.Dock = 'Fill'; $wordList.Height = Get-Px 260
    $wordList.MinimumSize = New-Object System.Drawing.Size(0, (Get-Px 260))
    [void]$wordList.Columns.Add('Word or phrase (ticked: in use)', (Get-Px 250))
    [void]$wordList.Columns.Add('Said as', (Get-Px 270))
    [void]$wordList.Columns.Add('Matches', (Get-Px 260))
    Add-UiRow $page $wordList
    $wordLine = New-UiLine @('auto', 'fill', 'auto', 'fill')
    $wordFromLabel = New-UiLabel 'Word or phrase'
    $wordFromLabel.Dock = 'None'; $wordFromLabel.Anchor = 'Left'
    $wordFrom = New-Object System.Windows.Forms.TextBox
    $wordFrom.Dock = 'Fill'; $wordFrom.MaxLength = 200
    $wordToLabel = New-UiLabel 'Say it as'
    $wordToLabel.Dock = 'None'; $wordToLabel.Anchor = 'Left'
    $wordTo = New-Object System.Windows.Forms.TextBox
    $wordTo.Dock = 'Fill'; $wordTo.MaxLength = 200
    $wordLine.Controls.Add($wordFromLabel, 0, 0); $wordLine.Controls.Add($wordFrom, 1, 0)
    $wordLine.Controls.Add($wordToLabel, 2, 0); $wordLine.Controls.Add($wordTo, 3, 0)
    Add-UiRow $page $wordLine
    $wordTools = New-UiFlow
    $wordWhole = New-Object System.Windows.Forms.CheckBox
    $wordWhole.Text = 'Whole words only'; $wordWhole.AutoSize = $true; $wordWhole.Checked = $true; $wordWhole.Anchor = 'Left'
    $wordCase = New-Object System.Windows.Forms.CheckBox
    $wordCase.Text = 'Match capitals'; $wordCase.AutoSize = $true; $wordCase.Anchor = 'Left'
    $wordSave = New-UiButton 'Add rule'
    $wordNew = New-UiButton 'New rule'
    $wordRemove = New-UiButton 'Remove'
    $wordHear = New-UiButton 'Hear it'
    $wordStop = New-UiButton 'Stop'
    $wordRemove.Enabled = $false
    foreach ($c in $wordWhole, $wordCase, $wordSave, $wordNew, $wordRemove, $wordHear, $wordStop) { $wordTools.Controls.Add($c) }
    Add-UiRow $page $wordTools
    Add-UiRow $page (New-UiLabel ("To add a rule: type the word or phrase and how to say it (spell it as it sounds: Niamh as Neev), then Add rule. " +
        "To change one: select it, edit, then Save rule. Untick a rule to switch it off without removing it. Hear it says the " +
        "`"Say it as`" text with the voice selected on the Voice manager, to try a spelling before saving.") -Grey)
    Add-UiRow $page (New-UiLabel ("Rules apply from the next thing a program says. Whole words only: `"cat`" changes `"the cat's`" but not " +
        "`"catalogue`". Match capitals: `"US`" but not `"us`". Where two rules fit, the longer one is used, and a rule never changes " +
        "another rule's result. Programs still show and highlight the original words.") -Grey)

    # ---- Add or remove voices (the list takes the room there is; the tab scrolls below a minimum)
    $addGrid = New-Object System.Windows.Forms.TableLayoutPanel
    $addGrid.ColumnCount = 1
    [void]$addGrid.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 100)))
    $addGrid.Padding = New-Object System.Windows.Forms.Padding (Get-Px 6)
    $addGrid.Location = New-Object System.Drawing.Point(0, 0)
    $tabAdd.Controls.Add($addGrid)
    Add-UiRow $addGrid (New-UiLabel ("Tick the voices to install; untick one to remove it. Select a voice and press Play sample to hear it. " +
        "Then press Apply changes: the window shows the progress below.") )
    $list = New-Object System.Windows.Forms.ListView
    $list.View = 'Details'; $list.CheckBoxes = $true; $list.FullRowSelect = $true; $list.HideSelection = $false
    $list.MultiSelect = $false; $list.Dock = 'Fill'
    [void]$list.Columns.Add('Voice', (Get-Px 220))
    [void]$list.Columns.Add('Accent', (Get-Px 90))
    [void]$list.Columns.Add('Quality', (Get-Px 70))
    [void]$list.Columns.Add('Speakers', (Get-Px 90))
    [void]$list.Columns.Add('Download', (Get-Px 80))
    [void]$list.Columns.Add('Catalogue key', (Get-Px 220))
    $chosenSpeakers = @{}
    foreach ($key in $speakersOf.Keys) { if (@($speakersOf[$key]).Count) { $chosenSpeakers[$key] = @($speakersOf[$key]) -join ', ' } }
    foreach ($v in $Catalogue) {
        $item = New-Object System.Windows.Forms.ListViewItem($v.Display)
        [void]$item.SubItems.Add($v.Accent)
        [void]$item.SubItems.Add($v.Quality)
        [void]$item.SubItems.Add($(if ($v.Speakers.Count -gt 1) { "$($v.Speakers.Count) speakers" } else { '1' }))
        [void]$item.SubItems.Add("$($v.SizeMb) MB")
        [void]$item.SubItems.Add($v.Key)
        $item.Tag = $v
        $item.Checked = $speakersOf.Contains($v.Key)
        if ($item.Checked) { $item.Font = $bold }
        [void]$list.Items.Add($item)
    }
    Add-UiRow $addGrid $list -Fill
    $playLine = New-UiLine @('auto', 'fill')
    $play = New-UiButton 'Play sample'
    $speakerLabel = New-UiLabel 'Select a voice with several speakers to choose which of them to install (each becomes a voice).'
    $playLine.Controls.Add($play, 0, 0); $playLine.Controls.Add($speakerLabel, 1, 0)
    Add-UiRow $addGrid $playLine
    $speakerList = New-Object System.Windows.Forms.CheckedListBox
    $speakerList.MultiColumn = $true; $speakerList.ColumnWidth = Get-Px 90; $speakerList.CheckOnClick = $true
    $speakerList.IntegralHeight = $false; $speakerList.Enabled = $false
    $speakerList.Dock = 'Fill'; $speakerList.Height = Get-Px 110
    $speakerList.MinimumSize = New-Object System.Drawing.Size(0, (Get-Px 110))
    Add-UiRow $addGrid $speakerList
    $sizeLabel = New-UiLabel ''
    Add-UiRow $addGrid $sizeLabel
    $warnLabel = New-UiLabel ''
    $warnLabel.ForeColor = $red; $warnLabel.Tag = 'warn'
    Add-UiRow $addGrid $warnLabel
    $usBox = New-Object System.Windows.Forms.CheckBox
    $usBox.Text = 'Also list British voices as US English (only if a program does not show them)'
    $usBox.AutoSize = $true; $usBox.Checked = $UsTags
    Add-UiRow $addGrid $usBox
    Add-UiRow $addGrid (New-UiLabel ("Speakers of one voice share its model, so extra speakers take no extra space. Two qualities of the same voice " +
        "(medium and high) are separate models: each takes its full size. Some programs (World of Warcraft, for one) take longer to " +
        "start the more voices Windows has: keep the list to the ones you use.") -Grey)

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 350
    $timer.add_Tick({ $script:Ui.SaveTimer.Stop(); Save-UiSettings })
    $globalTimer = New-Object System.Windows.Forms.Timer
    $globalTimer.Interval = 350
    $globalTimer.add_Tick({ $script:Ui.GlobalTimer.Stop(); Save-UiGlobalSettings })

    $script:Ui = @{ Form = $form; Pages = $pages; TabButtons = $tabButtons; TabStrip = $tabStrip; PageHost = $pageHost; PageIndex = 0; ThemeBox = $themeBox; List = $list; SpeakerLabel = $speakerLabel; SpeakerList = $speakerList; UsBox = $usBox
        Speakers = $chosenSpeakers; Current = $null; Player = $null; Filling = $false; InstalledKeys = @($speakersOf.Keys)
        InstalledList = $installedList; SoundLabel = $soundLabel; Sliders = $sliders; PreviewBox = $previewBox
        PreviewButton = $previewButton; ResetButton = $resetButton; CurrentInstalled = $null; SaveTimer = $timer
        PreviewProcess = $null; SizeLabel = $sizeLabel; WarnLabel = $warnLabel; Catalogue = $Catalogue; ByKey = $ByKey
        DefaultLabel = $defaultLabel; DefaultButton = $defaultButton; Progress = $progress; Status = $status; Apply = $apply
        Close = $close; Busy = $false; UsTags = $UsTags; Installed = @($Installed); Bold = $bold; Plain = $form.Font; Changed = $false
        OwnBox = $ownBox; GlobalBars = $globalBars; GlobalTimer = $globalTimer; SpeedBar = $speedBar; SpeedValue = $speedValue
        RateWarn = $rateWarn; RateReset = $rateReset; PreviewVoiceBox = $previewVoiceBox
        GlobalPreview = $globalPreview; GapGrid = $gapGrid; AddGrid = $addGrid; AddTab = $tabAdd
        WordList = $wordList; WordFrom = $wordFrom; WordTo = $wordTo; WordWhole = $wordWhole; WordCase = $wordCase
        WordSave = $wordSave; WordRemove = $wordRemove
        NickBox = $nickBox; NickButton = $nickButton; NameClick = $false }
    $list.add_SelectedIndexChanged({ Update-UiSelection })
    $list.add_ItemChecked({ if (-not $script:Ui.Filling) { Update-UiSummary } })
    # A click on a speaker's name only selects it (to play its sample); its box is what ticks it.
    $speakerList.add_MouseDown({
        param($sender, $e)
        $index = $sender.IndexFromPoint($e.Location)
        $script:Ui.NameClick = ($index -ge 0) -and (($e.X - $sender.GetItemRectangle($index).Left) -gt (Get-Px 18))
    })
    $speakerList.add_MouseUp({ $script:Ui.NameClick = $false })
    $speakerList.add_ItemCheck({
        param($sender, $e)
        if ($script:Ui.NameClick) { $e.NewValue = $e.CurrentValue; return }
        if (-not $script:Ui.Filling -and $e.NewValue -eq [System.Windows.Forms.CheckState]::Checked -and
            $script:Ui.List.SelectedItems.Count -gt 0) { $script:Ui.List.SelectedItems[0].Checked = $true }
    })
    $play.add_Click({ Invoke-UiSample })
    # Selecting a speaker plays its sample (Play sample plays it again, or a voice with one speaker).
    $speakerList.add_SelectedIndexChanged({ if (-not $script:Ui.Filling -and $script:Ui.SpeakerList.SelectedIndex -ge 0) { Invoke-UiSample } })
    $installedList.add_SelectedIndexChanged({ Update-UiInstalled })
    $previewButton.add_Click({ Invoke-UiPreview })
    $globalPreview.add_Click({ Invoke-UiGlobalPreview })
    $stopButton.add_Click({ Stop-UiPreview })
    $globalStop.add_Click({ Stop-UiPreview })
    $nickButton.add_Click({ Set-UiNickname })
    $tabAdd.add_Resize({ Update-UiAddLayout })
    $tabAdd.add_ClientSizeChanged({ Update-UiAddLayout })
    $defaultButton.add_Click({ Set-UiDefaultVoice })
    $sapiButton.add_Click({ Start-Process "$env:SystemRoot\System32\Speech\SpeechUX\sapi.cpl" })
    $wordList.add_SelectedIndexChanged({ Update-UiWordFields })
    $wordList.add_ItemChecked({ if (-not $script:Ui.Filling) { Save-UiWords } })
    $wordSave.add_Click({ Save-UiWord })
    $wordNew.add_Click({ Clear-UiWordFields })
    $wordRemove.add_Click({ Remove-UiWord })
    $wordHear.add_Click({ Invoke-UiWordPreview })
    $wordStop.add_Click({ Stop-UiPreview })
    Update-UiWordList
    $apply.add_Click({ Invoke-UiApply })
    $resetButton.add_Click({ Reset-UiSettings })
    $ownBox.add_CheckedChanged({ Update-UiOwnPauses })
    $pausesReset.add_Click({ Reset-UiGlobalPauses })
    $rateReset.add_Click({ Set-WindowsRate 0; Update-UiWindowsRate })
    # Windows' speed can be changed in its Speech settings while the window is open: checked on return.
    $form.add_Activated({ Update-UiWindowsRate })
    $form.add_FormClosing({
        param($sender, $e)
        if ($script:Ui.Busy) { $e.Cancel = $true; return }   # not while changes are being applied
        Save-UiSpeakers
        if ($script:Ui.SaveTimer.Enabled) { $script:Ui.SaveTimer.Stop(); Save-UiSettings }
        if ($script:Ui.GlobalTimer.Enabled) { $script:Ui.GlobalTimer.Stop(); Save-UiGlobalSettings }
        if ($script:Ui.Player) { $script:Ui.Player.Stop() }
        Stop-UiPreview
    })
    Set-UiGlobalBars (Get-Pauses $SettingsFile) (Get-GlobalSpeed $SettingsFile)
    Update-UiWindowsRate
    Update-UiAddLayout
    Update-UiInstalledList
    Update-UiSummary
    # Dark or light: as chosen, or as Windows is (looked at again whenever the window comes to the front).
    $script:UiDark = $null
    foreach ($view in $installedList, $list, $wordList) { Enable-UiDarkHeader $view }
    Set-UiTheme
    $form.add_Activated({ Set-UiTheme })
    Show-UiPage $(if ($installedList.Items.Count -eq 0) { 3 } else { 0 })
    return $script:Ui
}

function Update-UiSliderLabels {
    $s = $script:Ui.Sliders
    $volume = $s.Volume.Bar.Value
    $s.Volume.Value.Text = if ($volume -eq 0) { 'off (as Piper made it)' } else { "$volume%" }
    $boost = $s.Boost.Bar.Value / 10.0
    $s.Boost.Value.Text = if ($boost -le 1.0) { 'none' } else { $boost.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) + 'x' }
    $s.Speed.Value.Text = "$($s.Speed.Bar.Value)%"
    $speed = $script:Ui.SpeedBar.Value / 100.0
    $script:Ui.SpeedValue.Text = "$($script:Ui.SpeedBar.Value)%"
    foreach ($k in 'Sentence gap', 'Comma gap', 'Paragraph gap') {
        $ms = Get-RoundedMs $s[$k].Bar.Value
        $s[$k].Value.Text = Format-Gap $ms $k
        $g = $script:Ui.GlobalBars[$k]
        $gms = Get-RoundedMs $g.Bar.Value
        $g.Value.Text = Format-Gap $gms $k
        $g.Example.Text = if ($gms -gt 0) { "at $($script:Ui.SpeedBar.Value)%: $([math]::Round($gms / $speed)) ms at rate 0, " +
            "$([math]::Round($gms / $speed / [math]::Pow(3, 0.2))) ms at rate +2, $([math]::Round($gms / $speed * [math]::Pow(3, 0.2))) ms at rate -2" } else { '' }
    }
}

function Get-RoundedMs([double]$Value) { return [int]([math]::Round($Value / 10) * 10) }
function Format-Gap([int]$Ms, [string]$Kind) {
    if ($Ms -le 0) { return $(if ($Kind -eq 'Comma gap') { 'none (as said)' } else { 'none added' }) }
    return "+$Ms ms"
}

# The Speed and pauses tab's sliders, from the settings file's values.
function Set-UiGlobalBars($Pauses, [double]$Speed) {
    $ui = $script:Ui
    $ui.Filling = $true
    try {
        $ui.SpeedBar.Value = [int][math]::Max(25, [math]::Min(200, $Speed))
        $ui.GlobalBars['Sentence gap'].Bar.Value = [int][math]::Min($PauseLimits.Sentence, $Pauses.Sentence)
        $ui.GlobalBars['Comma gap'].Bar.Value = [int][math]::Min($PauseLimits.Comma, $Pauses.Comma)
        $ui.GlobalBars['Paragraph gap'].Bar.Value = [int][math]::Min($PauseLimits.Paragraph, $Pauses.Paragraph)
        Update-UiSliderLabels
    } finally {
        $ui.Filling = $false
    }
}

function Save-UiGlobalSettings {
    $g = $script:Ui.GlobalBars
    try {
        Set-GlobalSpeed $SettingsFile $script:Ui.SpeedBar.Value
        Set-GlobalPauses $SettingsFile (Get-RoundedMs $g['Sentence gap'].Bar.Value) (Get-RoundedMs $g['Comma gap'].Bar.Value) (Get-RoundedMs $g['Paragraph gap'].Bar.Value)
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show("Could not save the speed and pauses: $($_.Exception.Message)", $AppName)
    }
}

function Reset-UiGlobalPauses {
    $g = $script:Ui.GlobalBars
    $script:Ui.SpeedBar.Value = $SpeedDefault
    $g['Sentence gap'].Bar.Value = $PauseDefaults.Sentence
    $g['Comma gap'].Bar.Value = $PauseDefaults.Comma
    $g['Paragraph gap'].Bar.Value = $PauseDefaults.Paragraph
}

# Windows' own default speech speed (its Speech settings, sapi.cpl: -10 to 10, per user). Programs
# that leave the speed to Windows add it on top of the speed set here.
function Get-WindowsRate {
    $p = Get-ItemProperty 'HKCU:\Software\Microsoft\Speech\Voices' -Name DefaultTTSRate -ErrorAction SilentlyContinue
    if ($null -eq $p) { return 0 }
    return [int]$p.DefaultTTSRate
}
function Set-WindowsRate([int]$Rate) {
    $key = 'HKCU:\Software\Microsoft\Speech\Voices'
    if (-not (Test-Path $key)) { New-Item $key -Force | Out-Null }
    Set-ItemProperty $key -Name DefaultTTSRate -Value $Rate -Type DWord
}

function Update-UiWindowsRate {
    $ui = $script:Ui
    $rate = Get-WindowsRate
    if ($rate -eq 0) {
        $ui.RateWarn.Text = ''; $ui.RateReset.Visible = $false
    } else {
        $sign = if ($rate -gt 0) { '+' } else { '' }
        $ui.RateWarn.Text = ("Windows' speech speed (its Speech settings) is $sign$rate. Programs that leave the speed to Windows " +
            "add it on top of this one, so these voices are " + $(if ($rate -gt 0) { 'faster' } else { 'slower' }) + " still.")
        $ui.RateReset.Visible = $true
    }
}

# The selected voice's pauses: its own (ticked: its three sliders show), else those for every voice.
function Update-UiOwnPauses {
    $ui = $script:Ui
    $own = $ui.OwnBox.Checked
    $s = $ui.Sliders
    foreach ($k in 'Sentence gap', 'Comma gap', 'Paragraph gap') { $s[$k].Bar.Enabled = ($ui.CurrentInstalled -ne $null) -and $own }
    $ui.GapGrid.Visible = $own
    if (-not $ui.Filling -and $ui.CurrentInstalled) { $ui.SaveTimer.Stop(); $ui.SaveTimer.Start() }
}

# The Voice manager's list, from what is installed now (one row per model), the default voice marked.
function Update-UiInstalledList {
    $ui = $script:Ui
    $speakersOf = [ordered]@{}
    foreach ($id in $ui.Installed) {
        $idParts = Split-VoiceId $id
        if (-not $speakersOf.Contains($idParts[0])) { $speakersOf[$idParts[0]] = @() }
        if ($null -ne $idParts[1]) { $speakersOf[$idParts[0]] += $idParts[1] }
    }
    $ui.InstalledKeys = @($speakersOf.Keys)
    $default = Get-DefaultVoiceToken
    $defaultName = ''
    $allVoices = @()
    $ui.InstalledList.BeginUpdate()
    $ui.InstalledList.Items.Clear()
    foreach ($key in $speakersOf.Keys) {
        $entry = if ($ui.ByKey.ContainsKey($key)) { $ui.ByKey[$key] } else { $null }
        $title = if ($entry) { "$($entry.Display) ($($entry.Accent))" } else { $key }
        $names = @($speakersOf[$key])
        $first = if ($names.Count) { $names[0] } else { $null }
        $voiceName = if ($entry) { Get-DisplayName $entry $first } else { $key }
        $tokens = @(if ($names.Count) { $names | ForEach-Object { Get-TokenName "$key#$_" } } else { Get-TokenName $key })
        $isDefault = @($tokens | Where-Object { $_ -eq $default }).Count -gt 0
        if ($isDefault) {
            $which = @($names | Where-Object { (Get-TokenName "$key#$_") -eq $default })
            $defaultName = if ($entry) { Get-DisplayName $entry $(if ($which.Count) { $which[0] } else { $null }) } else { $key }
        }
        $dir = Join-Path $VoiceRoot $key
        # A row for each installed speaker (one row for a voice with one); the voice named on its first.
        $rows = @(if ($names.Count) { $names } else { '' })
        for ($r = 0; $r -lt $rows.Count; $r++) {
            $speaker = $rows[$r]
            $id = if ($speaker) { "$key#$speaker" } else { $key }
            $nick = Get-Nickname $id
            $item = New-Object System.Windows.Forms.ListViewItem($(if ($r -eq 0) { $title } else { '' }))
            [void]$item.SubItems.Add($(if ($speaker) { $speaker } else { 'n/a' }))
            [void]$item.SubItems.Add($(if ($nick) { "($nick)" } else { '' }))
            [void]$item.SubItems.Add($(if ($r -eq 0) { "$(Get-FolderMb $dir) MB" } else { '' }))
            [void]$item.SubItems.Add($(if ((Get-TokenName $id) -eq $default) { 'yes' } else { '' }))
            [void]$item.SubItems.Add($(if ($r -eq 0) { $key } else { '' }))
            $item.Tag = @{ Key = $key; Dir = $dir; Title = $title; Shared = ($names.Count -gt 1); Speaker = $speaker; Entry = $entry }
            [void]$ui.InstalledList.Items.Add($item)
            $allVoices += $(if ($entry) { Get-DisplayName $entry $speaker } else { $key })
        }
    }
    $ui.InstalledList.EndUpdate()
    # The Speed and pauses tab's Preview: any installed voice (Windows' default first, if it is one).
    $ui.PreviewVoiceBox.Items.Clear()
    foreach ($n in $allVoices) { [void]$ui.PreviewVoiceBox.Items.Add($n) }
    if ($allVoices.Count) { $ui.PreviewVoiceBox.SelectedIndex = [math]::Max(0, [array]::IndexOf($allVoices, $defaultName)) }
    $ui.GlobalPreview.Enabled = $allVoices.Count -gt 0
    $ui.DefaultLabel.Text = if ($defaultName) { "Windows default voice: $defaultName" }
        elseif ($default) { "Windows default voice: $default (not one of these)" } else { 'Windows default voice: not set' }
    if ($ui.InstalledList.Items.Count -gt 0) { $ui.InstalledList.Items[0].Selected = $true; Update-UiInstalled }
    else {
        $ui.SoundLabel.Text = 'No voices installed yet: add some on the Add or remove voices tab.'
        Update-UiInstalled
    }
}

# Makes the selected voice SAPI's default: what programs that don't choose a voice speak with (the
# setting in sapi.cpl, Windows' Speech control panel).
function Set-UiDefaultVoice {
    $voice = Get-UiCurrentVoice
    if (-not $voice) { return }
    Set-DefaultVoiceToken $voice.Token
    $selected = $script:Ui.InstalledList.SelectedIndices
    $index = if ($selected.Count) { $selected[0] } else { 0 }
    Update-UiInstalledList
    if ($script:Ui.InstalledList.Items.Count -gt $index) { $script:Ui.InstalledList.Items[$index].Selected = $true; Update-UiInstalled }
}

# Applies the Add or remove tab's ticks in the window: downloads, removals, registration and tests, with
# progress below; then the window shows the new state.
function Invoke-UiApply {
    $ui = $script:Ui
    Save-UiSpeakers
    $wanted = @(Get-PickedIds $ui)
    $usTags = [bool]$ui.UsBox.Checked
    $same = ((@($wanted | Sort-Object) -join '|') -eq (@($ui.Installed | Sort-Object) -join '|')) -and ($usTags -eq $ui.UsTags)
    if ($same) { $ui.Status.Text = 'Nothing to change.'; return }
    $ui.Busy = $true
    $ui.PageHost.Enabled = $false; $ui.TabStrip.Enabled = $false; $ui.Apply.Enabled = $false; $ui.Close.Enabled = $false
    $ui.Progress.Value = 0; $ui.Progress.Visible = $true
    $ui.Form.Cursor = 'WaitCursor'
    $ok = $false
    try {
        $ok = Set-InstalledVoices $wanted $usTags $ui.ByKey
        $ui.Installed = @(Get-InstalledIds)
        $ui.UsTags = $usTags
        $ui.Changed = $true
    } catch {
        $ui.Status.Text = "Stopped: $($_.Exception.Message)"
        [void][System.Windows.Forms.MessageBox]::Show("Stopped: $($_.Exception.Message)", $AppName)
        $ui.Installed = @(Get-InstalledIds)
    } finally {
        $ui.Busy = $false
        $ui.Progress.Visible = $false
        $ui.PageHost.Enabled = $true; $ui.TabStrip.Enabled = $true; $ui.Apply.Enabled = $true; $ui.Close.Enabled = $true
        $ui.Form.Cursor = 'Default'
    }
    # The new state: ticks and bold for what is installed, the manager's list.
    $ui.Filling = $true
    foreach ($item in $ui.List.Items) {
        $isIn = @($ui.Installed | Where-Object { (Split-VoiceId $_)[0] -eq $item.Tag.Key }).Count -gt 0
        $item.Checked = $isIn
        $item.Font = if ($isIn) { $ui.Bold } else { $ui.Plain }
    }
    $ui.Filling = $false
    Update-UiInstalledList
    Update-UiSummary
    if ($ok) {
        $ui.Status.Text = 'Done: every voice passed its test. Restart programs that were open to see new voices.'
        if ($ui.InstalledList.Items.Count -gt 0) { Show-UiPage 0 }
    } elseif ($ui.Status.Text -notlike 'Stopped*') {
        $ui.Status.Text = "A voice failed its test. The engine's log says why: $env:LOCALAPPDATA\ModernVoices\engine.log"
    }
}

# The voice (speaker) of the row selected in the Voice manager: its token and the name programs see.
function Get-UiCurrentVoice {
    $ui = $script:Ui
    $tag = $ui.CurrentInstalled
    if (-not $tag) { return $null }
    $name = if ($tag.Speaker) { [string]$tag.Speaker } else { $null }
    $id = if ($name) { "$($tag.Key)#$name" } else { $tag.Key }
    $voice = if ($tag.Entry) { Get-DisplayName $tag.Entry $name } else { $tag.Key }
    return @{ Token = (Get-TokenName $id); Voice = $voice; Id = $id; Speaker = $name }
}

# The nickname box: the selected row's nickname.
function Update-UiNickBox {
    $ui = $script:Ui
    $voice = Get-UiCurrentVoice
    $on = ($null -ne $voice) -and ($null -ne $ui.CurrentInstalled.Entry)
    $ui.NickBox.Enabled = $on; $ui.NickButton.Enabled = $on
    $ui.NickBox.Text = if ($on) { Get-Nickname $voice.Id } else { '' }
}

# Gives the selected row's voice the nickname in the box (empty: none), renaming the voice in Windows.
function Set-UiNickname {
    $ui = $script:Ui
    $voice = Get-UiCurrentVoice
    $tag = $ui.CurrentInstalled
    if (-not $voice -or -not $tag.Entry) { return }
    $nick = ($ui.NickBox.Text -replace '[()\r\n]', ' ' -replace '\s+', ' ').Trim()
    $wanted = Format-DisplayName $tag.Entry $voice.Speaker $nick
    if ($wanted -ne $voice.Voice -and @(Get-OurVoiceNames) -contains $wanted) {
        $ui.Status.Text = "Another voice is already named $wanted."
        return
    }
    try {
        $display = Set-VoiceNickname $tag.Entry $voice.Speaker $nick
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show("Could not rename the voice: $($_.Exception.Message)", $AppName)
        return
    }
    $selected = $ui.InstalledList.SelectedIndices
    $index = if ($selected.Count) { $selected[0] } else { 0 }
    Update-UiInstalledList
    if ($ui.InstalledList.Items.Count -gt $index) { $ui.InstalledList.Items[$index].Selected = $true; Update-UiInstalled }
    $ui.Status.Text = "Now named $display. Restart programs that were open to see the new name."
}

function Update-UiInstalled {
    $ui = $script:Ui
    if ($ui.SaveTimer.Enabled) { $ui.SaveTimer.Stop(); Save-UiSettings }
    $ui.CurrentInstalled = $null
    $ui.NickBox.Text = ''; $ui.NickBox.Enabled = $false; $ui.NickButton.Enabled = $false
    $on = $ui.InstalledList.SelectedItems.Count -gt 0
    foreach ($k in 'Volume', 'Boost', 'Speed') { $ui.Sliders[$k].Bar.Enabled = $on }
    $ui.OwnBox.Enabled = $on
    foreach ($k in 'Sentence gap', 'Comma gap', 'Paragraph gap') { $ui.Sliders[$k].Bar.Enabled = $false }
    $ui.PreviewButton.Enabled = $on; $ui.ResetButton.Enabled = $on; $ui.DefaultButton.Enabled = $on
    if (-not $on) { return }
    $tag = $ui.InstalledList.SelectedItems[0].Tag
    $settings = Get-VoiceSettings $tag.Dir
    $peak = $DefaultPeak; $boost = $DefaultBoost; $lengthScale = 1.0
    if ($settings) {
        if ($null -ne $settings.Peak) { $peak = $settings.Peak }
        if ($null -ne $settings.Boost) { $boost = $settings.Boost }
        if ($null -ne $settings.LengthScale) { $lengthScale = $settings.LengthScale }
    }
    $voicePauses = Get-Pauses (Join-Path $tag.Dir 'voice.json')
    $shown = if ($voicePauses.Own -or $voicePauses.Stored) { $voicePauses } else { Get-Pauses $SettingsFile }
    $ui.Filling = $true
    try {
        $ui.OwnBox.Checked = $voicePauses.Own
        $ui.GapGrid.Visible = $voicePauses.Own
        $ui.Sliders['Sentence gap'].Bar.Value = [int][math]::Min($PauseLimits.Sentence, $shown.Sentence)
        $ui.Sliders['Comma gap'].Bar.Value = [int][math]::Min($PauseLimits.Comma, $shown.Comma)
        $ui.Sliders['Paragraph gap'].Bar.Value = [int][math]::Min($PauseLimits.Paragraph, $shown.Paragraph)
        $ui.Sliders.Volume.Bar.Value = [int][math]::Max(0, [math]::Min(100, [math]::Round($peak * 100)))
        $ui.Sliders.Boost.Bar.Value = [int][math]::Max(10, [math]::Min(40, [math]::Round($boost * 10)))
        $ui.Sliders.Speed.Bar.Value = [int][math]::Max(25, [math]::Min(200, [math]::Round(100 / $lengthScale)))
        Update-UiSliderLabels
    } finally {
        $ui.Filling = $false
    }
    $ui.CurrentInstalled = $tag
    foreach ($k in 'Sentence gap', 'Comma gap', 'Paragraph gap') { $ui.Sliders[$k].Bar.Enabled = $ui.OwnBox.Checked }
    Update-UiNickBox
    $ui.SoundLabel.Text = "How $($tag.Title) sounds" + $(if ($tag.Shared) { ' (all its speakers)' } else { '' })
}

function Save-UiSettings {
    $ui = $script:Ui
    $tag = $ui.CurrentInstalled
    if (-not $tag -or -not (Test-Path (Join-Path $tag.Dir 'voice.json'))) { return }
    $peak = $ui.Sliders.Volume.Bar.Value / 100.0
    $boost = $ui.Sliders.Boost.Bar.Value / 10.0
    $lengthScale = [math]::Round(100.0 / $ui.Sliders.Speed.Bar.Value, 3)
    $s = $ui.Sliders
    try {
        Set-VoiceSettings $tag.Dir $peak $boost $lengthScale
        Set-VoicePauses $tag.Dir $ui.OwnBox.Checked (Get-RoundedMs $s['Sentence gap'].Bar.Value) `
            (Get-RoundedMs $s['Comma gap'].Bar.Value) (Get-RoundedMs $s['Paragraph gap'].Bar.Value)
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show("Could not save the settings: $($_.Exception.Message)", $AppName)
    }
}

function Reset-UiSettings {
    $ui = $script:Ui
    if (-not $ui.CurrentInstalled) { return }
    $ui.Sliders.Volume.Bar.Value = [int]($DefaultPeak * 100)
    $ui.Sliders.Boost.Bar.Value = [int]($DefaultBoost * 10)
    $ui.Sliders.Speed.Bar.Value = 100
}

function Stop-UiPreview {
    $p = $script:Ui.PreviewProcess
    if ($p -and -not $p.HasExited) { try { $p.Kill() } catch { } }
    $script:Ui.PreviewProcess = $null
}

# Speaks the line with a voice in a separate PowerShell, as any program would (the engine is never
# loaded into this one, so nothing here holds its files). Settings not yet saved are saved first.
function Start-UiPreview([string]$VoiceName, [string]$Say) {
    $ui = $script:Ui
    if ($ui.SaveTimer.Enabled) { $ui.SaveTimer.Stop(); Save-UiSettings }
    if ($ui.GlobalTimer.Enabled) { $ui.GlobalTimer.Stop(); Save-UiGlobalSettings }
    Stop-UiPreview
    $text = if ($Say) { $Say } elseif ($ui.PreviewBox.Text.Trim()) { $ui.PreviewBox.Text } else { $PreviewText }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($text))
    $ui.PreviewProcess = Start-Process $PS64 -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$(Join-Path $Here 'Speak.ps1')`"", '-Voice', "`"$VoiceName`"", '-TextBase64', $encoded)
}

# ---- the Pronunciation tab. Each row of its list is a rule (its Tag); changes are saved at once.
function Format-UiWordMatch($Rule) {
    $what = if ($Rule.Whole) { 'whole words' } else { 'parts of words too' }
    if ($Rule.Case) { $what += ', with these capitals' }
    return $what
}
function Add-UiWordRow($Rule) {
    $item = New-Object System.Windows.Forms.ListViewItem([string]$Rule.From)
    [void]$item.SubItems.Add([string]$Rule.To)
    [void]$item.SubItems.Add((Format-UiWordMatch $Rule))
    $item.Tag = $Rule
    $item.Checked = [bool]$Rule.On
    [void]$script:Ui.WordList.Items.Add($item)
    return $item
}
function Update-UiWordList {
    $ui = $script:Ui
    $ui.Filling = $true
    try {
        $ui.WordList.BeginUpdate()
        $ui.WordList.Items.Clear()
        foreach ($rule in (Get-Dictionary $DictionaryFile)) { [void](Add-UiWordRow $rule) }
        $ui.WordList.EndUpdate()
    } finally { $ui.Filling = $false }
    Update-UiWordFields
}
# Writes the list to the dictionary file (the engine picks it up at the next thing said).
function Save-UiWords {
    $ui = $script:Ui
    $rules = @()
    foreach ($item in $ui.WordList.Items) { $item.Tag.On = $item.Checked; $rules += $item.Tag }
    try { Set-Dictionary $DictionaryFile $rules }
    catch { $ui.Status.Text = "Could not save the pronunciation rules: $($_.Exception.Message)" }
}
# The boxes under the list: the selected rule's, to change it; empty for a new one.
function Update-UiWordFields {
    $ui = $script:Ui
    $selected = $ui.WordList.SelectedItems
    if ($selected.Count) {
        $rule = $selected[0].Tag
        $ui.WordFrom.Text = $rule.From; $ui.WordTo.Text = $rule.To
        $ui.WordWhole.Checked = $rule.Whole; $ui.WordCase.Checked = $rule.Case
    }
    $ui.WordSave.Text = if ($selected.Count) { 'Save rule' } else { 'Add rule' }
    $ui.WordRemove.Enabled = $selected.Count -gt 0
}
function Clear-UiWordFields {
    $ui = $script:Ui
    foreach ($item in @($ui.WordList.SelectedItems)) { $item.Selected = $false }
    $ui.WordFrom.Text = ''; $ui.WordTo.Text = ''; $ui.WordWhole.Checked = $true; $ui.WordCase.Checked = $false
    Update-UiWordFields
    [void]$ui.WordFrom.Focus()
}
# Adds the rule in the boxes, or changes the selected one (or the one already there for that word).
function Save-UiWord {
    $ui = $script:Ui
    $from = ($ui.WordFrom.Text -replace '\s+', ' ').Trim()
    $to = ($ui.WordTo.Text -replace '\s+', ' ').Trim()
    if (-not $from) { $ui.Status.Text = 'Type the word or phrase first.'; return }
    $rule = [pscustomobject]@{ On = $true; Whole = $ui.WordWhole.Checked; Case = $ui.WordCase.Checked; From = $from; To = $to }
    $target = if ($ui.WordList.SelectedItems.Count) { $ui.WordList.SelectedItems[0] } else { $null }
    if (-not $target) {
        foreach ($item in $ui.WordList.Items) {
            $same = if ($rule.Case -or $item.Tag.Case) { $item.Tag.From -ceq $from } else { $item.Tag.From -eq $from }
            if ($same -and $item.Tag.Case -eq $rule.Case) { $target = $item; break }
        }
    }
    $ui.Filling = $true
    try {
        if ($target) {
            $rule.On = $target.Checked
            $target.Tag = $rule
            $target.Text = $from; $target.SubItems[1].Text = $to; $target.SubItems[2].Text = Format-UiWordMatch $rule
        } else { $target = Add-UiWordRow $rule }
    } finally { $ui.Filling = $false }
    Save-UiWords
    $target.Selected = $true
    $target.EnsureVisible()
    Update-UiWordFields
    $ui.Status.Text = "Saved: `"$from`" is said as `"$to`" from the next thing a program says."
}
function Remove-UiWord {
    $ui = $script:Ui
    if (-not $ui.WordList.SelectedItems.Count) { return }
    $ui.WordList.Items.Remove($ui.WordList.SelectedItems[0])
    Save-UiWords
    Clear-UiWordFields
}
# Says the "Say it as" text (the word itself if that is empty) with the Voice manager's selected voice,
# else the one chosen on Speed and pauses: to try a spelling before saving it.
function Invoke-UiWordPreview {
    $ui = $script:Ui
    $say = if ($ui.WordTo.Text.Trim()) { $ui.WordTo.Text.Trim() } else { $ui.WordFrom.Text.Trim() }
    if (-not $say) { $ui.Status.Text = 'Type something to hear first.'; return }
    $voice = Get-UiCurrentVoice
    $name = if ($voice) { $voice.Voice } else { [string]$ui.PreviewVoiceBox.SelectedItem }
    if (-not $name) { $ui.Status.Text = 'Install a voice first (Add or remove voices).'; return }
    Start-UiPreview $name $say
}

# The Voice manager's Preview: the selected voice (its chosen speaker).
function Invoke-UiPreview {
    $voice = Get-UiCurrentVoice
    if ($voice) { Start-UiPreview $voice.Voice }
}

# The Speed and pauses tab's Preview: the voice chosen there.
function Invoke-UiGlobalPreview {
    $name = [string]$script:Ui.PreviewVoiceBox.SelectedItem
    if ($name) { Start-UiPreview $name }
}

function Update-UiSummary {
    $ui = $script:Ui
    $ticked = @($ui.List.Items | Where-Object { $_.Checked } | ForEach-Object { $_.Tag })
    $total = ($ticked | Measure-Object SizeMb -Sum).Sum
    $new = @($ticked | Where-Object { $ui.InstalledKeys -notcontains $_.Key })
    $download = ($new | Measure-Object SizeMb -Sum).Sum
    $gone = @($ui.InstalledKeys | Where-Object { @($ticked | ForEach-Object { $_.Key }) -notcontains $_ })
    $text = "Ticked: $($ticked.Count) voice(s), about $([int]$total) MB on disk."
    if ($new.Count) { $text += " To download: $($new.Count), $([int]$download) MB." }
    if ($gone.Count) { $text += " To remove: $($gone.Count)." }
    if (-not $new.Count -and -not $gone.Count) { $text += ' No changes to apply.' }
    $ui.SizeLabel.Text = $text
    $warnings = @()
    foreach ($group in ($ticked | Group-Object { "$($_.Name)|$($_.Code)" } | Where-Object { $_.Count -gt 1 })) {
        $g = @($group.Group)
        $warnings += "$((Get-PrettyName $g[0].Name)) ($($g[0].Accent)) is ticked in $($g.Count) qualities ($(@($g | ForEach-Object Quality) -join ' and ')): " +
            "each is a separate model, $(@($g | ForEach-Object { "$($_.SizeMb) MB" }) -join ' + ')."
    }
    $ui.WarnLabel.Text = $warnings -join ' '
}

function Save-UiSpeakers {
    $current = $script:Ui.Current
    if ($current -and $current.Speakers.Count -gt 1) {
        $script:Ui.Speakers[$current.Key] = (@($script:Ui.SpeakerList.CheckedItems) | ForEach-Object { [string]$_ }) -join ', '
    }
}

function Update-UiSelection {
    Save-UiSpeakers
    $ui = $script:Ui
    $ui.Current = $null
    $ui.Filling = $true
    $ui.SpeakerList.BeginUpdate()
    try {
        $ui.SpeakerList.Items.Clear()
        $ui.SpeakerList.Enabled = $false
        if ($ui.List.SelectedItems.Count -eq 0) { return }
        $v = $ui.List.SelectedItems[0].Tag
        $ui.Current = $v
        if ($v.Speakers.Count -le 1) {
            $ui.SpeakerLabel.Text = "$($v.Display): one speaker."
            return
        }
        # Listed in name order, numbers by value (p225 before p1000; the voice's own order is kept in its files).
        $names = @($v.Speakers.Keys | Sort-Object { [regex]::Replace([string]$_, '\d+', { param($m) $m.Value.PadLeft(10, '0') }) })
        $firstSpeaker = @($v.Speakers.Keys)[0]
        $ticked = @()
        if ($ui.Speakers.ContainsKey($v.Key)) { $ticked = @(($ui.Speakers[$v.Key] -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
        foreach ($n in $names) { [void]$ui.SpeakerList.Items.Add($n, ($ticked -contains $n)) }
        $ui.SpeakerList.Enabled = $true
        $ui.SpeakerLabel.Text = "Speakers of $($v.Display) ($($names.Count)): click a name to hear it (Play sample plays it again); tick its box to " +
            "install it, each becomes a voice (none ticked: $firstSpeaker)."
    } finally {
        $ui.SpeakerList.EndUpdate()
        $ui.Filling = $false
    }
}

function Invoke-UiSample {
    $ui = $script:Ui
    if ($ui.List.SelectedItems.Count -eq 0) { return }
    Save-UiSpeakers
    $v = $ui.List.SelectedItems[0].Tag
    $speakerNumber = 0
    if ($v.Speakers.Count -gt 1) {
        # The speaker selected in the list, else the first ticked, else the first.
        $name = [string]$ui.SpeakerList.SelectedItem
        if (-not $name -and $ui.SpeakerList.CheckedItems.Count -gt 0) { $name = [string]$ui.SpeakerList.CheckedItems[0] }
        if ($name -and $v.Speakers.Contains($name)) { $speakerNumber = $v.Speakers[$name] }
    }
    $folder = $v.Onnx.Substring(0, $v.Onnx.LastIndexOf('/'))
    $file = Join-Path $env:TEMP ("modernvoices_sample_{0}_{1}.mp3" -f $v.Key, $speakerNumber)
    try {
        $ui.Form.Cursor = 'WaitCursor'
        if (-not (Test-Path $file)) { Get-Download "$Base/$folder/samples/speaker_$speakerNumber.mp3" $file }
        Add-Type -AssemblyName PresentationCore
        if ($ui.Player) { $ui.Player.Stop(); $ui.Player.Close() }
        $ui.Player = New-Object System.Windows.Media.MediaPlayer
        $ui.Player.Open([uri]$file)
        $ui.Player.Play()
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show("Could not play the sample: $($_.Exception.Message)", $AppName)
    } finally {
        $ui.Form.Cursor = 'Default'
    }
}

# The ids the list's ticks stand for: a key per single-speaker voice, key#name per chosen speaker.
function Get-PickedIds($Picker) {
    $ids = @()
    foreach ($item in $Picker.List.Items) {
        if (-not $item.Checked) { continue }
        $v = $item.Tag
        if ($v.Speakers.Count -le 1) { $ids += $v.Key; continue }
        $wanted = if ($Picker.Speakers.ContainsKey($v.Key)) { $Picker.Speakers[$v.Key] } else { @($v.Speakers.Keys)[0] }
        $names = @(($wanted -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $good = @($names | Where-Object { $v.Speakers.Contains($_) })
        if ($good.Count -eq 0) { $good = @(@($v.Speakers.Keys)[0]) }
        foreach ($n in $good) { $ids += "$($v.Key)#$n" }
    }
    $ids | Select-Object -Unique
}

# --- installing -----------------------------------------------------------------------------------

function Test-VoiceFolder([string]$Folder) {
    return (Test-Path (Join-Path $Folder 'model.onnx')) -and (Test-Path (Join-Path $Folder 'tokens.txt')) -and
        (Test-Path (Join-Path $Folder 'voice.json'))
}

function Test-FileFree([string]$Path) {
    if (-not (Test-Path $Path)) { return $true }
    try { $stream = [System.IO.File]::Open($Path, 'Open', 'ReadWrite', 'None'); $stream.Dispose(); return $true }
    catch { return $false }
}

function Install-Engine {
    $source = Join-Path $Root 'engine'
    if (-not (Test-Path (Join-Path $source 'x64\modern_voices.dll'))) { throw "The engine is missing from $source." }
    if ($InPlace) { return }
    foreach ($dll in $Dll64, $Dll32) {
        if (-not (Test-FileFree $dll)) {
            throw "A program is using the voices now (the engine is in use: $dll). Close programs that speak, then run this again."
        }
    }
    New-Item $Engine -ItemType Directory -Force | Out-Null
    foreach ($sub in 'x64', 'x86') {
        $from = Join-Path $source $sub
        $into = Join-Path $Engine $sub
        if (Test-Path $from) { New-Item $into -ItemType Directory -Force | Out-Null; Copy-Item (Join-Path $from '*') $into -Force }
        elseif (Test-Path $into) { Remove-Item $into -Recurse -Force }
    }
    Copy-Item (Join-Path $source 'modern_voices.cer') $Cer -Force
    if (Test-Path $Data) { Remove-Item $Data -Recurse -Force }
    Copy-Item (Join-Path $source 'espeak-ng-data') $Data -Recurse -Force
    foreach ($file in 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'README.txt') {
        $path = Join-Path $Root $file
        if (Test-Path $path) { Copy-Item $path (Join-Path $App $file) -Force }
    }
    $licenses = Join-Path $Root 'licenses'
    if (Test-Path $licenses) { Copy-Item $licenses $App -Recurse -Force }
    $scripts = Join-Path $App 'installer'
    New-Item $scripts -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $Here '*.ps1') $scripts -Force
    # Modern Voices: Reader (a program of its own, beside this one).
    $readerFrom = Join-Path $Root 'reader'
    if (Test-Path $readerFrom) {
        $readerInto = Join-Path $App 'reader'
        New-Item $readerInto -ItemType Directory -Force | Out-Null
        try { Copy-Item (Join-Path $readerFrom '*') $readerInto -Force }
        catch { throw "The Reader is open: close it, then run this again. ($($_.Exception.Message))" }
    }
}

function Install-Voice($Entry) {
    $folder = Join-Path $VoiceRoot $Entry.Key
    $temp = Join-Path $env:TEMP 'ModernVoices'
    New-Item $temp -ItemType Directory -Force | Out-Null
    $onnx = Join-Path $temp ($Entry.Key + '.onnx')
    $config = "$onnx.json"
    foreach ($pair in @(@($Entry.Onnx, $onnx, $Entry.OnnxMd5, $Entry.OnnxBytes), @($Entry.Config, $config, $Entry.ConfigMd5, $Entry.ConfigBytes))) {
        Write-Status "    downloading $($Entry.Display) ($($Entry.Accent)): $(Split-Path $pair[0] -Leaf) ..."
        Get-Download "$Base/$($pair[0])" $pair[1] $pair[3]
        $md5 = (Get-FileHash $pair[1] -Algorithm MD5).Hash.ToLower()
        if ($pair[2] -and $md5 -ne $pair[2].ToLower()) {
            Remove-Item $pair[1] -ErrorAction SilentlyContinue
            throw "$($pair[0]) did not download correctly (its MD5 is $md5, not $($pair[2])). Run this again."
        }
    }
    Write-Status "    preparing $($Entry.Display) ($($Entry.Accent)) ..."
    Convert-PiperVoice -Onnx $onnx -Config $config -OutDir $folder -Name $Entry.Key -Label $Entry.Display `
        -Peak $DefaultPeak -Boost $DefaultBoost
    Remove-Item $onnx, $config -ErrorAction SilentlyContinue
}

function Remove-OurTokens([string]$TokenRoot) {
    if (-not (Test-Path $TokenRoot)) { return }
    foreach ($key in Get-ChildItem $TokenRoot) {
        if ((Get-ItemProperty $key.PSPath).ModernVoice -eq 1) { Remove-Item $key.PSPath -Recurse -Force }
    }
}

function Register-Class([string]$ClassKey, [string]$DllPath) {
    New-Item "$ClassKey\InprocServer32" -Force | Out-Null
    Set-ItemProperty $ClassKey -Name '(default)' -Value $AppName
    Set-ItemProperty "$ClassKey\InprocServer32" -Name '(default)' -Value $DllPath
    Set-ItemProperty "$ClassKey\InprocServer32" -Name 'ThreadingModel' -Value 'Both'
}

function Register-Voice([string]$TokenRoot, [string]$Id, $Entry, [string]$SpeakerName, [int]$SpeakerId, [bool]$UsTags) {
    $token = Join-Path $TokenRoot (Get-TokenName $Id)
    $display = Get-DisplayName $Entry $SpeakerName
    $lcid = if ($UsTags) { '409' } elseif ($Lcids.ContainsKey($Entry.Code)) { $Lcids[$Entry.Code] } else { '409' }
    if (Test-Path $token) { Remove-Item $token -Recurse -Force }
    New-Item "$token\Attributes" -Force | Out-Null
    Set-ItemProperty $token -Name '(default)' -Value $display
    Set-ItemProperty $token -Name 'CLSID' -Value $Clsid
    Set-ItemProperty $token -Name 'ModelDir' -Value (Join-Path $VoiceRoot $Entry.Key)
    Set-ItemProperty $token -Name 'DataDir' -Value $Data
    Set-ItemProperty $token -Name 'SettingsFile' -Value $SettingsFile
    Set-ItemProperty $token -Name 'SpeakerId' -Value $SpeakerId -Type DWord
    Set-ItemProperty $token -Name 'ModernVoice' -Value 1 -Type DWord
    Set-ItemProperty $token -Name 'VoiceId' -Value $Id
    Set-ItemProperty "$token\Attributes" -Name 'Name' -Value $display
    if (-not $SpeakerName -and $Genders.ContainsKey($Entry.Name)) {
        Set-ItemProperty "$token\Attributes" -Name 'Gender' -Value $Genders[$Entry.Name]
    }
    Set-ItemProperty "$token\Attributes" -Name 'Age' -Value 'Adult'
    Set-ItemProperty "$token\Attributes" -Name 'Language' -Value $lcid
    Set-ItemProperty "$token\Attributes" -Name 'Vendor' -Value 'Piper'
    return $display
}

function New-Shortcut([string]$Path, [string]$Arguments, [string]$Description) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $PS64
    $link.Arguments = $Arguments
    $link.WorkingDirectory = $App
    $link.Description = $Description
    $link.IconLocation = "$env:SystemRoot\System32\SpeechUX\sapi.cpl,0"
    $link.Save()
}

function Write-Launcher([string]$Destination, [string]$Script, [string]$Extra, [switch]$LeaveFolder) {
    $run = "`"$PS64`" -NoProfile -ExecutionPolicy Bypass -File `"%~dp0installer\$Script`" $Extra".TrimEnd()
    $text = if ($LeaveFolder) { "@cd /d `"%TEMP%`" & $run & exit /b`r`n" } else { "@echo off`r`n$run`r`n" }
    [System.IO.File]::WriteAllText($Destination, $text, (New-Object System.Text.ASCIIEncoding))
}

function Register-Program {
    Write-Launcher (Join-Path $App 'Modern Voices.cmd') 'Install.ps1' ''
    Write-Launcher (Join-Path $App 'Repair.cmd') 'Install.ps1' '-Repair'
    Write-Launcher (Join-Path $App 'Test voices.cmd') 'Test.ps1' ''
    Write-Launcher (Join-Path $App 'Uninstall.cmd') 'Uninstall.ps1' '' -LeaveFolder
    $sizeKb = [int]((Get-ChildItem $App -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
    New-Item $OurKey -Force | Out-Null
    Set-ItemProperty $OurKey -Name 'DisplayName' -Value $AppName
    Set-ItemProperty $OurKey -Name 'DisplayVersion' -Value $Version
    Set-ItemProperty $OurKey -Name 'Publisher' -Value $AppName
    Set-ItemProperty $OurKey -Name 'InstallLocation' -Value $App
    Set-ItemProperty $OurKey -Name 'DisplayIcon' -Value "$env:SystemRoot\System32\SpeechUX\sapi.cpl,0"
    Set-ItemProperty $OurKey -Name 'UninstallString' -Value "`"$PS64`" -NoProfile -ExecutionPolicy Bypass -File `"$App\installer\Uninstall.ps1`""
    Set-ItemProperty $OurKey -Name 'ModifyPath' -Value "`"$PS64`" -NoProfile -ExecutionPolicy Bypass -File `"$App\installer\Install.ps1`""
    Set-ItemProperty $OurKey -Name 'NoRepair' -Value 1 -Type DWord
    Set-ItemProperty $OurKey -Name 'EstimatedSize' -Value $sizeKb -Type DWord
    New-Item $StartMenu -ItemType Directory -Force | Out-Null
    $common = "-NoProfile -ExecutionPolicy Bypass -File `"$App\installer\"
    New-Shortcut (Join-Path $StartMenu 'Modern Voices.lnk') ($common + 'Install.ps1"') 'Voices installed, how they sound, add or remove voices'
    $readerExe = Join-Path $App 'reader\ModernVoicesReader.exe'
    if (Test-Path $readerExe) {
        $link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $StartMenu 'Modern Voices Reader.lnk'))
        $link.TargetPath = $readerExe
        $link.WorkingDirectory = Split-Path $readerExe -Parent
        $link.Description = 'Reads text aloud with the Modern Voices: pasted or copied text, and a list to read later'
        $link.IconLocation = "$env:SystemRoot\System32\SpeechUX\sapi.cpl,0"
        $link.Save()
    }
    New-Shortcut (Join-Path $StartMenu 'Repair.lnk') ($common + 'Install.ps1" -Repair') 'Register the voices again'
    New-Shortcut (Join-Path $StartMenu 'Test voices.lnk') ($common + 'Test.ps1"') 'Hear each Modern Voice'
    New-Shortcut (Join-Path $StartMenu 'Uninstall.lnk') ($common + 'Uninstall.ps1"') "Uninstall $AppName"
}

# Runs a program with no window and returns what it printed; the manager window keeps responding meanwhile.
function Invoke-Hidden([string]$Exe, [string[]]$Arguments) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
        while (-not $p.HasExited) {
            if ($script:Ui -and $script:Ui.Busy) { [System.Windows.Forms.Application]::DoEvents() }
            Start-Sleep -Milliseconds 100
        }
        return @(Get-Content $out) + @(Get-Content $err)
    } finally {
        Remove-Item $out, $err -ErrorAction SilentlyContinue
    }
}

# Runs Test.ps1 -Quiet in 64-bit (and 32-bit) PowerShell; returns its results.
function Invoke-SelfTest([bool]$With32) {
    $results = @()
    $runs = @(,@($PS64, 64))   # (the comma keeps it a list of pairs)
    if ($With32 -and (Test-Path $PS32)) { $runs += ,@($PS32, 32) }
    foreach ($r in $runs) {
        $lines = Invoke-Hidden $r[0] @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $App 'installer\Test.ps1')`"", '-Quiet')
        foreach ($line in $lines) {
            $text = [string]$line
            if ($text.StartsWith('{')) {
                $d = Read-Json $text
                $one = @{ bits = $r[1]; name = ''; seconds = 0; words = 0; bookmark = ''; error = ''; none = $false }
                foreach ($k in @($one.Keys)) { if ($d.ContainsKey($k)) { $one[$k] = $d[$k] } }
                $results += $one
            }
            elseif ($text.Trim()) { $results += @{ bits = $r[1]; name = '(test)'; error = $text; seconds = 0; words = 0; bookmark = ''; none = $false } }
        }
    }
    return $results
}

# The engine copied in and registered for 64- and 32-bit programs, with its certificate trusted.
function Install-AndRegisterEngine {
    Install-Engine
    Register-Class $Class64 $Dll64
    if (Test-Path $Dll32) { Register-Class $Class32 $Dll32 } elseif (Test-Path $Class32) { Remove-Item $Class32 -Recurse -Force }
    # Programs that only load signed engines (World of Warcraft, for one) need the build's certificate trusted.
    foreach ($store in 'Root', 'TrustedPublisher') {
        & "$env:SystemRoot\System32\certutil.exe" -f -addstore $store $Cer | Out-Null
    }
}

# Makes the installed voices exactly $Wanted: files downloaded or removed, voices registered, tested.
# True when every voice passed its test.
function Set-InstalledVoices([string[]]$Wanted, [bool]$UsTags, $ByKey) {
    foreach ($id in $Wanted) {
        $idParts = Split-VoiceId $id
        if (-not $ByKey.ContainsKey($idParts[0])) { throw "Unknown voice '$id' (not an English medium or high voice in the catalogue)." }
        $entry = $ByKey[$idParts[0]]
        if ($null -ne $idParts[1] -and -not $entry.Speakers.Contains($idParts[1])) { throw "Voice '$($idParts[0])' has no speaker '$($idParts[1])'." }
        if ($null -eq $idParts[1] -and $entry.Speakers.Count -gt 1) { throw "Voice '$id' has several speakers: give one as $id#name." }
    }
    $has32 = Test-Path $Dll32

    Write-Status "`n  voice files"
    New-Item $VoiceRoot -ItemType Directory -Force | Out-Null
    $keys = @($Wanted | ForEach-Object { (Split-VoiceId $_)[0] } | Select-Object -Unique)
    foreach ($folder in Get-ChildItem $VoiceRoot -Directory) {
        if ($keys -notcontains $folder.Name) {
            try {
                Remove-Item $folder.FullName -Recurse -Force
                Write-Status "    removed $($folder.Name)"
            } catch {
                Write-Status "    could not remove $($folder.Name) (a program is using it): it goes next time" 'Yellow'
            }
        }
    }
    foreach ($key in $keys) {
        $folder = Join-Path $VoiceRoot $key
        if (Test-VoiceFolder $folder) { Write-Status "    $key (installed)"; continue }
        Install-Voice $ByKey[$key]
        Write-Status "    $key ready"
    }

    Write-Status "`n  registering"
    Remove-OurTokens $Tokens64
    Remove-OurTokens $Tokens32
    foreach ($id in $Wanted) {
        $idParts = Split-VoiceId $id
        $entry = $ByKey[$idParts[0]]
        $speakerId = if ($null -ne $idParts[1]) { [int]$entry.Speakers[$idParts[1]] } else { 0 }
        $display = Register-Voice $Tokens64 $id $entry $idParts[1] $speakerId $UsTags
        if ($has32) { [void](Register-Voice $Tokens32 $id $entry $idParts[1] $speakerId $UsTags) }
        Write-Status "    $display"
    }
    if (-not (Test-Path $Settings)) { New-Item $Settings -Force | Out-Null }   # (its nicknames stay)
    Set-ItemProperty $Settings -Name 'UsEnglishTags' -Value ([int]$UsTags) -Type DWord
    Register-Program

    $allGood = $true
    if ($Wanted.Count -gt 0) {
        Write-Status "`n  testing each voice through Windows' speech API"
        foreach ($r in (Invoke-SelfTest $has32)) {
            $ok = -not $r['error'] -and [double]$r['seconds'] -gt 0.5 -and [int]$r['words'] -ge 5 -and [string]$r['bookmark'] -eq '7'
            if ($r['none']) { $ok = $false; $r['name'] = '(no voices found)'; $r['error'] = "none listed for $($r['bits'])-bit programs" }
            $line = "    {0,-44} {1}-bit  {2,5:N1} s  {3} words  bookmark {4}" -f $r['name'], $r['bits'], [double]$r['seconds'], $r['words'], $(if ($r['bookmark']) { 'ok' } else { 'missing' })
            if ($r['error']) { $line += "  ERROR: $($r['error'])" }
            Write-Status $line $(if ($ok) { 'Green' } else { 'Red' })
            if (-not $ok) { $allGood = $false }
        }
    }
    return $allGood
}

# Hides or shows this PowerShell's console window.
function Set-ConsoleShown([bool]$Shown) {
    try {
        if (-not ('ModernVoices.Console' -as [type])) {
            Add-Type -Namespace ModernVoices -Name Console -MemberDefinition @'
[DllImport("kernel32.dll")] public static extern System.IntPtr GetConsoleWindow();
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr window, int show);
'@
        }
        $window = [ModernVoices.Console]::GetConsoleWindow()
        if ($window -ne [System.IntPtr]::Zero) { [void][ModernVoices.Console]::ShowWindow($window, $(if ($Shown) { 5 } else { 0 })) }
    } catch { }
}

# --- the steps --------------------------------------------------------------------------------------

if ($FunctionsOnly) { return }

try {
    if ($PickerSelfTest) {
        # The window built (not shown) and driven as clicks would: the build's test of it.
        # An error in one of the window's handlers is reported and ends the test (Windows would otherwise
        # show a box and wait for a click that never comes).
        Add-Type -AssemblyName System.Windows.Forms
        [System.Windows.Forms.Application]::add_ThreadException({
            param($sender, $e)
            [Console]::Out.WriteLine("ERROR in the window: $($e.Exception)")
            [Console]::Out.Flush()
            [Environment]::Exit(3)
        })
        $existing = Get-InstalledFolder
        if ($existing) { Set-AppPaths $existing }
        $bad = @()
        # The first screen: where to install.
        $where = New-LocationForm $DefaultApp
        $first = $where.Path.Text
        $where.Path.Text = 'D:\Tools'
        $note = $where.Note.Text
        $where.Form.Dispose()
        Write-Output "location: default $first; D:\Tools -> $(Resolve-AppFolder 'D:\Tools')"
        if ($first -ne $DefaultApp -or (Resolve-AppFolder 'D:\Tools') -ne "D:\Tools\$AppName" -or $note -notmatch 'Outside Program Files' -or
            (Resolve-AppFolder "D:\Tools\$AppName") -ne "D:\Tools\$AppName") { $bad += 'location screen' }
        $catalogue = Get-Catalogue
        $byKey = @{}; foreach ($v in $catalogue) { $byKey[$v.Key] = $v }
        $real = @(Get-InstalledIds)
        $ids = if ($real.Count) { $real } else { @('en_GB-alba-medium', 'en_GB-vctk-medium#p239') }
        $ui = New-ManagerForm $catalogue $ids $false $byKey
        $null = $ui.Form.Handle; $null = $ui.List.Handle; $null = $ui.InstalledList.Handle
        Write-Output ("window: {0} voices in the catalogue, {1} installed shown; {2}" -f $ui.List.Items.Count, $ui.InstalledList.Items.Count, $ui.SizeLabel.Text)
        # Speakers: select VCTK, tick a second speaker.
        $vctk = @($ui.List.Items | Where-Object { $_.Tag.Key -eq 'en_GB-vctk-medium' })[0]
        $vctk.Selected = $true
        Update-UiSelection
        $count = $ui.SpeakerList.Items.Count
        $before = @($ui.SpeakerList.CheckedItems | ForEach-Object { [string]$_ })
        $index = 0; while ($before -contains [string]$ui.SpeakerList.Items[$index]) { $index++ }
        $other = [string]$ui.SpeakerList.Items[$index]
        $ui.SpeakerList.SetItemChecked($index, $true)
        Save-UiSpeakers
        $picked = @(Get-PickedIds $ui)
        Write-Output ("speakers: {0} listed for VCTK, ticked {1}; after ticking {2}: {3}" -f $count, ($before -join ','), $other, ($picked -join ' '))
        if ($count -ne $vctk.Tag.Speakers.Count -or $picked -notcontains "en_GB-vctk-medium#$other") { $bad += 'speaker list' }
        foreach ($b in $before) { if ($picked -notcontains "en_GB-vctk-medium#$b") { $bad += "speaker $b dropped" } }
        # Two qualities of one voice: the warning.
        foreach ($item in $ui.List.Items) { if ($item.Tag.Name -eq 'lessac') { $item.Checked = $true } }
        Update-UiSummary
        Write-Output "warning: $($ui.WarnLabel.Text)"
        if ($ui.WarnLabel.Text -notmatch 'Lessac') { $bad += 'duplicate warning' }
        # Sliders, on a real installed voice: saved to its voice.json.
        if ($real.Count -and $ui.InstalledList.Items.Count) {
            $ui.InstalledList.Items[0].Selected = $true
            Update-UiInstalled
            $dir = $ui.CurrentInstalled.Dir
            $was = Get-VoiceSettings $dir
            $ui.Sliders.Volume.Bar.Value = 60; $ui.Sliders.Boost.Bar.Value = 20; $ui.Sliders.Speed.Bar.Value = 80
            Save-UiSettings
            $now = Get-VoiceSettings $dir
            Write-Output ("sliders: {0} -> peak {1}, boost {2}, length_scale {3}" -f $ui.CurrentInstalled.Title, $now.Peak, $now.Boost, $now.LengthScale)
            if ($now.Peak -ne 0.6 -or $now.Boost -ne 2 -or $now.LengthScale -ne 1.25) { $bad += 'sliders' }
            Set-VoiceSettings $dir $was.Peak $was.Boost $was.LengthScale
            # The Speed and pauses tab: saved to settings.json.
            $before = Get-Pauses $SettingsFile
            $speedBefore = Get-GlobalSpeed $SettingsFile
            $ui.GlobalBars['Sentence gap'].Bar.Value = 750
            $ui.SpeedBar.Value = 150
            Save-UiGlobalSettings
            $now = Get-Pauses $SettingsFile
            $speedNow = Get-GlobalSpeed $SettingsFile
            Write-Output ("speed and pauses tab: speed {0} -> {1}%, sentence {2} -> {3} ms; example: {4}" -f $speedBefore, $speedNow,
                $before.Sentence, $now.Sentence, $ui.GlobalBars['Sentence gap'].Example.Text)
            if ($now.Sentence -ne 750 -or $speedNow -ne 150 -or $speedBefore -ne 100 -or $now.Paragraph -ne $before.Paragraph) { $bad += 'speed and pauses tab' }
            Set-GlobalSpeed $SettingsFile $speedBefore
            Set-GlobalPauses $SettingsFile $before.Sentence $before.Comma $before.Paragraph
            Set-UiGlobalBars (Get-Pauses $SettingsFile) (Get-GlobalSpeed $SettingsFile)
            # Windows' own speed: the warning shows when it isn't 0, and the button sets it back.
            $rateWas = Get-ItemProperty 'HKCU:\Software\Microsoft\Speech\Voices' -Name DefaultTTSRate -ErrorAction SilentlyContinue
            Set-WindowsRate 2
            Update-UiWindowsRate
            $warned = $ui.RateWarn.Text -like '*+2*'
            Set-WindowsRate 0; Update-UiWindowsRate      # what the button does
            $cleared = (Get-WindowsRate) -eq 0 -and $ui.RateWarn.Text -eq ''
            Write-Output ("Windows' speed +2: warning shown {0}; button sets it to 0 {1}" -f $warned, $cleared)
            if (-not ($warned -and $cleared)) { $bad += "Windows' speed warning" }
            if ($null -eq $rateWas) { Remove-ItemProperty 'HKCU:\Software\Microsoft\Speech\Voices' -Name DefaultTTSRate -ErrorAction SilentlyContinue }
            else { Set-WindowsRate $rateWas.DefaultTTSRate }
            # A voice's own pauses: off to begin with (its sliders hidden); ticked, its sentence gap moved
            # and saved to its voice.json; unticked again.
            $offFirst = -not $ui.OwnBox.Checked -and -not $ui.GapGrid.Visible
            $ui.OwnBox.Checked = $true
            Update-UiOwnPauses
            $enabled = $ui.Sliders['Sentence gap'].Bar.Enabled
            $ui.Sliders['Sentence gap'].Bar.Value = 300
            Save-UiSettings
            $own = Get-Pauses (Join-Path $dir 'voice.json')
            if ($ScreenshotDir) { Save-UiPictures $ScreenshotDir }
            $ui.OwnBox.Checked = $false
            Update-UiOwnPauses
            Save-UiSettings
            $back = Get-Pauses (Join-Path $dir 'voice.json')
            Write-Output ("own pauses: off at first {0}; ticked: sliders on {1}, own {2}, sentence {3} ms; unticked: own {4}" -f $offFirst, $enabled,
                $own.Own, $own.Sentence, $back.Own)
            if (-not $offFirst -or -not $enabled -or -not $own.Own -or $own.Sentence -ne 300 -or $back.Own) { $bad += 'own pauses' }
            # A nickname: the voice renamed where Windows lists it, and back.
            $plain = (Get-UiCurrentVoice).Voice
            $ui.NickBox.Text = 'Test (Name)'
            Set-UiNickname
            $named = (Get-UiCurrentVoice).Voice
            $listed = @(Get-OurVoiceNames) -contains $named
            $shown = $ui.NickBox.Text
            $ui.NickBox.Text = ''
            Set-UiNickname
            $restored = (Get-UiCurrentVoice).Voice
            Write-Output ("nickname: {0} -> {1} (Windows lists it: {2}; box shows '{3}') -> {4}" -f $plain, $named, $listed, $shown, $restored)
            if ($named -notlike '*(Test Name)' -or -not $listed -or $shown -ne 'Test Name' -or $restored -ne $plain -or
                @(Get-OurVoiceNames) -notcontains $plain) { $bad += 'nickname' }
            # A pronunciation rule: added, changed, switched off and removed, each saved to the dictionary file.
            $rulesBefore = @(Get-Dictionary $DictionaryFile).Count
            $ui.WordFrom.Text = ' Niamh '; $ui.WordTo.Text = 'Neev'
            Save-UiWord
            $added = @(Get-Dictionary $DictionaryFile | Where-Object { $_.From -eq 'Niamh' })
            $ui.WordTo.Text = 'Neeve'; $ui.WordCase.Checked = $true
            Save-UiWord
            $changed = @(Get-Dictionary $DictionaryFile | Where-Object { $_.From -eq 'Niamh' })
            $ui.WordList.SelectedItems[0].Checked = $false
            Save-UiWords      # (what unticking does, once the list is on screen)
            $off = @(Get-Dictionary $DictionaryFile | Where-Object { $_.From -eq 'Niamh' })
            Remove-UiWord
            $rulesAfter = @(Get-Dictionary $DictionaryFile).Count
            Write-Output ("pronunciation: added '{0}' as '{1}' (on {2}, whole {3}); changed to '{4}' (capitals {5}, {6} rule); unticked: on {7}; removed: {8} -> {9} rules" -f
                $added[0].From, $added[0].To, $added[0].On, $added[0].Whole, $changed[0].To, $changed[0].Case, $changed.Count, $off[0].On, $rulesBefore, $rulesAfter)
            if ($added.Count -ne 1 -or $added[0].To -ne 'Neev' -or -not $added[0].On -or -not $added[0].Whole -or $changed.Count -ne 1 -or $changed[0].To -ne 'Neeve' -or
                -not $changed[0].Case -or $off[0].On -or $rulesAfter -ne $rulesBefore) { $bad += 'pronunciation rules' }
            # Preview: the selected voice by the name programs see; the Speed and pauses tab lists every voice.
            $voice = Get-UiCurrentVoice
            $known = @(Get-OurVoiceNames)
            Write-Output ("preview: Voice manager would say it with '{0}'; Speed and pauses offers {1} voice(s), '{2}' chosen" -f $voice.Voice,
                $ui.PreviewVoiceBox.Items.Count, $ui.PreviewVoiceBox.SelectedItem)
            if ($known -notcontains $voice.Voice -or $ui.PreviewVoiceBox.Items.Count -ne $known.Count -or
                @($ui.PreviewVoiceBox.Items | Where-Object { $known -notcontains $_ }).Count) { $bad += 'preview voices' }
        }
        # Apply with nothing changed: says so, changes nothing.
        foreach ($item in $ui.List.Items) { if ($item.Tag.Name -eq 'lessac') { $item.Checked = $false } }
        $ui.Speakers = @{}; foreach ($id in $ids) { $idParts = Split-VoiceId $id; if ($null -ne $idParts[1]) { $ui.Speakers[$idParts[0]] = $idParts[1] } }
        $ui.Current = $null
        Invoke-UiApply
        Write-Output "apply, nothing changed: $($ui.Status.Text)"
        if ($ui.Status.Text -ne 'Nothing to change.') { $bad += 'apply with no changes' }
        # Windows' default voice, set from the Voice manager (put back afterwards).
        if ($real.Count -and $ui.InstalledList.Items.Count) {
            $before = (Get-ItemProperty $DefaultVoiceKey -ErrorAction SilentlyContinue).DefaultTokenId
            $ui.InstalledList.Items[0].Selected = $true
            Update-UiInstalled
            $token = (Get-UiCurrentVoice).Token
            Set-UiDefaultVoice
            $now = (Get-ItemProperty $DefaultVoiceKey).DefaultTokenId
            $marked = $ui.InstalledList.Items[0].SubItems[4].Text
            Write-Output "default voice: $now (marked '$marked'); $($ui.DefaultLabel.Text)"
            if ($now -ne "HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\$token" -or $marked -ne 'yes') { $bad += 'default voice' }
            if ($before) { Set-ItemProperty $DefaultVoiceKey -Name 'DefaultTokenId' -Value $before } else { Remove-ItemProperty $DefaultVoiceKey -Name 'DefaultTokenId' }
        }
        # A download in the background with progress, as when applying changes.
        $ui.Busy = $true
        $sample = Join-Path $env:TEMP 'modernvoices_selftest.mp3'
        Remove-Item $sample -ErrorAction SilentlyContinue
        Get-Download "$Base/en/en_GB/alba/medium/samples/speaker_0.mp3" $sample
        $ui.Busy = $false
        Write-Output ("background download: {0} bytes, progress bar at {1}" -f (Get-Item $sample).Length, $ui.Progress.Value)
        if ((Get-Item $sample).Length -lt 1000) { $bad += 'background download' }
        $ui.Form.Dispose()
        if ($bad.Count) { Write-Output "window: FAILED ($($bad -join ', '))"; exit 3 }
        Write-Output 'window: ok'
        exit 0
    }

    Write-Host "$AppName $Version" -ForegroundColor Cyan
    $interactive = -not ($Repair -or $Voices.Count -gt 0 -or $Unattended)
    # Where: the installed folder (running from it, or a kit over it), else the one given or chosen.
    $existing = Get-InstalledFolder
    if ($existing) {
        Set-AppPaths $existing
        $InPlace = ($Root.TrimEnd('\') -eq $existing)
    } elseif ($InstallDir) {
        Set-AppPaths (Resolve-AppFolder $InstallDir)
    } elseif ($interactive) {
        $chosen = Read-InstallFolder
        if (-not $chosen) { Write-Host '  cancelled: nothing installed'; exit 0 }
        Set-AppPaths $chosen
    }
    if ($InPlace) { Write-Host '(from the installed folder)' }
    Write-Host "  $App"
    $fresh = -not (Test-Path $App)
    New-Item $App -ItemType Directory -Force | Out-Null
    if ($fresh) { Protect-AppFolder }
    if (-not (Test-Path $SettingsFile)) {
        Set-GlobalSpeed $SettingsFile $SpeedDefault
        Set-GlobalPauses $SettingsFile $PauseDefaults.Sentence $PauseDefaults.Comma $PauseDefaults.Paragraph
    }
    $usTags = [bool]$UsEnglishTags
    if (-not $UsEnglishTags -and (Test-Path $Settings)) { $usTags = ((Get-ItemProperty $Settings).UsEnglishTags -eq 1) }

    Write-Host "`nEngine"
    Install-AndRegisterEngine
    Register-Program
    Write-Host ("  {0}{1}" -f $Dll64, $(if (Test-Path $Dll32) { ' and 32-bit' } else { ' (no 32-bit engine in this build)' }))

    Write-Host "`nVoice list"
    $catalogue = Get-Catalogue
    Write-Host "  $($catalogue.Count) English voices"
    $byKey = @{}
    foreach ($v in $catalogue) { $byKey[$v.Key] = $v }

    $allGood = $true
    if ($interactive) {
        # The window is the program: the console behind it is hidden while it is open.
        $ui = New-ManagerForm $catalogue @(Get-InstalledIds) $usTags $byKey
        Write-Host "`nThe Modern Voices window is open (if you can't see it, it's on the taskbar)."
        Set-ConsoleShown $false
        try { [void]$ui.Form.ShowDialog() } finally { Set-ConsoleShown $true }
        $ui.Form.Dispose()
        exit 0
    }
    $installed = @(Get-InstalledIds)
    if ($Repair) {
        $wanted = $installed
    } elseif ($Voices.Count -gt 0) {
        $wanted = $Voices
    } else {
        $wanted = if ($installed.Count -gt 0) { $installed } else { @('en_GB-alba-medium', 'en_GB-northern_english_male-medium') }
    }
    Write-Host "`nApplying"
    $allGood = Set-InstalledVoices @($wanted | Select-Object -Unique) $usTags $byKey

    Write-Host ''
    if (-not $allGood) {
        Write-Host "Something did not work. The engine's log says why: $env:LOCALAPPDATA\ModernVoices\engine.log" -ForegroundColor Red
        if ($Unattended) { exit 2 }
    } else {
        Write-Host "Done. Installed in $App" -ForegroundColor Green
        Write-Host 'The voices are in every program that uses Windows'' installed voices (restart programs that were open).'
        Write-Host 'Start menu > Modern Voices for Windows: Modern Voices, Test voices, Repair, Uninstall.'
        $signature = (Get-AuthenticodeSignature $Dll64).Status
        if ($signature -ne 'Valid') {
            Write-Host "Note: the engine's signature is '$signature': programs that insist on signed engines (WoW) will not use it." -ForegroundColor Yellow
        }
    }
} catch {
    if ($Unattended) { Write-Host "Stopped: $($_.Exception.Message)`n$($_.ScriptStackTrace)" -ForegroundColor Red; exit 1 }
    Stop-Here "Stopped: $($_.Exception.Message)"
}
if (-not $NoPause -and -not $Unattended) { Read-Host 'Press Enter to close' | Out-Null }
