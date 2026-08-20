# Getting Started (Windows)

## 1. Install the tools

| Tool | Version | Notes |
|---|---|---|
| Unity Hub | latest | https://unity.com/download |
| Unity Editor | **6000.0 LTS (Unity 6)** | In Hub → Installs → Install Editor. Add module **Windows Build Support (IL2CPP)**. |
| Android Build Support (optional) | — | Only to build for a phone or tablet. Hub → Installs → the 6000.0 editor → Add modules → **Android Build Support**, plus its **OpenJDK** and **Android SDK & NDK Tools**. See `docs/40-ANDROID.md`. |
| WebGL Build Support (optional) | — | Only to build for a browser. Hub → Installs → the 6000.0 editor → Add modules → **WebGL Build Support**. See `docs/41-WEB.md`. |
| Linux Build Support (optional) | — | Only for a **native Steam Deck** build — a Deck runs the Windows player under Proton without it. Hub → Installs → Add modules → **Linux Build Support (IL2CPP)**. See `docs/42-STEAM-DECK.md`. |
| iOS Build Support (optional) | — | Only to export an Xcode project. The **archive and signing need a Mac with Xcode**; the export itself runs on Windows. Hub → Installs → Add modules → **iOS Build Support**. See `docs/43-IOS.md`. |
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
2. Open it with Unity 6000.0. The first import takes a few minutes — the **Cesium for Unity** package (1.25) is pulled automatically from the Cesium scoped registry defined in `Packages/manifest.json`.
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

## 7. Build

`make build` produces the Windows player, `make android` an APK, `make web` a
browser build, `make linux` a native Steam Deck one and `make ios` an Xcode
project. See `docs/06-WINDOWS-BUILD.md`, `docs/40-ANDROID.md`, `docs/41-WEB.md`,
`docs/42-STEAM-DECK.md` and `docs/43-IOS.md`.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Android: empty globe, no units, no missions | A shipped data file could not be read. StreamingAssets is inside the APK there — anything reading it must go through `Core/StreamingAssetsFile`, not `File.ReadAllText`. `docs/40-ANDROID.md` §1a. |
| Android: a scenario is in the build but not in the list | `StreamingAssets/Maps/index.json` is stale. Run `make setup`. `docs/40-ANDROID.md` §1b. |
| Web: the page loads and then hangs forever | Something read shipped data before the preload finished. WebGL has one thread, so a blocking read never returns. `docs/41-WEB.md` §1. |
| Web: "invalid magic number" in the console | The server is not sending `Content-Encoding: br` for the Brotli files. Use `make serve`, or build with `-Uncompressed`. `docs/41-WEB.md` §5c. |
| Web: saves vanish when the tab closes | A write is missing its `WebStorage.Flush()`. `docs/41-WEB.md` §2. |
| Steam Deck: the map pans on its own | Stick drift past the dead zone. `Core/GamepadInput.StickDeadZone` is the number. `docs/42-STEAM-DECK.md` §3. |
| iOS: the rail is under the notch | `SafeAreaCanvas` did not attach, or the canvas was made without `UIFactory.CreateCanvas`. `docs/43-IOS.md` §3. |
| iOS: an Info.plist edit keeps disappearing | The Xcode project is regenerated on every export. Put the key in `IosBuild.OnPostprocessBuild`. `docs/43-IOS.md` §6. |
| Steam Deck: the right stick does nothing | The `Pad*` axes are missing from `ProjectSettings/InputManager.asset`, or the wrong platform's set is in use. `docs/42-STEAM-DECK.md` §3a. |
| Black/empty globe | Token missing or invalid — see step 4; check Console for `[Cesium]` warnings. |
| `CesiumForUnity` namespace errors | Package didn't resolve: check internet access, then `Window → Package Manager` → refresh. |
| Units drop onto nothing / fall through | Terrain tiles not loaded yet at that spot — zoom in and wait a second, then drop again. |
| No scenes in Build Settings | Re-run **Tools → Iron Meridian → Setup Project**. |
