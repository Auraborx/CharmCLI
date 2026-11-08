@echo off
rem CharmCLI (S.I.V.A.): speed test on a map that was NEVER exported by CharmCLI before (its shaders are not cached yet):
rem Nessus - Exodus Black. No build (uses the last build). Log: siva_tools\cold_log.txt
set CHARM_DIR=
if exist "%~dp0charm_dir.txt" set /p CHARM_DIR=<"%~dp0charm_dir.txt"
if "%CHARM_DIR%"=="" set /p CHARM_DIR=Folder of your normal Charm.exe (the one with config.json): 
> "%~dp0charm_dir.txt" echo %CHARM_DIR%
cd /d "%~dp0.."
set N=1
:next
if exist "%~dp0export_cold_%N%" (set /a N+=1 & goto next)
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
if "%EXE%"=="" (echo Build first: 2_build.bat & goto end)
echo Exporting Nessus - Exodus Black into siva_tools\export_cold_%N% ...
echo Started %time%
"%EXE%" -siva export -activity 80EE155F -bubble 80EE05A4 -out "%~dp0export_cold_%N%" -textures png -charmconfig "%CHARM_DIR%\config.json" > "%~dp0cold_log.txt" 2>&1
echo Finished %time%  -  exit code %errorlevel%. Send siva_tools\cold_log.txt to Claude.
:end
echo.
pause
