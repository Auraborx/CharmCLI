@echo off
rem CharmCLI (S.I.V.A.): exports BrayTech Futurescape again into a NEW folder (export_timing_N) and logs where the time goes
rem (PART lines: each exporter, scene textures, materials, shader decompiles, texture saves). Log: siva_tools\timing_log.txt
rem Note: decompiled shaders are cached in Charm\bin\...\hlsl_temp, so shaders already done in an earlier run are fast now.
set CHARM_DIR=
if exist "%~dp0charm_dir.txt" set /p CHARM_DIR=<"%~dp0charm_dir.txt"
if "%CHARM_DIR%"=="" set /p CHARM_DIR=Folder of your normal Charm.exe (the one with config.json): 
> "%~dp0charm_dir.txt" echo %CHARM_DIR%
set N=1
:next
if exist "%~dp0export_timing_%N%" (set /a N+=1 & goto next)
cd /d "%~dp0.."
set EXE=
for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" set EXE=%%f
if "%EXE%"=="" (echo Build first: 2_build.bat & goto end)
echo Exporting BrayTech Futurescape into siva_tools\export_timing_%N% ...
echo Started %time%
"%EXE%" -siva export -activity 80F6DCE1 -bubble 80EA89E8 -out "%~dp0export_timing_%N%" -textures png -charmconfig "%CHARM_DIR%\config.json" > "%~dp0timing_log.txt" 2>&1
echo Finished %time%  -  exit code %errorlevel%. Log: siva_tools\timing_log.txt
:end
echo.
pause
