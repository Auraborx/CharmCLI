@echo off
rem CharmCLI (S.I.V.A.): makes a release. Tags the current commit v3.3.8-cli<N> (N = CliVersion in Charm\SivaCli.cs) and pushes the tag:
rem GitHub then builds CharmCLI (about 10 minutes, Actions tab) and makes a DRAFT release - press Publish on it.
cd /d "%~dp0.."
where git >nul 2>nul
if errorlevel 1 (echo Git is not installed. & goto end)
for /f "tokens=2 delims==" %%a in ('findstr /c:"CliVersion =" Charm\SivaCli.cs') do set V=%%a
set V=%V:"=%
set V=%V:;=%
set V=%V: =%
set TAG=v3.3.8-cli%V%
echo Release %TAG%
git status --porcelain | findstr /r "." >nul
if not errorlevel 1 (echo There are changes not on GitHub yet - run 16_push_to_github.bat first. & goto end)
git tag %TAG%
if errorlevel 1 (echo The tag %TAG% exists already: raise CliVersion for a new release. & goto end)
git push mine %TAG%
if errorlevel 1 (echo PUSH FAILED - copy the lines above to Claude. & goto end)
echo.
echo Done. Watch it on GitHub: your fork - Actions. When it is green: Releases - the draft - Publish release.
:end
echo.
pause
