@echo off
rem CharmCLI (S.I.V.A.): installs the .NET 8 SDK (Microsoft's C# compiler) with winget - only if it is missing.
echo Checking for the .NET 8 SDK...
where dotnet >nul 2>nul && (dotnet --list-sdks | findstr /b "8." >nul && (echo The .NET 8 SDK is already installed - nothing to do. & dotnet --list-sdks & goto end))
echo The .NET 8 SDK is not installed. Installing it with winget (Microsoft.DotNet.SDK.8)...
where winget >nul 2>nul || (echo winget was not found. Install the .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0 and run this again. & goto end)
winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
echo.
echo Done. If it says the SDK is installed, close this window and run 2_build.bat.
:end
echo.
pause
