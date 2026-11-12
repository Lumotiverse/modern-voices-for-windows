<#
Builds Modern Voices: Reader (reader\ModernVoicesReader.exe in -Out) with the C# compiler that is part
of Windows' .NET Framework 4: nothing to install. -Version sets the program's version.
#>
param([string]$Out = 'built\reader', [string]$Version = '0.0.0')

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'
if (-not (Test-Path $csc)) { throw "No C# compiler at $csc." }
New-Item $Out -ItemType Directory -Force | Out-Null
$source = [System.IO.File]::ReadAllText((Join-Path $PSScriptRoot 'Reader.cs'), [System.Text.Encoding]::UTF8)
$source = [regex]::Replace($source, 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$Version.0`")")
$stamped = Join-Path (Resolve-Path $Out) 'Reader.stamped.cs'
[System.IO.File]::WriteAllText($stamped, $source, (New-Object System.Text.UTF8Encoding($true)))
$exe = Join-Path (Resolve-Path $Out) 'ModernVoicesReader.exe'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 "/out:$exe" "/lib:$framework,$framework\WPF" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Speech.dll /r:System.Web.Extensions.dll /r:System.Xml.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll $stamped
if ($LASTEXITCODE -ne 0) { throw "The Reader did not compile (csc exit code $LASTEXITCODE)." }
Remove-Item $stamped
Write-Host "built $exe ($([math]::Round((Get-Item $exe).Length / 1KB)) KB)"
