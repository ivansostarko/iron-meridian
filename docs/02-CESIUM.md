# Cesium 3D Maps & the ion API Token

## What Cesium does in Iron Meridian

[Cesium for Unity](https://cesium.com/platform/cesium-for-unity/) streams a full 3D globe into the game at runtime:

| Layer | Cesium ion asset ID | Used for |
|---|---|---|
| Cesium World Terrain | `1` | 3D terrain relief |
| Bing Maps Aerial imagery | `2` | Satellite imagery draped on the terrain |
| Bing Maps Aerial with Labels | `3` | Satellite imagery with place names |
| Bing Maps Road | `4` | Road cartography |
| Sentinel-2 | `3954` | Cloudless 10 m satellite mosaic |
| Cesium OSM Buildings | `96188` | 3D buildings — shown or hidden by the MAP panel toggle, independent of the 2D/3D view |

## Tile styles

Selected from the map editor's **MAP** panel. `MapManager.SetMapStyle` handles three kinds of style, and only ever leaves one overlay enabled — two would stack imagery on the same tileset:

| Style | Source | Notes |
|---|---|---|
| `Satellite` | ion asset 2 | Default |
| `SatelliteLabels` | ion asset 3 | Aerial with place names |
| `Roads` | ion asset 4 | Road cartography |
| `Sentinel2` | ion asset 3954 | Consistent global mosaic; lower resolution than Bing at city scale |
| `OpenStreetMap` | `https://tile.openstreetmap.org/{z}/{x}/{y}.png` via `CesiumUrlTemplateRasterOverlay` | **No ion token needed.** Created on first use; capped at z19 because OSM serves nothing beyond it. Mind OSM's tile usage policy for anything public. |
| `Terrain` | none | Overlay disabled — bare shaded relief |

Saved per map as `mapStyle`.

## 2D and 3D parity

The view mode is a **camera choice, not a different world**. Buildings used to be hidden in 2D, which meant the two views did not show the same thing; they are now governed by the MAP panel's toggle alone (`showBuildings` in the save). Units, effects, weather and labels are unaffected by the mode.

The one thing that does change is control-measure clamping: lines are drawn either following the terrain (3D) or on a flat band (2D). `GameController` re-clamps every line via `LineManager.SetAll3D` when the projection switches, so the same graphics are visible either way rather than one projection burying them in the ground.

`MapManager.cs` creates a `CesiumGeoreference` centred on the map's lat/lon (Lyon by default: **45.7640 N, 4.8357 E**) plus the three tilesets above. Physics meshes are enabled so units and line points can be placed by raycasting the terrain.

The package itself is declared in `Packages/manifest.json` via Cesium's scoped registry:

```json
"scopedRegistries": [
  { "name": "Cesium", "url": "https://unity.pkg.cesium.com", "scopes": ["com.cesium.unity"] }
],
"dependencies": { "com.cesium.unity": "1.25.0" }
```

## Getting a token

1. Create a **free** account at https://ion.cesium.com
2. Go to https://ion.cesium.com/tokens
3. **Create token** — the default scopes (`assets:read`, `assets:list`, `geocode`) are sufficient.
4. Copy the long `eyJ...` string.

## ⚠️ Where to add your token (two options)

**Option 1 — token file (recommended):**

```
Assets/StreamingAssets/cesium-token.txt
```

Replace the `PASTE_YOUR_CESIUM_ION_TOKEN_HERE` line with your token. This file is listed in `.gitignore`, so your token is never committed.

**Option 2 — code constant (quick local testing):**

```
Assets/Scripts/Core/CesiumTokenConfig.cs
```

```csharp
public const string IonAccessToken = "eyJhbGciOi...";   // your token here
```

Do **not** commit a real token in code to a public repository.

`CesiumTokenConfig.GetToken()` checks the file first, then the constant, and logs a clear warning if neither is set. The token is applied per-tileset at runtime, so no Cesium editor windows are needed.

## Handing a build to someone else

A player build copies `cesium-token.txt` into its own `StreamingAssets`, so **whoever gets the build gets the token** — in a text file they can open. The installer therefore strips it back out by default (`docs/34-INSTALLER.md` §2a); the game starts, and the wizard's last page tells the player where to paste their own.

If you deliberately bundle one (`build-installer.ps1 -IncludeToken` — an internal build, a demo machine), issue a token scoped to nothing but asset read for the tilesets listed above, and be ready to revoke it.

⚠️ **The web build is worse than all of them.** StreamingAssets in a WebGL build
is served as plain files, so `cesium-token.txt` is one fetch away from anyone who
loads the page — no unpacking, just a URL. A public web build is a **published
token**. `docs/41-WEB.md` §6.

⚠️ **The Android build has no strip.** `scripts/build-android.ps1` has no
`-IncludeToken` switch because it has no way *not* to include it: the file is
packed into the APK, in plain text, and an APK is easier to pass around than an
installer. Anyone you hand one to has your token. See `docs/40-ANDROID.md` §6 and
§8.

## Attribution & terms

Cesium ion's free tier covers development use. Shipping a game requires complying with [Cesium ion terms](https://cesium.com/legal/terms-of-service/) and showing data attribution (Cesium, Bing Maps, OpenStreetMap). Cesium for Unity renders its attribution overlay automatically — do not disable it.

### Selling the game

Beyond the licence, there is a **cost** question that only appears at scale: the terrain is not in the build, so every copy sold is another client streaming tiles against your ion account, for as long as they play. A successful launch is a bigger bill than a quiet one, and nothing in the game currently caps or caches that.

This is the first item in **`docs/36-STEAM.md` §1a**, and it needs settling — with Cesium, in writing — before a release date means anything.

## Useful links

- Cesium for Unity quickstart: https://cesium.com/learn/unity/unity-quickstart/
- API reference: https://cesium.com/learn/cesium-unity/ref-doc/
- Change log: https://cesium.com/learn/cesium-unity/ref-doc/changes.html
- Cesium ion asset catalogue: https://ion.cesium.com/assetdepot


---

## The credit overlay

Cesium for Unity creates its own screen-space canvas for the ion attribution -
a logo and a credit line - lazily, the first time a tileset has something to
attribute. Left alone it lands on top of the game's HUD, because it is created
after our canvas and sorts above it.

`Map/CesiumCreditStyler.cs` pins the whole block — the ion logo, the credit line
and the "upgrade" prompt — to the **bottom-left corner**, scales it down to
**0.4%** (about a pixel square), drops it to 5% opacity, stops it taking clicks,
and sorts the canvas at -500 so every canvas the game draws is in front of it.

The bottom-left is the corner the editor's **rail** stands in: 232 px of opaque
nav, drawn on the game canvas, which sorts in front of this one. The credit is
therefore behind the side menu — as far out of the way as a thing can be got
without deleting it.

Position is an **anchor** change, not just a scale one. The package pins each
piece to whichever corner its own prefab chose, and shrinking about a top-left
pivot leaves a one-pixel mark in the top-left; every direct child of the credit
canvas is re-anchored to (0, 0) with a matching pivot.

**It keeps re-applying.** The credit system does not exist when the map is built,
and it rebuilds its children as credits come and go — a new tileset, a style
change, the data-attribution popup opening — so a one-shot pass is undone by the
next rebuild. The twenty-second budget bounds the *search* only; once the system
is found it is restyled once a second for as long as the map is up, from a cached
reference rather than a repeated `GameObject.Find`.

The scale is applied as a `localScale` on the credit's own children, **not** as
the canvas's `scaleFactor`. A scale factor that small asks uGUI's dynamic font
for a zero-point glyph and blows the canvas rect up to a hundred thousand units;
scaling the transform draws the same mesh smaller and asks nothing of the font.

**It is deliberately not removed.** Cesium ion's terms of service require the
attribution to be present, and a build that deleted it would be shipping in
breach of the licence its terrain streams under. What is adjustable is how loudly
it shouts — and at a pixel behind the rail it is as quiet as a thing can be while
still being on the screen at all.

Whether that is still *attribution* is a licence question and not a code one.
**If the project's ion terms are ever reviewed, `CesiumCreditStyler` is the class
to look at**: raising `Scale` alone undoes the whole of it, and the position and
opacity are one constant each beside it.
