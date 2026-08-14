# Fog of War & Reconnaissance

Limited intelligence: the player sees the enemy only where something of theirs is
actually looking, and the reconnaissance tasks that extend that reach.

> Fog is off by default; the line-of-sight ring is on. Both live in the map
> editor's **GENERAL → INTELLIGENCE** block. Keep this file in step with
> `FogOfWarSystem.cs` and `ReconTaskCatalog.cs`.

---

## 0. Line of sight

Separate from the fog and useful without it: selecting a unit — **in scenario
mode as well as battle** — draws a ring at its `viewRangeKm`, captioned on the
ring itself with the distance **in metres** (`LINE OF SIGHT  4 500 m`). Metres
rather than kilometres because a line of sight is a distance you judge against
the ground, and "4.5 km" reads as an approximation of the number you are judging.

**Left rail → GENERAL → LINE OF SIGHT** toggles it; on by default. The weapon
range ring is independent and always shown for the selected unit.

This is the same range the fog's detection sweep uses, so with fog on the ring is
literally the edge of what that formation can reveal.

### Why the rings are volumes and not lines

They were dashed `LineRenderer` circles twelve metres wide, and were effectively
invisible: twelve metres on a circle kilometres across, seen from a camera
kilometres up, is well under a pixel — and being depth-tested against the terrain
they followed, whatever survived was chopped up by every fold of ground it passed
behind. Widening a line does not fix that; a line thick enough to see from
altitude is a smear up close.

A range is now drawn as a **fence of light standing on the terrain**: a
translucent wall rising out of the ground along the whole circumference, fading
with height, over a bright band where it meets the ground, with motes drifting up
off the rim. The wall's base is sunk 90 m below the sampled terrain, so the fence
always cuts the surface and can neither float nor be buried however rough the
ground. From overhead the band reads as a circle; from a shallow angle the wall
reads as a boundary standing in the landscape.

The radius itself is never animated — it states a real distance. Only brightness
breathes.

Geometry is built in the anchor's **local east-north-up frame** with heights
relative to the centre, which is what makes a ring affordable on a marching unit:
it follows by moving its anchor, and the 128 terrain samples are only re-taken
once the centre has moved 4% of the radius.

---

## 1. What it does

| State | Enemy formation |
|---|---|
| Inside a friendly unit's `viewRangeKm`, or a recon sensor's footprint | Drawn normally |
| Outside all of them | **Removed from the map** — icon, strength bar, label, selection ring and any fire attached to it |

Losing sight of a formation leaves a **contact** where it was last seen:

- a red ring centred on the last known position,
- captioned with the unit's designation, the **scenario clock time** of the
  sighting, and the current uncertainty (`LAST SEEN 14:32 · ±3.2 km`),
- growing as the estimate ages — the radius is how far that formation could have
  driven since, at the same accelerated clock movement runs on, floored at 0.4 km
  and capped at 30 km.

Regaining contact removes the ring and puts the unit back.

**The unit is still there and still fighting.** Only its graphics are gone: the
player has lost sight of it, not the game. It keeps taking part in combat, keeps
moving, and its rounds still land.

### Battle mode only

Fog is armed in the editor and only takes effect while a battle is running. The
scenario editor exists to lay out **both** sides; blanking half of what is being
edited would make it useless. Turning fog on with no battle running arms it for
the next one and leaves the map alone.

Stopping the battle, or turning fog off, reveals everything and clears every
contact.

### Hysteresis

A formation already being watched is held for an extra 0.6 km past the edge of
the arc. Without it, a unit walking the boundary of a view range would blink in
and out every sweep, which reads as a bug rather than as intelligence.

---

## 2. Reconnaissance tasks

Battle order bar → **RECON** → pick a task → **click a point on the ground**.
Recon wants a *point*, not a unit — the whole purpose is to look at ground you
cannot currently see, which by definition has nothing clickable on it. `Esc` or
right-click cancels; clicking terrain that has not streamed in yet leaves the
order armed.

Every task registers a **sensor**: a detection footprint the fog reads alongside
each unit's own eyes. The tasks differ in where that footprint sits, how wide it
is, and how it gets there.

| Task | Sensor | Unit moves | Sensor rides the unit | Notes |
|---|---|---|---|---|
| **RECON AREA** | ×1.9 view range | yes | no — waits on the objective | The unit drives there and searches it |
| **RECON ROUTE** | ×1.4 | yes | yes | Narrower: it is covering a line, not a box |
| **OBSERVE** | **×2.6** | **no** | yes (it is stationary) | Furthest-seeing task. Standing still on chosen ground is the best observation there is |
| **UAV RECON** | ×2.2 | no | the *sensor* flies | Straight over the terrain at 140 km/h, out and back, 90 s endurance |
| **COMBAT PATROL** | ×1.5 | yes | yes | Shuttles between start and objective until cancelled; fights normally |

All factors are > 1: a unit given a recon task is *looking*, rather than merely
being somewhere. The sensor radius is floored at 1.5 km so a short-sighted unit
still reports something.

An **axis arrow** in the task's colour runs from the unit to the objective while
the task is outbound, and fades on arrival — the same arrow the attack orders
use (`AxisArrow`), pointed at a fixed ground point rather than at a formation.

A task ends when the battle stops, the unit dies, or that unit is given another
order. Its sensor goes with it, and whatever it was holding in view returns to
fog.

---

## 2a. The terrain blanket

Hiding the enemy's counters while leaving every road, ridge and town in plain
view is not intelligence. So with fog in effect the **map itself** goes dark
outside what the player is covering: an unscouted valley is a blank rather than a
photograph with the enemy politely removed from it.

`FogBlanket` lays a grid over the operational area, clamps it to the terrain a
few tens of metres above the surface, and gives each vertex an alpha recomputed
on the fog's own sweep — from the same watchers and sensors the unit sweep uses,
so what the map shows and what the counters show can never disagree.

| Tier | Opacity | Meaning |
|---|---|---|
| Watched | clear | Something of the player's can see this ground now |
| Explored | 0.52 | They have been here, but are not looking now |
| Unexplored | 0.94 | Never observed this battle |

**Two tiers, not one.** Terrain does not move: a commander who has been somewhere
still knows the shape of it. Blacking ground out again the moment the patrol
leaves would make the map unnavigable without adding any uncertainty that hiding
the enemy does not already provide.

**Implementation notes.** Vertex colours rather than a projected texture, because
the project builds every material from code and this needs no shader of its own.
Heights are sampled a few hundred vertices per frame rather than all at once —
the grid is thousands of physics raycasts, and Cesium streams the terrain in
anyway, so a blanket that settles over the first second is both cheaper and more
correct than one that samples everything the instant the battle starts. Vertices
are only re-uploaded when a sample actually moved one. The grid is re-laid if an
advance carries a formation out toward its edge, and exploration is forgotten by
`RevealAll`, so the next battle starts blind.

---

## 3. Known leaks

Fog hides the **units**, and now the **ground**. Several derived graphics are
still computed from the truth rather than from what the player has seen, and will
report enemy positions the player has not earned:

| Leak | Why |
|---|---|
| Auto front line (`FrontlineSystem`) | Reads every living unit's position to place the boundary |
| Red sectors and FEBA (`SectorSystem`) | Derives the enemy's control measures from where its units stand |
| Enemy defence lines and markers | `DefenceOrderSystem` graphics are map data and are not fogged |
| Range rings on a selected enemy | Only reachable if the unit is visible, so minor |

The **DEPLOYED list** does *not* leak: hidden formations are excluded from it,
and a formation that vanishes while selected is deselected so the info panel
cannot keep reporting it.

Closing these means keeping a separate "what the player has seen" model and
deriving graphics from that instead of from the world. That is a larger change
than the fog itself, and is deliberately not in this one.

---

## 4. Where the code lives

| File | Role |
|---|---|
| `Assets/Scripts/Units/FogOfWarSystem.cs` | Detection sweep, hiding, contacts, sensor registry |
| `Assets/Scripts/Units/ReconTaskCatalog.cs` | The five recon tasks in numbers — the table in §2 |
| `Assets/Scripts/Units/ReconOrderSystem.cs` | Task lifecycle: outbound, on station, patrol, UAV flight |
| `Assets/Scripts/Units/AxisArrow.cs` | The objective arrow (shared with attack orders) |
| `Assets/Scripts/Units/UnitActor.cs` | `HiddenByFog`, `SetHiddenByFog` |
| `Assets/Scripts/Units/RangeRing.cs` | Range volumes — line of sight, weapon range, contact rings |
| `Assets/Scripts/Units/FogBlanket.cs` | The dark over unobserved ground (§2a) |
| `Assets/Scripts/UI/UnitPaletteUI.cs` | **GENERAL → INTELLIGENCE** toggles (line of sight, fog) |
| `Assets/Scripts/Core/GameController.cs` | `SetLineOfSightVisible`, ring captions |
| `Assets/Scripts/UI/UnitActionBarUI.cs` | The RECON button and its submenu |

## Cost

The sweep runs every 0.4 s and is O(friendly × enemy) distance checks — cheap
arithmetic, no raycasts. Contact rings are the expensive part: rebuilding one
re-samples the terrain under 96 vertices, so a ring is only rebuilt when its
radius has grown by more than 5%.

## Adding a recon task

1. Add a value to `ReconTask` in `Data/Enums.cs`.
2. Add its row to `ReconTaskCatalog.Defs`.
3. Nothing else — the submenu is built from the catalogue and
   `ReconOrderSystem` runs the same loop for every task.
4. **Update §2 of this file.**
