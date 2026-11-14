<#
Builds Modern Voices Config (ModernVoicesConfig.exe in -Out) with the C# compiler that is part of Windows'
.NET Framework 4: nothing to install. -Version sets the program's version. Its icon is the cog in a speech
bubble (installer\manager.ico, which tools\make_icons.py draws).
#>
param([string]$Out = 'built\config', [string]$Version = '0.0.0')

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path $csc)) { throw "No C# compiler at $csc." }
New-Item $Out -ItemType Directory -Force | Out-Null
$source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Config.cs'), [System.Text.Encoding]::UTF8)
$source = [regex]::Replace($source, 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$Version.0`")")
$stamped = Join-Path (Resolve-Path $Out) 'Config.stamped.cs'
[System.IO.File]::WriteAllText($stamped, $source, (New-Object System.Text.UTF8Encoding($true)))
$exe = Join-Path (Resolve-Path $Out) 'ModernVoicesConfig.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 "/out:$exe" "/win32icon:$(Join-Path $PSScriptRoot '..\installer\manager.ico')" `
    "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')" "/lib:$framework,$framework\WPF" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Speech.dll /r:System.Web.Extensions.dll `
    /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll $stamped
if ($LASTEXITCODE -ne 0) { throw "Modern Voices Config did not compile (csc exit code $LASTEXITCODE)." }
Remove-Item $stamped
Write-Host "built $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)"
