<#
Modern Voices for Windows - uninstaller. Removes the voices (64- and 32-bit), the engine's
registration and its certificate's trust, the Start menu folder, the Installed apps entry and the
program's folder. Nothing else is touched.
#>
param([switch]$NoPause)

$ErrorActionPreference = 'Stop'
Set-Location $env:TEMP   # out of the folder being deleted
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $forward = "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`""
    if ($NoPause) { $forward += ' -NoPause' }
    Start-Process powershell.exe -Verb RunAs -WorkingDirectory $env:TEMP -ArgumentList $forward
    exit
}

$AppName = 'Modern Voices for Windows'
$Clsid = '{F9857585-FF01-467B-84C0-D6CA4E0572D2}'
# Where it is installed: Installed apps' entry says; else the folder this script is in, if it is the
# program's. Only a folder named Modern Voices for Windows with the engine in it is ever deleted.
$App = $null
$entry = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ModernVoicesForWindows'
if (Test-Path $entry) { $App = (Get-ItemProperty $entry).InstallLocation }
if (-not $App) { $App = Split-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) -Parent }
$App = $App.TrimEnd('\')
$isOurs = ((Split-Path $App -Leaf) -eq $AppName) -and (Test-Path (Join-Path $App 'engine'))
$Cer = Join-Path $App 'engine\modern_voices.cer'

Write-Host "Uninstalling $AppName" -ForegroundColor Cyan
try {
    foreach ($tokens in 'HKLM:\SOFTWARE\Microsoft\Speech\Voices\Tokens', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Speech\Voices\Tokens') {
        if (-not (Test-Path $tokens)) { continue }
        foreach ($key in Get-ChildItem $tokens) {
            if ((Get-ItemProperty $key.PSPath).ModernVoice -eq 1) {
                Remove-Item $key.PSPath -Recurse -Force
                Write-Host "removed voice $($key.PSChildName)"
            }
        }
    }
    foreach ($class in "HKLM:\SOFTWARE\Classes\CLSID\$Clsid", "HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID\$Clsid") {
        if (Test-Path $class) { Remove-Item $class -Recurse -Force }
    }
    Write-Host 'engine registration removed'
    if (Test-Path $Cer) {
        $thumbprint = (New-Object Security.Cryptography.X509Certificates.X509Certificate2 $Cer).Thumbprint
        foreach ($store in 'Root', 'TrustedPublisher') {
            & "$env:SystemRoot\System32\certutil.exe" -delstore $store $thumbprint | Out-Null
        }
        Write-Host 'engine certificate no longer trusted'
    }
    foreach ($key in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ModernVoicesForWindows', 'HKLM:\SOFTWARE\Modern Voices for Windows') {
        if (Test-Path $key) { Remove-Item $key -Recurse -Force }
    }
    $menu = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\$AppName"
    if (Test-Path $menu) { Remove-Item $menu -Recurse -Force }
    if (-not $isOurs) {
        Write-Host "Not deleting $App (it isn't the program's folder)." -ForegroundColor Yellow
    } elseif (Test-Path $App) {
        Remove-Item $App -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $App) {
            Write-Host "Could not remove all of ${App}: a program still has the engine loaded. Close programs that speak (or restart), then delete the folder." -ForegroundColor Yellow
        } else {
            Write-Host "removed $App"
        }
    }
    Write-Host 'Done.' -ForegroundColor Green
} catch {
    Write-Host "Stopped: $($_.Exception.Message)" -ForegroundColor Red
}
if (-not $NoPause) { Read-Host 'Press Enter to close' | Out-Null }
