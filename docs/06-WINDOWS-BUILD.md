# Building for Windows

## Prerequisites

- Unity 6000.0 LTS with **Windows Build Support** module installed
- Scenes generated (**Tools → Iron Meridian → Setup Project**)
- Cesium ion token configured (builds without it run, but the map stays empty)

Note: `Assets/StreamingAssets/cesium-token.txt` is copied into the build's `StreamingAssets` folder. If you ship the build to others, be aware the token travels with it — use a token restricted to the required scopes.

## Option A — Unity Editor

1. **File → Build Profiles** (or Build Settings) → platform **Windows**, architecture x86_64.
2. All five scenes should be listed (MainMenu first). If not, re-run the setup tool.
3. **Build** → choose an output folder (e.g. `Builds/Windows`).
4. Run `Iron Meridian.exe`.

## Option B — command line (PowerShell)

```powershell
# from the repo root
.\scripts\build-windows.ps1 -UnityPath "C:\Program Files\Unity\Hub\Editor\6000.0.80f1\Editor\Unity.exe"
```

The script runs Unity in batch mode with `-buildWindows64Player` and writes to `Builds\Windows\IronMeridian.exe`. Check `Builds\build.log` on failure.

## CI note

For GitHub Actions, use [game-ci/unity-builder](https://game.ci/) with `targetPlatform: StandaloneWindows64` and a `UNITY_LICENSE` secret; run the scene-setup method via `-executeMethod IronMeridian.EditorTools.ProjectBootstrap.SetupProject` before the build step.
