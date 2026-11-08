@echo off
rem CharmCLI (S.I.V.A.): makes the release zip dist\CharmCLI-<version>-win-x64.zip (+ .sha256): self-contained, WITHOUT Oodle.
rem Log: siva_tools\publish_log.txt
cd /d "%~dp0"
echo Publishing CharmCLI (a few minutes; the first time it downloads the .NET runtime packs)...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish_charmcli.ps1" > "%~dp0publish_log.txt" 2>&1
if errorlevel 1 (echo FAILED - send siva_tools\publish_log.txt to Claude.) else (echo Done - see siva_tools\publish_log.txt)
echo.
pause
