@echo off
rem CharmCLI (S.I.V.A.): builds Charm with the -siva command-line mode. The log goes to siva_tools\build_log.txt.
cd /d "%~dp0.."
echo Building CharmCLI (first time: downloads Charm's libraries from nuget.org, a few minutes)...
dotnet build Charm\Charm.csproj -c Release -p:Platform=x64 > siva_tools\build_log.txt 2>&1
if errorlevel 1 (
  echo BUILD FAILED - see siva_tools\build_log.txt (Claude can read it).
) else (
  echo Build OK.
  for /r Charm\bin %%f in (Charm.exe) do if exist "%%f" echo   %%f
)
echo.
pause
