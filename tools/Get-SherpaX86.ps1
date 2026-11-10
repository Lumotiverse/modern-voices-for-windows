<#
The sherpa-onnx static libraries for 32-bit Windows (i686-pc-windows-msvc), which the sherpa-onnx crate
does not download itself: from sherpa-onnx's release if it publishes them, else built from source.
Leaves them in <Out>\lib, for SHERPA_ONNX_LIB_DIR.
#>
param(
    [string]$Version = '1.13.8',
    [Parameter(Mandatory)][string]$Out
)

$ErrorActionPreference = 'Stop'
$needed = 'sherpa-onnx-c-api', 'sherpa-onnx-core', 'kaldi-decoder-core', 'sherpa-onnx-kaldifst-core', 'sherpa-onnx-fstfar',
    'sherpa-onnx-fst', 'kaldi-native-fbank-core', 'kissfft-float', 'piper_phonemize', 'espeak-ng', 'ucd', 'onnxruntime',
    'ssentencepiece_core'
$lib = Join-Path $Out 'lib'

function Test-Libs([string]$Dir) {
    $missing = @($needed | Where-Object { -not (Test-Path (Join-Path $Dir "$_.lib")) })
    return $missing
}

if ((Test-Path $lib) -and (Test-Libs $lib).Count -eq 0) { Write-Host "cached: $lib"; return }
New-Item $Out -ItemType Directory -Force | Out-Null

# 1. A published package.
$headers = @{ 'User-Agent' = 'modern-voices-build' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }
$release = Invoke-RestMethod "https://api.github.com/repos/k2-fsa/sherpa-onnx/releases/tags/v$Version" -Headers $headers
$assets = @($release.assets | Where-Object { $_.name -match 'win-x86' -and $_.name -match 'static' -and $_.name -match '\.tar\.bz2$' })
Write-Host "32-bit static packages in v${Version}: $(@($assets | ForEach-Object name) -join ', ')"
$asset = $assets | Sort-Object { if ($_.name -match 'MT-Release-lib') { 0 } elseif ($_.name -match 'MT') { 1 } else { 2 } } | Select-Object -First 1
if ($asset) {
    $archive = Join-Path $Out $asset.name
    Invoke-WebRequest $asset.browser_download_url -OutFile $archive
    $unpacked = Join-Path $Out 'unpacked'
    New-Item $unpacked -ItemType Directory -Force | Out-Null
    tar -xjf $archive -C $unpacked
    $api = Get-ChildItem $unpacked -Recurse -Filter 'sherpa-onnx-c-api.lib' | Select-Object -First 1
    if ($api) {
        $missing = @(Test-Libs $api.DirectoryName)
        if ($missing.Count -eq 0) {
            New-Item $lib -ItemType Directory -Force | Out-Null
            Copy-Item (Join-Path $api.DirectoryName '*.lib') $lib
            Write-Host "from $($asset.name): $lib"
            return
        }
        Write-Host "$($asset.name) lacks: $($missing -join ', '); building from source instead" -ForegroundColor Yellow
    }
}

# 2. From source (static, /MT, 32-bit).
$src = Join-Path $Out 'src'
if (-not (Test-Path $src)) { git clone --depth 1 --branch "v$Version" https://github.com/k2-fsa/sherpa-onnx.git $src }
$build = Join-Path $Out 'build'
$prefix = Join-Path $Out 'install'
cmake -S $src -B $build -A Win32 -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF `
    -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DSHERPA_ONNX_ENABLE_PORTAUDIO=OFF -DSHERPA_ONNX_ENABLE_WEBSOCKET=OFF `
    -DSHERPA_ONNX_ENABLE_BINARY=OFF -DSHERPA_ONNX_ENABLE_PYTHON=OFF -DSHERPA_ONNX_ENABLE_TESTS=OFF `
    -DSHERPA_ONNX_ENABLE_CHECK=OFF -DSHERPA_ONNX_ENABLE_TTS=ON -DSHERPA_ONNX_ENABLE_C_API=ON `
    -DSHERPA_ONNX_ENABLE_SPEAKER_DIARIZATION=OFF "-DCMAKE_INSTALL_PREFIX=$prefix"
if ($LASTEXITCODE) { throw 'cmake configure failed' }
cmake --build $build --config Release --target install -- /m
if ($LASTEXITCODE) { throw 'cmake build failed' }
$found = Get-ChildItem $prefix, $build -Recurse -Filter '*.lib' | Where-Object { $needed -contains $_.BaseName }
New-Item $lib -ItemType Directory -Force | Out-Null
foreach ($f in $found) { Copy-Item $f.FullName (Join-Path $lib $f.Name) -Force }
$missing = @(Test-Libs $lib)
if ($missing.Count -gt 0) {
    Write-Host 'libraries built:'; Get-ChildItem $prefix, $build -Recurse -Filter '*.lib' | ForEach-Object { "  $($_.FullName)" }
    throw "missing after the build: $($missing -join ', ')"
}
Write-Host "built from source: $lib"
