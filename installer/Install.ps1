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

$Version = '0.6.0'
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

# --- the manager window ------------------------------------------------------------------------------
# Two tabs: Installed voices (how each sounds: sliders saved to its voice.json as they move, which the
# engine reads while speaking; Preview) and Add or remove voices (the catalogue to tick, with samples,
# sizes and a warning about duplicate qualities). The window's controls and state live in $script:Ui:
# its event handlers run later (while it is shown), in the script's scope.

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

function New-ManagerForm($Catalogue, [string[]]$Installed, [bool]$UsTags, $ByKey) {
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    [System.Windows.Forms.Application]::EnableVisualStyles()

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "$AppName $Version"
    $form.Size = New-Object System.Drawing.Size(880, 730)
    $form.MinimumSize = New-Object System.Drawing.Size(760, 650)
    $form.StartPosition = 'CenterScreen'
    $form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
    # In front of the console it was started from (else it can open behind it, unseen).
    $form.TopMost = $true
    $form.add_Shown({ $script:Ui.Form.Activate(); $script:Ui.Form.TopMost = $false })
    $bold = New-Object System.Drawing.Font($form.Font, [System.Drawing.FontStyle]::Bold)
    $grey = [System.Drawing.Color]::DimGray

    $tabs = New-Control 'TabControl' $form 12 12 840 610 '' 'Top, Bottom, Left, Right'
    $tabInstalled = New-Object System.Windows.Forms.TabPage 'Installed voices'
    $tabAdd = New-Object System.Windows.Forms.TabPage 'Add or remove voices'
    $tabs.TabPages.Add($tabInstalled); $tabs.TabPages.Add($tabAdd)

    # ---- Installed voices
    $installedList = New-Control 'ListView' $tabInstalled 10 10 810 230 '' 'Top, Left, Right'
    $installedList.View = 'Details'; $installedList.FullRowSelect = $true; $installedList.HideSelection = $false
    $installedList.MultiSelect = $false
    [void]$installedList.Columns.Add('Voice', 230)
    [void]$installedList.Columns.Add('Speakers installed', 300)
    [void]$installedList.Columns.Add('On disk', 80)
    [void]$installedList.Columns.Add('Folder', 180)
    $speakersOf = [ordered]@{}
    foreach ($id in $Installed) {
        $idParts = Split-VoiceId $id
        if (-not $speakersOf.Contains($idParts[0])) { $speakersOf[$idParts[0]] = @() }
        if ($null -ne $idParts[1]) { $speakersOf[$idParts[0]] += $idParts[1] }
    }
    foreach ($key in $speakersOf.Keys) {
        $entry = if ($ByKey.ContainsKey($key)) { $ByKey[$key] } else { $null }
        $title = if ($entry) { "$($entry.Display) ($($entry.Accent))" } else { $key }
        $names = @($speakersOf[$key])
        $voiceName = if ($entry) { Get-DisplayName $entry $(if ($names.Count) { $names[0] } else { $null }) } else { $key }
        $dir = Join-Path $VoiceRoot $key
        $item = New-Object System.Windows.Forms.ListViewItem($title)
        [void]$item.SubItems.Add($(if ($names.Count) { $names -join ', ' } else { '1' }))
        [void]$item.SubItems.Add("$(Get-FolderMb $dir) MB")
        [void]$item.SubItems.Add($key)
        $item.Tag = @{ Key = $key; Dir = $dir; Title = $title; Voice = $voiceName; Shared = ($names.Count -gt 1) }
        [void]$installedList.Items.Add($item)
    }

    $soundLabel = New-Control 'Label' $tabInstalled 10 252 810 20 'Select a voice above to set how it sounds.' 'Top, Left, Right'
    $soundLabel.Font = $bold
    $sliders = @{}
    $y = 282
    foreach ($spec in @(@('Volume', 0, 100, 10), @('Boost', 10, 40, 5), @('Speed', 50, 200, 10))) {
        [void](New-Control 'Label' $tabInstalled 10 ($y + 6) 80 20 $spec[0] '')
        $bar = New-Control 'TrackBar' $tabInstalled 90 $y 560 45 '' 'Top, Left, Right'
        $bar.Minimum = $spec[1]; $bar.Maximum = $spec[2]; $bar.TickFrequency = $spec[3]
        $bar.SmallChange = 1; $bar.LargeChange = 5; $bar.Enabled = $false
        $value = New-Control 'Label' $tabInstalled 660 ($y + 6) 160 20 '' 'Top, Right'
        $sliders[$spec[0]] = @{ Bar = $bar; Value = $value }
        $bar.add_ValueChanged({ Update-UiSliderLabels; if (-not $script:Ui.Filling) { $script:Ui.SaveTimer.Stop(); $script:Ui.SaveTimer.Start() } })
        $y += 50
    }
    $explain = New-Control 'Label' $tabInstalled 90 ($y - 2) 730 48 ("Volume: how loud each sentence's loudest moment is (0 = as Piper made it, quieter). " +
        "Boost: lifts the quieter parts more than the loud ones, so the voice sounds louder overall without clipping (higher sounds more " +
        "compressed). Speed: 100% is the voice's own pace.") 'Top, Left, Right'
    $explain.ForeColor = $grey
    $y += 52
    $previewBox = New-Control 'TextBox' $tabInstalled 10 $y 600 24 $PreviewText 'Top, Left, Right'
    $previewButton = New-Control 'Button' $tabInstalled 620 ($y - 1) 95 27 'Preview' 'Top, Right'
    $resetButton = New-Control 'Button' $tabInstalled 725 ($y - 1) 95 27 'Reset' 'Top, Right'
    $previewButton.Enabled = $false; $resetButton.Enabled = $false
    $live = New-Control 'Label' $tabInstalled 10 ($y + 34) 810 36 ("Changes are saved as you move a slider and take effect straight away, also in programs already " +
        "using the voice (from the next thing they say). A voice's speakers share its settings.") 'Top, Left, Right'
    $live.ForeColor = $grey
    if ($installedList.Items.Count -eq 0) {
        $soundLabel.Text = 'No voices installed yet: add some on the Add or remove voices tab.'
    }

    # ---- Add or remove voices
    $intro = New-Control 'Label' $tabAdd 10 8 810 32 ("Tick the voices to install; untick one to remove it. Select a voice and press Play sample to hear it. " +
        "Then press Apply changes.") 'Top, Left, Right'
    $list = New-Control 'ListView' $tabAdd 10 42 810 260 '' 'Top, Bottom, Left, Right'
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
    $speakerLabel = New-Control 'Label' $tabAdd 10 310 810 18 'Select a voice with several speakers to choose which of them to install (each becomes a voice).' 'Bottom, Left, Right'
    $speakerLabel.AutoEllipsis = $true
    $speakerList = New-Control 'CheckedListBox' $tabAdd 10 330 700 104 '' 'Bottom, Left, Right'
    $speakerList.MultiColumn = $true; $speakerList.ColumnWidth = 90; $speakerList.CheckOnClick = $true
    $speakerList.IntegralHeight = $false; $speakerList.Enabled = $false
    $play = New-Control 'Button' $tabAdd 720 330 100 27 'Play sample' 'Bottom, Right'
    $playHint = New-Control 'Label' $tabAdd 720 360 100 60 'plays the voice, or the speaker selected in this list' 'Bottom, Right'
    $playHint.ForeColor = $grey
    $sizeLabel = New-Control 'Label' $tabAdd 10 440 810 20 '' 'Bottom, Left, Right'
    $warnLabel = New-Control 'Label' $tabAdd 10 462 810 36 '' 'Bottom, Left, Right'
    $warnLabel.ForeColor = [System.Drawing.Color]::Firebrick
    $usBox = New-Control 'CheckBox' $tabAdd 10 500 810 22 'Also list British voices as US English (only if a program does not show them)' 'Bottom, Left, Right'
    $usBox.Checked = $UsTags
    $sizeNote = New-Control 'Label' $tabAdd 10 524 810 34 ("Speakers of one voice share its model, so extra speakers take no extra space. Two qualities of the same voice " +
        "(medium and high) are separate models: each takes its full size.") 'Bottom, Left, Right'
    $sizeNote.ForeColor = $grey

    # ---- bottom of the window
    $note = New-Control 'Label' $form 12 634 600 34 ('Some programs (World of Warcraft, for one) take longer to start the more voices Windows has: ' +
        'keep the list to the ones you use.') 'Bottom, Left, Right'
    $note.ForeColor = $grey
    $apply = New-Control 'Button' $form 632 640 110 30 'Apply changes' 'Bottom, Right'
    $apply.DialogResult = 'OK'
    $close = New-Control 'Button' $form 752 640 100 30 'Close' 'Bottom, Right'
    $close.DialogResult = 'Cancel'
    $form.CancelButton = $close

    $timer = New-Object System.Windows.Forms.Timer
    $timer.Interval = 350
    $timer.add_Tick({ $script:Ui.SaveTimer.Stop(); Save-UiSettings })

    $script:Ui = @{ Form = $form; Tabs = $tabs; List = $list; SpeakerLabel = $speakerLabel; SpeakerList = $speakerList; UsBox = $usBox
        Speakers = $chosenSpeakers; Current = $null; Player = $null; Filling = $false; InstalledKeys = @($speakersOf.Keys)
        InstalledList = $installedList; SoundLabel = $soundLabel; Sliders = $sliders; PreviewBox = $previewBox
        PreviewButton = $previewButton; ResetButton = $resetButton; CurrentInstalled = $null; SaveTimer = $timer
        PreviewProcess = $null; SizeLabel = $sizeLabel; WarnLabel = $warnLabel }
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
    $resetButton.add_Click({ Reset-UiSettings })
    $form.add_FormClosing({
        Save-UiSpeakers
        if ($script:Ui.SaveTimer.Enabled) { $script:Ui.SaveTimer.Stop(); Save-UiSettings }
        if ($script:Ui.Player) { $script:Ui.Player.Stop() }
        Stop-UiPreview
    })
    Update-UiSummary
    if ($installedList.Items.Count -gt 0) { $installedList.Items[0].Selected = $true; Update-UiInstalled }
    else { $tabs.SelectedTab = $tabAdd }
    return $script:Ui
}

function Update-UiSliderLabels {
    $s = $script:Ui.Sliders
    $volume = $s.Volume.Bar.Value
    $s.Volume.Value.Text = if ($volume -eq 0) { 'off (as Piper made it)' } else { "$volume%" }
    $boost = $s.Boost.Bar.Value / 10.0
    $s.Boost.Value.Text = if ($boost -le 1.0) { 'none' } else { $boost.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) + 'x' }
    $s.Speed.Value.Text = "$($s.Speed.Bar.Value)%"
}

function Update-UiInstalled {
    $ui = $script:Ui
    if ($ui.SaveTimer.Enabled) { $ui.SaveTimer.Stop(); Save-UiSettings }
    $ui.CurrentInstalled = $null
    $on = $ui.InstalledList.SelectedItems.Count -gt 0
    foreach ($k in 'Volume', 'Boost', 'Speed') { $ui.Sliders[$k].Bar.Enabled = $on }
    $ui.PreviewButton.Enabled = $on; $ui.ResetButton.Enabled = $on
    if (-not $on) { return }
    $tag = $ui.InstalledList.SelectedItems[0].Tag
    $settings = Get-VoiceSettings $tag.Dir
    $peak = $DefaultPeak; $boost = $DefaultBoost; $lengthScale = 1.0
    if ($settings) {
        if ($null -ne $settings.Peak) { $peak = $settings.Peak }
        if ($null -ne $settings.Boost) { $boost = $settings.Boost }
        if ($null -ne $settings.LengthScale) { $lengthScale = $settings.LengthScale }
    }
    $ui.Filling = $true
    try {
        $ui.Sliders.Volume.Bar.Value = [int][math]::Max(0, [math]::Min(100, [math]::Round($peak * 100)))
        $ui.Sliders.Boost.Bar.Value = [int][math]::Max(10, [math]::Min(40, [math]::Round($boost * 10)))
        $ui.Sliders.Speed.Bar.Value = [int][math]::Max(50, [math]::Min(200, [math]::Round(100 / $lengthScale)))
        Update-UiSliderLabels
    } finally {
        $ui.Filling = $false
    }
    $ui.CurrentInstalled = $tag
    $ui.SoundLabel.Text = "How $($tag.Title) sounds" + $(if ($tag.Shared) { ' (all its speakers)' } else { '' })
}

function Save-UiSettings {
    $ui = $script:Ui
    $tag = $ui.CurrentInstalled
    if (-not $tag -or -not (Test-Path (Join-Path $tag.Dir 'voice.json'))) { return }
    $peak = $ui.Sliders.Volume.Bar.Value / 100.0
    $boost = $ui.Sliders.Boost.Bar.Value / 10.0
    $lengthScale = [math]::Round(100.0 / $ui.Sliders.Speed.Bar.Value, 3)
    try {
        Set-VoiceSettings $tag.Dir $peak $boost $lengthScale
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
    $display = Get-DisplayName $Entry $SpeakerName
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
    foreach ($old in (Join-Path $App 'Choose voices.cmd'), (Join-Path $StartMenu 'Choose voices.lnk')) {   # 0.5's names
        if (Test-Path $old) { Remove-Item $old -Force }
    }
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

# Voices installed before 0.6 have their loudness inside their models: the engine applies it now.
function Update-OldVoices {
    if (-not (Test-Path $VoiceRoot)) { return }
    foreach ($folder in Get-ChildItem $VoiceRoot -Directory) {
        if (-not (Test-VoiceFolder $folder.FullName)) { continue }
        if (Convert-ToEngineLoudness $folder.FullName $DefaultPeak $DefaultBoost) {
            Write-Host "  $($folder.Name): its loudness is applied by the engine now (done once)"
        }
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

    Write-Host "`n  voice files"
    New-Item $VoiceRoot -ItemType Directory -Force | Out-Null
    $keys = @($Wanted | ForEach-Object { (Split-VoiceId $_)[0] } | Select-Object -Unique)
    foreach ($folder in Get-ChildItem $VoiceRoot -Directory) {
        if ($keys -notcontains $folder.Name) {
            try {
                Remove-Item $folder.FullName -Recurse -Force
                Write-Host "    removed $($folder.Name)"
            } catch {
                Write-Host "    could not remove $($folder.Name) (a program is using it): it goes next time" -ForegroundColor Yellow
            }
        }
    }
    foreach ($key in $keys) {
        $folder = Join-Path $VoiceRoot $key
        if (Test-VoiceFolder $folder) { Write-Host "    $key (installed)"; continue }
        Install-Voice $ByKey[$key]
        Write-Host "    $key ready"
    }

    Write-Host "`n  registering"
    Remove-OurTokens $Tokens64
    Remove-OurTokens $Tokens32
    foreach ($id in $Wanted) {
        $idParts = Split-VoiceId $id
        $entry = $ByKey[$idParts[0]]
        $speakerId = if ($null -ne $idParts[1]) { [int]$entry.Speakers[$idParts[1]] } else { 0 }
        $display = Register-Voice $Tokens64 $id $entry $idParts[1] $speakerId $UsTags
        if ($has32) { [void](Register-Voice $Tokens32 $id $entry $idParts[1] $speakerId $UsTags) }
        Write-Host "    $display"
    }
    New-Item $Settings -Force | Out-Null
    Set-ItemProperty $Settings -Name 'UsEnglishTags' -Value ([int]$UsTags) -Type DWord
    Register-Program

    $allGood = $true
    if ($Wanted.Count -gt 0) {
        Write-Host "`n  testing each voice through Windows' speech API"
        foreach ($r in (Invoke-SelfTest $has32)) {
            $ok = -not $r['error'] -and [double]$r['seconds'] -gt 0.5 -and [int]$r['words'] -ge 5 -and [string]$r['bookmark'] -eq '7'
            if ($r['none']) { $ok = $false; $r['name'] = '(no voices found)'; $r['error'] = "none listed for $($r['bits'])-bit programs" }
            $line = "    {0,-44} {1}-bit  {2,5:N1} s  {3} words  bookmark {4}" -f $r['name'], $r['bits'], [double]$r['seconds'], $r['words'], $(if ($r['bookmark']) { 'ok' } else { 'missing' })
            if ($r['error']) { $line += "  ERROR: $($r['error'])" }
            Write-Host $line -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
            if (-not $ok) { $allGood = $false }
        }
    }
    return $allGood
}

# --- the steps --------------------------------------------------------------------------------------

if ($FunctionsOnly) { return }

try {
    if ($PickerSelfTest) {
        # The window built (not shown) and driven as clicks would: the build's test of it.
        $catalogue = Get-Catalogue
        $byKey = @{}; foreach ($v in $catalogue) { $byKey[$v.Key] = $v }
        $real = @(Get-InstalledIds)
        $ids = if ($real.Count) { $real } else { @('en_GB-alba-medium', 'en_GB-vctk-medium#p239') }
        $ui = New-ManagerForm $catalogue $ids $false $byKey
        $null = $ui.Form.Handle; $null = $ui.List.Handle; $null = $ui.InstalledList.Handle
        $bad = @()
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
            if ($now.Peak -ne 0.6 -or $now.Boost -ne 2 -or $now.LengthScale -ne 1.25 -or -not $now.Engine) { $bad += 'sliders' }
            Set-VoiceSettings $dir $was.Peak $was.Boost $was.LengthScale
        }
        $ui.Form.Dispose()
        if ($bad.Count) { Write-Output "window: FAILED ($($bad -join ', '))"; exit 3 }
        Write-Output 'window: ok'
        exit 0
    }

    Write-Host "$AppName $Version" -ForegroundColor Cyan
    if ($InPlace) { Write-Host '(from the installed folder)' }
    New-Item $App -ItemType Directory -Force | Out-Null
    $usTags = [bool]$UsEnglishTags
    if (-not $UsEnglishTags -and (Test-Path $Settings)) { $usTags = ((Get-ItemProperty $Settings).UsEnglishTags -eq 1) }

    Write-Host "`nEngine"
    Install-AndRegisterEngine
    Register-Program
    Write-Host ("  {0}{1}" -f $Dll64, $(if (Test-Path $Dll32) { ' and 32-bit' } else { ' (no 32-bit engine in this build)' }))
    Update-OldVoices

    Write-Host "`nVoice list"
    $catalogue = Get-Catalogue
    Write-Host "  $($catalogue.Count) English voices"
    $byKey = @{}
    foreach ($v in $catalogue) { $byKey[$v.Key] = $v }

    $interactive = -not ($Repair -or $Voices.Count -gt 0 -or $Unattended)
    $allGood = $true
    while ($true) {
        $installed = @(Get-InstalledIds)
        if ($Repair) {
            $wanted = $installed
        } elseif ($Voices.Count -gt 0) {
            $wanted = $Voices
        } elseif ($Unattended) {
            $wanted = if ($installed.Count -gt 0) { $installed } else { @('en_GB-alba-medium', 'en_GB-northern_english_male-medium') }
        } else {
            $ui = New-ManagerForm $catalogue $installed $usTags $byKey
            Write-Host "`nThe Modern Voices window is open (if you can't see it, it's on the taskbar)."
            $answer = $ui.Form.ShowDialog()
            $wanted = @(Get-PickedIds $ui)
            $newTags = $ui.UsBox.Checked
            $ui.Form.Dispose()
            if ($answer -ne 'OK') { break }
            $same = ((@($wanted | Sort-Object) -join '|') -eq (@($installed | Sort-Object) -join '|')) -and ($newTags -eq $usTags)
            $usTags = $newTags
            if ($same) { continue }
        }
        Write-Host "`nApplying"
        $allGood = Set-InstalledVoices @($wanted | Select-Object -Unique) $usTags $byKey
        if (-not $interactive) { break }
        if (-not $allGood) {
            Add-Type -AssemblyName System.Windows.Forms
            [void][System.Windows.Forms.MessageBox]::Show("A voice failed its test: see the window behind this one. The engine's log says why: " +
                "$env:LOCALAPPDATA\ModernVoices\engine.log", $AppName)
        }
    }

    Write-Host ''
    if (-not $allGood) {
        Write-Host "Something did not work. The engine's log says why: $env:LOCALAPPDATA\ModernVoices\engine.log" -ForegroundColor Red
        if ($Unattended) { exit 2 }
    } else {
        Write-Host "Done. Installed in $App" -ForegroundColor Green
        if (-not $interactive) {
            Write-Host 'The voices are in every program that uses Windows'' installed voices (restart programs that were open).'
            Write-Host 'Start menu > Modern Voices for Windows: Modern Voices, Test voices, Repair, Uninstall.'
        }
        $signature = (Get-AuthenticodeSignature $Dll64).Status
        if ($signature -ne 'Valid') {
            Write-Host "Note: the engine's signature is '$signature': programs that insist on signed engines (WoW) will not use it." -ForegroundColor Yellow
        }
    }
} catch {
    if ($Unattended) { Write-Host "Stopped: $($_.Exception.Message)`n$($_.ScriptStackTrace)" -ForegroundColor Red; exit 1 }
    Stop-Here "Stopped: $($_.Exception.Message)"
}
if (-not $NoPause -and -not $Unattended -and -not $interactive) { Read-Host 'Press Enter to close' | Out-Null }
