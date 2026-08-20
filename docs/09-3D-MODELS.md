# 3D Models

The register of every 3D model in Iron Meridian — where it came from, how it is imported, which units it represents, and where it is shown.

> **Keep this file current.** Every new model, animation clip or call site must be recorded here in the same commit. See [Rules](#rules) at the bottom.

---

## 1. Model register

| Model id | Prefab (Resources) | Source asset | Animations | Used for |
|---|---|---|---|---|
| `soldier_rifleman` | `Models/Soldier_Rifleman` | [Low Poly Soldiers Demo](https://assetstore.unity.com/packages/3d/characters/low-poly-soldiers-demo-73611) — `Soldier_demo.FBX` | `combat_idle`, `combat_run`, `combat_shoot` | Fallback for every Core Ground type with no equipment of its own |
| `air_defence_radar` | `Models/AirDefenceRadar` | [Anti-Air Defense Radar](https://assetstore.unity.com/packages/3d/environments/anti-air-defense-radar-100032) | static | `ad_radar`, `air_defence`, `air_surveillance_radar`, `surveillance_radar`, `counter_battery_radar`, `target_acquisition` |
| `field_artillery` | `Models/FieldArtillery` | [Military Prop Pack: Defense](https://assetstore.unity.com/packages/3d/props/weapons/military-prop-pack-defense-321415) | static | `artillery`, `self_propelled_artillery`, `towed_artillery`, `rocket_artillery`, `mortar`, `spaag` |
| `sam_launcher` | `Models/SamLauncher` | [Homing Missile](https://assetstore.unity.com/packages/tools/behavior-ai/homing-missile-307255) | static | `sam`, `mrad`, `lrad`, `missile_defence`, `shorad`, `surface_to_surface_missile`, `deep_precision_strike`, `coastal_defence_missile` |
| `military_truck` | `Models/MilitaryTruck` | [ZIL-130 Military Truck](https://assetstore.unity.com/packages/3d/vehicles/land/zil-130-military-truck-208991) | static | `transport`, `logistics`, `supply`, `ammunition`, `fuel_pol`, `water_supply`, `recovery`, `medevac`, `bridging`, `movement_control` |
| `main_battle_tank` | `Models/Leopard2` | [Tank Leopard 2](https://assetstore.unity.com/packages/3d/vehicles/land/tank-leopard2-264329) | static | `armour`, `combined_arms` |
| `scout_car` | `Models/ScoutCar` | [M3A1 Scout Car](https://assetstore.unity.com/packages/3d/vehicles/land/m3a1-scout-car-53149) | static | `recon`, `armoured_recon` |
| `attack_helicopter` | `Models/AttackHelicopter` | [RTS Modern Combat Vehicle Pack Free](https://assetstore.unity.com/packages/3d/vehicles/rts-modern-combat-vehicle-pack-free-281758) — `MSH_N2_LE.fbx` | static; rotors spun by `RotorSpinner` | Flown by `AirStrikeSystem`; also the whole rotary-wing branch: `attack_helicopter`, `recon_helicopter`, `transport_helicopter`, `utility_helicopter`, `medevac_helicopter` |
| `strike_fighter` | `Models/StrikeFighter` | RTS Modern Combat Vehicle Pack Free — `FA_N26_LE.fbx` | static | Flown by `AirStrikeSystem`; also the fixed-wing branch: `cas_aircraft`, `strike_aircraft`, `fighter_aircraft`, `isr_aircraft`, `aewc`, `aerial_refuelling`, `ew_aircraft` |
| `kamikaze_drone` | **none — built in code** | `ProceduralModels.BuildKamikazeDrone` | `combat_idle`, built at runtime: propeller spin + airframe sway | Flown by `UavStrikeSystem` (docs/19-UAV-STRIKES.md); also `fpv_attack_uas`, `loitering_munition`, `interceptor_uas`, `cargo_uas`, `decoy_uas` |
| `recon_drone` | **none — built in code** | `ProceduralModels.BuildReconDrone` | `combat_idle`, built at runtime: propeller spin + airframe sway + **sensor turret sweep** (one revolution every 8 s) | Flown by `UavStrikeSystem` as the reconnaissance sortie (docs/19-UAV-STRIKES.md); also the fixed-wing unmanned types `recon_uas`, `armed_uas`, `ew_uas`, `relay_uas` |
| `airlift_transport` | **none — built in code** | `ProceduralModels.BuildTransportAircraft` | `combat_idle`, built at runtime: four propellers turning + a very slight roll | Flies air supply drops (`SupplyRun`, docs/29-AIR-SUPPLY.md); also the `transport_aircraft` unit type, which used to borrow the strike fighter |
| `supply_bundle` | **none — built in code** | `ProceduralModels.BuildSupplyBundle` | `combat_idle`, built at runtime: a pendulum swing under the canopy | One air-dropped load under parachute (`ParachuteDrop`, docs/29-AIR-SUPPLY.md) |
| `shahed_drone` | `Models/ShahedDrone` | [ALSTRA INFINITE — Kamikaze Drones PolyPack Starter](https://assetstore.unity.com/packages/3d/vehicles/air/kamikaze-drones-polypack-starter-low-poly-asset-381716) — `StarterAsset_KamikazeDroneV1.fbx` | static; propeller spun by `RotorSpinner` | The Shahed UAV strike type (docs/19-UAV-STRIKES.md) |
| `stealth_bomber` | `Models/StealthBomber` | [Hessburg — Stealth Bomber](https://assetstore.unity.com/packages/package/56765) — `Stealth_Bomber.fbx` | static | **Not a unit** — flown by `AirStrikeSystem`; see docs/18-AIR-STRIKES.md |

**Missiles are built in code too.** `MissileRun` builds its airframe from primitives inline rather than through the library — a body, a nose and four fins. It has no `UnitModelLibrary` entry because no unit type is a missile and nothing else needs to reach it. See docs/20-MISSILE-SYSTEMS.md.

`InterceptorRun` — the surface-to-air missile air defence fires at a drone — is built the same way and for the same reasons, at 34 m nose to tail. See docs/24-AIR-DEFENCE.md.

**A drone that has been shot down keeps its model and loses its animation.** `DroneFall` takes the airframe over from the flight that was interrupted and **stops** every `Animation` on it: a wreck with its propeller still turning and its sensor turret still quartering the ground is the single detail that would give the whole thing away. The tumble is applied to the transform directly rather than through a clip, because it is a random rate per wreck and a clip is a shared authored thing.

**One model is an aircraft rather than a unit.** `stealth_bomber` has no entry in `UnitModelLibrary.Overrides` and `Resolve()` never returns it — no formation is represented by a B-2, and the strike airframes are picked from `AirStrikeCatalog` rather than from the unit catalogue. It is in the library because it is the only sanctioned way to reach a model prefab (golden rule 10) and because the installer builds its prefab from that list. `BomberRun.LoadModel` fetches it by id.

The other three airframes now serve double duty: the strike systems fly them, and the Air/Drone unit types in the catalogue are represented by them. A helicopter reads as a helicopter at map scale, which is the whole test — one airframe standing in for a branch is right in a way that a rifleman standing in for a helicopter never was.

**Rotors without a rig.** The helicopter and the Shahed airframe ship their rotors as separate named meshes (`3_Screw_Main`, `3_Screw_Back`, `Prop…`), which `RotorSpinner` finds by name substring and turns. No skeleton, no clip, no Animator — which is what makes it possible at all under the project's runtime-only constraints.

`RotorSpinner` matches by **substring**, which is a trap worth knowing about: a hub called `Propeller` containing blades called `PropellerA` and `PropellerB` gets all three spun independently, and the propeller comes apart. The procedural drone names its blades `BladeA`/`BladeB` for exactly this reason.

## Models built in code

`Assets/Scripts/Models/ProceduralModels.cs` builds a model from primitives instead of importing one. A `UnitModelDef` with a `proceduralId` has **no source FBX and no prefab**: `ModelInstaller` skips it (and says so rather than reporting it missing), and `UnitModelLibrary.CreateInstance` constructs it on demand.

**Why.** Everything else here comes from a store pack, is turned into a prefab by the installer, and stops existing the moment that pack is removed — which is exactly what happened to the kamikaze drone when `free-pack-117641` was taken out of the project. A procedural model has no pack to lose: it is a few dozen lines of geometry, it ships with the source, and it cannot be missing. Same argument `ProceduralVfx` and `ProceduralAudio` already make for effects and sound.

**Why it is legitimate rather than a placeholder.** At map scale a loitering munition is forty pixels across. What has to read is the silhouette — delta body, warhead nose, pusher propeller — and a silhouette is precisely what primitives are good at.

**Four models are built this way now**, and the two airlift ones follow the same argument as the drones below: a supply run must not be able to lose its aeroplane to a pack somebody removed. The transport is deliberately the opposite silhouette to every combat airframe — high wing, four turning propellers, upswept tail with a ramp — because a supply drop has to read as *not an attack* from the first frame. The bundle's canopy is a cone rather than a dome: at map zoom both are twelve pixels, but the cone has a point, and that is what makes it read as a parachute.

**Two of them are built to be told apart.** The `recon_drone` is the opposite of the `kamikaze_drone` in every respect that reads at that size: a long straight high wing against a swept delta, twin tail booms against a stubby body, a pale finish against an olive one, and a sensor turret under the nose where the other has a warhead. A player has to be able to tell in one glance whether the thing overhead is looking at them or coming for them, and colour alone will not carry that at forty pixels. The recon drone's clip adds a third motion for the same reason: the turret **sweeps**, once every eight seconds, which is what makes it read as a drone doing a job rather than a drone transiting.

**Animation.** The idle clip is an `AnimationClip` created at runtime and driven by legacy `Animation`. Clips *can* be built at runtime; Animator Controllers cannot, which is why the whole project is on the legacy path. Two details matter:

- **Rotation is animated as a quaternion**, on `localRotation.x/y/z/w`. `localEulerAngles` is a computed convenience on `Transform`, not a serialised property, so a curve bound to it is not reliably applied by legacy playback. Legacy `Animation` normalises quaternion curves as it applies them, so linear interpolation between keys is safe as long as the keys are close together — hence 45° steps on the propeller.
- **The sway is bound to a `Sway` child, not to the model root.** `DroneRun` and `ReconDroneRun` set the root's rotation to fly the aircraft; two things writing one transform is a fight whose winner depends on script execution order. Everything either clip animates hangs off that child, including the recon drone's sensor turret (`Sway/Sensor`).

**`UnitModelLibrary.CreateInstance` is the only way to build a model.** It was already golden rule 10 that call sites do not `Resources.Load` a prefab; it matters more now that a model can legitimately have no prefab at all, because a call site checking for one would decide the kamikaze drone was missing when it is the one model that cannot be. `BomberRun`, `DroneRun`, `MissileRun` and `ModelPreview` all go through it.

**Not yet modelled:** the `Naval` category outright, and `deep_strike_uas`. `UnitModelLibrary.Resolve` returns `null` for them and the preview shows an explicit "no model yet" message. The fixed-wing unmanned types that used to be in that list — `recon_uas`, `armed_uas`, `ew_uas`, `relay_uas` — now take `recon_drone`, which is the shape they actually are. Handing a MALE drone the quadcopter would be the same mistake as handing a drone the infantryman — the point was never "show something", it was "do not show a lie".

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

Assignment order: an explicit per-unit-id entry in `Overrides` wins; otherwise `DefaultFor` applies the category rule — `CoreGround` gets the stand-in rifleman, and `Drone`, `Air` and `Naval` get **nothing**, because none of them is standing on the ground and an infantryman would misrepresent all three. As real models arrive, add them to `Models` and list them in `Overrides` — **no call site changes**.

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
| Development → Units List | Detail panel on the right: the selected unit type's model playing `combat_idle`, orbitable by drag, zoomable by scroll. The AIR STRIKES and UAV STRIKES tabs preview their `modelId` the same way, through `ModelPreview.ShowModel` | `UnitsListUI.RefreshPreview` → `ModelPreview` |
| Map editor → in flight | The strike airframes over the map: bomber, fighter, helicopter, attack drones and the reconnaissance drone on its orbit | `BomberRun`, `DroneRun`, `ReconDroneRun`, `MissileRun` |
| Map editor → **GENERAL → SHOW UNIT 3D MODELS** | **Every formation on the map**, standing on the ground under its counter, in both scenario and battle mode. Off by default — see below | `UnitActor.SetModelsVisible` |
| Map editor → **an airdropped cache** | `supply_bundle` standing on the ground where a bundle landed. A dropped cache is drawn as a model; a hand-placed installation keeps its doctrinal symbol — see docs/29-AIR-SUPPLY.md | `LogisticsSite.BuildCacheModel` |
| Map editor → air defence | The interceptor climbing off a launcher, and the drone it hit tumbling down with its animation stopped | `InterceptorRun`, `DroneFall` (docs/24-AIR-DEFENCE.md) |

**Add a row here whenever a model appears somewhere new.**

### Unit models on the map

`UnitActor.ModelsVisible`, switched from **GENERAL → SHOW UNIT 3D MODELS**.

**The counters never go away.** An APP-6 icon says arm, echelon, side and
strength at any zoom, and a hundred of them read as an order of battle. A hundred
models read as a diorama: slower to draw, hiding each other on broken ground, and
none of them saying whether the thing is a company or a division. So the models
are something you turn *on* to look at a piece of the battle, not the way the map
is read.

| | |
|---|---|
| **Sized from the echelon** | `_baseScale × 0.55` — the same figure the selection ring is drawn from, so a division's model is bigger than a company's with no second table to keep in step |
| **Oversized, like every model here** | A tank is 8 m and this map is played at kilometres across, where 8 m is under a pixel. A scrupulously scaled model would be an invisible one |
| **A child of the actor** | So it rides every position update the formation already gets, including a march. A model moved by its own code would be a second thing that could disagree about where the unit is |
| **Faces the heading**, re-read every frame | A marching formation turns continuously, and seeing which way things face is most of the reason to look at models at all |
| **Goes with the counter when hidden** | Fog and clustering both hide it. A fogged formation that left a tank standing on the map would be the fog leaking the position it exists to withhold |
| **Colliders stripped** | The icon is the unit's hit target. A mesh under it would let a formation be selected by its left track, and would put geometry in the way of every terrain raycast the placement tools use |
| **No model is a normal answer** | Ships, aircraft and several support arms have none yet. The counter is still there, which is the point of the counter |

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
6. **Check the preview**: Play → Development → Units List → click the unit. Console must be clean.
7. **Update this file** — the register in §1 *and*, if it appears somewhere new, the usage table in §3.

### Animation clip naming

Game-facing names live in `ModelClips` (`combat_idle`, `combat_run`, `combat_shoot`) and are independent of the source file names. A new pack maps its own clips onto these names in `ModelInstaller.Clips`, so nothing downstream cares what the artist called them.

---

## 5. Known gaps

- Only one model exists; every ground unit type shares it. Unit-specific models (armour, artillery, drones) are the obvious next step.
- `combat_run` and `combat_shoot` are installed and registered but nothing plays them yet — the natural users are a moving unit (`UnitMover`) and a firing unit (`CombatSystem.Exchange`), which today show only particle effects (`docs/08-PARTICLE-SYSTEMS.md`).
- Models appear only in the Units List. Units on the map are still APP-6 icon billboards (`UnitActor`), which is correct for an operational-level view — 3D models on the map would be a deliberate design change, not an oversight.

---


## The 3D MODELS lab

**DEVELOPMENT → 3D MODELS** (`UI/ModelListUI.cs`) lists every entry in
`UnitModelLibrary` and shows the selected one in 3D — drag to orbit, wheel to
zoom, through the same `ModelPreview` rig the unit library and the catalogue
editor use.

| Column | What it says |
|---|---|
| Model id | The library key |
| Source | The asset pack the mesh came from, or "Built in code" |
| State | **INSTALLED** (prefab resolves) · **PROCEDURAL** (built in code, cannot be missing) · **NOT INSTALLED** |

Rows are ordered installed → procedural → missing, which is the order somebody
opening the screen is looking for, and each says how many unit types wear it —
resolved through `UnitModelLibrary.Resolve`, the same call the map makes, so the
screen cannot disagree with what is actually spawned.

**It reports the truth, not the register.** A library naming a prefab path is not
the same as the prefab existing: an art pack that was never imported, or an
installer run that was never made, shows here as NOT INSTALLED. Before this
screen the only way to find that out was to deploy a unit on the map and fly the
camera to it.

---

## Rules

1. **This document is the register of every 3D model in the game.** Adding, replacing or reassigning a model, adding an animation clip, or showing a model in a new place is not done until §1 and §3 here are updated in the same commit.
2. Gameplay and UI code resolves models through `UnitModelLibrary`. No `Resources.Load` of a model prefab at a call site.
3. Model prefabs under `Assets/Resources/Models/` are **generated** by `ModelInstaller`. Do not hand-edit them.
4. Every model must degrade gracefully — a missing model, prefab or clip shows a specific message, never a blank panel or a magenta box.
5. Record the source asset and its Asset Store URL for every imported pack, so licensing stays traceable.

## Related

`docs/07-ARCHITECTURE.md` (script map) · `docs/04-UNITS.md` (unit catalogue) · `docs/08-PARTICLE-SYSTEMS.md` (effects register)
