# Missions and Campaigns

Single player: three campaigns, each holding a list of missions, each mission a piece of real ground with an order of battle on it. Missions are authored in the map editor and played from the main menu — **the same two files on both sides**, so there is no publish step to forget.

> **Keep this file current.** Every new campaign, every new field on a mission, and every change to how missions are stored or loaded must be recorded here in the same change. See [Rules](#rules) at the bottom.

---

## 1. What a mission is

A mission is **two files**:

| Part | Where | Owns |
|---|---|---|
| The **record** | one entry in `missions.json` | Name, campaign, location, briefing, start point, start altitude, view, weather, H-hour, fog |
| The **map** | `Maps/<id>.json` — an ordinary `MapSaveData` | Units, control measures, task markers |

That split is the whole design. The map format already existed and is what the editor reads and writes; the record is the handful of things the *selection screens* need before any map is loaded — you cannot draw a campaign board by opening seven scenarios.

**A mission with no map file is still playable.** `MissionLibrary.LoadMap` synthesises an empty `MapSaveData` at the mission's start point when the file is missing, so a mission created in the editor works the moment it exists rather than being broken until somebody remembers to save.

### Campaign register

Campaigns are a **closed enum** (`Data.Campaign`), because a campaign is navigation structure: the single-player screen draws one board per value, and a mission whose campaign nobody recognises would be a mission with nowhere to appear. Missions are data and can be added freely; campaigns are added deliberately, in code, with a display name and a blurb.

| Campaign | Shipped missions |
|---|---|
| **West Europe** | Berlin, Oslo |
| **East Europe** | Zagreb, Bjelovar, Budapest |
| **North America** | Denver, New York |

Adding one: a value in `Data.Campaign`, an entry in `CampaignInfo.All`, and a case in `DisplayName` / `Blurb`. The boards, the editor's campaign dropdown and the filtering all read from those.

### Mission fields

All of these are edited in the map editor's **MISSIONS** panel. `MissionDefinition` in `Assets/Scripts/Data/MissionData.cs`.

| Field | Meaning |
|---|---|
| `id` | Stable, file-safe, and the stem of the map file. Derived from the name and made unique on creation |
| `campaign` | Which board it appears on |
| `name` | Title on the board and in the HUD's identity block |
| `location` | Where in the world, in words — the caption on the mission row |
| `briefing` | One line of what it is about |
| `mapFile` | Scenario under `Maps/`. Empty resolves to `<id>.json` |
| `latitude` / `longitude` | Where the map opens |
| `startAltitudeMeters` | Camera standoff on entry, clamped to the rig's limits |
| `viewMode`, `mapStyle`, `showBuildings` | The view the mission opens in |
| `startDateTime` | H-hour — docs/13-DATE-AND-TIME.md |
| `skyPhase`, `weatherCondition`, `autoDayNight` | Weather — docs/14-WEATHER.md |
| `fogOfWar` | Armed on entry. The one editor toggle a mission decides for the player, because a mission is a fight rather than a layout exercise |
| `area` | The ground the mission is fought over — a closed polygon. Empty means unbounded. See §1a |
| `order` | Position on its campaign board, ascending |
| `available` | False hides a work-in-progress mission from the board without deleting it |

---

## 1a. The mission area

`MissionArea` in `Assets/Scripts/Data/MissionArea.cs`. A list of WGS84 vertices, implicitly closed, drawn in the editor's MISSIONS panel and stored on the mission record — **not** in the map file, because the same ground can carry two missions with different boundaries and the boundary has to survive the map being re-saved.

**Fewer than three vertices means no area**, which is the state of every mission written before this existed and is read as *unbounded*. Everything below is then switched off.

It does three things at once, and they are the same thing:

| It bounds… | How | Where |
|---|---|---|
| **the battle** | The camera's focus is clamped to the polygon, and the zoom-out ceiling drops to about 2.4 × the area's radius. **In battle only** — the editor has to be able to fly outside an area to draw it | `CameraRig.ClampFocus` / `SetMaxDistance`, driven by `GameController.ApplyMissionArea` |
| **what is shown** | Terrain outside the polygon is blacked out at `MissionArea.OutsideOpacity` and stays that way whatever anybody can see. This happens **whether or not fog of war is armed** — the boundary is what the scenario *is*, not an intelligence setting | `FogBlanket`, see docs/16-FOG-OF-WAR.md §2a |
| **intelligence** | A formation outside the area is off the battlefield: hidden outright, and *not* tracked as a contact. A watcher outside the area reveals nothing, or it would punch a hole in the dark from off the map | `FogOfWarSystem.InBounds` |

**Point-in-polygon runs in plate carrée.** The even-odd crossing test is exact in any consistent planar frame, and lon/lat is one over ground a battle is fought on. It would be wrong for an area straddling the antimeridian or a pole — neither of which a scenario can usefully cover. Area and radius figures use the local east/north plane instead, because a degree of longitude is 111 km at the equator and 71 km at Lyon and the number is shown to somebody sizing a battlefield.

**Which vertices of the fog grid fall inside is baked once**, when the grid is laid, not per sweep: the answer cannot change for the length of a battle, and thousands of point-in-polygon tests several times a second for a constant would be the most expensive thing the fog does. See docs/16-FOG-OF-WAR.md §2b for how the grid is sized to the area.

**Clamping slides along the edge** rather than snapping to the centre. A camera that jumped to the middle of the map when it touched the boundary would be unusable; one that slides feels like a wall.

---

## 2. Storage

```
Assets/StreamingAssets/Data/missions.json          shipped list
%USERPROFILE%/AppData/LocalLow/…/missions.json     the player's list  (shadows it)
Assets/StreamingAssets/Maps/<id>.json              shipped scenarios
%USERPROFILE%/AppData/LocalLow/…/Maps/<id>.json    the player's scenarios (shadow them)
```

`MissionLibrary` reads the user file if it exists and the shipped one otherwise — **wholesale, not merged**, exactly as `SaveSystem` already does for maps.

Merging per-mission was the obvious alternative and is worse: a player who deletes a shipped mission in the editor would watch it come back, because a merge cannot tell *never had it* from *got rid of it*. Shadowing means the editor owns the list once it has been touched, and deleting `missions.json` from the save folder is the documented way back to the shipped set.

The list is **one file** because the campaign board reads all of it before anything is picked, and seven file reads to draw one board would be seven chances to half-load a menu. The **maps** stay one file each — those are big, and only ever one is wanted.

---

## 3. Playing a mission

```
Main menu → SINGLE PLAYER        SinglePlayerUI (campaign board)
  ↓ pick a campaign              same scene, second page (mission board)
  ↓ pick a mission               MissionLibrary.Select(mission)
  ↓                              SceneLoader.Load(Game, …)   ← loader stage 1: building the scene
  ↓                              GameController.Start reads MissionLibrary.Selected
  ↓                              LoadingScreenUI              ← loader stage 2: streaming terrain
  ↓ playing                      Esc / P → PauseMenuUI, EXIT returns to SINGLE PLAYER
```

### Mission mode — the map and the timer

The Game scene strips itself down when `_mission != null`
(`GameController.ApplyMissionMode`):

| Gone | Why |
|---|---|
| The left rail and its section panel | Nine sections that deploy units, set the weather and name the players. Authoring tools, in a scenario somebody else authored |
| The strike dock | The five fire menus at the top right go with the rail — see docs/03-GAMEPLAY.md |
| The identity block and its home button | The bar's name and the one-click hop out. Esc → EXIT is the way out |
| The mode chip | There is only one mode in a mission |
| RESET | Reloads the scenario and resets every setting. Actively dangerous here |
| START BATTLE | See below — there is nothing to press because the battle is already running |
| The editor key list along the bottom | Copy-paste, undo and control-measure keys are editor bindings |

| Kept | Why |
|---|---|
| The **operational clock** | The mission's timer, and the one piece of chrome a mission needs |
| The zoom cluster and compass | Map controls, not editor tools — the same argument that removes the rail keeps these |
| Unit info panel, order bar | Only appear while something is selected, and are the only way to give an order |
| Flash line, strike countdown, alerts | Gameplay feedback. A strike with no countdown is a strike you cannot time |
| Esc / P → pause menu | The way out, now that the home button has gone |

**The battle starts by itself.** The control that would start it is gone, and the
clock only reads out while a battle is running — so "show the timer" and "start
the fight" are the same instruction. `TickMissionAutoStart` runs it once the
loading overlay has gone, so the first combat tick does not land on units still
being clamped to terrain Cesium has not delivered.

**One scene does both jobs.** The Game scene is the map editor reached from DEVELOPMENT *and* what a mission is played in — deliberately, because a mission is a map with an order of battle on it, and a second scene would be the same dozen systems wired the same way under a different name. What a mission changes is where the map opens, what the HUD's identity block says, and where BACK and EXIT go.

**Two pages, one scene, for the menus too.** The campaign board and the mission board share a background, a music bed and a frame, and moving between them has to be instant — a scene load between two pages of a menu would put a loading screen in the middle of picking something.

**Two loader stages, both registered in docs/12-LOADERS.md.** `SceneLoader` covers building the scene (`LoadSceneAsync`, held at 90 % until the bar catches up); `GameController`'s own loader covers streaming the terrain. Splitting them is what stops the click reading as a freeze: `GameController.Start` builds every system and every panel before it yields, and a synchronous `LoadScene` would spend all of that on the player's last frame.

**Escape and EXIT go back one step**, not to the top: missions → campaigns → main menu, and a paused mission → the campaign browser. A player who has just lost a mission wants to retry it, not to walk the menu again.

---

## 4. Authoring a mission

Map editor (Development → Map Editor) → **MISSIONS** in the left rail. The panel scrolls — it is the one section that outgrew the rail, and its controls are placed at absolute offsets, so the whole page sits inside a scroll view rather than being reflowed into a stack.

| Control | What it does |
|---|---|
| **CAMPAIGN** dropdown | Which board's missions the list below shows |
| **MISSION** dropdown | Picks one to edit. Hidden missions are listed here with `(hidden)` |
| **OPEN IN EDITOR** | Loads that mission's map, settings and start point — replaces what is on the map now |
| Name / location / briefing | The board's text |
| Start point, start altitude | Where the mission opens |
| **FOG OF WAR** | Armed on entry |
| **MISSION AREA** readout | `UNBOUNDED`, or `BOUNDED` with corner count, km² and radius |
| **DRAW AREA ON MAP** | Click the corners on the terrain. Right-click or Enter closes it (min 3), Backspace undoes a corner, Esc cancels |
| **20 KM / 50 KM / 120 KM** | Replaces the area with a box that wide, centred on the point the camera is looking at |
| **CLEAR AREA** | Drops the area — the mission is unbounded again |
| **SAVE MISSION + MAP** | Writes the record **and** the current map |
| **NEW MISSION HERE** | Starts one at the point the camera is looking at, in the chosen campaign |
| **DELETE MISSION** | Removes it from the board, after a confirmation |

**Three box sizes rather than a number field.** Those are the scales a scenario is actually laid out at — a town, a corps sector, a theatre — and typing `37` would be a decision nobody has a reason to make. Draw the polygon when the ground has a shape worth following; press a box when it does not.

**The area overlay is always visible** while a mission is open, drawing or not, in amber and wider than a control measure. It is deliberately not the doctrinal boundary yellow: this is the edge of the scenario, not a control measure somebody drew for the troops, and the two must never read as the same object. It is built directly rather than through `LineManager`, which is what keeps it out of `MapSaveData.lines`.

**Picking a mission and opening it are separate on purpose.** Choosing one in the dropdown to correct its briefing is cheap; loading its map throws away whatever is on the editor's map right now, and that should take a deliberate second click.

**SAVE writes both files.** This is the sentence the whole feature turns on: whatever is on the editor's map at that moment — units, control measures, markers, weather, H-hour, view mode, tile style — becomes what the player gets from SINGLE PLAYER. There is no separate publish step, because there are no separate files. F5 in a mission does the same thing.

**DELETE keeps the map file.** A scenario takes an evening to lay out and the button is one mis-click; removing the record is reversible by hand and removing the work would not be.

**Fields are read back on every end-edit**, not only on save, so the record in memory always matches what is on screen — typing a new latitude and then pressing OPEN flies to the new one. Nothing touches the disk until SAVE. A malformed number leaves the old value alone rather than zeroing it: half-typed input is not an instruction to move the mission into the Atlantic.

---

## 5. Where the code lives

| Script | Role |
|---|---|
| `Data/MissionData.cs` | `Campaign`, `CampaignInfo`, `MissionDefinition`, `MissionBook` |
| `Data/MissionArea.cs` | The mission's boundary polygon: containment, extent, clamping, and the rectangle builder |
| `Lines/MissionAreaTool.cs` | Click-to-draw the boundary, and the always-on overlay that shows it |
| `Save/MissionLibrary.cs` | Read / write / create / delete, the map fallback, and the `Selected` hand-off |
| `UI/SinglePlayerUI.cs` | The campaign board and the mission board |
| `Core/SceneLoader.cs` | Async scene load behind the standard overlay |
| `UI/UnitPaletteUI.cs` | `BuildMissionsSection` — the editor panel |
| `Core/GameController.cs` | `OpenMission` / `SaveMission` / `CreateMissionHere` / `DeleteMission` / `ApplyMissionArea`, and reading `MissionLibrary.Selected` at startup |
| `UI/GameHUD.cs` | `SetTitle`, `HomeScene` — the bar says which job the scene is doing |
| `UI/PauseMenuUI.cs` | `ExitScene` — where EXIT goes |

`MissionLibrary.Selected` is a **static**, because Unity's scene loader takes a name and nothing else. It is cleared when the campaign screen opens, so a stale pick from earlier in the session cannot hijack the map editor.

---

## 6. Known gaps

- **The shipped missions have no scenarios.** Every one of the seven opens on empty ground at the right place — they are start points and briefings waiting for an order of battle. Lay one out in the editor and save it.
- **No objectives, no victory condition, no scoring.** A mission is a place and a force; nothing yet says what winning is.
- **No progression.** Every mission is available from the start; `order` decides where it sits on the board and nothing gates it.
- **No campaign-level state.** Missions do not carry losses forward, and finishing one does not change another.
- **The editor's undo does not cover mission edits.** Ctrl+Z tracks unit placements, not the MISSIONS panel's fields — including the area.
- **Nothing stops a unit being deployed outside the mission area.** The editor will place it and the battle will hide it, which is honest but unhelpful; a warning at deploy time would be better.
- **The area does not constrain the AI or movement orders.** A formation ordered across the boundary will march across it and disappear rather than refusing the order.

---

## Rules

1. **`MissionLibrary` is the only way to read or write missions.** No screen loads `missions.json` itself.
2. **A mission is its record plus its map, and saving means saving both.** Anything that writes one must write the other, or the feature's central promise stops being true.
3. **Adding a campaign is a code change** (`Data.Campaign` + `CampaignInfo`); adding a mission is a data change. Do not turn the campaign into a free string to avoid the former.
4. **New fields on `MissionDefinition` must default harmlessly** — `JsonUtility` leaves missing fields at their initialiser values, and old lists must keep loading.
5. **Record every new field in §1** and every storage change in §2, in the same commit.

---

## Related

`docs/03-GAMEPLAY.md` (the editor and its rail) · `docs/05-MAP-SAVES.md` (the map format a mission's scenario is in) · `docs/11-GAME-MENU.md` (screen and background register) · `docs/12-LOADERS.md` (loader register) · `docs/13-DATE-AND-TIME.md` · `docs/14-WEATHER.md` · `docs/16-FOG-OF-WAR.md`
