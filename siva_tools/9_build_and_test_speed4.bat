@echo off
rem CharmCLI (S.I.V.A.): builds CharmCLI, then exports BrayTech Futurescape with maps + data tables read on all cores, FBX scenes freed in bulk
rem into a NEW folder (export_t4_N). Logs: siva_tools\build_log.txt and siva_tools\speed4_log.txt
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
if exist "%~dp0export_t4_%N%" (set /a N+=1 & goto next)
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
echo Exporting BrayTech Futurescape into siva_tools\export_t4_%N% ...
echo Started %time%
"%EXE%" -siva export -activity 80F6DCE1 -bubble 80EA89E8 -out "%~dp0export_t4_%N%" -textures png -charmconfig "%CHARM_DIR%\config.json" > "%~dp0speed4_log.txt" 2>&1
echo Finished %time%  -  exit code %errorlevel%. Send siva_tools\speed4_log.txt to Claude.
:end
echo.
pause
