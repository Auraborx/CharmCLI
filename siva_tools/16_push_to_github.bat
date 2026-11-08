@echo off
rem CharmCLI (S.I.V.A.): puts this fork on YOUR GitHub (branch siva-cli). Run it once after making the fork on github.com,
rem and again whenever CharmCLI changed (it commits everything that changed and pushes it).
cd /d "%~dp0.."
where git >nul 2>nul
if errorlevel 1 (echo Git is not installed. Install "Git for Windows" from https://git-scm.com/download/win , then run this again. & goto end)
set URL=
if exist "%~dp0github_url.txt" set /p URL=<"%~dp0github_url.txt"
if "%URL%"=="" set /p URL=Paste the address of your fork (for example https://github.com/YourName/CharmCLI): 
> "%~dp0github_url.txt" echo %URL%
rem who the commits are from (asked once)
git config user.email >nul 2>nul
if errorlevel 1 (set /p GEMAIL=Your GitHub e-mail ^(for the commits^): & set /p GNAME=Your GitHub user name: )
if defined GEMAIL git config user.email "%GEMAIL%"
if defined GNAME git config user.name "%GNAME%"
rem line endings: Windows CRLF on disk, LF in the repository (otherwise ~860 files look changed)
git config core.autocrlf true
git remote remove mine >nul 2>nul
git remote add mine %URL%.git
rem this fork's own release workflow instead of upstream Charm's (those would fail / try to publish Charm itself)
if not exist .github\workflows mkdir .github\workflows
copy /y siva_tools\new_workflows\release-charmcli.yml .github\workflows\release-charmcli.yml >nul
git rm -q --ignore-unmatch .github/workflows/dotnet.yml .github/workflows/release-please.yml .github/workflows/super-linter.yml
rem Oodle is never published (it stays on your disk for your own builds)
git rm -q --cached --ignore-unmatch Charm/ThirdParty/oo2core_3_win64.dll Charm/ThirdParty/oo2core_9_win64.dll
git add -A
git commit -q -m "CharmCLI for S.I.V.A.: -siva list / export / bubbles, speed work, own FBX writer, release workflow"
git push -u mine siva-cli
if errorlevel 1 (echo PUSH FAILED - copy the lines above to Claude. & goto end)
echo.
echo Done: your fork has the branch siva-cli. Next: step 4 of the walkthrough (switch on Actions on GitHub).
:end
echo.
pause
