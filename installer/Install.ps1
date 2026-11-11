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
    Start-Process powershell.exe -Verb RunAs -ArgumentList $forward
    exit
}
if (-not [Environment]::Is64BitProcess -and -not $FunctionsOnly) { throw 'Run this from 64-bit PowerShell (use Install.cmd).' }
# -File passes "a,b" as one string: split it.
$Voices = @($Voices | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

. (Join-Path $Here 'VoiceConvert.ps1')

$Version = '0.5.2'
$AppName = 'Modern Voices for Windows'
$Clsid = '{F9857585-FF01-467B-84C0-D6CA4E0572D2}'
$App = Join-Path $env:ProgramFiles $AppName
$Engine = Join-Path $App 'engine'
$Dll64 = Join-Path $Engine 'x64\modern_voices.dll'
$Dll32 = Join-Path $Engine 'x86\modern_voices.dll'
$Cer = Join-Path $Engine 'modern_voices.cer'
$Data = Join-Path $Engine 'espeak-ng-data'
$VoiceRoot = Join-Path $App 'voices'
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
$InPlace = ($Root.TrimEnd('\') -eq $App.TrimEnd('\'))

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

function Get-Download([string]$Url, [string]$Path) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $part = "$Path.part"
    $client = New-Object Net.WebClient
    $client.Headers.Add('User-Agent', "ModernVoicesForWindows/$Version")
    try { $client.DownloadFile($Url, $part) } finally { $client.Dispose() }
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

# --- the list to tick --------------------------------------------------------------------------------

# The window's controls and state live in $script:Ui: its event handlers run later (while the window
# is shown), in the script's scope, where the function's own variables are gone.
function New-PickerForm($Catalogue, [string[]]$Installed, [bool]$UsTags) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName $Version - choose voices"
    $form.Size = New-Object System.Drawing.Size(820, 700)
    $form.StartPosition = 'CenterScreen'
    # In front of the console it was started from (else it can open behind it, unseen).
    $form.TopMost = $true
    $form.add_Shown({ $script:Ui.Form.Activate(); $script:Ui.Form.TopMost = $false })
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
    $form.MinimumSize = New-Object System.Drawing.Size(700, 600)

    $intro = New-Object System.Windows.Forms.Label
    $intro.Text = "Tick the voices to install; untick an installed one to remove it. They are offline neural voices " +
        "(Piper) that any program using Windows' installed voices can speak with. Select a voice and press " +
        "Play sample to hear it. Each voice is about 60 to 110 MB."
    $intro.Location = New-Object System.Drawing.Point(12, 10)
    $intro.Size = New-Object System.Drawing.Size(780, 36)
    $intro.Anchor = 'Top, Left, Right'
    $form.Controls.Add($intro)

    $list = New-Object System.Windows.Forms.ListView
    $list.View = 'Details'
    $list.CheckBoxes = $true
    $list.FullRowSelect = $true
    $list.HideSelection = $false
    $list.MultiSelect = $false
    $list.Location = New-Object System.Drawing.Point(12, 52)
    $list.Size = New-Object System.Drawing.Size(780, 300)
    $list.Anchor = 'Top, Bottom, Left, Right'
    [void]$list.Columns.Add('Voice', 220)
    [void]$list.Columns.Add('Accent', 90)
    [void]$list.Columns.Add('Quality', 70)
    [void]$list.Columns.Add('Speakers', 90)
    [void]$list.Columns.Add('Download', 80)
    [void]$list.Columns.Add('Catalogue key', 200)

    # Speakers chosen for each multi-speaker voice: key -> "name1, name2"
    $chosenSpeakers = @{}
    $installedKeys = @{}
    foreach ($id in $Installed) {
        $idParts = Split-VoiceId $id
        if ($null -ne $idParts[1]) {
            if ($chosenSpeakers.ContainsKey($idParts[0])) { $chosenSpeakers[$idParts[0]] += ", $($idParts[1])" }
            else { $chosenSpeakers[$idParts[0]] = $idParts[1] }
        }
        $installedKeys[$idParts[0]] = $true
    }
    $bold = New-Object System.Drawing.Font($form.Font, [System.Drawing.FontStyle]::Bold)
    foreach ($v in $Catalogue) {
        $item = New-Object System.Windows.Forms.ListViewItem($v.Display)
        [void]$item.SubItems.Add($v.Accent)
        [void]$item.SubItems.Add($v.Quality)
        [void]$item.SubItems.Add($(if ($v.Speakers.Count -gt 1) { "$($v.Speakers.Count) speakers" } else { '1' }))
        [void]$item.SubItems.Add("$($v.SizeMb) MB")
        [void]$item.SubItems.Add($v.Key)
        $item.Tag = $v
        $item.Checked = $installedKeys.ContainsKey($v.Key)
        if ($item.Checked) { $item.Font = $bold }
        [void]$list.Items.Add($item)
    }
    $form.Controls.Add($list)

    $speakerLabel = New-Object System.Windows.Forms.Label
    $speakerLabel.Location = New-Object System.Drawing.Point(12, 362)
    $speakerLabel.Size = New-Object System.Drawing.Size(780, 18)
    $speakerLabel.Anchor = 'Bottom, Left, Right'
    $speakerLabel.AutoEllipsis = $true
    $speakerLabel.Text = 'Select a voice with several speakers to choose which of them to install (each becomes a voice).'
    $form.Controls.Add($speakerLabel)

    # Every speaker of the selected voice, to tick (scrolls sideways when there are many).
    $speakerList = New-Object System.Windows.Forms.CheckedListBox
    $speakerList.Location = New-Object System.Drawing.Point(12, 382)
    $speakerList.Size = New-Object System.Drawing.Size(670, 112)
    $speakerList.Anchor = 'Bottom, Left, Right'
    $speakerList.MultiColumn = $true
    $speakerList.ColumnWidth = 90
    $speakerList.CheckOnClick = $true
    $speakerList.IntegralHeight = $false
    $speakerList.Enabled = $false
    $form.Controls.Add($speakerList)

    $play = New-Object System.Windows.Forms.Button
    $play.Text = 'Play sample'
    $play.Location = New-Object System.Drawing.Point(692, 382)
    $play.Size = New-Object System.Drawing.Size(100, 26)
    $play.Anchor = 'Bottom, Right'
    $form.Controls.Add($play)

    $playHint = New-Object System.Windows.Forms.Label
    $playHint.Text = 'plays the voice, or the speaker selected in this list'
    $playHint.Location = New-Object System.Drawing.Point(692, 412)
    $playHint.Size = New-Object System.Drawing.Size(100, 60)
    $playHint.Anchor = 'Bottom, Right'
    $playHint.ForeColor = [System.Drawing.Color]::DimGray
    $form.Controls.Add($playHint)

    $usBox = New-Object System.Windows.Forms.CheckBox
    $usBox.Text = 'Also list British voices as US English (only if a program does not show them)'
    $usBox.Location = New-Object System.Drawing.Point(12, 504)
    $usBox.Size = New-Object System.Drawing.Size(780, 22)
    $usBox.Anchor = 'Bottom, Left, Right'
    $usBox.Checked = $UsTags
    $form.Controls.Add($usBox)

    $note = New-Object System.Windows.Forms.Label
    $note.Text = 'Some programs (World of Warcraft, for one) take longer to start the more voices Windows has installed: keep the list to the ones you use.'
    $note.Location = New-Object System.Drawing.Point(12, 530)
    $note.Size = New-Object System.Drawing.Size(560, 34)
    $note.Anchor = 'Bottom, Left, Right'
    $note.ForeColor = [System.Drawing.Color]::DimGray
    $form.Controls.Add($note)

    $ok = New-Object System.Windows.Forms.Button
    $ok.Text = 'Install'
    $ok.Location = New-Object System.Drawing.Point(582, 610)
    $ok.Size = New-Object System.Drawing.Size(100, 30)
    $ok.Anchor = 'Bottom, Right'
    $ok.DialogResult = 'OK'
    $form.Controls.Add($ok)
    $form.AcceptButton = $ok

    $cancel = New-Object System.Windows.Forms.Button
    $cancel.Text = 'Cancel'
    $cancel.Location = New-Object System.Drawing.Point(692, 610)
    $cancel.Size = New-Object System.Drawing.Size(100, 30)
    $cancel.Anchor = 'Bottom, Right'
    $cancel.DialogResult = 'Cancel'
    $form.Controls.Add($cancel)
    $form.CancelButton = $cancel

    $script:Ui = @{ Form = $form; List = $list; SpeakerLabel = $speakerLabel; SpeakerList = $speakerList; UsBox = $usBox
                    Speakers = $chosenSpeakers; Current = $null; Player = $null; Filling = $false }
    $list.add_SelectedIndexChanged({ Update-UiSelection })
    # Ticking a speaker ticks its voice too.
    $speakerList.add_ItemCheck({
        param($sender, $e)
        if (-not $script:Ui.Filling -and $e.NewValue -eq [System.Windows.Forms.CheckState]::Checked -and
            $script:Ui.List.SelectedItems.Count -gt 0) { $script:Ui.List.SelectedItems[0].Checked = $true }
    })
    $play.add_Click({ Invoke-UiSample })
    $form.add_FormClosing({ Save-UiSpeakers; if ($script:Ui.Player) { $script:Ui.Player.Stop() } })
    return $script:Ui
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
    foreach ($pair in @(@($Entry.Onnx, $onnx, $Entry.OnnxMd5), @($Entry.Config, $config, $Entry.ConfigMd5))) {
        Write-Host "  downloading $($pair[0]) ..."
        Get-Download "$Base/$($pair[0])" $pair[1]
        $md5 = (Get-FileHash $pair[1] -Algorithm MD5).Hash.ToLower()
        if ($pair[2] -and $md5 -ne $pair[2].ToLower()) {
            Remove-Item $pair[1] -ErrorAction SilentlyContinue
            throw "$($pair[0]) did not download correctly (its MD5 is $md5, not $($pair[2])). Run this again."
        }
    }
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
    $display = if ($SpeakerName) { "$($Entry.Display) $SpeakerName ($($Entry.Accent))" } else { "$($Entry.Display) ($($Entry.Accent))" }
    $lcid = if ($UsTags) { '409' } elseif ($Lcids.ContainsKey($Entry.Code)) { $Lcids[$Entry.Code] } else { '409' }
    if (Test-Path $token) { Remove-Item $token -Recurse -Force }
    New-Item "$token\Attributes" -Force | Out-Null
    Set-ItemProperty $token -Name '(default)' -Value $display
    Set-ItemProperty $token -Name 'CLSID' -Value $Clsid
    Set-ItemProperty $token -Name 'ModelDir' -Value (Join-Path $VoiceRoot $Entry.Key)
    Set-ItemProperty $token -Name 'DataDir' -Value $Data
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
    Write-Launcher (Join-Path $App 'Choose voices.cmd') 'Install.ps1' ''
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
    New-Shortcut (Join-Path $StartMenu 'Choose voices.lnk') ($common + 'Install.ps1"') 'Choose which Modern Voices are installed'
    New-Shortcut (Join-Path $StartMenu 'Test voices.lnk') ($common + 'Test.ps1"') 'Hear each Modern Voice'
    New-Shortcut (Join-Path $StartMenu 'Uninstall.lnk') ($common + 'Uninstall.ps1"') "Uninstall $AppName"
}

# Runs Test.ps1 -Quiet in 64-bit (and 32-bit) PowerShell; returns its results.
function Invoke-SelfTest([bool]$With32) {
    $results = @()
    $runs = @(,@($PS64, 64))   # (the comma keeps it a list of pairs)
    if ($With32 -and (Test-Path $PS32)) { $runs += ,@($PS32, 32) }
    foreach ($r in $runs) {
        $lines = & $r[0] -NoProfile -ExecutionPolicy Bypass -File (Join-Path $App 'installer\Test.ps1') -Quiet 2>&1
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

# --- the steps --------------------------------------------------------------------------------------

if ($FunctionsOnly) { return }

try {
    if ($PickerSelfTest) {
        $catalogue = Get-Catalogue
        $picker = New-PickerForm $catalogue @('en_GB-alba-medium', 'en_GB-vctk-medium#p239') $false
        $picked = @(Get-PickedIds $picker)
        Write-Output ("picker: {0} voices listed, {1} ticked: {2}" -f $picker.List.Items.Count, $picked.Count, ($picked -join ' '))
        # The speaker list, driven as a click would: select VCTK, tick a second speaker.
        $null = $picker.Form.Handle; $null = $picker.List.Handle
        $vctk = @($picker.List.Items | Where-Object { $_.Tag.Key -eq 'en_GB-vctk-medium' })[0]
        $vctk.Selected = $true
        Update-UiSelection
        $speakerCount = $picker.SpeakerList.Items.Count
        $ticked = @($picker.SpeakerList.CheckedItems | ForEach-Object { [string]$_ })
        $other = [string]$picker.SpeakerList.Items[0]
        $picker.SpeakerList.SetItemChecked(0, $true)
        Save-UiSpeakers
        $picked = @(Get-PickedIds $picker)
        Write-Output ("speakers: {0} listed for VCTK ({1} in the catalogue), ticked {2}; after ticking {3}: {4}" -f
            $speakerCount, $vctk.Tag.Speakers.Count, ($ticked -join ','), $other, ($picked -join ' '))
        $good = $speakerCount -eq $vctk.Tag.Speakers.Count -and ($ticked -join ',') -eq 'p239' -and
            $picked -contains 'en_GB-vctk-medium#p239' -and $picked -contains "en_GB-vctk-medium#$other"
        $picker.Form.Dispose()
        if (-not $good) { Write-Output 'picker: speaker list FAILED'; exit 3 }
        exit 0
    }

    Write-Host "$AppName $Version" -ForegroundColor Cyan
    if ($InPlace) { Write-Host '(from the installed folder)' }
    New-Item $App -ItemType Directory -Force | Out-Null

    # 1. Which voices.
    $installed = @(Get-InstalledIds)
    $usTags = [bool]$UsEnglishTags
    if (-not $UsEnglishTags -and (Test-Path $Settings)) { $usTags = ((Get-ItemProperty $Settings).UsEnglishTags -eq 1) }
    Write-Host "`n[1/5] Voices"
    Write-Host '  getting the voice list ...'
    $catalogue = Get-Catalogue
    Write-Host "  $($catalogue.Count) English voices"
    $byKey = @{}
    foreach ($v in $catalogue) { $byKey[$v.Key] = $v }
    if ($Repair) {
        $wanted = $installed
    } elseif ($Voices.Count -gt 0) {
        $wanted = $Voices
    } elseif ($Unattended) {
        $wanted = if ($installed.Count -gt 0) { $installed } else { @('en_GB-alba-medium', 'en_GB-northern_english_male-medium') }
    } else {
        $picker = New-PickerForm $catalogue $installed $usTags
        Write-Host '  choose voices in the window that opened (it may be behind this one: see the taskbar)'
        $answer = $picker.Form.ShowDialog()
        if ($answer -ne 'OK') { Write-Host '  cancelled: nothing changed'; if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }; exit 0 }
        $wanted = @(Get-PickedIds $picker)
        $usTags = $picker.UsBox.Checked
        $picker.Form.Dispose()
    }
    $wanted = @($wanted | Select-Object -Unique)
    foreach ($id in $wanted) {
        $parts = Split-VoiceId $id
        if (-not $byKey.ContainsKey($parts[0])) { throw "Unknown voice '$id' (not an English medium or high voice in the catalogue)." }
        $v = $byKey[$parts[0]]
        if ($null -ne $parts[1] -and -not $v.Speakers.Contains($parts[1])) { throw "Voice '$($parts[0])' has no speaker '$($parts[1])'." }
        if ($null -eq $parts[1] -and $v.Speakers.Count -gt 1) { throw "Voice '$id' has several speakers: give one as $id#name." }
    }
    if ($wanted.Count -eq 0) { Write-Host '  no voices chosen: the engine stays installed with none (run Choose voices to add some)' -ForegroundColor Yellow }

    # 2. The engine.
    Write-Host "`n[2/5] Engine"
    Install-Engine
    $has32 = Test-Path $Dll32
    Write-Host ("  {0}{1}" -f $Dll64, $(if ($has32) { " and 32-bit" } else { ' (no 32-bit engine in this build)' }))

    # 3. The voices' files: new ones downloaded, ones no longer wanted removed, loudness applied on repair.
    Write-Host "`n[3/5] Voice files"
    New-Item $VoiceRoot -ItemType Directory -Force | Out-Null
    $keys = @($wanted | ForEach-Object { (Split-VoiceId $_)[0] } | Select-Object -Unique)
    foreach ($folder in Get-ChildItem $VoiceRoot -Directory) {
        if ($keys -notcontains $folder.Name) {
            try {
                Remove-Item $folder.FullName -Recurse -Force
                Write-Host "  removed $($folder.Name)"
            } catch {
                Write-Host "  could not remove $($folder.Name) (a program is using it): it goes next time" -ForegroundColor Yellow
            }
        }
    }
    foreach ($key in $keys) {
        $folder = Join-Path $VoiceRoot $key
        if (Test-VoiceFolder $folder) {
            if ($Repair) {
                $loud = Get-PiperLoudness $folder
                if (-not $loud) { $loud = @($DefaultPeak, $DefaultBoost) }
                Set-PiperLoudness $folder $loud[0] $loud[1]
                Write-Host "  $key (loudness: peak $(Format-Number $loud[0]), boost $(Format-Number $loud[1]))"
            } else {
                Write-Host "  $key (installed)"
            }
            continue
        }
        Install-Voice $byKey[$key]
        Write-Host "  $key ready"
    }

    # 4. Registration: the engine for 64- and 32-bit programs, its certificate, the voices.
    Write-Host "`n[4/5] Registering"
    Register-Class $Class64 $Dll64
    if ($has32) { Register-Class $Class32 $Dll32 } elseif (Test-Path $Class32) { Remove-Item $Class32 -Recurse -Force }
    # Programs that only load signed engines (World of Warcraft, for one) need the build's certificate trusted.
    foreach ($store in 'Root', 'TrustedPublisher') {
        & "$env:SystemRoot\System32\certutil.exe" -f -addstore $store $Cer | Out-Null
    }
    Remove-OurTokens $Tokens64
    Remove-OurTokens $Tokens32
    foreach ($id in $wanted) {
        $parts = Split-VoiceId $id
        $v = $byKey[$parts[0]]
        $speakerId = if ($null -ne $parts[1]) { [int]$v.Speakers[$parts[1]] } else { 0 }
        $display = Register-Voice $Tokens64 $id $v $parts[1] $speakerId $usTags
        if ($has32) { [void](Register-Voice $Tokens32 $id $v $parts[1] $speakerId $usTags) }
        Write-Host "  $display"
    }
    New-Item $Settings -Force | Out-Null
    Set-ItemProperty $Settings -Name 'UsEnglishTags' -Value ([int]$usTags) -Type DWord
    Register-Program
    $signature = (Get-AuthenticodeSignature $Dll64).Status

    # 5. Each voice, through Windows' speech API, as programs use it.
    Write-Host "`n[5/5] Testing"
    $allGood = $true
    if ($wanted.Count -gt 0) {
        foreach ($r in (Invoke-SelfTest $has32)) {
            $ok = -not $r['error'] -and [double]$r['seconds'] -gt 0.5 -and [int]$r['words'] -ge 5 -and [string]$r['bookmark'] -eq '7'
            if ($r['none']) { $ok = $false; $r['name'] = '(no voices found)'; $r['error'] = "none listed for $($r['bits'])-bit programs" }
            $text = "  {0,-44} {1}-bit  {2,5:N1} s  {3} words  bookmark {4}" -f $r['name'], $r['bits'], [double]$r['seconds'], $r['words'], $(if ($r['bookmark']) { 'ok' } else { 'missing' })
            if ($r['error']) { $text += "  ERROR: $($r['error'])" }
            Write-Host $text -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
            if (-not $ok) { $allGood = $false }
        }
    }

    Write-Host ''
    if (-not $allGood) {
        Write-Host "Something did not work. The engine's log says why: $env:LOCALAPPDATA\ModernVoices\engine.log" -ForegroundColor Red
        if ($Unattended) { exit 2 }
    } else {
        Write-Host "Done. Installed in $App" -ForegroundColor Green
        Write-Host 'The voices are in every program that uses Windows'' installed voices (restart programs that were open).'
        Write-Host 'Start menu > Modern Voices for Windows: Choose voices, Test voices, Uninstall.'
        if ($signature -ne 'Valid') {
            Write-Host "Note: the engine's signature is '$signature': programs that insist on signed engines (WoW) will not use it." -ForegroundColor Yellow
        }
    }
} catch {
    if ($Unattended) { Write-Host "Stopped: $($_.Exception.Message)`n$($_.ScriptStackTrace)" -ForegroundColor Red; exit 1 }
    Stop-Here "Stopped: $($_.Exception.Message)"
}
if (-not $NoPause -and -not $Unattended) { Read-Host 'Press Enter to close' | Out-Null }
