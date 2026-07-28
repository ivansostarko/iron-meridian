---
name: cesium-maps
description: Cesium for Unity work — tilesets, georeferencing, tokens, imagery, terrain sampling. Use when touching MapManager, GeoUtils, anchors, or map/token docs.
---

# Cesium maps in Iron Meridian

## Setup facts

- Package `com.cesium.unity` **1.24.0** from scoped registry `https://unity.pkg.cesium.com` (declared in `Packages/manifest.json`).
- Ion assets used: `1` Cesium World Terrain, `2` Bing Aerial imagery (as `CesiumIonRasterOverlay` on the terrain), `96188` OSM Buildings (hidden in 2D view mode).
- Token resolution: `CesiumTokenConfig.GetToken()` — `Assets/StreamingAssets/cesium-token.txt` first (git-ignored), then the code constant. Applied per-tileset via `Cesium3DTileset.ionAccessToken`. **Never log, print, or commit a token.**

## Coordinate rules

- Persist WGS84 lat/lon/height only. `MapSaveData` and `UnitState` are geodetic.
- lat/lon ⇄ Unity: `GeoUtils.GeoToUnity` / `UnityToGeo` (wraps ECEF transforms; note Cesium's `double3` order is **lon, lat, height**).
- Distances/bearings: `GeoUtils.DistanceKm` / `BearingDeg` (haversine) — never Vector3 distance for gameplay ranges.
- Terrain height: `GeoUtils.SampleTerrainHeight` raycasts Cesium physics meshes from 9 km altitude; it can miss un-streamed tiles — always keep a fallback and re-clamp when the unit next moves.

## Gotchas

- `Cesium3DTileset.createPhysicsMeshes = true` is required for unit placement and line drawing; changing it breaks all raycast interaction.
- Moving the `CesiumGeoreference` origin rebases Unity world space — recompute cached Vector3s (lines call `Rebuild()`, units re-anchor via `CesiumGlobeAnchor`).
- Keep `maximumScreenSpaceError` ~16; lowering it sharply increases tile load and memory.
- Attribution overlay is legally required — never disable it.
