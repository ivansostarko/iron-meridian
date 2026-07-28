# Cesium 3D Maps & the ion API Token

## What Cesium does in Iron Meridian

[Cesium for Unity](https://cesium.com/platform/cesium-for-unity/) streams a full 3D globe into the game at runtime:

| Layer | Cesium ion asset ID | Used for |
|---|---|---|
| Cesium World Terrain | `1` | 3D terrain relief |
| Bing Maps Aerial imagery | `2` | Satellite imagery draped on the terrain |
| Cesium OSM Buildings | `96188` | 3D buildings (visible in 3D view mode only) |

`MapManager.cs` creates a `CesiumGeoreference` centred on the map's lat/lon (Lyon by default: **45.7640 N, 4.8357 E**) plus the three tilesets above. Physics meshes are enabled so units and line points can be placed by raycasting the terrain.

The package itself is declared in `Packages/manifest.json` via Cesium's scoped registry:

```json
"scopedRegistries": [
  { "name": "Cesium", "url": "https://unity.pkg.cesium.com", "scopes": ["com.cesium.unity"] }
],
"dependencies": { "com.cesium.unity": "1.24.0" }
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

## Attribution & terms

Cesium ion's free tier covers development use. Shipping a game requires complying with [Cesium ion terms](https://cesium.com/legal/terms-of-service/) and showing data attribution (Cesium, Bing Maps, OpenStreetMap). Cesium for Unity renders its attribution overlay automatically — do not disable it.

## Useful links

- Cesium for Unity quickstart: https://cesium.com/learn/unity/unity-quickstart/
- API reference: https://cesium.com/learn/cesium-unity/ref-doc/
- Change log: https://cesium.com/learn/cesium-unity/ref-doc/changes.html
- Cesium ion asset catalogue: https://ion.cesium.com/assetdepot
