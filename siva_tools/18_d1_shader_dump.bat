@echo off
rem CharmCLI (S.I.V.A.): D1 shader dump test. Builds CharmCLI, then opens the Charm WINDOW with CHARM_D1SHADERS=1.
rem In that window, export a D1 map as usual (e.g. Felwinter Peak). Every D1 material then also writes its raw PS4 shader files to
rem   <export folder>\Shaders\PS4\PS_<hash>.bin / VS_<hash>.bin  (+ a small .json header each).
rem D2 exports are unchanged. Your normal Charm and its config.json are not touched (a copy is used the first time).
setlocal
set CHARM_DIR=
if exist "%~dp0charm_dir.txt" set /p CHARM_DIR=<"%~dp0charm_dir.txt"
if "%CHARM_DIR%"=="" set /p CHARM_DIR=Folder of your normal Charm.exe (the one with config.json): 
> "%~dp0charm_dir.txt" echo %CHARM_DIR%
cd /d "%~dp0.."
echo Building CharmCLI (log: siva_tools\build_log.txt)...
dotnet build Charm.sln -c Release -p:Platform=x64 > siva_tools\build_log.txt 2>&1
if errorlevel 1 (echo BUILD FAILED - see siva_tools\build_log.txt ^(Claude can read it^). & goto end)
echo Build OK.
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
if "%EXE%"=="" (echo Charm.exe not found after the build. & goto end)
for %%d in ("%EXE%") do set EXEDIR=%%~dpd
if not exist "%EXEDIR%config.json" (
  if exist "%CHARM_DIR%\config.json" (copy "%CHARM_DIR%\config.json" "%EXEDIR%config.json" > nul & echo Copied your Charm settings for this build.)
)
set CHARM_D1SHADERS=1
echo.
echo Opening Charm with the D1 shader dump ON.
echo   1. Make sure the game version is Destiny 1 (Rise of Iron).
echo   2. Export a D1 map as usual, e.g. Felwinter Peak - one bubble is enough.
echo   3. Close Charm, then tell Claude which export folder it went to.
cd /d "%EXEDIR%"
"%EXE%"
:end
echo.
pause
