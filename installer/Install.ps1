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

$Version = '0.7.1'
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
$DefaultPeak = 0.95
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

$PreviewText = 'Pulling in three. Watch the patrol on the left, it has a skull!'

function Get-DisplayName($Entry, [string]$SpeakerName) {
    if ($SpeakerName) { return "$($Entry.Display) $SpeakerName ($($Entry.Accent))" }
    return "$($Entry.Display) ($($Entry.Accent))"
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

function New-ManagerForm($Catalogue, [string[]]$Installed, [bool]$UsTags, $ByKey) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName $Version"
    $form.Size = New-Object System.Drawing.Size(880, 770)
    $form.MinimumSize = New-Object System.Drawing.Size(760, 690)
    $form.StartPosition = 'CenterScreen'
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
    # In front of the console it was started from (else it can open behind it, unseen).
    $form.TopMost = $true
    $form.add_Shown({ $script:Ui.Form.Activate(); $script:Ui.Form.TopMost = $false })
    $bold = New-Object System.Drawing.Font($form.Font, [System.Drawing.FontStyle]::Bold)
    $grey = [System.Drawing.Color]::DimGray

    $tabs = New-Control 'TabControl' $form 12 12 840 640 '' 'Top, Bottom, Left, Right'
    $tabInstalled = New-Object System.Windows.Forms.TabPage 'Voice manager'
    $tabPauses = New-Object System.Windows.Forms.TabPage 'Speed and pauses'
    $tabAdd = New-Object System.Windows.Forms.TabPage 'Add or remove voices'
    $tabs.TabPages.Add($tabInstalled); $tabs.TabPages.Add($tabPauses); $tabs.TabPages.Add($tabAdd)

    # ---- Voice manager
    $installedList = New-Control 'ListView' $tabInstalled 10 10 810 150 '' 'Top, Left, Right'
    $installedList.View = 'Details'; $installedList.FullRowSelect = $true; $installedList.HideSelection = $false
    $installedList.MultiSelect = $false
    [void]$installedList.Columns.Add('Voice', 230)
    [void]$installedList.Columns.Add('Speakers installed', 270)
    [void]$installedList.Columns.Add('On disk', 70)
    [void]$installedList.Columns.Add('Windows default', 110)
    [void]$installedList.Columns.Add('Folder', 120)
    $defaultLabel = New-Control 'Label' $tabInstalled 10 170 470 20 '' 'Top, Left, Right'
    $defaultButton = New-Control 'Button' $tabInstalled 490 166 160 27 'Make default voice' 'Top, Right'
    $defaultButton.Enabled = $false
    $sapiButton = New-Control 'Button' $tabInstalled 660 166 160 27 'Windows speech settings' 'Top, Right'
    $speakersOf = [ordered]@{}
    foreach ($id in $Installed) {
        $idParts = Split-VoiceId $id
        if (-not $speakersOf.Contains($idParts[0])) { $speakersOf[$idParts[0]] = @() }
        if ($null -ne $idParts[1]) { $speakersOf[$idParts[0]] += $idParts[1] }
    }

    $soundLabel = New-Control 'Label' $tabInstalled 10 204 810 20 'Select a voice above to set how it sounds.' 'Top, Left, Right'
    $soundLabel.Font = $bold
    $sliders = @{}
    $y = 228
    foreach ($spec in @(@('Volume', 0, 100, 1, 5), @('Boost', 10, 40, 1, 5), @('Speed', 50, 200, 1, 5),
                        @('Sentence gap', 0, $PauseLimits.Sentence, 50, 250), @('Comma gap', 0, $PauseLimits.Comma, 50, 250),
                        @('Paragraph gap', 0, $PauseLimits.Paragraph, 50, 250))) {
        if ($spec[0] -eq 'Sentence gap') {
            $ownBox = New-Control 'CheckBox' $tabInstalled 10 ($y + 2) 810 22 'Use the pauses for every voice (Speed and pauses tab); untick to give this voice its own' 'Top, Left, Right'
            $ownBox.Enabled = $false
            $y += 28
        }
        [void](New-Control 'Label' $tabInstalled 10 ($y + 6) 100 20 $spec[0] '')
        $bar = New-Control 'TrackBar' $tabInstalled 110 $y 540 30 '' 'Top, Left, Right'
        $bar.AutoSize = $false; $bar.TickStyle = 'None'
        $bar.Minimum = $spec[1]; $bar.Maximum = $spec[2]; $bar.SmallChange = $spec[3]; $bar.LargeChange = $spec[4]
        $bar.Enabled = $false
        $value = New-Control 'Label' $tabInstalled 660 ($y + 6) 160 20 '' 'Top, Right'
        $sliders[$spec[0]] = @{ Bar = $bar; Value = $value }
        $bar.add_ValueChanged({ Update-UiSliderLabels; if (-not $script:Ui.Filling) { $script:Ui.SaveTimer.Stop(); $script:Ui.SaveTimer.Start() } })
        $y += 33
    }
    $explain = New-Control 'Label' $tabInstalled 10 ($y + 2) 810 34 ("Volume: each sentence's loudest moment (0 = as Piper made it). Boost: lifts quiet parts more than " +
        "loud ones (higher sounds more compressed). Speed: 100% = the speed for every voice (Speed and pauses tab). Gaps: added to the voice's own pause.") 'Top, Left, Right'
    $explain.ForeColor = $grey
    $y += 40
    $previewBox = New-Control 'TextBox' $tabInstalled 10 $y 600 24 $PreviewText 'Top, Left, Right'
    $previewButton = New-Control 'Button' $tabInstalled 620 ($y - 1) 95 27 'Preview' 'Top, Right'
    $resetButton = New-Control 'Button' $tabInstalled 725 ($y - 1) 95 27 'Reset' 'Top, Right'
    $previewButton.Enabled = $false; $resetButton.Enabled = $false
    $live = New-Control 'Label' $tabInstalled 10 ($y + 32) 810 34 ("Changes are saved as you move a slider and take effect straight away, also in programs already " +
        "using the voice (from the next thing they say). A voice's speakers share its settings.") 'Top, Left, Right'
    $live.ForeColor = $grey

    # ---- Speed and pauses (every voice; pauses for those without their own)
    $pausesTitle = New-Control 'Label' $tabPauses 10 12 810 20 'Speed and pauses for every voice (a voice can adjust its speed and have its own pauses: Voice manager)' 'Top, Left, Right'
    $pausesTitle.Font = $bold
    [void](New-Control 'Label' $tabPauses 10 52 100 20 'Speed' '')
    $speedBar = New-Control 'TrackBar' $tabPauses 110 44 540 45 '' 'Top, Left, Right'
    $speedBar.Minimum = 50; $speedBar.Maximum = 200; $speedBar.SmallChange = 5; $speedBar.LargeChange = 25; $speedBar.TickFrequency = 25
    $speedValue = New-Control 'Label' $tabPauses 660 52 160 20 '' 'Top, Right'
    $speedValue.Font = $bold
    $speedWhat = New-Control 'Label' $tabPauses 110 88 710 20 ("how fast every voice speaks before a program's own rate: 125% is about rate +2 " +
        "(each voice's Speed adjusts from here)") 'Top, Left, Right'
    $speedWhat.ForeColor = $grey
    $rateWarn = New-Control 'Label' $tabPauses 110 108 520 34 '' 'Top, Left, Right'
    $rateWarn.ForeColor = [System.Drawing.Color]::Firebrick
    $rateReset = New-Control 'Button' $tabPauses 640 108 180 27 "Set Windows' speed to 0" 'Top, Right'
    $rateReset.Visible = $false
    $speedBar.add_ValueChanged({ Update-UiSliderLabels; if (-not $script:Ui.Filling) { $script:Ui.GlobalTimer.Stop(); $script:Ui.GlobalTimer.Start() } })
    $globalBars = @{}
    $y = 150
    foreach ($spec in @(@('Sentence gap', $PauseLimits.Sentence, 'after each sentence, when another follows (none after the last)'),
                        @('Comma gap', $PauseLimits.Comma, 'at each , ; or : (0 = commas left as the voice says them)'),
                        @('Paragraph gap', $PauseLimits.Paragraph, 'at a line break, in place of the sentence gap'))) {
        [void](New-Control 'Label' $tabPauses 10 ($y + 8) 100 20 $spec[0] '')
        $bar = New-Control 'TrackBar' $tabPauses 110 $y 540 45 '' 'Top, Left, Right'
        $bar.Minimum = 0; $bar.Maximum = $spec[1]; $bar.SmallChange = 50; $bar.LargeChange = 250; $bar.TickFrequency = 250
        $value = New-Control 'Label' $tabPauses 660 ($y + 8) 160 20 '' 'Top, Right'
        $value.Font = $bold
        $what = New-Control 'Label' $tabPauses 110 ($y + 44) 710 20 $spec[2] 'Top, Left, Right'
        $what.ForeColor = $grey
        $example = New-Control 'Label' $tabPauses 110 ($y + 62) 710 20 '' 'Top, Left, Right'
        $globalBars[$spec[0]] = @{ Bar = $bar; Value = $value; Example = $example }
        $bar.add_ValueChanged({ Update-UiSliderLabels; if (-not $script:Ui.Filling) { $script:Ui.GlobalTimer.Stop(); $script:Ui.GlobalTimer.Start() } })
        $y += 96
    }
    $pausesExplain = New-Control 'Label' $tabPauses 10 ($y + 2) 810 64 ("Each gap is silence added to the voice's own pause, in milliseconds at 100% speed and a program's rate 0. " +
        "Faster speech uses a smaller share of it and slower a larger share, in step with the speech (the line under each shows what you get). " +
        "A comma gap above 0 has the voice say each part of a sentence separately, so the intonation can sound a little more broken up.") 'Top, Left, Right'
    $pausesExplain.ForeColor = $grey
    $pausesReset = New-Control 'Button' $tabPauses 10 ($y + 70) 140 27 'Reset to defaults' 'Top, Left'
    $pausesNote = New-Control 'Label' $tabPauses 160 ($y + 75) 660 20 ("Defaults: speed 125%, sentence 600 ms, comma 0, paragraph 1200 ms. Saved as you move a slider; " +
        "applies straight away.") 'Top, Left, Right'
    $pausesNote.ForeColor = $grey

    # ---- Add or remove voices
    $intro = New-Control 'Label' $tabAdd 10 8 810 32 ("Tick the voices to install; untick one to remove it. Select a voice and press Play sample to hear it. " +
        "Then press Apply changes: the window shows the progress below.") 'Top, Left, Right'
    $list = New-Control 'ListView' $tabAdd 10 42 810 290 '' 'Top, Bottom, Left, Right'
    $list.View = 'Details'; $list.CheckBoxes = $true; $list.FullRowSelect = $true; $list.HideSelection = $false
    $list.MultiSelect = $false
    [void]$list.Columns.Add('Voice', 220)
    [void]$list.Columns.Add('Accent', 90)
    [void]$list.Columns.Add('Quality', 70)
    [void]$list.Columns.Add('Speakers', 90)
    [void]$list.Columns.Add('Download', 80)
    [void]$list.Columns.Add('Catalogue key', 220)
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
    $speakerLabel = New-Control 'Label' $tabAdd 10 340 810 18 'Select a voice with several speakers to choose which of them to install (each becomes a voice).' 'Bottom, Left, Right'
    $speakerLabel.AutoEllipsis = $true
    $speakerList = New-Control 'CheckedListBox' $tabAdd 10 360 700 104 '' 'Bottom, Left, Right'
    $speakerList.MultiColumn = $true; $speakerList.ColumnWidth = 90; $speakerList.CheckOnClick = $true
    $speakerList.IntegralHeight = $false; $speakerList.Enabled = $false
    $play = New-Control 'Button' $tabAdd 720 360 100 27 'Play sample' 'Bottom, Right'
    $playHint = New-Control 'Label' $tabAdd 720 390 100 60 'plays the voice, or the speaker selected in this list' 'Bottom, Right'
    $playHint.ForeColor = $grey
    $sizeLabel = New-Control 'Label' $tabAdd 10 470 810 20 '' 'Bottom, Left, Right'
    $warnLabel = New-Control 'Label' $tabAdd 10 492 810 36 '' 'Bottom, Left, Right'
    $warnLabel.ForeColor = [System.Drawing.Color]::Firebrick
    $usBox = New-Control 'CheckBox' $tabAdd 10 530 810 22 'Also list British voices as US English (only if a program does not show them)' 'Bottom, Left, Right'
    $usBox.Checked = $UsTags
    $sizeNote = New-Control 'Label' $tabAdd 10 554 810 50 ("Speakers of one voice share its model, so extra speakers take no extra space. Two qualities of the same voice " +
        "(medium and high) are separate models: each takes its full size. Some programs (World of Warcraft, for one) take longer to " +
        "start the more voices Windows has: keep the list to the ones you use.") 'Bottom, Left, Right'
    $sizeNote.ForeColor = $grey

    # ---- bottom of the window: progress while applying changes
    $progress = New-Control 'ProgressBar' $form 12 664 600 14 '' 'Bottom, Left, Right'
    $progress.Visible = $false
    $status = New-Control 'Label' $form 12 682 600 36 '' 'Bottom, Left, Right'
    $status.ForeColor = $grey
    $apply = New-Control 'Button' $form 632 670 110 30 'Apply changes' 'Bottom, Right'
    $close = New-Control 'Button' $form 752 670 100 30 'Close' 'Bottom, Right'
    $close.DialogResult = 'Cancel'
    $form.CancelButton = $close

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 350
    $timer.add_Tick({ $script:Ui.SaveTimer.Stop(); Save-UiSettings })
    $globalTimer = New-Object System.Windows.Forms.Timer
    $globalTimer.Interval = 350
    $globalTimer.add_Tick({ $script:Ui.GlobalTimer.Stop(); Save-UiGlobalSettings })

    $script:Ui = @{ Form = $form; Tabs = $tabs; List = $list; SpeakerLabel = $speakerLabel; SpeakerList = $speakerList; UsBox = $usBox
        Speakers = $chosenSpeakers; Current = $null; Player = $null; Filling = $false; InstalledKeys = @($speakersOf.Keys)
        InstalledList = $installedList; SoundLabel = $soundLabel; Sliders = $sliders; PreviewBox = $previewBox
        PreviewButton = $previewButton; ResetButton = $resetButton; CurrentInstalled = $null; SaveTimer = $timer
        PreviewProcess = $null; SizeLabel = $sizeLabel; WarnLabel = $warnLabel; Catalogue = $Catalogue; ByKey = $ByKey
        DefaultLabel = $defaultLabel; DefaultButton = $defaultButton; Progress = $progress; Status = $status; Apply = $apply
        Close = $close; Busy = $false; UsTags = $UsTags; Installed = @($Installed); Bold = $bold; Plain = $form.Font; Changed = $false
        OwnBox = $ownBox; GlobalBars = $globalBars; GlobalTimer = $globalTimer; SpeedBar = $speedBar; SpeedValue = $speedValue
        RateWarn = $rateWarn; RateReset = $rateReset }
    $list.add_SelectedIndexChanged({ Update-UiSelection })
    $list.add_ItemChecked({ if (-not $script:Ui.Filling) { Update-UiSummary } })
    $speakerList.add_ItemCheck({
        param($sender, $e)
        if (-not $script:Ui.Filling -and $e.NewValue -eq [System.Windows.Forms.CheckState]::Checked -and
            $script:Ui.List.SelectedItems.Count -gt 0) { $script:Ui.List.SelectedItems[0].Checked = $true }
    })
    $play.add_Click({ Invoke-UiSample })
    $installedList.add_SelectedIndexChanged({ Update-UiInstalled })
    $previewButton.add_Click({ Invoke-UiPreview })
    $defaultButton.add_Click({ Set-UiDefaultVoice })
    $sapiButton.add_Click({ Start-Process "$env:SystemRoot\System32\Speech\SpeechUX\sapi.cpl" })
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
    Update-UiInstalledList
    Update-UiSummary
    if ($installedList.Items.Count -eq 0) { $tabs.SelectedTab = $tabAdd }
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
        $ui.SpeedBar.Value = [int][math]::Max(50, [math]::Min(200, $Speed))
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
    # A voice that uses these shows them.
    if ($script:Ui.CurrentInstalled -and $script:Ui.OwnBox.Checked) { Update-UiOwnPauses }
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

# The selected voice's pause sliders: its own (unticked), or the Speed and pauses tab's shown greyed (ticked).
function Update-UiOwnPauses {
    $ui = $script:Ui
    $useGlobal = $ui.OwnBox.Checked
    $s = $ui.Sliders
    foreach ($k in 'Sentence gap', 'Comma gap', 'Paragraph gap') { $s[$k].Bar.Enabled = ($ui.CurrentInstalled -ne $null) -and -not $useGlobal }
    if ($useGlobal -and -not $ui.Filling) {
        $was = $ui.Filling
        $ui.Filling = $true
        try {
            foreach ($k in 'Sentence gap', 'Comma gap', 'Paragraph gap') { $s[$k].Bar.Value = $ui.GlobalBars[$k].Bar.Value }
            Update-UiSliderLabels
        } finally {
            $ui.Filling = $was
        }
    }
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
        $item = New-Object System.Windows.Forms.ListViewItem($title)
        [void]$item.SubItems.Add($(if ($names.Count) { $names -join ', ' } else { '1' }))
        [void]$item.SubItems.Add("$(Get-FolderMb $dir) MB")
        [void]$item.SubItems.Add($(if ($isDefault) { 'yes' } else { '' }))
        [void]$item.SubItems.Add($key)
        $item.Tag = @{ Key = $key; Dir = $dir; Title = $title; Voice = $voiceName; Shared = ($names.Count -gt 1); Token = $tokens[0] }
        [void]$ui.InstalledList.Items.Add($item)
    }
    $ui.InstalledList.EndUpdate()
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
    $tag = $script:Ui.CurrentInstalled
    if (-not $tag) { return }
    Set-DefaultVoiceToken $tag.Token
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
    $ui.Tabs.Enabled = $false; $ui.Apply.Enabled = $false; $ui.Close.Enabled = $false
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
        $ui.Tabs.Enabled = $true; $ui.Apply.Enabled = $true; $ui.Close.Enabled = $true
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
        if ($ui.InstalledList.Items.Count -gt 0) { $ui.Tabs.SelectedIndex = 0 }
    } elseif ($ui.Status.Text -notlike 'Stopped*') {
        $ui.Status.Text = "A voice failed its test. The engine's log says why: $env:LOCALAPPDATA\ModernVoices\engine.log"
    }
}

function Update-UiInstalled {
    $ui = $script:Ui
    if ($ui.SaveTimer.Enabled) { $ui.SaveTimer.Stop(); Save-UiSettings }
    $ui.CurrentInstalled = $null
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
        $ui.OwnBox.Checked = -not $voicePauses.Own
        $ui.Sliders['Sentence gap'].Bar.Value = [int][math]::Min($PauseLimits.Sentence, $shown.Sentence)
        $ui.Sliders['Comma gap'].Bar.Value = [int][math]::Min($PauseLimits.Comma, $shown.Comma)
        $ui.Sliders['Paragraph gap'].Bar.Value = [int][math]::Min($PauseLimits.Paragraph, $shown.Paragraph)
        $ui.Sliders.Volume.Bar.Value = [int][math]::Max(0, [math]::Min(100, [math]::Round($peak * 100)))
        $ui.Sliders.Boost.Bar.Value = [int][math]::Max(10, [math]::Min(40, [math]::Round($boost * 10)))
        $ui.Sliders.Speed.Bar.Value = [int][math]::Max(50, [math]::Min(200, [math]::Round(100 / $lengthScale)))
        Update-UiSliderLabels
    } finally {
        $ui.Filling = $false
    }
    $ui.CurrentInstalled = $tag
    foreach ($k in 'Sentence gap', 'Comma gap', 'Paragraph gap') { $ui.Sliders[$k].Bar.Enabled = -not $ui.OwnBox.Checked }
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
        Set-VoicePauses $tag.Dir (-not $ui.OwnBox.Checked) (Get-RoundedMs $s['Sentence gap'].Bar.Value) `
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

# Speaks the line with the selected voice in a separate PowerShell, as any program would (the engine
# is never loaded into this one, so nothing here holds its files).
function Invoke-UiPreview {
    $ui = $script:Ui
    $tag = $ui.CurrentInstalled
    if (-not $tag) { return }
    if ($ui.SaveTimer.Enabled) { $ui.SaveTimer.Stop(); Save-UiSettings }
    Stop-UiPreview
    $text = if ($ui.PreviewBox.Text.Trim()) { $ui.PreviewBox.Text } else { $PreviewText }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($text))
    $ui.PreviewProcess = Start-Process $PS64 -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$(Join-Path $Here 'Speak.ps1')`"", '-Voice', "`"$($tag.Voice)`"", '-TextBase64', $encoded)
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
        $names = @($v.Speakers.Keys)
        $ticked = @()
        if ($ui.Speakers.ContainsKey($v.Key)) { $ticked = @(($ui.Speakers[$v.Key] -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
        foreach ($n in $names) { [void]$ui.SpeakerList.Items.Add($n, ($ticked -contains $n)) }
        $ui.SpeakerList.Enabled = $true
        $ui.SpeakerLabel.Text = "Speakers of $($v.Display) ($($names.Count)): tick the ones to install, each becomes a voice " +
            "(none ticked: the first, $($names[0])). Select one and press Play sample to hear it."
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
    New-Item $Settings -Force | Out-Null
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
            if ($now.Sentence -ne 750 -or $speedNow -ne 150 -or $speedBefore -ne 125 -or $now.Paragraph -ne $before.Paragraph) { $bad += 'speed and pauses tab' }
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
            # A voice of its own: untick, move its sentence gap, saved to its voice.json; tick again.
            $ui.OwnBox.Checked = $false
            Update-UiOwnPauses
            $enabled = $ui.Sliders['Sentence gap'].Bar.Enabled
            $ui.Sliders['Sentence gap'].Bar.Value = 300
            Save-UiSettings
            $own = Get-Pauses (Join-Path $dir 'voice.json')
            $ui.OwnBox.Checked = $true
            Update-UiOwnPauses
            Save-UiSettings
            $back = Get-Pauses (Join-Path $dir 'voice.json')
            Write-Output ("own pauses: sliders on {0}; own {1}, sentence {2} ms; ticked again: own {3}, shows {4} ms" -f $enabled, $own.Own,
                $own.Sentence, $back.Own, $ui.Sliders['Sentence gap'].Bar.Value)
            if (-not $enabled -or -not $own.Own -or $own.Sentence -ne 300 -or $back.Own -or
                $ui.Sliders['Sentence gap'].Bar.Value -ne $ui.GlobalBars['Sentence gap'].Bar.Value) { $bad += 'own pauses' }
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
            $token = $ui.CurrentInstalled.Token
            Set-UiDefaultVoice
            $now = (Get-ItemProperty $DefaultVoiceKey).DefaultTokenId
            $marked = $ui.InstalledList.Items[0].SubItems[3].Text
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
