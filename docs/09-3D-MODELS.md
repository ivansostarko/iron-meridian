# 3D Models

The register of every 3D model in Iron Meridian — where it came from, how it is imported, which units it represents, and where it is shown.

> **Keep this file current.** Every new model, animation clip or call site must be recorded here in the same commit. See [Rules](#rules) at the bottom.

---

## 1. Model register

| Model id | Prefab (Resources) | Source asset | Animations | Used for |
|---|---|---|---|---|
| `soldier_rifleman` | `Models/Soldier_Rifleman` | [Low Poly Soldiers Demo](https://assetstore.unity.com/packages/3d/characters/low-poly-soldiers-demo-73611) — `Soldier_demo.FBX` | `combat_idle`, `combat_run`, `combat_shoot` | Fallback for every Core Ground type with no equipment of its own |
| `air_defence_radar` | `Models/AirDefenceRadar` | [Anti-Air Defense Radar](https://assetstore.unity.com/packages/3d/environments/anti-air-defense-radar-100032) | static | `ad_radar`, `air_defence` |
| `field_artillery` | `Models/FieldArtillery` | [Military Prop Pack: Defense](https://assetstore.unity.com/packages/3d/props/weapons/military-prop-pack-defense-321415) | static | `artillery`, `rocket_artillery` |
| `sam_launcher` | `Models/SamLauncher` | [Homing Missile](https://assetstore.unity.com/packages/tools/behavior-ai/homing-missile-307255) | static | `sam` |
| `military_truck` | `Models/MilitaryTruck` | [ZIL-130 Military Truck](https://assetstore.unity.com/packages/3d/vehicles/land/zil-130-military-truck-208991) | static | `transport`, `logistics`, `supply` |
| `main_battle_tank` | `Models/Leopard2` | [Tank Leopard 2](https://assetstore.unity.com/packages/3d/vehicles/land/tank-leopard2-264329) | static | `armour` |
| `scout_car` | `Models/ScoutCar` | [M3A1 Scout Car](https://assetstore.unity.com/packages/3d/vehicles/land/m3a1-scout-car-53149) | static | `recon` |
| `attack_helicopter` | `Models/AttackHelicopter` | [RTS Modern Combat Vehicle Pack Free](https://assetstore.unity.com/packages/3d/vehicles/rts-modern-combat-vehicle-pack-free-281758) — `MSH_N2_LE.fbx` | static; rotors spun by `RotorSpinner` | **Not a unit** — flown by `AirStrikeSystem` |
| `strike_fighter` | `Models/StrikeFighter` | RTS Modern Combat Vehicle Pack Free — `FA_N26_LE.fbx` | static | **Not a unit** — flown by `AirStrikeSystem` |
| `kamikaze_drone` | `Models/KamikazeDrone` | [Professional Assets DronePack](https://assetstore.unity.com/packages/p/free-pack-117641) — `_FBX Mesh [Quad].FBX` | static; propellers spun by `RotorSpinner` | **Not a unit** — flown by `UavStrikeSystem`; see docs/19-UAV-STRIKES.md |
| `stealth_bomber` | `Models/StealthBomber` | [Hessburg — Stealth Bomber](https://assetstore.unity.com/packages/package/56765) — `Stealth_Bomber.fbx` | static | **Not a unit** — flown by `AirStrikeSystem`; see docs/18-AIR-STRIKES.md |

**Four models are aircraft rather than units.** `stealth_bomber`, `strike_fighter`, `attack_helicopter` and `kamikaze_drone` have no entry in `UnitModelLibrary.Overrides` and `Resolve()` never returns them — no formation is represented by a B-2 or a quadcopter. They are in the library because it is the only sanctioned way to reach a model prefab (golden rule 10) and because the installer builds their prefabs from that list.

**Rotors without a rig.** The helicopter and the drone ship their rotors as separate named meshes (`3_Screw_Main`, `3_Screw_Back`, `Quad Propeller 1/2`), which `RotorSpinner` finds by name substring and turns. No skeleton, no clip, no Animator — which is what makes it possible at all under the project's runtime-only constraints.

**Original note on `stealth_bomber`:** It has no entry in `UnitModelLibrary.Overrides` and `Resolve()` never returns it — no formation is represented by a B-2. It is in the library anyway because the library is the only sanctioned way to reach a model prefab (golden rule 10) and because the installer builds its prefab from that list. `BomberRun.LoadModel` fetches it by id.

**Not yet modelled:** every `Drone` category unit. `UnitModelLibrary.Resolve` returns `null` for them and the preview shows an explicit "no model yet" message rather than a misleading infantryman.

**Static vs animated.** Only the rifleman has a rig. The vehicles and props are static meshes, which is what they should be — the preview's turntable is their motion. `idleClip` is `null` and `animated` is `false` for them, so the installer skips the Legacy-rig conversion (forcing a rig type onto a mesh with no skeleton reimports the pack for nothing) and `ModelPreview` does not warn about a clip that was never meant to exist.

### Source assets

| Asset | Expected location | Status |
|---|---|---|
| Low Poly Soldiers Demo | `Assets/LowPolySoldiers_demo/` | **Imported.** `models/Soldier_demo.FBX` (Biped rig, `Bip001` root), `animation/demo_combat_{idle,run,shoot}.FBX`, two materials + TGA textures |
| Anti-Air Defense Radar | `Assets/Radar/` | **Imported** — `Radar.FBX` |
| Military Prop Pack: Defense | `Assets/Defensive_props/` | **Imported** — `models/Defensive_props.fbx` |
| Homing Missile | `Assets/homing missile/` | **Imported** — `Models/Missiles_Pack.FBX`. Its demo *prefabs* are broken (see below); the mesh is fine |
| ZIL-130 Military Truck | `Assets/ZIL130_MilitaryTruck/` | **Imported** — `Meshes/ZIL130.fbx` |
| Tank Leopard 2 | `Assets/Kucher/Tank Leopard2/` | **Imported** — `Models/Leopard2.fbx` |
| M3A1 Scout Car | `Assets/M3A1 Scout Car/` | **Imported** — `WW2_M3A1_Scout_Car.FBX` |
| Hessburg — Stealth Bomber | `Assets/Hessburg - Stealth Bomber/` | **Imported** — `Stealth_Bomber.fbx`. Shipped as nested `.unitypackage` archives; see below |

> **Audited 2026-08-14.** All seven packs are now present. Three of them ship
> their mesh under a name none of the original `sourceCandidates` matched, so the
> installer was silently skipping them — the candidate lists have been corrected:
>
> | Model | Actual mesh file | Candidate added |
> |---|---|---|
> | `field_artillery` | `Defensive_props.fbx` | `Defensive_props` |
> | `sam_launcher` | `Missiles_Pack.FBX` | `Missiles_Pack` |
> | `scout_car` | `WW2_M3A1_Scout_Car.FBX` | `WW2_M3A1_Scout_Car` (+ `_NoRoof`) |
>
> `air_defence_radar` (`Radar.FBX`), `military_truck` (`ZIL130.fbx`),
> `main_battle_tank` (`Leopard2.fbx`) and `stealth_bomber` (`Stealth_Bomber.fbx`)
> already matched. Re-run **Install Unit Models** to build all seven.

When a pack is **registered but not present**, the entry, its unit assignments
and the installer support are all still in place, and it builds its prefab the
moment the pack arrives. Until then, selecting one of those unit types shows
*"Model 'Models/Leopard2' is not installed. Run Tools > Iron Meridian > Install
Unit Models."* rather than a soldier standing in for a tank, because a silent
wrong model is worse than an explicit missing one.

### Broken demo prefabs in imported packs

Some packs import with their **demo prefabs** referencing assets that were never
brought in, which Unity reports as *"The file might be corrupt or have a missing
Variant parent or nested Prefabs."* Known cases: FORGE3D — Sci-Fi Effects
(`Missile 1`, `Sci-Fi Effects - Missile 2`, `Missile Impact`, `Assembled Turrets
… Variant`) and Homing Missile (`homing_missile.prefab`). All are Prefab Variants
whose parents are absent from the project.

**This does not affect Iron Meridian.** Nothing in `Assets/Scripts` references a
FORGE3D or Homing Missile prefab — the model register uses the packs' **meshes**,
which import fine. The errors are noise from demo content. Fix them by
re-importing the pack completely from the Package Manager, or delete the demo
folders if the pack is only wanted for its meshes.

### Finding the source mesh

The installer does not know a pack's internal file names, and packs rename their
meshes between versions. Each entry therefore carries **`sourceCandidates`**: a
list of file names (no extension) tried in order.

If a pack is imported and the installer still reports it missing, its mesh is
named something not on the list. The warning names every candidate it tried —
add the real file name to that entry in `UnitModelLibrary.cs` and re-run. That is
the only edit needed; nothing else in the pipeline cares what the file is called.

---

## 2. How models are wired

```
Assets/Scripts/Models/
  UnitModelLibrary.cs   unit definition → model; the model register in code
  ModelPreview.cs       renders a model into a uGUI panel (RenderTexture + own camera)
Assets/Editor/
  ModelInstaller.cs     Tools > Iron Meridian > Install Unit Models
```

`UnitModelLibrary` is the single lookup. Call sites never load a prefab directly:

```csharp
UnitModelDef model = UnitModelLibrary.Resolve(unitDefinition);   // null when none exists
```

Assignment order: an explicit per-unit-id entry in `Overrides` wins; otherwise `DefaultFor` applies the category rule above. As real models arrive, add them to `Models` and list them in `Overrides` — **no call site changes**.

### Why Legacy animation

The project generates every scene and prefab from code, so there is **no Animator Controller asset to reference, and none can be created at runtime** (`AnimatorController` is an editor-only type). Legacy `Animation` accepts a clip handed to it directly, which is the only fully runtime-driven path — and it is consistent with the project's legacy Input and legacy `Text` stack.

So `ModelInstaller` sets the source FBX rigs to `ModelImporterAnimationType.Legacy`. **This modifies the imported pack's import settings.** That is deliberate and is what makes the clips usable; it is reversible from the FBX inspector.

### Why a generated prefab

`Resources.Load` is the only runtime lookup path available, and the pack does not live under `Resources`. `ModelInstaller` generates `Assets/Resources/Models/Soldier_Rifleman.prefab`: an instance of the original FBX carrying an `Animation` component with all three clips registered under the game's own names (`combat_idle`, …). The prefab references the FBX by GUID, so **the mesh and textures are not duplicated** — it is a small file.

This is a *generated* asset, in the same spirit as the scenes `ProjectBootstrap` generates. Do not hand-edit it; re-run the installer.

### Installing

**Tools → Iron Meridian → Install Unit Models**

It will: find the source FBXs, switch their rigs to Legacy (reimporting if needed), collect the legacy clips, and write the prefab. It logs exactly what it installed and warns for anything it could not find.

---

## 3. Where models are shown

| Screen | What it shows | File |
|---|---|---|
| Testing → Units List | Detail panel on the right: the selected unit type's model playing `combat_idle`, orbitable by drag, zoomable by scroll | `UnitsListUI.BuildDetailPanel` → `ModelPreview` |

**Add a row here whenever a model appears somewhere new.**

### How `ModelPreview` works

uGUI cannot draw a mesh, so the model is rendered offscreen and displayed as a texture:

1. A rig is parked at **Y = -5000**, far outside the menu camera's default 1000 far-clip, so it can never leak into the actual scene.
2. A dedicated camera (28° FOV — a long lens, less distortion on a figure) with two camera-parented directional lights films it.
3. It renders to a `RenderTexture` at 1.5× the panel size, shown by a `RawImage`.
4. Framing is computed from the model's combined renderer **bounds**, not per-model magic numbers, so a new model drops in correctly sized. `UnitModelDef.framing` is a nudge for the rare model that sits oddly.
5. The camera is disabled whenever no model is shown, so an empty panel costs nothing.
6. A **silhouette outline** is added to the instantiated model (`ModelPreview.AddRimOutline`). A dark vehicle on a dark panel otherwise reads as a hole rather than an object, and no amount of relighting fixes that from every orbit angle.

Degradation is explicit at every step: no model for this unit, prefab not installed, or no legacy clip each produce a specific on-screen or console message — never a blank box.

### The outline (QuickOutline)

The rim uses the imported [QuickOutline](https://assetstore.unity.com/packages/p/quick-outline-115488) asset (`Assets/QuickOutline/`) in `OutlineVisible` mode — `OutlineAll` would show the outline through the model's near side and turn a solid figure into a wireframe.

This is a **hard compile-time dependency**: `ModelPreview.AddRimOutline` names the global `Outline` type directly, so removing the package means removing that method with it.

QuickOutline works here and only here. It extrudes geometry along vertex normals, which needs a real mesh with varying normals — it cannot outline the map's unit icons, which are single camera-facing quads whose normals all point at the viewer. Those trace their own texture alpha instead; see `Assets/Resources/Shaders/IconOutline.shader` and docs/07-ARCHITECTURE.md.

---

## 4. Adding a new 3D model

1. **Import the pack** into `Assets/<PackName>/`. Note its licence and Asset Store URL — both go in the table in §1.
2. **Add a `UnitModelDef`** to `UnitModelLibrary.Models` with its `resourcePath`, `sourceAsset` and idle clip.
3. **Assign it**: add per-unit-id entries to `UnitModelLibrary.Overrides`, or extend `DefaultFor` if it is a category-wide stand-in.
4. **Register the source FBXs** in `ModelInstaller` (`SourceModelFbx` / `Clips`) if the installer must generate a prefab for it.
5. **Run Tools → Iron Meridian → Install Unit Models.**
6. **Check the preview**: Play → Testing → Units List → click the unit. Console must be clean.
7. **Update this file** — the register in §1 *and*, if it appears somewhere new, the usage table in §3.

### Animation clip naming

Game-facing names live in `ModelClips` (`combat_idle`, `combat_run`, `combat_shoot`) and are independent of the source file names. A new pack maps its own clips onto these names in `ModelInstaller.Clips`, so nothing downstream cares what the artist called them.

---

## 5. Known gaps

- Only one model exists; every ground unit type shares it. Unit-specific models (armour, artillery, drones) are the obvious next step.
- `combat_run` and `combat_shoot` are installed and registered but nothing plays them yet — the natural users are a moving unit (`UnitMover`) and a firing unit (`CombatSystem.Exchange`), which today show only particle effects (`docs/08-PARTICLE-SYSTEMS.md`).
- Models appear only in the Units List. Units on the map are still APP-6 icon billboards (`UnitActor`), which is correct for an operational-level view — 3D models on the map would be a deliberate design change, not an oversight.

---

## Rules

1. **This document is the register of every 3D model in the game.** Adding, replacing or reassigning a model, adding an animation clip, or showing a model in a new place is not done until §1 and §3 here are updated in the same commit.
2. Gameplay and UI code resolves models through `UnitModelLibrary`. No `Resources.Load` of a model prefab at a call site.
3. Model prefabs under `Assets/Resources/Models/` are **generated** by `ModelInstaller`. Do not hand-edit them.
4. Every model must degrade gracefully — a missing model, prefab or clip shows a specific message, never a blank panel or a magenta box.
5. Record the source asset and its Asset Store URL for every imported pack, so licensing stays traceable.

## Related

`docs/07-ARCHITECTURE.md` (script map) · `docs/04-UNITS.md` (unit catalogue) · `docs/08-PARTICLE-SYSTEMS.md` (effects register)
