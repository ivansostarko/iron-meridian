# Mines and Obstacles

The register of every barrier graphic a scenario can be given. This is the
human-readable version of `Assets/Scripts/Data/ObstacleCatalog.cs` — **keep it in
step with that file in the same change.**

Left rail → **MINES AND OBSTACLES**.

---

## 1. What these are

**Control measures, not units.** A minefield symbol tells whoever is reading the
map that there are mines there. It does not fight, it is not in anybody's order
of battle, and it belongs to the scenario rather than to a formation — which is
why it is owned beside the other control measures rather than being a unit with
no weapons, or a task marker that would be swept away when its owner died.

The same argument the logistic installations make (docs/26-LOGISTICS.md §1), and
the same three-way distinction:

| | Unit | Task marker | Obstacle graphic |
|---|---|---|---|
| Fights | yes | — | no |
| Belongs to | itself | the formation ordered | the scenario |
| Removed when its owner dies | — | yes | never |

---

## 2. The register

Two families, listed in that order because that is the order they are planned in.

| Kind | Button | What it does | Width | Symbol |
|---|---|---|---|---|
| `MinesGeneral` | **MINES** | Mines of unspecified type | 260 m | Filled circle |
| `Minefield` | **MINEFIELD** | A laid and recorded belt | 520 m | Three mines inside their boundary |
| `AntiPersonnelMines` | **AP MINES** | Against dismounted infantry | 300 m | Circle on two prongs |
| `AntiTankMines` | **AT MINES** | Against armour and vehicles | 320 m | Circle with its bar |
| `WireFence` | **WIRE FENCE** | Delays and channels infantry | 420 m | Line with crosses |
| `AntiTankDitch` | **AT DITCH** | Stops vehicles, not men | 480 m | Line with teeth |
| `ObstacleGeneral` | **OBSTACLE** | Obstacle of unspecified type | 380 m | Crossed belt |
| `Roadblock` | **ROADBLOCK** | Closes a route | 240 m | Bar across the route with posts |

Symbols follow **MIL-STD-2525 / APP-6** obstacle graphics, drawn procedurally in
`UiIcons` to the same rule as the rest of that set: the silhouette has to survive
being 20 px on a rail button and whatever the camera makes of it on the ground.
The doctrinal forms are already silhouettes, which is why they have lasted.

**Widths are drawing sizes, not doctrine.** A minefield symbol says "mines here",
not "mines over exactly this"; the figures are chosen to read at the zoom this
map is played at and to be proportionate to one another.

**Mines are red-orange whoever lays them** — the colour of danger, not of a side
— and constructed obstacles are engineer green, each tinted a third of the way
toward the owning side's colour. A belt is read first as *mines* and only second
as *whose*.

---

## 3. Laying them

1. Pick a type. It lights, and a **ghost of that symbol** lies on the ground
   under the cursor at the size it will be placed.
2. **Face the way the belt runs**, then click. The graphic is laid along the
   bearing the camera is looking — an obstacle lies *across* something, so it
   needs a direction, and taking it from the view needs no extra control and is
   almost always right. (In the top-down 2D view the camera is north-up, so
   everything lays due north; that is a stated convention rather than an
   accident.)
3. The tool **stays armed**, because a barrier is several graphics rather than
   one. Right-click, `Esc` or **STOP LAYING** puts it away.

The team tab decides whose barrier it is, and the panel says which in that side's
colour. **LAID** below lists everything on the map with its bearing, with **◎**
to fly to one and **✕** to remove it; **REMOVE ALL** clears the lot. On the map,
right-click any graphic → **REMOVE GRAPHIC**.

---

## 4. On the map

**Flat, not billboarded** — the opposite choice from a logistic site's symbol. A
supply point is a *place* and what matters is which one it is, so its glyph
stands up to face the camera. An obstacle is a piece of *ground*: it has extent,
it lies across an axis, and reading it means seeing how it sits against the
terrain and what it blocks. A symbol painted on the map answers that; one
standing up like a signpost does not.

**Sized in metres, not in pixels.** Every other marker here holds a constant
apparent size, because a counter is a counter at any zoom. An obstacle belt is
500 m of ground and has to *stay* 500 m of ground — a minefield that shrank as
you zoomed out would be lying about what it covers, which is the one thing a
control measure exists to state. The caption is the exception, because text that
scaled with the ground would be unreadable at every zoom but one.

---

## 5. Saving

Written to the map file as `obstacles`:

```json
"obstacles": [
  { "id": "obs-91c4ab02", "kind": "AntiTankMines", "team": "User",
    "label": "", "latitude": 45.76, "longitude": 4.82,
    "heightMeters": 214.0, "headingDeg": 94.0 }
]
```

Empty on a map saved before they existed, which reads correctly as "nothing is
mined".

---

## 6. Known gaps

- **Nothing enforces them.** No movement or combat code reads an obstacle: a
  formation will drive through a minefield and take nothing for it. They are the
  barrier *plan* — drawn, saved, and readable by a human — and making them bite
  needs the movement system to consult them, which is a change to how orders are
  executed rather than to how they are drawn.
- **No linear obstacles.** Each graphic is a point symbol with a bearing; a
  10 km belt is laid as several. A polyline obstacle drawn like a control measure
  would be the natural next step.
- **The lay bearing comes from the camera**, which is convenient and occasionally
  wrong — there is no way to re-aim one after placing it except to remove and
  re-lay it.

---

## 7. Where the code lives

| File | Role |
|---|---|
| `Data/ObstacleCatalog.cs` | **The register** — the eight types in numbers |
| `Data/MapSaveData.cs` | `ObstacleSiteData` and the `obstacles` list |
| `Lines/ObstacleSystem.cs` | Owns the graphics, the pick-then-click tool, save/load |
| `Lines/ObstacleMarker.cs` | The map graphic: flat symbol, terrain-clamped, captioned |
| `UI/UiIcons.cs` | The eight symbols, and `GlyphFor(ObstacleKind)` |
| `UI/UnitPaletteUI.cs` | `BuildObstacleSection` — the panel, generated from the catalogue |
| `Core/GameController.cs` | Wiring, the LAID list's actions, the right-click menu, save/load |

---

## 8. Adding a type

1. A value on `ObstacleKind` and **a row in `ObstacleCatalog.All`** — family,
   name, one-line detail, drawing width, tint.
2. A symbol in `UiIcons` and a case in `UiIcons.GlyphFor(ObstacleKind)`.
3. **Update the table in §2 of this file.**
