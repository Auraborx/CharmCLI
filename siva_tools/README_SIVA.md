# CharmCLI - Charm with a command-line mode for S.I.V.A.

Fork of [Charm](https://github.com/MontagueM/Charm) (GPLv3, by MontagueM and contributors), based on tag **v3.3.8**, branch `siva-cli`.
Everything Charm does still works the same; this adds `Charm.exe -siva ...` (no window):

    Charm.exe -siva list   -out maps.json [-charmconfig <your Charm's config.json>] [-strategy NAME]
    Charm.exe -siva export -activity <hash> -bubble <hash|all> [-out <export folder>] [-textures png|tga|dds] [-charmconfig ...]

- Settings (game version, packages folder per version, export folder, texture format) are read from Charm's own config.json. The CLI never
  changes that file: for its own run it uses a temporary copy with "save shaders as HLSL" on and "single folder for map assets" off.
- Each bubble goes to `<export>/Maps/<Destination>/<Bubble>/` with `siva_export.json` (destination, activity, bubble, game version).
- Output lines: `PROGRESS n/total step`, `TIME seconds step`, `DONE folder`; errors start with `ERROR` (stderr); exit code 0 = ok.

Build (Windows): siva_tools\1_install_dotnet8.bat (once), then siva_tools\2_build.bat. Test: siva_tools\3_test_list.bat.

Releases of S.I.V.A. must NOT include oo2core_*_win64.dll (Charm/ThirdParty has copies from upstream): the Oodle DLL comes from the user's game.
