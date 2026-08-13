# Loaders

The register of everything in Iron Meridian that makes the player wait — what loads, whether it shows a loading screen, and which artwork that screen uses.

> **Keep this file current.** Every new loader, and every background image used by one, must be recorded in §2 and §3 with its path, the screen it appears on, and a description, in the same commit that introduces it. See [Rules](#rules) at the bottom.

---

## 1. Architecture

```
Assets/Scripts/UI/
  LoadingScreenUI.cs     full-screen overlay: artwork, progress bar, status line
  BackgroundCatalog.cs   loader artwork + its scrim level (BackgroundCatalog.LoaderScrim)
Assets/Scripts/Map/
  MapManager.cs          TerrainLoadProgress01 — the Cesium streaming signal
```

A loading screen is created, given a progress source, and forgotten:

```csharp
var loading = LoadingScreenUI.Show(GameConfig.GameName, "Preparing the operational map");
loading.SetStatus("Streaming terrain — Lyon Dev");
loading.Track(
    () => _map.TerrainLoadProgress01,          // 0..1 progress
    () => _map.TerrainLoadProgress01 >= 0.999f // completion test
);
```

### Design rules the overlay enforces

| Rule | Reason |
|---|---|
| **It always goes away.** Completion, a 30 s timeout, or an explicit `Dismiss` — every path ends the overlay. | A loader that can trap the player is worse than no loader. A missing Cesium token means tiles never load; the timeout is what stops that becoming a locked screen. |
| **Its own canvas at sorting order 500.** | uGUI draws by hierarchy order, so a loader created *before* the screen's UI would end up *behind* it. A separate high-order canvas lets the loader be created first and still cover everything built after. |
| **The bar only moves forward.** | `ComputeLoadProgress` is an estimate *for the current view* and drops when the camera moves and new tiles are needed. A retreating bar reads as a fault, not as honest reporting. |
| **The bar is eased, and shows 100 % on the way out.** | A snapping bar reads as a glitch; a fade-out frozen at 87 % reads as a failure. |
| **Minimum 0.8 s on screen.** | A warm tile cache would otherwise make it flash for one frame. |
| **Blocks input, including the camera.** | `CanvasGroup.blocksRaycasts` stops UI clicks, and `GameController` feeds its `Loading` flag into `CameraRig.InputBlocked` and `SelectionManager.InputBlocked` — the camera rig reads raw `Input` and would otherwise be draggable behind the overlay. |
| **Unscaled time throughout.** | The pause menu zeroes `timeScale`; a loader that freezes half-faded would be a trap. |

---

## 2. Loader background images

| Asset | Path | Resource path | Used by | Scrim | Description |
|---|---|---|---|---|---|
| Default menu artwork | `Assets/Resources/Backgrounds/default_background.png` | `Backgrounds/default_background` | Map editor loader (§3.1) | **0.48** (`BackgroundCatalog.LoaderScrim`) | Shared game artwork. Loaders use a lighter scrim than working screens — there is little text to read and the art is the point while waiting. |

Loader artwork goes through the same builder as screen backgrounds, so it is aspect-preserved and never stretched. See `docs/11-GAME-MENU.md` for the layer stack.

---

## 3. Loader register

### 3.1 Blocking loading screens

Full-screen overlays that cover a screen until it is ready.

| Loader | Screen | Scene | Waits for | Progress source | Dismisses when | Background |
|---|---|---|---|---|---|---|
| **Map load** | Map editor / game | `Game` | Cesium World Terrain streaming the opening view | `MapManager.TerrainLoadProgress01` (`Cesium3DTileset.ComputeLoadProgress()`) | Terrain ≥ 99.9 %, **or** 30 s timeout, **or** `MapManager.LoadError` fires | `default_background.png`, scrim 0.48 |

Implementation: `LoadingScreenUI` + `GameController.Start`.

### 3.2 Scene transitions — no loader

All navigation uses synchronous `SceneManager.LoadScene`. Menu scenes build their UI from code in a few milliseconds, so there is nothing to wait for and a loader would only add a flash.

| From → To | Trigger | Why no loader |
|---|---|---|
| Main Menu → Testing / Settings | Menu buttons | Runtime-built uGUI only |
| Testing → Game | "Dev" card | The `Game` scene shows its own loader (§3.1) once it starts |
| Testing → East France / Units List | Cards | Runtime-built uGUI only |
| Any → previous screen | Back buttons, Escape | Runtime-built uGUI only |

If a screen ever gains a slow build step, give it a `LoadingScreenUI` and add it to §3.1.

### 3.3 Background streaming — no UI

Continues after the loading screen has gone. Progressive by design: the player works while detail arrives.

| What | Owner | Notes |
|---|---|---|
| Cesium World Terrain tiles | `MapManager.Terrain` | Keeps streaming as the camera moves. Gates the §3.1 loader for the *opening view only*. |
| Cesium OSM Buildings | `MapManager.Buildings` | 3D mode only. Never gates the loader — buildings are detail, not the map. |
| Ion raster imagery | `MapManager._overlay` | Draped on the terrain; streams independently of tile geometry. |
| Music clip | `MusicManager` | `Resources.Load<AudioClip>` on the first screen, then a 1.5 s fade-in. See `docs/10-AUDIO.md`. |

### 3.4 Synchronous loads — no UI

Fast enough to be invisible. Listed so the inventory is complete and so anything that grows slow is easy to spot.

| What | Path | Loaded by | When |
|---|---|---|---|
| Unit catalogue | `Assets/StreamingAssets/Data/units.json` | `UnitDatabase` | First access, cached for the session |
| Map save | `Assets/StreamingAssets/Maps/*.json` | `SaveSystem.LoadMap` | `Game` scene start, F9 |
| Unit icons | `Assets/Resources/Icons/**` | `UIFactory.LoadSprite` | On demand, cached by path |
| Screen backgrounds | `Assets/Resources/Backgrounds/**` | `UIFactory.LoadSprite` | On demand, cached by path |
| Unit 3D model | `Assets/Resources/Models/**` | `ModelPreview` | Units List row selection |
| VFX prefabs | `Assets/Resources/VFX/**` | `VfxSystem` | First use of each effect, cached per id |

---

## 4. Known limitation

The overlay is created first in `GameController.Start`, but **Unity does not paint a frame until `Start` returns** — so the synchronous portion (building the map objects, the UI and the order of battle) happens before the loader is visible. The loader covers the long part, which is asynchronous tile streaming.

If the synchronous build ever becomes slow enough to notice, the fix is to show the overlay, `yield return null` so it paints, then run the build from a coroutine — guarding `Update` against running before the scene is wired.

---

## 5. Adding a new loader

1. **Confirm a loader is warranted.** If the wait is synchronous and under a frame or two, it will only flash — leave it out and record it in §3.2 or §3.4 instead.
2. **Show it:** `LoadingScreenUI.Show(title, subtitle)`, created before the screen's UI.
3. **Give it a progress source and a completion test** via `Track(...)`. Both must be cheap — they run every frame.
4. **Give it an escape hatch.** Set a timeout, and dismiss explicitly on any error event that means completion will never arrive (as `MapManager.LoadError` does for the map).
5. **Block input** by feeding the loading flag into the screen's own input guards — `blocksRaycasts` alone does not stop code that reads `UnityEngine.Input` directly.
6. **Use a catalogued background** and `BackgroundCatalog.LoaderScrim`; register any new artwork in §2 *and* in `docs/11-GAME-MENU.md`.
7. **Verify** the failure path, not just the happy one: rename the Cesium token file and confirm the loader still dismisses and the error surfaces.
8. **Update this file** — §2 if new artwork, §3 for the loader itself.

---

## Rules

1. **This document is the register of every loader in the game.** Adding, removing or repurposing a loader, or using a new background image in one, is not done until §2 and §3 here are updated in the same commit — with the file path, the screen it appears on, and a description.
2. Every loading screen must have a guaranteed dismissal path: a completion test *and* a timeout *and* explicit dismissal on error.
3. Loading screens go through `LoadingScreenUI`. No hand-rolled full-screen overlays.
4. Loader artwork comes from `BackgroundCatalog`; scrim levels live there, not at call sites.
5. Progress bars never move backwards and always reach 100 % before fading.
6. Loaders run on unscaled time.
7. A loader must block the screen's own input paths, not just uGUI raycasts.

## Related

`docs/11-GAME-MENU.md` (background image register) · `docs/02-CESIUM.md` (tilesets and tokens) · `docs/07-ARCHITECTURE.md` (script map) · `docs/10-AUDIO.md` (audio register)
