<#
Signs the built engines, Modern Voices Config and the Reader, and packages the kit: out\ModernVoicesForWindows<version>\ and its .zip.

Signing: a certificate made here for code signing only signs the DLLs, then its private key is deleted,
so it can never sign anything else (the Spoken Players voicepack's method). Its public part goes in
the kit (engine\modern_voices.cer), for the installer to trust: programs that only load signed speech
engines (World of Warcraft) need that. Run in Windows PowerShell as administrator (the build does).
#>
param(
    [Parameter(Mandatory)][string]$X64,
    [string]$X86,
    [string]$Reader,
    [string]$Config,
    [string]$Out = 'out'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$cargo = Get-Content (Join-Path $repo 'engine\Cargo.toml') -Raw
$version = [regex]::Match($cargo, '(?m)^version\s*=\s*"([^"]+)"').Groups[1].Value
if (-not $version) { throw 'No version in engine\Cargo.toml.' }
$installer = Get-Content (Join-Path $repo 'installer\Install.ps1') -Raw
if ($installer -notmatch "\`$Version = '$([regex]::Escape($version))'") { throw "installer\Install.ps1 is not version $version." }

$name = "ModernVoicesForWindows$version"
New-Item $Out -ItemType Directory -Force | Out-Null
$kit = Join-Path (Resolve-Path $Out) $name
if (Test-Path $kit) { Remove-Item $kit -Recurse -Force }
New-Item "$kit\engine\x64", "$kit\installer" -ItemType Directory -Force | Out-Null
Copy-Item $X64 "$kit\engine\x64\modern_voices.dll"
$dlls = @("$kit\engine\x64\modern_voices.dll")
if ($X86 -and (Test-Path $X86)) {
    New-Item "$kit\engine\x86" -ItemType Directory -Force | Out-Null
    Copy-Item $X86 "$kit\engine\x86\modern_voices.dll"
    $dlls += "$kit\engine\x86\modern_voices.dll"
} else {
    Write-Host 'no 32-bit engine: this kit is 64-bit only' -ForegroundColor Yellow
}
if ($Reader -and (Test-Path $Reader)) {
    New-Item "$kit\reader" -ItemType Directory -Force | Out-Null
    Copy-Item $Reader "$kit\reader\ModernVoicesReader.exe"
    $dlls += "$kit\reader\ModernVoicesReader.exe"
} else {
    throw 'The Reader (reader\ModernVoicesReader.exe) was not built.'
}
# The Reader's two outside libraries, fetched for the kit (not kept in this repository) and checked
# against the SHA-256 each had when it was chosen: a file that differs stops the build.
#  - PdfPig 0.1.8 (Apache-2.0, https://github.com/UglyToad/PdfPig): reads the text of PDFs. Its .NET
#    Framework 4.7 DLLs, from the project's own release.
#  - LAME 3.100 (LGPL, https://lame.sourceforge.io): makes MP3s. libmp3lame as built by RareWares, the
#    copies the NAudio.Lame project keeps (https://github.com/Corey-M/NAudio.Lame, tag v2.1.0).
function Get-Checked([string]$Url, [string]$Sha256, [string]$Into) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    for ($attempt = 1; ; $attempt++) {
        try { Invoke-WebRequest $Url -OutFile $Into -UseBasicParsing; break }
        catch { if ($attempt -ge 3) { throw "Could not download $Url ($($_.Exception.Message))." }; Start-Sleep -Seconds 5 }
    }
    $got = (Get-FileHash $Into -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($got -ne $Sha256) { Remove-Item $Into -Force; throw "$Url is not the file expected (SHA-256 $got, expected $Sha256)." }
}
$fetched = Join-Path (Resolve-Path $Out) 'fetched'
New-Item $fetched -ItemType Directory -Force | Out-Null
$nupkg = Join-Path $fetched 'PdfPig.0.1.8.nupkg'
Get-Checked 'https://github.com/UglyToad/PdfPig/releases/download/v0.1.8/PdfPig.0.1.8.nupkg' '7bf349f7b57d9d91a2fff568bb7828ffc66ff6a65174abbee2c10d7b4d10566c' $nupkg
Add-Type -AssemblyName System.IO.Compression.FileSystem
$package = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
try {
    foreach ($part in 'UglyToad.PdfPig', 'UglyToad.PdfPig.Core', 'UglyToad.PdfPig.Fonts', 'UglyToad.PdfPig.Tokens', 'UglyToad.PdfPig.Tokenization', 'UglyToad.PdfPig.DocumentLayoutAnalysis') {
        $inside = $package.GetEntry("lib/net47/$part.dll")
        if (-not $inside) { throw "PdfPig's package has no lib/net47/$part.dll." }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($inside, "$kit\reader\$part.dll", $true)
    }
} finally { $package.Dispose() }
Get-Checked 'https://raw.githubusercontent.com/Corey-M/NAudio.Lame/v2.1.0/NAudio.Lame/libmp3lame.64.dll' '71dba147b6c16d8d0f012b8e657d52e12cb8796a659e1151200d0a6c8b04bce3' "$kit\reader\libmp3lame.64.dll"
Get-Checked 'https://raw.githubusercontent.com/Corey-M/NAudio.Lame/v2.1.0/NAudio.Lame/libmp3lame.32.dll' '91d3c0cc833265066c51408d940faaf76c5b8fc5bda854540a9cc6c88b1d1f20' "$kit\reader\libmp3lame.32.dll"
Write-Host "the Reader's libraries: $((Get-ChildItem "$kit\reader\*.dll").Count) DLLs, $([math]::Round(((Get-ChildItem "$kit\reader\*.dll" | Measure-Object Length -Sum).Sum) / 1MB, 1)) MB"

if ($Config -and (Test-Path $Config)) {
    New-Item "$kit\config" -ItemType Directory -Force | Out-Null
    Copy-Item $Config "$kit\config\ModernVoicesConfig.exe"
    $dlls += "$kit\config\ModernVoicesConfig.exe"
} else {
    throw 'Modern Voices Config (config\ModernVoicesConfig.exe) was not built.'
}
Copy-Item (Join-Path $repo 'engine\espeak-ng-data') "$kit\engine\espeak-ng-data" -Recurse
Copy-Item (Join-Path $repo 'installer\*.ps1') "$kit\installer"
Copy-Item (Join-Path $repo 'installer\*.ico') "$kit\installer"
Copy-Item (Join-Path $repo 'kit\*') $kit
Copy-Item (Join-Path $repo 'LICENSE'), (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $kit
Copy-Item (Join-Path $repo 'licenses') "$kit\licenses" -Recurse

# --- sign ---
$subject = "CN=Modern Voices for Windows (self-built $version)"
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyExportPolicy NonExportable -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(20)
$public = "$kit\engine\modern_voices.cer"
Export-Certificate -Cert $cert -FilePath $public | Out-Null
foreach ($store in 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\TrustedPublisher') { Import-Certificate -FilePath $public -CertStoreLocation $store | Out-Null }
foreach ($dll in $dlls) {
    $signed = $null
    try { $signed = Set-AuthenticodeSignature -FilePath $dll -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com' }
    catch { Write-Host "timestamping failed ($($_.Exception.Message)); signing without" -ForegroundColor Yellow }
    if (-not $signed -or $signed.Status -ne 'Valid') { $signed = Set-AuthenticodeSignature -FilePath $dll -Certificate $cert -HashAlgorithm SHA256 }
    Write-Host "signed $dll : $($signed.Status)"
    if ($signed.Status -ne 'Valid') { throw "signing $dll failed: $($signed.StatusMessage)" }
}
Remove-Item "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey
# Untrusted again here, so the installer's own trusting is what the tests rely on.
foreach ($store in 'Root', 'TrustedPublisher') { & certutil.exe -delstore $store $cert.Thumbprint | Out-Null }

$zip = Join-Path (Resolve-Path $Out) "$name.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $kit -DestinationPath $zip
Write-Host "kit: $kit"
Write-Host "zip: $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
# (No Out-File: Windows PowerShell's utf8 starts the file with a byte order mark.)
if ($env:GITHUB_OUTPUT) { [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "name=$name`n") }
