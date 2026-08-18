# Package the Windows installer

Follow `docs/34-INSTALLER.md`:

1. Make sure a player build exists in `Builds/Windows` (`scripts/build-windows.ps1`). To do both in one go: `scripts/build-windows.ps1 -Clean -Installer`.
2. Run `scripts/build-installer.ps1` from the repo root. It finds the `.exe`, the version (`bundleVersion`) and ISCC itself; overrides are `-SourceDir`, `-Version`, `-IsccPath`.
3. Inno Setup 6 must be installed — `winget install --id JRSoftware.InnoSetup`.
4. **Do not pass `-IncludeToken` unless the user explicitly asks for it.** It bundles the Cesium ion token into a file anyone who gets the installer can read (golden rule 1). Say so plainly if they do ask.
5. Report the output path, size and SHA-256 that the script prints. The result is `Builds/Installer/IronMeridian-<version>-Setup.exe`.
6. Editing the setup: `installer/iron-meridian.iss`. Never change `AppId` — it is how Windows recognises an existing install for upgrades. In the `[Code]` section, `{ }` is a Pascal comment, so never write `{app}` inside one.
7. Installer artwork is generated: `python scripts/generate_installer_art.py` from the game logo. Never hand-edit `installer/assets/`.
