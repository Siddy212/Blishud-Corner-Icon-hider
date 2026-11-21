# Hide Corner Icons (Blish HUD module)

Hides the Blish HUD corner icons (top-left of the screen). Nothing else.

## Build

1. Install the .NET SDK (or Visual Studio 2022 with the ".NET desktop development" workload).
2. In this folder: `dotnet build -c Release`
3. Zip `manifest.json` and `HideCornerIcons.dll` (from `bin/x64/Release/net48/`) together, with both
   at the top level of the zip, then rename the zip to `HideCornerIcons.bhm`.

## Install

Drop `HideCornerIcons.bhm` into `Documents\Guild Wars 2\addons\blishhud\modules\`,
restart Blish HUD, and enable it under Manage Modules.

## Notes

- Press Ctrl+Alt+H (rebindable) to hide or show the icons. "Icon opacity" (default 40%) sets how faint they are while shown. Disabling the module restores everything.
- Blish HUD's own icon is hidden too; open Blish HUD with its hotkey instead.
- GW2's built-in corner icons (inventory, trading post, etc.) are not Blish HUD controls and can't be hidden by a module.

## Build without installing anything (GitHub)

1. Create a new GitHub repo and upload the contents of this folder (including `.github`).
2. Open the repo's **Actions** tab, wait for "Build module" to finish (or click Run workflow).
3. Download the `HideCornerIcons.bhm` artifact from the finished run and install it as above.
