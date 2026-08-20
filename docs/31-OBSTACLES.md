# Mines and Obstacles

The register of every barrier graphic a scenario can be given. This is the
human-readable version of `Assets/Scripts/Data/ObstacleCatalog.cs` — **keep it in
step with that file in the same change.**

Left rail → **MINES AND OBSTACLES**.

---

> **Choosing a side.** The panel carries its own **FRIENDLY / ENEMY** selector at
> the top. It used only to report the side, which was chosen on the UNITS tab —
> so working on the enemy's barrier graphics meant leaving this panel to switch, coming
> back, and remembering to switch again afterwards. It is the same side every
> other panel uses, and all of their tabs repaint together; there is one selected
> side in the editor, not one per panel.


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
| `Minefield` | **MINEFIELD** | A laid and recorded belt — **drawn as an area** | polygon | Outline studded with mines |
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

**Widths are drawing sizes, not doctrine.** A stamped symbol says "mines here",
not "mines over exactly this"; the figures are chosen to read at the zoom this
map is played at and to be proportionate to one another.

**MINEFIELD is the exception, and is outlined instead.** It is the one kind
flagged `ObstacleDef.areaDrawn`, so picking it arms a polygon tool rather than a
stamp. The reason is that a minefield is not a *place*, it is *ground*: a
roadblock closes one point and a wire fence runs along one line, but the only
thing anybody ever asks a minefield graphic is **where its edge is**, and a
nominal 520 m circle cannot answer that. It is also what
`Units/MinefieldSystem.cs` needs before it can tell whether a column has driven
into one (§6) — the map and the model now read the same ground.

It is a catalogue flag rather than a special case in the tool, so the next kind
that turns out to be an area is a flag flip.

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

### Outlining a minefield

MINEFIELD is drawn, not stamped — the same gesture the mission area and the map
objects use, because it is the same act: saying which ground something covers.

| | |
|---|---|
| **Left click** | Add a corner |
| **Backspace** | Undo the last corner |
| **Right click** / `⏎` | Close the belt — at least three corners |
| `Esc` | Abandon it |

Three corners rather than four: a belt tied into a river bend and a road is
genuinely a triangle, and unlike a bridge or an airfield there is no built thing
whose shape it has to match. Below three there is no inside — the same floor
`MissionArea` uses.

A short outline is **kept, not thrown away**: the count line above the LAID list
switches to `OUTLINING — n CORNER(S), 3 NEEDED` while one is open, and closing
too early says what is missing and leaves the corners where they are. Closing
reports the ground enclosed in km², which is the one figure that cannot be read
off the map. The kind stays armed afterwards, like the stamp tool.

The team tab decides whose barrier it is, and the panel says which in that side's
colour. **LAID** below lists everything on the map — an outlined belt reports the
km² it covers, a stamped symbol the bearing it was laid on, each being the figure
that says whether that sort of graphic went where it was meant to. **◎** flies to
one and **✕** removes it; **REMOVE ALL** clears the lot. On the map, right-click
any graphic → **REMOVE GRAPHIC**.

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

### The minefield graphic

An outlined belt is drawn the way APP-6 and MIL-STD-2525 draw one, and the way
it has been drawn on paper for eighty years: **the outline of the belt with mine
symbols studded along it.**

| Part | How |
|---|---|
| **Outline** | A `MapLine` of the `Boundary` kind — the one kind always draped flat on the terrain, which a belt edge has to be to stay readable across a ridge or a river bank. 90 m wide: it is a control measure, not a wall |
| **Studs** | `UiIcons.MineGeneral`, the doctrinal filled circle, at 150 m on the ground |
| **Spacing** | One every ~420 m of perimeter, floored at 6 and capped at 44 |
| **Caption** | At the belt's centre, not pushed clear of it — there is no symbol underneath to hide |

The studs are the plain filled circle rather than the composite MINEFIELD glyph,
because that glyph *is* an outline with mines in it and nesting one inside
another would be the symbol drawn twice.

Spacing is by **distance travelled along the perimeter**, not one per corner: a
belt tied into a road bend has its corners bunched at the bend, and a symbol per
corner would draw six mines in the bend and none along the two-kilometre run away
from it. Each stud samples its own terrain height and is re-clamped on the same
cadence as the marker, so a belt drawn over ground that has not streamed in yet
settles onto it rather than staying buried.

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

## 6. Mines bite

`Units/MinefieldSystem.cs`. A formation that **moves through an enemy mine
graphic while a battle is running** sets off mines: a blast where the formation
actually is, dust over it, and strength off it.

Until this existed the barrier plan was a picture. A designer could lay a belt
across the one road into the objective, the attacker could march straight down
it, and nothing whatever happened — which made every minefield in the game a
decoration and every decision about where to put one a decision about nothing.

### The rules, and why each one

| Rule | Why |
|---|---|
| **Only while moving** | A formation sitting in a belt has stopped, and a stopped unit is breaching, probing or picking its way. The whole cost of a minefield is paid by whoever tries to *cross* it at speed — which is the decision the graphic exists to make expensive. It also keeps the model legible: a column driving into mines and losing strength, not units quietly bleeding wherever they are parked |
| **Only the enemy's belts** | Real minefields are not so polite. But the editor has no gapping, no lane marking and no recorded breach plan, so friendly-fire mines would be a bug report rather than a decision: the player laid them, cannot mark them, and their own attack dies in them |
| **One strike, then a wait** | A hit on entry and another every **22 s of scenario time** still moving inside. A single hit on entry would make a two-kilometre belt no worse than a hundred-metre one; continuous damage would kill anything that touched a field at all |
| **Battle mode only** | In the editor the same formations are being dragged across the same ground on purpose |
| **Measured to the formation's centre** | The opposite of `BlastDamage`, and right for the opposite reason: a shell is aimed at a point and the question is whether it reached the formation; a minefield is ground and the question is whether the formation went into it. Crediting the footprint would set a division off from a kilometre away |

### What it costs

**5.5 % strength** per detonation on a full formation, before three adjustments:

| Adjustment | Effect | Why |
|---|---|---|
| **AT mines vs. mounted** | ×2.0 mounted, ×0.35 dismounted | A pressure plate built to break a track does very little to a rifle company on foot |
| **AP mines vs. dismounted** | ×1.8 dismounted, ×0.40 mounted | And a fragmentation mine does very little to a tank. Laying the right sort for what is expected down the road is the one interesting decision the catalogue offers, and it is worth nothing unless the model can tell them apart |
| **Echelon** | ÷√(manpower multiplier) | A mine strike is a vehicle or a section — a larger share of a company than of a division. The absolute loss stays roughly constant, the proportional one falls |
| **Armour** | ×1.0 → ×0.5 across 0–100 | Halved at most: armour is protection against blast from the side, and a mine is underneath |

MINES and MINEFIELD are mixed belts by definition — the catalogue calls one
"unspecified" and the other "laid and recorded", and both are laid against
whatever comes — so neither takes the type adjustment.

Shock is dealt at **80× the strength lost**, higher than a shell's: the point of
a minefield is what it does to an attack's momentum rather than to its order of
battle. Damage goes through `UnitActor.ApplyDamage`, so the burning, routing and
death sequences all follow without the system knowing about any of them — the
same route `BlastDamage` takes.

Only the **player's own** losses are flashed. The side that laid the belt learns
it worked from the map — a formation slowing, burning and turning back — which is
what a real report looks like, and a flash line per mine on a busy front would be
noise.

Cooldowns are cleared when the battle starts or stops and when a map is loaded: a
formation that crossed a belt in the last battle must not be immune in the next.

### Effects

| | |
|---|---|
| `VfxId.MineBlast` | `ArtilleryDirtColumn` fallback, 130 m, 1.7 s — the smallest detonation in the game. The charge is buried, so most of its energy goes into the ground and into whatever is standing on it; what is seen is a short throw of earth. Anything bigger would read as incoming fire, which is the one thing it must not be mistaken for |
| `VfxId.MineSmoke` | `Smoke` fallback, 120 m, stopped after 14 s |
| `EffectSound.MineBlast` | Sharp, short, almost no tail — the earth takes the low end and returns a crack. The player has to be able to tell "I have driven into something" from "I am being shelled" without looking, because the two call for opposite responses |

See docs/08-PARTICLE-SYSTEMS.md and docs/10-AUDIO.md.

---

## 6a. Known gaps

- **Only mines bite.** Wire, ditches and roadblocks are still drawn and saved and
  read by nobody. They are delay-and-channel obstacles, which means the thing
  they should do is slow a formation and push it sideways — that is a change to
  route planning rather than a damage rule, and it is the natural next step.
- **No breaching.** A minefield cannot be gapped, lane-marked or cleared, so the
  only answer to one is to go round it or accept the cost. Engineers exist in the
  catalogue and have nothing to do with it.
- **No linear obstacles.** Apart from MINEFIELD every graphic is a point symbol
  with a bearing; a 10 km wire belt is laid as several.
- **The lay bearing comes from the camera**, which is convenient and occasionally
  wrong — there is no way to re-aim a stamped graphic after placing it except to
  remove and re-lay it. An outlined belt has no bearing to get wrong.

---

## 7. Where the code lives

| File | Role |
|---|---|
| `Data/ObstacleCatalog.cs` | **The register** — the eight types in numbers |
| `Data/MapSaveData.cs` | `ObstacleSiteData` and the `obstacles` list |
| `Lines/ObstacleSystem.cs` | Owns the graphics, the pick-then-click tool, the polygon tool, save/load |
| `Lines/ObstacleMarker.cs` | The map graphic: flat symbol or studded outline, terrain-clamped, captioned |
| `Units/MinefieldSystem.cs` | **What a belt does to whoever drives into it** (§6) |
| `UI/UiIcons.cs` | The eight symbols, and `GlyphFor(ObstacleKind)` |
| `UI/UnitPaletteUI.cs` | `BuildObstacleSection` — the panel, generated from the catalogue |
| `Core/GameController.cs` | Wiring, the LAID list's actions, the right-click menu, save/load |

---

## 8. Adding a type

1. A value on `ObstacleKind` and **a row in `ObstacleCatalog.All`** — family,
   name, one-line detail, drawing width, tint.
2. A symbol in `UiIcons` and a case in `UiIcons.GlyphFor(ObstacleKind)`.
3. **Update the table in §2 of this file.**
