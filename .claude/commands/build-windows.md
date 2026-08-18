# Build the Windows player

Follow `docs/06-WINDOWS-BUILD.md`:

1. Confirm the Cesium token is configured (`Assets/StreamingAssets/cesium-token.txt` — never print or commit its contents).
2. Run `scripts/build-windows.ps1` from the repo root (Windows). It finds the newest Unity 6000.x under the Hub itself; pass `-UnityPath "<path to Unity.exe>"` if that fails. Add `-Clean` after a `productName` change. If Unity isn't scriptable in this environment, instruct the user to build via File → Build Profiles instead.
3. On failure, read `Builds/setup.log` and `Builds/build.log` and diagnose from the bottom up.
4. To hand the result to someone, package it: `scripts/build-windows.ps1 -Clean -Installer` — see `.claude/commands/build-installer.md` and `docs/34-INSTALLER.md`.
