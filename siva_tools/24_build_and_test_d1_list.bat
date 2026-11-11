@echo off
rem CharmCLI (S.I.V.A.) cli13 test: builds, then lists the maps of the game version chosen in your Charm (Destiny 1: Rise of Iron).
rem cli12 listed no D1 maps (the D1 table was read in the background and was still empty). Output: siva_tools\maps.json, log: list_log.txt
set CHARM_DIR=
if exist "%~dp0charm_dir.txt" set /p CHARM_DIR=<"%~dp0charm_dir.txt"
if "%CHARM_DIR%"=="" set /p CHARM_DIR=Folder of your normal Charm.exe (the one with config.json): 
> "%~dp0charm_dir.txt" echo %CHARM_DIR%
cd /d "%~dp0.."
echo Building CharmCLI...
dotnet build Charm.sln -c Release -p:Platform=x64 > siva_tools\build_log.txt 2>&1
if errorlevel 1 (echo BUILD FAILED - tell Claude, it reads siva_tools\build_log.txt. & goto end)
echo Build OK.
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
echo Listing the maps (Charm's settings must be on Destiny 1: Rise of Iron)...   started %time%
"%EXE%" -siva list -out "%~dp0maps.json" -charmconfig "%CHARM_DIR%\config.json" -oodle "%CHARM_DIR%\ThirdParty" > "%~dp0list_log.txt" 2>&1
echo    finished %time%  -  exit code %errorlevel%
findstr /c:"TIME" "%~dp0list_log.txt"
echo Tell Claude it is done (it reads list_log.txt and maps.json itself).
:end
echo.
pause
