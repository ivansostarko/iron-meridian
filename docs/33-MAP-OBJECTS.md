# Map Objects

The register of every kind of infrastructure a scenario can be given — bridges, airfields, ports, built-up areas — drawn on the terrain as polygons.

> **Keep this file current.** Every new kind must be recorded in §2 with its colour, its width and what it is for, in the same change that adds it to `MapObjectCatalog`. See [Rules](#rules) at the bottom.

Left rail → **OBJECTS** (scenario mode).

---

## 1. Why these are drawn, not dropped

A depot is a *place*, and `LOGISTICS` marks it with a point (docs/26-LOGISTICS.md). A bridge, an airfield or a quarter of a city is an **extent**: what matters about it is how much ground it covers, where its ends are, and what has to be held to hold it. None of that fits in a marker.

So each object is a polygon on the terrain, of **at least four corners**.

**Four, not three.** Everything here is a built thing with an extent — a span, a runway, a yard, a block of town — and a triangle is a shape none of them are. It also stops a stray double-click leaving a sliver on the map that has to be hunted down to delete. `MapObjectCatalog.MinCorners`.

**Each belongs to a side.** A bridge in friendly hands and the same bridge in the enemy's are different problems. The side comes from the panel's FRIENDLY / ENEMY tabs — the same selected side the whole editor uses (docs/03-GAMEPLAY.md). Neutral ownership is deliberately not modelled: the editor works one side at a time, and a third state nobody can select would be a state nobody can edit.

---

## 2. Object register

| Kind | Colour | Width | What it is |
|---|---|---|---|
| **Bridge** | `#E8C15A` | 90 m | A crossing and its approaches. The one object whose loss can stop a manoeuvre outright |
| **Airfield** | `#6FB3E8` | 140 m | Runway, apron and dispersals — where air support and air supply come from |
| **Hospital** | `#E86F86` | 90 m | A medical facility. Protected under the laws of armed conflict; worth marking so it is not fired on by accident |
| **Port** | `#5ED0C0` | 140 m | Quays and the water they front. A theatre's throughput |
| **Rail yard** | `#B48FE0` | 110 m | Sidings and a transhipment point. Rail is how heavy formations move any distance |
| **Power station** | `#E8A25A` | 110 m | Generation and its switchyard |
| **Fuel terminal** | `#D8E85A` | 110 m | Tank farm and pumping — see docs/27-SUSTAINMENT.md |
| **Factory** | `#9AA5B1` | 110 m | Industry worth holding, denying or repairing |
| **Built-up area** | `#C8CDD4` | 160 m | A town or a quarter of a city. Slow going, short sight lines |
| **Dam** | `#7FE87F` | 110 m | A dam and its reservoir edge |

Ids in code: `MapObjectKind.Bridge` … `.Dam`, rows in `Data/MapObjectCatalog.cs`.

---

## 3. Drawing one

| Input | Action |
|---|---|
| A kind button | Arms it — the next click on the map starts an outline |
| **Left click** | Add a corner |
| **Backspace** | Undo the last corner |
| **Right click** or **Enter** | Close the outline (four corners minimum) |
| **Esc** | Abandon it |

**A short outline is kept, not thrown away.** Closing with three corners says what is missing and leaves them on the map to go on clicking. Discarding the work because the fourth corner had not been placed yet would punish the one mistake the minimum exists to prevent.

**The kind stays armed after a close**, so a row of bridges is a row of outlines rather than ten trips back to the panel. STOP DRAWING, or Esc, stands it down.

The panel lists what is on the map with its side, its kind and its corner count; a row flies the camera to it, and the bin removes it. Removing an object leaves nothing behind — unlike a mission's map file, an outline is a minute's work.

---

## 4. Storage

Objects live in the **map file**, in `MapSaveData.mapObjects` — they are the ground, and the ground is what a map is. (A mission's *zones* live in the mission record instead, because the same ground can carry two missions; see docs/22-MISSIONS.md.)

```json
{ "id": "a1b2c3d4e5", "kind": "Bridge", "team": "User", "label": "BRIDGE",
  "points": [ {"latitude": 45.77, "longitude": 4.83, "heightMeters": 168.0 }, … ] }
```

Empty on every map written before objects existed, which reads correctly as "this scenario names none". An entry with fewer than four points is dropped on load rather than drawn as a sliver.

---

## 5. Where the code lives

| File | Role |
|---|---|
| `Assets/Scripts/Data/MapObjectCatalog.cs` | `MapObjectKind`, `MapObjectDef`, the register and `MinCorners` |
| `Assets/Scripts/Lines/MapObjectSystem.cs` | Arming, drawing, closing, the overlay, save/load |
| `Assets/Scripts/UI/UnitPaletteUI.cs` | The OBJECTS panel — `BuildObjectsSection`, `RefreshMapObjects` |
| `Assets/Scripts/Data/MapSaveData.cs` | `MapObjectData` and `mapObjects` |

Outlines are drawn through `MapLine` with `LineKind.Boundary`, the one kind that always drapes on the terrain — which an outline has to do to stay readable across a river bank or a ridge.

---

## 6. Known gaps

- **Nothing reads them yet.** They are drawn, saved and shown; no system asks whether a formation is standing on a bridge or inside a built-up area. Movement cost, cover and objectives are the obvious next steps.
- **No labels on the ground beyond the kind's name.** An object cannot be renamed in the panel yet, though the data carries a `label`.
- **No editing after the fact.** A corner cannot be dragged; a wrong outline is deleted and redrawn.
- **Fog does not hide them.** Infrastructure is terrain, and both sides can see terrain — but an *enemy-owned* object arguably should not announce its owner through the fog.

---

## Rules

1. **This document is the register of every map object kind.** Adding, removing or restyling one is not done until §2 is updated in the same commit.
2. **Four corners minimum.** Anything that closes an outline goes through `MapObjectSystem`, which enforces it; nothing writes `mapObjects` directly.
3. **Every object belongs to a side**, and the side comes from the editor's selected team — never a second picker.
4. **Objects are map data**, not mission data. Anything that is about a *mission* belongs in the mission record instead (docs/22-MISSIONS.md).
5. **New fields on `MapObjectData` must default harmlessly** — `JsonUtility` leaves missing fields at their initialiser values, and old maps must keep loading.

---

## Related

`docs/26-LOGISTICS.md` (installations, which are points) · `docs/31-OBSTACLES.md` (barrier graphics) · `docs/05-MAP-SAVES.md` (the file they live in) · `docs/03-GAMEPLAY.md` (the rail and the side selector) · `docs/07-ARCHITECTURE.md` (script map)
