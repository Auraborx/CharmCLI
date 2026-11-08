@echo off
rem CharmCLI (S.I.V.A.): builds, then dumps what the game stores about bubbles (research: how bubbles connect):
rem   bubbles_hellas.json  - Hellas Basin (Mars) with every map entry (positions, types)
rem   bubbles_all.json     - all activities, bubble tables only
set CHARM_DIR=
if exist "%~dp0charm_dir.txt" set /p CHARM_DIR=<"%~dp0charm_dir.txt"
if "%CHARM_DIR%"=="" set /p CHARM_DIR=Folder of your normal Charm.exe (the one with config.json): 
> "%~dp0charm_dir.txt" echo %CHARM_DIR%
cd /d "%~dp0.."
echo Building CharmCLI...
dotnet build Charm.sln -c Release -p:Platform=x64 > siva_tools\build_log.txt 2>&1
if errorlevel 1 (echo BUILD FAILED - send siva_tools\build_log.txt to Claude. & goto end)
echo Build OK.
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
echo 1/2 Hellas Basin ...
"%EXE%" -siva bubbles -activity 80F6DCE1 -out "%~dp0bubbles_hellas.json" -charmconfig "%CHARM_DIR%\config.json" > "%~dp0bubbles_log.txt" 2>&1
echo 2/2 all activities (bubble tables only) ...
"%EXE%" -siva bubbles -activity all -noentries -out "%~dp0bubbles_all.json" -charmconfig "%CHARM_DIR%\config.json" >> "%~dp0bubbles_log.txt" 2>&1
echo Done. Tell Claude (it reads the files itself).
:end
echo.
pause
