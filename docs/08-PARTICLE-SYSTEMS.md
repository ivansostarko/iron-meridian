# Particle Systems

How Iron Meridian draws fire, smoke, explosions and dust — what exists, where each effect is used in the game, and how to add a new one.

> **Keep this file current.** Every new particle effect, every new call site, and every change to the catalogue must be reflected here in the same commit. See [Rules](#rules) at the bottom.

---

## 1. Architecture

All particle effects go through one system. Gameplay code never builds a `ParticleSystem` itself and never touches a prefab directly.

```
Assets/Scripts/Vfx/
  VfxCatalog.cs     VfxId enum + the catalogue row for each effect (prefab, scale, life, priority)
  VfxSystem.cs      the only entry point: resolve → anchor → scale → budget
  VfxInstance.cs    handle for a live effect; owns screen-size culling
  ProceduralVfx.cs  code-built fire/smoke/explosion/impact/dust fallbacks
Assets/Editor/
  VfxInstaller.cs   Tools > Iron Meridian > Install VFX Prefabs
```

Call sites use exactly three methods:

```csharp
// One-shot at a geodetic position, sitting on the terrain.
VfxSystem.Play(VfxId.Explosion, lat, lon, scaleMultiplier);

// Looping effect parented to a moving object; dies with its parent.
VfxInstance fire = VfxSystem.Attach(VfxId.FireMedium, unitTransform);
fire.Stop();

// Composite: detonation + a wreck that burns and smokes, then goes out.
VfxSystem.PlayWreck(lat, lon, severity01);
```

`VfxSystem` is a no-op when it has not been initialised, so effects are safe to call from any scene and non-game scenes cost nothing.

### Design rules the system enforces

| Rule | Why |
|---|---|
| **Positions are geodetic.** World-anchored effects get a `CesiumGlobeAnchor` at lat/lon + sampled terrain height. | Same as units and lines (`docs/07-ARCHITECTURE.md`). An effect placed in Unity world space drifts off the globe as the origin shifts. |
| **Effects are authored at ~1 world unit**, then scaled by `VfxDef.scaleMeters`. | Effect packs are built at human scale. A 2 m camp fire is sub-pixel when the strategic camera sits 20 km up. Fires here are 100–280 m across. |
| **`ParticleSystemScalingMode.Hierarchy` on every system.** | Makes the root transform scale drive particle *size and velocity* together, so one number converts author scale to map scale. |
| **Never hard-code particle values at the call site.** | The catalogue is the single source of truth; tuning happens in one file. |
| **Fallback always exists.** | The project ships no binary prefabs of its own; the game must look correct with zero asset dependencies. |

---

## 2. Effect catalogue

Defined in `VfxCatalog.cs`. `scaleMeters` is the on-map diameter; call sites pass a multiplier on top (typically 0.6–1.5, scaled by formation size).

| `VfxId` | Meaning | Scale | Life | Priority | Source |
|---|---|---|---|---|---|
| `Explosion` | Detonation — unit destroyed, ammo dump hit | 320 m | 2.6 s | 100 | procedural |
| `ImpactBurst` | Rounds landing on a unit under fire | 110 m | 1.1 s | 40 | procedural |
| `WeaponFire` | Firing signature at the shooter | 80 m | 0.7 s | 20 | procedural |
| `FireSmall` | Company/battalion burning | 100 m | loops | 60 | `VFX_Fire_01_Small_Smoke` |
| `FireMedium` | Brigade burning, struck vehicle park | 170 m | loops | 70 | `VFX_Fire_01_Medium_Smoke` |
| `FireLarge` | Division-scale conflagration, fuel/ammo fire | 280 m | loops | 80 | `VFX_Fire_01_Big_Smoke` |
| `GroundFire` | Burning ground — wreck site, torched terrain | 230 m | loops | 55 | `VFX_Fire_Floor_01_Smoke` |
| `SmokePlume` | Column of smoke off a wreck or fire | 300 m | loops | 50 | procedural |
| `SmokeScreen` | Deliberate obscuration (artillery / smoke generators) | 620 m | loops | 65 | procedural |
| `Dust` | Kicked up by movement or a deployment drop | 140 m | 1.5 s | 10 | procedural |

**Priority** decides who dies when the concurrent-effect budget is full: lowest priority is evicted first, oldest among equals. Dust is deliberately the cheapest thing on the map, an explosion the most protected.

`VfxCatalog.FireForScale(scale01)` picks Small / Medium / Large from a 0..1 formation size, so a burning squad and a burning army do not look the same.

---

## 3. Where each effect is used in the game

This is the complete list of call sites. **Add a row here whenever you add one.**

### Combat

| Case | Effect | Trigger | File |
|---|---|---|---|
| A unit shoots | `WeaponFire` at the attacker | Every resolved exchange, throttled to one per `GameConfig.VfxWeaponFireCooldownSeconds` (2.6 s) per unit | `CombatSystem.Exchange` → `UnitActor.NotifyFiring` |
| A unit takes damage | `ImpactBurst` at the defender | Every `ApplyDamage`, throttled to one per `GameConfig.VfxImpactCooldownSeconds` (1.8 s) per unit | `UnitActor.ApplyDamage` |
| A unit is badly mauled | `FireSmall`/`Medium`/`Large` **attached** to the unit | Strength drops to `GameConfig.VfxBurningStrength` (0.45) or below; cleared if it recovers above | `UnitActor.RefreshBurning` |
| A unit is destroyed | `PlayWreck`: `Explosion`, then `Fire*` + `SmokePlume` | On death; burns for 14–32 s scaled by echelon, then goes out | `UnitActor.Die` |
| A saved unit loads below strength | `Fire*` attached immediately | On spawn — damage is part of the map, not only something that happens live | `UnitActor.Build` |

Throttling matters: combat ticks once a second against **every** opposing unit in range, so an unthrottled effect per exchange would blanket the front line within seconds.

### Movement

| Case | Effect | Trigger | File |
|---|---|---|---|
| A formation on the march | `Dust` | One puff every 500 m of ground covered (distance-based, so trail spacing is speed-independent) | `UnitMover.Update` |

### Deployment

| Case | Effect | Trigger | File |
|---|---|---|---|
| Unit dragged from the palette onto the map | Shockwave ring + dust | `GameController.OnPaletteDrop` | `DeployEffect` |
| Units pasted (Ctrl+V) | Shockwave ring + dust | `GameController.PasteClipboard` | `DeployEffect` |

`DeployEffect` predates `VfxSystem` and owns its own shockwave ring, which the catalogue has no equivalent for. It is a migration candidate, not a second effects system — do not add new effects to it.

### Defined but not yet triggered

| Effect | Intended case |
|---|---|
| `SmokeScreen` | Artillery-delivered or generator-laid obscuration as a unit order; should also reduce spotting once vision modelling exists |
| `GroundFire` | Terrain set alight independently of a wreck — incendiary strikes, burning fuel dumps as map objects |

---

## 4. Render pipeline — read this before using the authored pack

**The imported pack ([Free Fire VFX URP](https://assetstore.unity.com/packages/p/free-fire-vfx-urp-266226), `Assets/Vefects/`) is URP-only, and this project runs the built-in render pipeline.**

Its shaders declare `Tags { "RenderPipeline"="UniversalPipeline" }`, include `com.unity.render-pipelines.universal/...` HLSL, and set `Fallback Off`. Under the built-in pipeline they have no matching sub-shader, so the particles draw magenta.

`VfxSystem` detects this: it checks `Shader.isSupported` on every material of a loaded prefab and, if any fails, logs one warning and falls back to `ProceduralVfx` for that effect. `VfxInstaller` reports the same at install time. **So the game looks correct today — it just isn't using the pack yet.**

Three ways forward:

1. **Stay procedural** (current state). No dependency, no pipeline change; the look is stylised rather than photoreal.
2. **Move the project to URP.** Cesium for Unity supports URP. This is a real migration: every runtime material goes through `RuntimeMaterials` (`Sprites/Default` etc.), which would need URP equivalents, and lighting/post would need re-tuning. Not a change to make casually — see `Assets/Scripts/Core/RuntimeMaterials.cs`.
3. **Re-target the pack's materials** to built-in shaders (`Particles/Standard Unlit`). Cheapest path to using the pack's textures and particle timing, but the Amplify-authored distortion, erosion and heat-haze effects are lost.

Also note the pack has **no explosion, standalone smoke or dust prefab** — it is a fire pack. Those catalogue rows are procedural regardless of pipeline. Its audio (`SFX_FireBig/Medium/Small_L.wav`) is not wired up; fire audio via `AudioManager` is an open item.

### Installing authored prefabs

Scenes and prefabs are generated in this project, so there is no serialised field anywhere to reference an asset — `VfxSystem` resolves everything through `Resources.Load`.

Run **Tools → Iron Meridian → Install VFX Prefabs**. It copies the prefabs named in the catalogue into `Assets/Resources/VFX/`. `AssetDatabase.CopyAsset` preserves GUID references, so each copy is a single file that still points at the pack's own materials and textures — the pack is not duplicated. Effects with no installed prefab silently use the procedural fallback, so running this is optional.

---

## 5. Performance

The strategic camera can show a whole front, so effect count is bounded rather than trusted.

| Guard | Value | Where |
|---|---|---|
| Concurrent effect cap, with priority eviction | `GameConfig.VfxMaxConcurrent` = 48 | `VfxSystem.MakeRoom` |
| Looping effects stop emitting when sub-pixel | `GameConfig.VfxMinApparentSize` = 0.005 (0.5 % of screen height), re-checked 4×/s | `VfxInstance.Update` |
| Per-unit throttles on impact and firing effects | 1.8 s / 2.6 s | `GameConfig` |
| Wreck fires burn out rather than persisting | 14–32 s | `GameConfig.VfxWreck*` |
| One shared material for all procedural effects | — | `ProceduralVfx.PuffMaterial` |

Effects are **not pooled** — each spawn allocates a `GameObject`. The cap and throttles keep the churn low enough that this has not mattered; if profiling says otherwise, pooling belongs in `VfxSystem.Populate`.

World-anchored effects (wrecks) deliberately outlive their unit, so `GameController.LoadMap` calls `VfxSystem.StopAll()` on reload.

---

## 6. Adding a new particle effect

1. **Add a `VfxId`** in `VfxCatalog.cs`, named for what it *means* in the game, not for the asset that draws it.
2. **Add its catalogue row**: prefab path (or `null`), fallback kind, `scaleMeters`, `lifeSeconds` (`0` = loops until stopped), tint, priority.
3. **Add a procedural fallback** in `ProceduralVfx` if none of the existing kinds fits. Author at ~1 world unit; `VfxSystem` handles the scale.
4. **Add tuning constants** to `GameConfig` if the effect needs thresholds or cooldowns — never magic numbers at the call site.
5. **Call it** via `VfxSystem.Play` / `Attach` / a composite helper. Throttle anything that can fire per combat tick.
6. **If it uses an authored prefab**, put the prefab name in the catalogue and run **Tools → Iron Meridian → Install VFX Prefabs**.
7. **Update this file** — the catalogue table in §2 *and* the usage table in §3.

---

## Rules

1. **This document is the register of every particle effect in the game.** Adding, removing or repurposing an effect, or adding a new call site, is not done until §2 and §3 here are updated in the same commit.
2. Gameplay code goes through `VfxSystem`. No `new GameObject().AddComponent<ParticleSystem>()` outside `Assets/Scripts/Vfx/`.
3. Effect tuning lives in `VfxCatalog` and `GameConfig`, never at the call site.
4. Every authored effect needs a procedural fallback — the game must run with the asset packs removed.
5. Anything that can be triggered by a combat tick must be throttled.

## Related

`docs/07-ARCHITECTURE.md` (script map) · `docs/03-GAMEPLAY.md` (combat model) · `docs/02-CESIUM.md` (georeferencing)
