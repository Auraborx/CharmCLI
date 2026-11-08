# CharmCLI - Charm with a command-line mode for S.I.V.A.

Fork of [Charm](https://github.com/MontagueM/Charm) (GPLv3, by MontagueM and contributors), based on tag **v3.3.8**, branch `siva-cli`.
Everything Charm does still works the same; this adds `Charm.exe -siva ...` (no window):

    Charm.exe -siva list   -out maps.json [-charmconfig <your Charm's config.json>] [-strategy NAME]
    Charm.exe -siva export -activity <hash> -bubble <hash|all> [-out <export folder>] [-textures png|tga|dds] [-charmconfig ...]
    common: [-oodle <folder with oo2core_*_win64.dll>]  [-threads N (1 = Charm's own one-by-one code)]  [-fbxwriter sdk]  [-fbxrecycle 0]  [-texlock]

- Settings (game version, packages folder per version, export folder, texture format) are read from Charm's own config.json. The CLI never
  changes that file: for its own run it uses a temporary copy with "save shaders as HLSL" on and "single folder for map assets" off.
- Each bubble goes to `<export>/Maps/<Destination>/<Bubble>/` with `siva_export.json` (destination, activity, bubble, game version).
- Output lines: `PROGRESS n/total step`, `TIME seconds step`, `DONE folder`; errors start with `ERROR` (stderr); exit code 0 = ok.

Build (Windows): siva_tools\1_install_dotnet8.bat (once), then siva_tools\2_build.bat. Test: siva_tools\3_test_list.bat.

Speed (BrayTech Futurescape 193 s -> 39 s, Io The Rupture 127 s -> 32 s, output checked identical to Charm's own code path): textures, maps, data
table entries, decorators and materials on all cores; models through CharmCLI's own FBX writer (Tiger/Exporters/SivaFbxWriter.cs) for everything
without a skeleton / dye slots. See the patches\ folder and S.I.V.A.'s MEMORY.md.

## Releases (S.I.V.A. downloads CharmCLI from here)
Releases must NOT include oo2core_*_win64.dll (Charm/ThirdParty has copies from upstream): the Oodle DLL comes from the user's game or Charm
(-oodle; S.I.V.A. finds it). siva_tools\publish_charmcli.ps1 strips it and refuses a zip that still has it.

- Local test: siva_tools\14_publish_charmcli.bat -> dist\CharmCLI-<charm>-cli<N>-win-x64.zip + .sha256 (self-contained: no .NET install needed).
- GitHub (once): publish this folder as a PUBLIC repository (GPLv3 needs the source to be available), copy
  siva_tools\new_workflows\release-charmcli.yml into .github\workflows\, Settings -> Actions -> Workflow permissions: read and write.
  Put the repository's name (owner/name) into S.I.V.A.'s release.json as "charmcli_repo".
- Each release: tag v<charm>-cli<N> (the version 14_publish_charmcli.bat prints; N = CliVersion in Charm\SivaCli.cs) -> the workflow makes a
  draft release -> Publish. S.I.V.A. offers it in "Add map from the game" (Download CharmCLI / Update CharmCLI).
