<#
Modern Voices for Windows - reads the text in a picture with Windows' own text recognition (the one
Photos and the Snipping Tool use). Used by the Reader's "Read from the screen".

  Ocr.ps1 -Image PICTURE -Out FILE    writes the text found (UTF-8) to FILE
  Ocr.ps1 -Check                      says whether this PC has text recognition, and for which language

Exit code: 0 done; 2 Windows has no text recognition installed for the user's languages; 1 it failed
(the reason is in FILE.error).
#>
param([string]$Image, [string]$Out, [switch]$Check)

$ErrorActionPreference = 'Stop'
try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $null = [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
    $null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime]
    $null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
    $null = [Windows.Storage.Streams.RandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]

    # Windows' newer functions answer later: this waits for the answer.
    $asTask = @([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    function Wait-Result($Operation, [Type]$Type) {
        $task = $asTask.MakeGenericMethod($Type).Invoke($null, @($Operation))
        [void]$task.Wait(60000)
        return $task.Result
    }

    $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
    if (-not $engine) {
        if ($Check) { 'no text recognition for this user''s languages' }
        exit 2
    }
    if ($Check) { "text recognition: $($engine.RecognizerLanguage.DisplayName)"; exit 0 }

    $file = Wait-Result ([Windows.Storage.StorageFile]::GetFileFromPathAsync((Resolve-Path $Image).Path)) ([Windows.Storage.StorageFile])
    $stream = Wait-Result ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
    $decoder = Wait-Result ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap = Wait-Result ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result = Wait-Result ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])

    # Lines of one paragraph are joined; a gap between lines larger than most of a line starts a new one.
    $text = New-Object System.Text.StringBuilder
    $lastBottom = $null; $lastHeight = 0
    foreach ($line in $result.Lines) {
        $tops = @($line.Words | ForEach-Object { $_.BoundingRect.Y })
        $bottoms = @($line.Words | ForEach-Object { $_.BoundingRect.Y + $_.BoundingRect.Height })
        $top = ($tops | Measure-Object -Minimum).Minimum
        $bottom = ($bottoms | Measure-Object -Maximum).Maximum
        $height = $bottom - $top
        if ($null -ne $lastBottom) {
            if (($top - $lastBottom) -gt 0.7 * [math]::Max($height, $lastHeight)) { [void]$text.Append("`r`n") } else { [void]$text.Append(' ') }
        }
        [void]$text.Append($line.Text)
        $lastBottom = $bottom; $lastHeight = $height
    }
    [System.IO.File]::WriteAllText($Out, $text.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    exit 0
} catch {
    if ($Out) { [System.IO.File]::WriteAllText("$Out.error", [string]$_.Exception.Message) }
    Write-Output "ERROR: $($_.Exception.Message)"
    exit 1
}
