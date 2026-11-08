@echo off
rem CharmCLI (S.I.V.A.): exports ONE bubble (Hellas Basin / polaris: BrayTech Futurescape) without a window, with timings.
rem Output: siva_tools\export_test\Maps\polaris\BrayTech Futurescape\  -  log: siva_tools\export_log.txt
set CHARM_DIR=
if exist "%~dp0charm_dir.txt" set /p CHARM_DIR=<"%~dp0charm_dir.txt"
if "%CHARM_DIR%"=="" set /p CHARM_DIR=Folder of your normal Charm.exe (the one with config.json): 
> "%~dp0charm_dir.txt" echo %CHARM_DIR%
cd /d "%~dp0.."
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
if "%EXE%"=="" (echo Build first: 2_build.bat & goto end)
echo Exporting BrayTech Futurescape (Hellas Basin) - this can take several minutes...
echo Started %time%
"%EXE%" -siva export -activity 80F6DCE1 -bubble 80EA89E8 -out "%~dp0export_test" -textures png -charmconfig "%CHARM_DIR%\config.json" > "%~dp0export_log.txt" 2>&1
echo Finished %time%  -  exit code %errorlevel%. Log: siva_tools\export_log.txt
:end
echo.
pause
