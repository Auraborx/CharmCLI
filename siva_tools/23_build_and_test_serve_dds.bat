@echo off
rem CharmCLI (S.I.V.A.) cli11 test: builds, then ONE background CharmCLI (-siva serve) exports Io - The Rupture twice
rem (1024 px textures, textures as DDS). The 2nd export shows the time saved by not starting again. Log: serve_log.txt
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
cd /d "%~dp0"
set OUT=%~dp0
set OUT=%OUT:\=/%
if exist "%~dp0export_dds_1" rmdir /s /q "%~dp0export_dds_1"
if exist "%~dp0export_dds_2" rmdir /s /q "%~dp0export_dds_2"
echo Exporting Io twice through one background CharmCLI ...   started %time%
(echo ["export","-activity","80ED48EF","-bubble","80ED4583","-out","%OUT%export_dds_1","-textures","png","-texfile","dds","-maxtex","1024"]
 echo ["export","-activity","80ED48EF","-bubble","80ED4583","-out","%OUT%export_dds_2","-textures","png","-texfile","dds","-maxtex","1024"]
 echo quit) | "%EXE%" -siva serve -strategy DESTINY2_SHADOWKEEP_2999 -charmconfig "%CHARM_DIR%\config.json" > "%~dp0serve_log.txt" 2>&1
echo    finished %time%  -  exit code %errorlevel%
echo Tell Claude it is done (it reads serve_log.txt itself).
:end
echo.
pause
