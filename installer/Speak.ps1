# Modern Voices for Windows: says a line with one voice, through Windows' speech API (System.Speech),
# as any program would. The manager's Preview runs this in its own PowerShell.
param([Parameter(Mandatory)][string]$Voice, [string]$TextBase64)

Add-Type -AssemblyName System.Speech
$text = 'Pulling in three. Watch the patrol on the left, it has a skull!'
if ($TextBase64) { $text = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($TextBase64)) }
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice($Voice)
$synth.SetOutputToDefaultAudioDevice()
$synth.Speak($text)
$synth.Dispose()
