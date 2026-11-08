@echo off
rem CharmCLI (S.I.V.A.): lists every activity / bubble of the game version chosen in YOUR Charm's settings (no window opens).
rem Uses the config.json of your normal Charm: put its folder below (the folder that holds your Charm.exe).
set CHARM_DIR=
if "%CHARM_DIR%"=="" set /p CHARM_DIR=Folder of your normal Charm.exe (the one with config.json): 
cd /d "%~dp0.."
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
if "%EXE%"=="" (echo Build first: 2_build.bat & goto end)
echo Running: list (this reads the game's packages; can take a few minutes)...
"%EXE%" -siva list -out "%~dp0maps.json" -charmconfig "%CHARM_DIR%\config.json" > "%~dp0list_log.txt" 2>&1
echo Exit code %errorlevel%. Output: siva_tools\maps.json, log: siva_tools\list_log.txt
:end
echo.
pause
