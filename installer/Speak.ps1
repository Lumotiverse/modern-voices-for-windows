# Modern Voices for Windows: says a line with one voice, through Windows' speech API (System.Speech),
# as any program would. The manager's Preview runs this in its own PowerShell.
param([Parameter(Mandatory)][string]$Voice, [string]$TextBase64)

Add-Type -AssemblyName System.Speech
$text = 'Speaking a preview of the selected voice, to hear how natural it sounds. Change the speed and pause values to adjust the sound to your liking.'
if ($TextBase64) { $text = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($TextBase64)) }
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice($Voice)
$synth.SetOutputToDefaultAudioDevice()
$synth.Speak($text)
$synth.Dispose()
