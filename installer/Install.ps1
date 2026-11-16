<#
Modern Voices for Windows - install, choose voices, repair.

Adds modern, offline voices (Piper, English) to Windows' speech API (SAPI 5), for programs that use
Windows' installed voices but cannot use Narrator's natural voices. 64-bit and 32-bit programs both.

  Install.ps1                       install or update, then open Modern Voices Config (the settings window,
                                    a program of its own: config\ModernVoicesConfig.exe)
  Install.ps1 -Repair               register everything again as it is installed (and apply loudness
                                    changes made in a voice's voice.json)
  Install.ps1 -Voices a,b -Unattended
                                    no window, no questions: exactly these voices (catalogue keys, such as
                                    en_GB-alba-medium; a speaker of a multi-speaker voice as key#speaker)
  Install.ps1 -ExactVoices -Voices a,b -Unattended -NoPause -Progress
                                    what Modern Voices Config's Apply changes runs: the voices ticked there
                                    (none given: none installed), with each download's progress printed

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
    [switch]$ExactVoices,     # exactly -Voices, also when that is none (else: none given means the ones installed)
    [switch]$Progress,        # prints "##progress N" (percent) lines while a file downloads
    [switch]$PickerSelfTest,  # builds the "where to install" screen without showing it (for the build's tests)
    [switch]$FunctionsOnly    # defines the functions and stops (for tests)
)

# (Said at once: Windows can take a while over what follows. Install.cmd says it itself, before PowerShell starts.)
if (-not $FunctionsOnly -and -not $env:MV_LOADING_SAID) { Write-Host 'Loading program...' }

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
    if ($ExactVoices) { $forward += '-ExactVoices' }
    if ($Progress) { $forward += '-Progress' }
    $env:MV_LOADING_SAID = $null      # (the administrator's window says it for itself)
    Start-Process powershell.exe -Verb RunAs -ArgumentList $forward
    exit
}
if (-not [Environment]::Is64BitProcess -and -not $FunctionsOnly) { throw 'Run this from 64-bit PowerShell (use Install.cmd).' }

# The program's own identity on the taskbar.
function Use-ConsoleApi {
    if (-not ('ModernVoices.Console' -as [type])) {
        Add-Type -Namespace ModernVoices -Name Console -MemberDefinition @'
[DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SetCurrentProcessExplicitAppUserModelID(string id);
'@
    }
}
# The installer's own identity on the taskbar: else Windows counts its "where to install" window as
# PowerShell's and shows PowerShell's logo there; with its own, the taskbar shows the window's icon (the cog
# in a speech bubble). Before any window.
function Set-UiTaskbarIdentity {
    try { Use-ConsoleApi; [void][ModernVoices.Console]::SetCurrentProcessExplicitAppUserModelID('ModernVoicesForWindows.Config') } catch { }
}
if (-not $FunctionsOnly -and -not $PickerSelfTest -and -not ($Repair -or $Voices -or $Unattended -or $ExactVoices)) { Set-UiTaskbarIdentity }
# -File passes "a,b" as one string: split it.
$Voices = @($Voices | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

. (Join-Path $Here 'VoiceConvert.ps1')

$Version = '0.12.7'
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
}
# The settings people change (speed, pauses, output volume, pronunciation, each voice's own) are kept apart
# from the program, where changing them needs no administrator: see $UserData in VoiceConvert.ps1.
$SettingsFolder = Join-Path $env:ProgramData $AppName
$SettingsFile = Join-Path $SettingsFolder 'settings.json'
$DictionaryFile = Join-Path $SettingsFolder 'dictionary.txt'

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

# A line of progress. (Modern Voices Config, applying changes, shows these lines in its window.)
function Write-Status([string]$Text, [string]$Color = 'Gray') {
    Write-Host $Text -ForegroundColor $Color
}

# Downloads to $Path (through a .part file). With -Progress, how far it has got is printed as it goes
# ("##progress 42": percent of $Bytes, the size to expect, when known), for Modern Voices Config's bar.
function Get-Download([string]$Url, [string]$Path, [double]$Bytes = 0) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $part = "$Path.part"
    $client = New-Object Net.WebClient
    $client.Headers.Add('User-Agent', "ModernVoicesForWindows/$Version")
    try {
        if ($Progress -and $Bytes -gt 0) {
            $said = -1
            $client.DownloadFileAsync([uri]$Url, $part)
            while ($client.IsBusy) {
                Start-Sleep -Milliseconds 200
                $done = if (Test-Path $part) { (Get-Item $part).Length } else { 0 }
                $percent = [int][math]::Floor([math]::Min(100, 100 * $done / $Bytes))
                if ($percent -ne $said) { $said = $percent; Write-Host "##progress $percent" }
            }
            $got = if (Test-Path $part) { (Get-Item $part).Length } else { 0 }
            if ($got -ne $Bytes) { throw "The download of $Url stopped part way (check the internet connection)." }
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
# The program's icon (a speech bubble with a gear) on a window: its title bar and the taskbar.
function Set-UiIcon($Form) {
    $icon = Join-Path $Here 'manager.ico'
    if (Test-Path $icon) { try { $Form.Icon = New-Object System.Drawing.Icon $icon } catch { } }
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

function New-LocationForm([string]$Default) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName $Version - install"
    Set-UiIcon $form
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

# The settings folder: made, every user of the PC allowed to change what is in it (Modern Voices Config runs
# without administrator rights), and the settings of a version before 0.12.0 (kept in the program's folder)
# brought over. A voice's own settings from then are still in its voice.json, which is read where its own
# file says nothing.
function Initialize-SettingsFolder {
    New-Item (Join-Path $SettingsFolder 'voices') -ItemType Directory -Force | Out-Null
    & "$env:SystemRoot\System32\icacls.exe" $SettingsFolder /grant '*S-1-5-32-545:(OI)(CI)M' | Out-Null
    if ($LASTEXITCODE) { throw "Could not set who may change $SettingsFolder (icacls: $LASTEXITCODE)." }
    foreach ($name in @('settings.json', 'dictionary.txt')) {
        $old = Join-Path $App $name
        if (-not (Test-Path $old)) { continue }
        $new = Join-Path $SettingsFolder $name
        if (-not (Test-Path $new)) { Copy-Item $old $new }      # (a copy: it takes the new folder's permissions)
        Remove-Item $old -Force
    }
    if (-not (Test-Path $SettingsFile)) {
        Set-GlobalSpeed $SettingsFile $SpeedDefault
        Set-GlobalPauses $SettingsFile $PauseDefaults.Sentence $PauseDefaults.Comma $PauseDefaults.Paragraph
    }
}

# --- names --------------------------------------------------------------------------------------------

# A nickname of your own for a voice, shown in brackets in its name in place of its accent ("VCTK p262
# (Irish Female)"): kept by voice id under the program's settings key. (Set in Modern Voices Config or the
# Reader; registering a voice keeps it.)
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

# The pronunciation rules the program comes with, in sets. A set is added once (the registry's RuleSets
# value says which have been): rules already in the list, the person's own, are left as they are, and a
# rule of a set that was removed later stays removed. A new set in a later version is added by an update.
$DefaultRuleSets = [ordered]@{
    smileys1 = @(
        @(':D', 'big grin'), @('xD', 'laughing'), @(':P', 'tongue out'), @(':)', 'smiley face'),
        @(':(', 'sad face'), @(';)', 'wink'), @('<3', 'love heart')
    )
    # Chat abbreviations (a third value of $false: in the list, but switched off, as it is also an ordinary
    # abbreviation: RN, NP, WP, LF).
    chat1 = @(
        @('lol', 'haha'), @('lmao', 'hahaha'), @('lmfao', 'ah hahaha'), @('rofl', 'ahh haha, hahahahaha'),
        @('ty', 'thank you'), @('thx', 'thanks'), @('tyvm', 'thank you very much'),
        @('np', 'no problem', $false), @('pls', 'please'), @('plz', 'please'),
        @('gg', 'good game'), @('wp', 'well played', $false), @('gl', 'good luck'), @('hf', 'have fun'),
        @('brb', 'be right back'), @('afk', 'A F K'), @('omw', 'on my way'),
        @('wb', 'welcome back'), @('rn', 'right now', $false), @('imo', 'in my opinion'),
        @('idk', "I don't know"), @('ikr', 'I know right'), @('inv', 'invite'),
        @('lfg', 'looking for group'), @('lf', 'looking for', $false), @('iirc', 'if I recall correctly')
    )
    trade1 = @(
        @('wtb', 'want to buy'), @('wts', 'want to sell'), @('lfw', 'looking for work'), @('lvl', 'level')
    )
}
function Add-DefaultRules {
    if (-not (Test-Path $Settings)) { New-Item $Settings -Force | Out-Null }
    $done = @(([string](Get-ItemProperty $Settings -ErrorAction SilentlyContinue).RuleSets) -split ',' | Where-Object { $_ })
    $new = @($DefaultRuleSets.Keys | Where-Object { $done -notcontains $_ })
    if ($new.Count -eq 0) { return }
    $rules = @(Get-Dictionary $DictionaryFile)
    $added = 0
    foreach ($set in $new) {
        foreach ($pair in $DefaultRuleSets[$set]) {
            if (@($rules | Where-Object { $_.From -ieq $pair[0] }).Count -gt 0) { continue }
            $rules += [pscustomobject]@{ On = ($pair.Count -lt 3 -or [bool]$pair[2]); Whole = $true; Case = $false; From = $pair[0]; To = $pair[1] }
            $added++
        }
    }
    if ($added -gt 0) { Set-Dictionary $DictionaryFile $rules }
    Set-ItemProperty $Settings -Name 'RuleSets' -Value (@($done + $new) -join ',')
    if ($added -gt 0) { Write-Host "  $added pronunciation rules added (the Pronunciation tab lists them)" }
}

# An update over an older version: the program files of that kind which this version no longer has go.
function Remove-Leftovers([string]$From, [string]$Into, [string]$Kind) {
    foreach ($file in @(Get-ChildItem $Into -Filter $Kind -File -ErrorAction SilentlyContinue)) {
        if (-not (Test-Path (Join-Path $From $file.Name))) { Remove-Item $file.FullName -Force -ErrorAction SilentlyContinue }
    }
}

# Before an update: the Reader and an open Modern Voices Config are closed (the Reader keeps its text
# and place), as their files are about to be replaced.
function Close-OurPrograms {
    foreach ($reader in @(Get-Process 'ModernVoicesReader' -ErrorAction SilentlyContinue)) {
        Write-Host '  closing the Reader'
        try {
            [void]$reader.CloseMainWindow()
            if (-not $reader.WaitForExit(5000)) { $reader.Kill(); [void]$reader.WaitForExit(5000) }
        } catch { }
    }
    foreach ($config in @(Get-Process 'ModernVoicesConfig' -ErrorAction SilentlyContinue)) {
        if ($config.MainWindowHandle -ne [IntPtr]::Zero) { Write-Host '  closing Modern Voices Config' }
        try {
            [void]$config.CloseMainWindow()      # (it saves what it was changing; a Preview's process has no window)
            if (-not $config.WaitForExit(5000)) { $config.Kill(); [void]$config.WaitForExit(5000) }
        } catch { }
    }
    # (Before 0.11.0, Modern Voices Config was a window of PowerShell's.)
    foreach ($shell in @(Get-Process 'powershell' -ErrorAction SilentlyContinue)) {
        if ($shell.Id -ne $PID -and $shell.MainWindowTitle -like 'Modern Voices Config*') {
            Write-Host '  closing Modern Voices Config'
            try { [void]$shell.CloseMainWindow(); [void]$shell.WaitForExit(3000) } catch { }
        }
    }
}

function Install-Engine {
    $source = Join-Path $Root 'engine'
    if (-not (Test-Path (Join-Path $source 'x64\modern_voices.dll'))) { throw "The engine is missing from $source." }
    if ($InPlace) { return }
    foreach ($dll in $Dll64, $Dll32) {
        # (A program that has just finished speaking, as the voice test before an Apply straight after another,
        # can take a moment to let go of the engine.)
        for ($freeTry = 0; $freeTry -lt 16 -and -not (Test-FileFree $dll); $freeTry++) { Start-Sleep -Milliseconds 500 }
        if (-not (Test-FileFree $dll)) {
            throw "A program is using the voices now (the engine is in use: $dll). Close programs that speak, then run this again."
        }
    }
    New-Item $Engine -ItemType Directory -Force | Out-Null
    foreach ($sub in 'x64', 'x86') {
        $from = Join-Path $source $sub
        $into = Join-Path $Engine $sub
        if (Test-Path $from) {
            New-Item $into -ItemType Directory -Force | Out-Null
            Copy-Item (Join-Path $from '*') $into -Force
            Remove-Leftovers $from $into '*.dll'
        }
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
    Copy-Item (Join-Path $Here '*.ico') $scripts -Force
    Remove-Leftovers $Here $scripts '*.ps1'
    Remove-Leftovers $Here $scripts '*.ico'
    # Modern Voices Config and Modern Voices: Reader (programs of their own, beside this one).
    foreach ($program in @(@('config', 'Modern Voices Config'), @('reader', 'The Reader'))) {
        $programFrom = Join-Path $Root $program[0]
        if (Test-Path $programFrom) {
            $programInto = Join-Path $App $program[0]
            New-Item $programInto -ItemType Directory -Force | Out-Null
            try { Copy-Item (Join-Path $programFrom '*') $programInto -Force }
            catch { throw "$($program[1]) is open: close it, then run this again. ($($_.Exception.Message))" }
            Remove-Leftovers $programFrom $programInto '*.exe'
            Remove-Leftovers $programFrom $programInto '*.dll'
        }
    }
    # A kit unzipped from a download carries Windows' "this came from the internet" mark on every file,
    # and copies keep it. Windows then checks a marked program with its reputation service the first time
    # it starts, which can hold the program's window back for a long while. The installed files lose the mark.
    foreach ($marked in @((Join-Path $App 'config'), (Join-Path $App 'reader'), $Engine, $scripts)) {
        try { Get-ChildItem $marked -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue } catch { }
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

# The program's icon (the speech bubble with the speaker: the Reader's own, which Windows' Installed apps
# shows); Modern Voices Config has its own, the bubble with the cog, and Repair, Test and Uninstall in the
# Start menu the bubble with the wrench.
function Get-AppIcon {
    $readerExe = Join-Path $App 'reader\ModernVoicesReader.exe'
    if (Test-Path $readerExe) { return "$readerExe,0" }
    return "$App\installer\manager.ico,0"
}

function New-Shortcut([string]$Path, [string]$Arguments, [string]$Description, [string]$Icon = '') {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath = $PS64
    $link.Arguments = $Arguments
    $link.WorkingDirectory = $App
    $link.Description = $Description
    $link.IconLocation = $(if ($Icon) { $Icon } else { Get-AppIcon })
    $link.Save()
}

function Write-Launcher([string]$Destination, [string]$Script, [string]$Extra, [switch]$LeaveFolder) {
    $run = "`"$PS64`" -NoProfile -ExecutionPolicy Bypass -File `"%~dp0installer\$Script`" $Extra".TrimEnd()
    $text = if ($LeaveFolder) { "@cd /d `"%TEMP%`" & $run & exit /b`r`n" } else { "@echo off`r`n$run`r`n" }
    [System.IO.File]::WriteAllText($Destination, $text, (New-Object System.Text.ASCIIEncoding))
}

function Register-Program {
    $configExe = Join-Path $App 'config\ModernVoicesConfig.exe'
    [System.IO.File]::WriteAllText((Join-Path $App 'Modern Voices Config.cmd'), "@start `"`" `"%~dp0config\ModernVoicesConfig.exe`"`r`n", (New-Object System.Text.ASCIIEncoding))
    Write-Launcher (Join-Path $App 'Modern Voices Repair.cmd') 'Install.ps1' '-Repair'
    Write-Launcher (Join-Path $App 'Modern Voices Test.cmd') 'Test.ps1' ''
    # (As they were named before 0.9.7.)
    foreach ($old in (Join-Path $App 'Repair.cmd'), (Join-Path $App 'Test voices.cmd'), (Join-Path $StartMenu 'Repair.lnk'), (Join-Path $StartMenu 'Test voices.lnk')) {
        Remove-Item $old -Force -ErrorAction SilentlyContinue
    }
    Write-Launcher (Join-Path $App 'Uninstall.cmd') 'Uninstall.ps1' '' -LeaveFolder
    $sizeKb = [int]((Get-ChildItem $App -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
    New-Item $OurKey -Force | Out-Null
    Set-ItemProperty $OurKey -Name 'DisplayName' -Value $AppName
    Set-ItemProperty $OurKey -Name 'DisplayVersion' -Value $Version
    Set-ItemProperty $OurKey -Name 'Publisher' -Value $AppName
    Set-ItemProperty $OurKey -Name 'InstallLocation' -Value $App
    Set-ItemProperty $OurKey -Name 'DisplayIcon' -Value (Get-AppIcon)
    Set-ItemProperty $OurKey -Name 'UninstallString' -Value "`"$PS64`" -NoProfile -ExecutionPolicy Bypass -File `"$App\installer\Uninstall.ps1`""
    Set-ItemProperty $OurKey -Name 'ModifyPath' -Value "`"$configExe`""
    Set-ItemProperty $OurKey -Name 'NoRepair' -Value 1 -Type DWord
    Set-ItemProperty $OurKey -Name 'EstimatedSize' -Value $sizeKb -Type DWord
    New-Item $StartMenu -ItemType Directory -Force | Out-Null
    $common = "-NoProfile -ExecutionPolicy Bypass -File `"$App\installer\"
    $readerExe = Join-Path $App 'reader\ModernVoicesReader.exe'
    foreach ($menuItem in @(@('Uninstall.lnk', ($common + 'Uninstall.ps1"'), "Uninstall $AppName"),
                            @('Modern Voices Repair.lnk', ($common + 'Install.ps1" -Repair'), 'Register the voices again (if one went missing from programs)'),
                            @('Modern Voices Test.lnk', ($common + 'Test.ps1"'), 'Hear each Modern Voice, with each word shown as the engine reports it'))) {
        New-Shortcut (Join-Path $StartMenu $menuItem[0]) $menuItem[1] $menuItem[2] "$App\installer\repair.ico,0"
    }
    # Modern Voices Config and the Reader are programs: their entries start them (their icons are their own).
    # (0.12.1 to 0.12.3 tried to order Windows' "Recently added" list, Reader then Config on top, by the
    # entries' creation order and the programs' dates. None changed it, so it was taken out in 0.12.4.)
    foreach ($program in @(@($configExe, 'Modern Voices Config.lnk', 'Voices installed, how they sound, add or remove voices'),
                           @($readerExe, 'Modern Voices Reader.lnk', 'Reads text aloud with the Modern Voices: pasted or copied text, files, and text on the screen'))) {
        if (-not (Test-Path $program[0])) { continue }
        $link = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $StartMenu $program[1]))
        $link.TargetPath = $program[0]
        $link.Arguments = ''
        $link.WorkingDirectory = Split-Path $program[0] -Parent
        $link.Description = $program[2]
        $link.IconLocation = "$($program[0]),0"
        $link.Save()
    }
}

# Runs a program with no window and returns what it printed.
function Invoke-Hidden([string]$Exe, [string[]]$Arguments) {
    $out = [System.IO.Path]::GetTempFileName()
    $err = [System.IO.Path]::GetTempFileName()
    try {
        $p = Start-Process $Exe -ArgumentList $Arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
        while (-not $p.HasExited) {
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
                Remove-Item (Get-VoiceFile $folder.FullName) -Force -ErrorAction SilentlyContinue      # (its own settings with it)
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

# --- the steps --------------------------------------------------------------------------------------

if ($FunctionsOnly) { return }

try {
    if ($PickerSelfTest) {
        # The "where to install" screen, built (not shown): the build's test of it. (Modern Voices Config has
        # its own: config\ModernVoicesConfig.exe --selftest.)
        $bad = @()
        $where = New-LocationForm $DefaultApp
        $first = $where.Path.Text
        $where.Path.Text = 'D:\Tools'
        $note = $where.Note.Text
        $where.Form.Dispose()
        Write-Output "location: default $first; D:\Tools -> $(Resolve-AppFolder 'D:\Tools')"
        if ($first -ne $DefaultApp -or (Resolve-AppFolder 'D:\Tools') -ne "D:\Tools\$AppName" -or $note -notmatch 'Outside Program Files' -or
            (Resolve-AppFolder "D:\Tools\$AppName") -ne "D:\Tools\$AppName") { $bad += 'location screen' }
        if ($bad.Count) { Write-Output "location screen: FAILED ($($bad -join ', '))"; exit 3 }
        Write-Output 'location screen: ok'
        exit 0
    }

    Write-Host "$AppName $Version" -ForegroundColor Cyan
    $interactive = -not ($Repair -or $Voices.Count -gt 0 -or $Unattended -or $ExactVoices)
    # Where: the installed folder (running from it, or a kit over it), else the one given or chosen.
    $existing = Get-InstalledFolder
    $updating = $false
    if ($existing) {
        Set-AppPaths $existing
        $InPlace = ($Root.TrimEnd('\') -eq $existing)
        # A kit run over the installed program updates it: voices, settings, nicknames and pronunciations stay.
        $updating = -not $InPlace
        if ($updating) {
            $versionWas = [string](Get-ItemProperty $OurKey -ErrorAction SilentlyContinue).DisplayVersion
            if ($versionWas -and $versionWas -ne $Version) { Write-Host "Updating $versionWas to $Version (voices and settings are kept)" -ForegroundColor Green }
            else { Write-Host "Installing $Version again over itself (voices and settings are kept)" -ForegroundColor Green }
            Close-OurPrograms
        }
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
    Initialize-SettingsFolder
    $usTags = [bool]$UsEnglishTags
    # (Modern Voices Config says exactly what is wanted; otherwise the option stays as it was unless asked for.)
    if (-not $UsEnglishTags -and -not $ExactVoices -and (Test-Path $Settings)) { $usTags = ((Get-ItemProperty $Settings).UsEnglishTags -eq 1) }

    Write-Host "`nEngine"
    Install-AndRegisterEngine
    Register-Program
    Add-DefaultRules
    Write-Host ("  {0}{1}" -f $Dll64, $(if (Test-Path $Dll32) { ' and 32-bit' } else { ' (no 32-bit engine in this build)' }))

    Write-Host "`nVoice list"
    $catalogue = Get-Catalogue
    Write-Host "  $($catalogue.Count) English voices"
    $byKey = @{}
    foreach ($v in $catalogue) { $byKey[$v.Key] = $v }

    $allGood = $true
    if ($interactive -and $updating) {
        # The voices already here are registered again, as this version registers them (nothing is downloaded).
        $have = @(Get-InstalledIds)
        if ($have.Count -gt 0) {
            Write-Host "`nVoices"
            try { [void](Set-InstalledVoices $have $usTags $byKey) }
            catch { Write-Host "  The voices were left as they were ($($_.Exception.Message)). Modern Voices Repair (Start menu) registers them again." -ForegroundColor Yellow }
        }
    }
    if ($interactive) {
        # Modern Voices Config opens (the installed one): the voices to add or remove, and how they sound.
        # This console has done its work and closes.
        $configExe = Join-Path $App 'config\ModernVoicesConfig.exe'
        if (-not (Test-Path $configExe)) { throw "Modern Voices Config is missing ($configExe)." }
        Write-Host "`nOpening Modern Voices Config (if you can't see it, it's on the taskbar)."
        # (Through Explorer, so that it runs as the person at the PC and not as this administrator's window.)
        Start-Process (Join-Path $env:SystemRoot 'explorer.exe') -ArgumentList "`"$configExe`""
        exit 0
    }
    $installed = @(Get-InstalledIds)
    if ($Repair) {
        $wanted = $installed
    } elseif ($ExactVoices -or $Voices.Count -gt 0) {
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
        Write-Host 'Start menu > Modern Voices for Windows: Modern Voices Config, Reader, Repair and Test, and Uninstall.'
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
