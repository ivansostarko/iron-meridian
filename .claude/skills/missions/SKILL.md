---
name: missions
description: Iron Meridian's single-player campaigns and missions — adding or editing missions, adding a campaign, and anything touching MissionLibrary, MissionDefinition, SinglePlayerUI, the editor's MISSIONS panel or the mission entry path in GameController.
---

# Missions and campaigns in Iron Meridian

Read `docs/22-MISSIONS.md` first — it is the register, and it is the file that has
to be updated in the same change.

## The one idea

A mission is **two files**, and both are written by the map editor and read by the
game:

| Part | File | Owns |
|---|---|---|
| Record | one entry in `missions.json` | Name, campaign, location, briefing, start point, altitude, view, weather, H-hour, fog |
| Map | `Maps/<id>.json` (`MapSaveData`) | Units, control measures, task markers |

Everything else follows from that. There is no publish step, no sync, no export —
"changed in the editor, applied in game" is true because the two screens read the
same two files. **Any change that breaks that property is the wrong change.**

## Non-negotiables

- **`MissionLibrary` is the only reader and writer.** No screen opens
  `missions.json` itself, and nothing writes a mission's map except through
  `SaveSystem.SaveMap` with `mission.ResolvedMapFile`.
- **Saving a mission saves both files.** `GameController.SaveMission` writes the
  map *and* `MissionLibrary.SaveBook()`. If you add another path that saves one,
  it must save the other.
- **The user's list shadows the shipped one wholesale.** Do not "improve" this
  into a per-mission merge: a merge cannot tell *never had it* from *deleted it*,
  so a mission the player removed would come back.
- **New `MissionDefinition` fields must default harmlessly.** `JsonUtility`
  leaves missing fields at their initialiser values, and old lists must keep
  loading. Add the field to the table in `docs/22-MISSIONS.md` §1 in the same
  change.
- **Campaigns are code, missions are data.** `Data.Campaign` is a closed enum
  because the campaign is navigation structure — a mission whose campaign nobody
  recognises has nowhere to appear. Do not turn it into a free string.
- **`MissionLibrary.Selected` is a static hand-off**, because Unity's scene
  loader takes a name and nothing else. It is cleared when the campaign screen
  opens; anything else that enters the Game scene without a mission must leave it
  cleared or the map editor will open somebody's mission.

## Where things are

```
Assets/Scripts/Data/MissionData.cs      Campaign, CampaignInfo, MissionDefinition, MissionBook
Assets/Scripts/Data/MissionArea.cs      the boundary polygon: Contains, Clamp, Rectangle, extent
Assets/Scripts/Lines/MissionAreaTool.cs click-to-draw the boundary + the always-on overlay
Assets/Scripts/Save/MissionLibrary.cs   read/write/create/delete, map fallback, Selected
Assets/Scripts/UI/SinglePlayerUI.cs     campaign board + mission board (two pages, one scene)
Assets/Scripts/Core/SceneLoader.cs      async scene load behind the loading overlay
Assets/Scripts/UI/UnitPaletteUI.cs      BuildMissionsSection — the editor panel
Assets/Scripts/Core/GameController.cs   OpenMission / SaveMission / CreateMissionHere / DeleteMission
                                        ApplyMissionArea — pushes the boundary into fog + camera
Assets/StreamingAssets/Data/missions.json   the shipped list
```

## Recipes

### Add a mission (as a designer)

Map editor → **MISSIONS** → set the campaign → **NEW MISSION HERE** (starts at the
point the camera is looking at) → fill in name, location, briefing, start altitude
→ lay out the order of battle → set its **MISSION AREA** (DRAW AREA ON MAP, or a
20/50/120 km box) → **SAVE MISSION + MAP**. It appears under SINGLE PLAYER
immediately.

The area is the mission's ground: in battle the camera is clamped to it,
everything outside it goes dark, and formations outside it are off the
battlefield. Empty means unbounded, which is what every mission written before
areas existed is. See docs/22-MISSIONS.md §1a and docs/16-FOG-OF-WAR.md §2b.

### Add a mission (in the shipped data)

Add an object to `Assets/StreamingAssets/Data/missions.json`. `id` must be unique
and file-safe; `mapFile` may be omitted and resolves to `<id>.json`. A mission
with no map file opens on empty ground at its start point — that is intentional,
not a bug to fix. Record it in `docs/22-MISSIONS.md` §1.

### Add a campaign

1. Value in `Data.Campaign`.
2. Entry in `CampaignInfo.All` (declaration order is board order).
3. Cases in `CampaignInfo.DisplayName` and `Blurb`.
4. Row in the campaign table in `docs/22-MISSIONS.md` §1.

The boards, the editor dropdown and the filtering all read from those; there is
nothing else to wire.

### Add a field to a mission

1. Field on `MissionDefinition`, with a sensible initialiser, plus the line in
   `Clone()`.
2. If the Game scene should honour it, apply it in `GameController.Start` (or in
   `MissionLibrary.ApplyTo` if it is a `MapSaveData` setting the mission
   overrides).
3. If a designer sets it, add the control to `BuildMissionsSection` and read it in
   `ReadMissionFields` / write it in `RefreshMissionFields`.
4. Row in `docs/22-MISSIONS.md` §1.

## Traps

- **`_missionSyncing`.** The panel writes its own controls, and those writes fire
  the same `onEndEdit` / `onValueChanged` callbacks a player's edit does. Every
  programmatic write is bracketed by that flag; forget it and the panel edits the
  record while displaying it.
- **Malformed numbers must not zero a field.** Half-typed input in the latitude
  box is not an instruction to move the mission into the Atlantic — `TryParse`
  and leave the old value alone.
- **`OPEN IN EDITOR` is destructive.** It replaces everything on the editor's map.

- **An area can only be drawn on the mission that is open.** Picking one in the
  dropdown to correct its briefing does not load its map, and an area drawn then
  would be written into one mission while the overlay drew it over another's
  ground. `GameController.PointAreaToolAtPanelMission` refuses instead.

- **The area belongs to the record, not the map.** It is drawn with
  `MissionAreaTool`, not `LineDrawTool`, so it never lands in `MapSaveData.lines`
  — the same ground can carry two missions with different boundaries.
  That is why picking a mission in the dropdown and opening it are two clicks.
- **DELETE keeps the map file.** A scenario takes an evening to lay out and the
  button is one mis-click. Do not "tidy up" by deleting the scenario with it.
- **Two loader stages.** `SceneLoader` (building the scene) then
  `GameController`'s (streaming terrain). Both are registered in
  `docs/12-LOADERS.md` §3.1 and both must always dismiss (golden rule 7). The
  outgoing one is `DontDestroyOnLoad` and one sorting order above the standard
  loader, so the handover has no gap and no coin toss over draw order.
- **The Game scene does two jobs.** `_mission == null` is the map editor. Anything
  that assumes a mission is present will break the editor, and anything that
  assumes there is none will break missions.

## Verifying

No automated tests. The chain that exercises the whole feature:

1. Main menu → SINGLE PLAYER → three campaign boards with the right mission counts.
2. Pick a campaign → the right missions; pick one → loader → the map opens at that
   place, at that altitude, with the HUD naming the mission.
3. `Esc` → pause menu → **EXIT** returns to SINGLE PLAYER, not the main menu.
4. Development → Map Editor → **MISSIONS** → pick the same mission → **OPEN IN EDITOR**
   → deploy a unit → **SAVE MISSION + MAP**.
5. Back to SINGLE PLAYER → the same mission → the unit is there. *This step is the
   feature; if it fails, nothing else matters.*
6. **NEW MISSION HERE** → it appears on the campaign board straight away.
7. **DELETE MISSION** → confirm → gone from the board, map file still on disk.
8. Delete `missions.json` from the persistent data folder → the shipped seven come
   back.
