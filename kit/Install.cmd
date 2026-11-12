@echo off
echo Loading program...
set MV_LOADING_SAID=1
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0installer\Install.ps1"
