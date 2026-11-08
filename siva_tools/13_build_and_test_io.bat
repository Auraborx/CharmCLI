@echo off
rem CharmCLI (S.I.V.A.): builds, then exports a map CharmCLI never exported (Io - The Rupture) TWICE:
rem   1. all cores (materials + shader decompiles in parallel)  -> export_io_fast_N   log io_fast_log.txt
rem   2. -threads 1 = Charm's original one-by-one code (reference) -> export_io_ref_N    log io_ref_log.txt
rem Claude compares the two folders afterwards. The reference run is slow (a few minutes).
set CHARM_DIR=
if exist "%~dp0charm_dir.txt" set /p CHARM_DIR=<"%~dp0charm_dir.txt"
if "%CHARM_DIR%"=="" set /p CHARM_DIR=Folder of your normal Charm.exe (the one with config.json): 
> "%~dp0charm_dir.txt" echo %CHARM_DIR%
cd /d "%~dp0.."
echo Building CharmCLI...
dotnet build Charm.sln -c Release -p:Platform=x64 > siva_tools\build_log.txt 2>&1
if errorlevel 1 (echo BUILD FAILED - send siva_tools\build_log.txt to Claude. & goto end)
echo Build OK.
set N=1
:next
if exist "%~dp0export_io_fast_%N%" (set /a N+=1 & goto next)
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
echo 1/2 Exporting Io - The Rupture on all cores ...   started %time%
"%EXE%" -siva export -activity 80ED48EF -bubble 80ED4583 -out "%~dp0export_io_fast_%N%" -textures png -charmconfig "%CHARM_DIR%\config.json" > "%~dp0io_fast_log.txt" 2>&1
echo    finished %time%  -  exit code %errorlevel%
echo 2/2 Exporting it again the original one-by-one way (reference, slower) ...   started %time%
"%EXE%" -siva export -activity 80ED48EF -bubble 80ED4583 -out "%~dp0export_io_ref_%N%" -textures png -threads 1 -charmconfig "%CHARM_DIR%\config.json" > "%~dp0io_ref_log.txt" 2>&1
echo    finished %time%  -  exit code %errorlevel%
echo Send siva_tools\io_fast_log.txt and io_ref_log.txt to Claude.
:end
echo.
pause
