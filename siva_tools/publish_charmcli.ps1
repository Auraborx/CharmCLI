# CharmCLI (S.I.V.A.): makes the release zip  dist\CharmCLI-<version>-win-x64.zip  (+ .sha256) - a self-contained build (no .NET install
# needed) WITHOUT Oodle (oo2core_*.dll: S.I.V.A. never ships it; CharmCLI loads the user's copy, -oodle). Also used by the GitHub workflow.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$cli = [regex]::Match((Get-Content 'Charm\SivaCli.cs' -Raw), 'CliVersion = "(\d+)"').Groups[1].Value
$charm = if ($env:CHARM_VERSION) { $env:CHARM_VERSION } else { '3.3.8' }
$ver = "$charm-cli$cli"
$out = Join-Path $root 'dist\CharmCLI'
$zip = Join-Path $root "dist\CharmCLI-$ver-win-x64.zip"
Write-Host "CharmCLI $ver -> $zip"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
dotnet publish Charm\Charm.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
# no Oodle, no stray state from a development run
Get-ChildItem $out -Recurse -Filter 'oo2core*.dll' | Remove-Item -Force
foreach ($d in 'PackagePathsCaches', 'hlsl_temp', 'Logs') { if (Test-Path (Join-Path $out $d)) { Remove-Item (Join-Path $out $d) -Recurse -Force } }
if (Test-Path (Join-Path $out 'config.json')) { Remove-Item (Join-Path $out 'config.json') -Force }
if (-not (Test-Path (Join-Path $out 'Charm.exe'))) { throw 'no Charm.exe in the publish folder' }
if (Get-ChildItem $out -Recurse -Filter 'oo2core*' ) { throw 'Oodle is still in the publish folder' }
# source and licence (GPLv3: the zip says where its source is)
Copy-Item 'LICENSE' (Join-Path $out 'LICENSE.txt') -ErrorAction SilentlyContinue
Set-Content (Join-Path $out 'CHARMCLI_README.txt') "CharmCLI $ver - S.I.V.A.'s copy of Charm (MontagueM/Charm, GPLv3) without a window.`r`nSource: the CharmCLI repository (branch siva-cli).`r`nOodle (oo2core_*_win64.dll) is NOT included: it comes with the game / with Charm."
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
$h = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
Set-Content "$zip.sha256" "$h *$(Split-Path $zip -Leaf)" -Encoding ascii
$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "OK: $zip ($mb MB)  sha256 $h"
