# Build the Windows player

Follow `docs/06-WINDOWS-BUILD.md`:

1. Confirm the Cesium token is configured (`Assets/StreamingAssets/cesium-token.txt` — never print or commit its contents).
2. Run `scripts/build-windows.ps1 -UnityPath "<path to Unity.exe 6000.0>"` from the repo root (Windows), or instruct the user to build via File → Build Profiles if Unity isn't scriptable in this environment.
3. On failure, read `Builds/setup.log` and `Builds/build.log` and diagnose from the bottom up.
