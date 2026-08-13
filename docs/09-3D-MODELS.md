# 3D Models

The register of every 3D model in Iron Meridian — where it came from, how it is imported, which units it represents, and where it is shown.

> **Keep this file current.** Every new model, animation clip or call site must be recorded here in the same commit. See [Rules](#rules) at the bottom.

---

## 1. Model register

| Model id | Prefab (Resources) | Source asset | Animations | Used for |
|---|---|---|---|---|
| `soldier_rifleman` | `Models/Soldier_Rifleman` | [Low Poly Soldiers Demo](https://assetstore.unity.com/packages/3d/characters/low-poly-soldiers-demo-73611) — `Soldier_demo.FBX` | `combat_idle`, `combat_run`, `combat_shoot` | Stand-in for **all Core Ground unit types** in the Units List preview |

**Not yet modelled:** every `Drone` category unit. `UnitModelLibrary.Resolve` returns `null` for them and the preview shows an explicit "no model yet" message rather than a misleading infantryman.

### Source assets

| Asset | Location | Contents |
|---|---|---|
| Low Poly Soldiers Demo | `Assets/LowPolySoldiers_demo/` | `models/Soldier_demo.FBX` (Biped rig, `Bip001` root), `animation/demo_combat_{idle,run,shoot}.FBX`, two materials + TGA textures |

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

Degradation is explicit at every step: no model for this unit, prefab not installed, or no legacy clip each produce a specific on-screen or console message — never a blank box.

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
