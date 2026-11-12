<#
Signs the built engines and the Reader, and packages the kit: out\ModernVoicesForWindows<version>\ and its .zip.

Signing: a certificate made here for code signing only signs the DLLs, then its private key is deleted,
so it can never sign anything else (the Spoken Players voicepack's method). Its public part goes in
the kit (engine\modern_voices.cer), for the installer to trust: programs that only load signed speech
engines (World of Warcraft) need that. Run in Windows PowerShell as administrator (the build does).
#>
param(
    [Parameter(Mandatory)][string]$X64,
    [string]$X86,
    [string]$Reader,
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
