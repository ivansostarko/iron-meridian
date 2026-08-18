# Getting Started (Windows)

## 1. Install the tools

| Tool | Version | Notes |
|---|---|---|
| Unity Hub | latest | https://unity.com/download |
| Unity Editor | **6000.0 LTS (Unity 6)** | In Hub → Installs → Install Editor. Add module **Windows Build Support (IL2CPP)**. |
| Git | latest | https://git-scm.com/download/win |
| Python 3 (optional) | 3.10+ | Only needed to regenerate icons/units/installer art (`scripts/*.py`, uses Pillow). |
| Inno Setup 6 (optional) | 6.x | Only needed to package the installer — `winget install --id JRSoftware.InnoSetup`. See `docs/34-INSTALLER.md`. |
| GNU Make (optional) | 4.x | `make` as a shortcut for every routine job — `winget install --id ezwinports.make`. Without it, `.\scripts\menu.ps1` is the same menu. See `docs/35-TASKS.md`. |

Run **`.\scripts\menu.ps1 -Run doctor`** (or `make doctor`) at any point: it reports which of these are present and the one command that installs each missing one.

Hardware: any 64-bit Windows 10/11 machine with a discrete or recent integrated GPU. Cesium streams tiles over the network, so an internet connection is required at runtime.

## 2. Get the project

```powershell
git clone https://github.com/ivansostark/iron-meridian.git
cd iron-meridian
```

## 3. Open in Unity

1. Unity Hub → **Add** → select the `iron-meridian` folder.
2. Open it with Unity 6000.0. The first import takes a few minutes — the **Cesium for Unity** package (1.24) is pulled automatically from the Cesium scoped registry defined in `Packages/manifest.json`.
3. If Unity asks about entering Safe Mode because of compile errors on very first open, choose **Ignore** — errors disappear once the Cesium package finishes resolving.

## 4. Add your Cesium ion token  ⚠️ required

The 3D map will not load without it. Follow [02-CESIUM.md](02-CESIUM.md) — short version:

1. Create a free account at https://ion.cesium.com
2. https://ion.cesium.com/tokens → **Create token** (default scopes are fine)
3. Paste the token into **`Assets/StreamingAssets/cesium-token.txt`** (replace the placeholder line)

## 5. Generate the scenes

Run **Tools → Iron Meridian → Setup Project** from the Unity menu bar.
This creates `Assets/Scenes/` (MainMenu, Settings, Testing, EastFrance, SinglePlayer, Multiplayer, Extras, UnitsList, EffectsList, AudioList, Game), registers them in Build Settings and opens the main menu scene. The `Testing` scene is the one the menu calls **DEVELOPMENT** — the scene name is kept so existing build-settings entries and `LoadScene` calls do not break.

## 6. Play

Press **Play**:

- **TESTING → DEV** — the game screen over Lyon: drag units from the left palette, draw lines, press **START BATTLE**.
- **SETTINGS** — Video (resolution, window mode) and Audio (master volume) tabs.
- **QUIT** — confirmation modal.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Black/empty globe | Token missing or invalid — see step 4; check Console for `[Cesium]` warnings. |
| `CesiumForUnity` namespace errors | Package didn't resolve: check internet access, then `Window → Package Manager` → refresh. |
| Units drop onto nothing / fall through | Terrain tiles not loaded yet at that spot — zoom in and wait a second, then drop again. |
| No scenes in Build Settings | Re-run **Tools → Iron Meridian → Setup Project**. |
