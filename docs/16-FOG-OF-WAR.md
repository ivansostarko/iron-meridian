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
| **Observed** — inside a friendly unit's `viewRangeKm`, or a recon sensor's footprint (including a reconnaissance drone's) | Drawn normally |
| **Contact** — the two units' view circles cross, but it is outside yours | **Removed from the map**, and a live contact ring placed on it |
| **None** — no overlap at all | Removed from the map, and nothing drawn |

### Three tiers, not two

Seeing used to be a switch: inside somebody's view range, or invisible. That threw
away the most interesting state on the map — two formations whose *observation*
overlaps but neither of which has the other in view. Real reconnaissance mostly
lives there: you know something is out there long before you can describe it.

So an enemy whose own view circle crosses one of yours, without being inside it,
becomes a **live contact** — captioned `UNIDENTIFIED · IN CONTACT hh:mm`, sized at
45 % of that formation's own view range, and re-centred on it every sweep. It
**tracks** rather than ageing, because something is watching that ground right
now; that is the whole difference from a lost contact, and it is what lets a
formation be followed at arm's length without ever being identified.

`FogOfWarSystem.Sighting` is the enum; `Detect` returns it.

### Nothing is invented

A formation you have **never observed** leaves no ring. Only what has actually been
seen and then lost ages into a last-known contact — tracked in `_everSeen`.

This was a real leak: on the first sweep of a battle every enemy was, technically,
transitioning from "not hidden" to "hidden", so every one of them dropped a
`LAST SEEN` ring on its exact start position. Turning fog on handed the player the
entire enemy order of battle.

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

**One task.** Route recon, observe, UAV recon and combat patrol were removed from
the menu — with fog of war on, the question is always *which ground do I want to
see*, and the answer is an area. `ReconTaskDef` keeps every field the four used
(scanning on the move, an airborne sensor, patrolling) and `ReconOrderSystem`
still honours all of them; the table simply has nothing that sets them, so a
second task is a row rather than a rewrite.

**The objective is drawn.** Four quadrants about the point, sized to
`viewRangeKm × sensorRangeFactor` — the ground the formation will *actually*
see rather than a fixed circle — each labelled on its own border, with looping
`TaskAreaRecon` motes over it. Quadrants because searching is responsibility
divided up, not a place somebody stands. See docs/15-COMBAT-ORDERS.md §1a.

All factors are > 1: a unit given a recon task is *looking*, rather than merely
being somewhere. The sensor radius is floored at 1.5 km so a short-sighted unit
still reports something.

**Sorties and dwells are scenario time.** The UAV's endurance was 90 seconds and
a patrol's dwell on the objective four, back when movement ran at sixty times a
formation's real speed and 90 seconds bought 210 km of flight. Movement is now on
the scenario clock (docs/13-DATE-AND-TIME.md), so those figures are what they
say: an hour on station and three minutes on the objective. At 90 seconds the
sensor would have got three kilometres out before turning for home. Speed the
clock up to watch a sortie complete quickly.

An **axis arrow** in the task's colour runs from the unit to the objective while
the task is outbound, and fades on arrival — the same arrow the attack orders
use (`AxisArrow`), pointed at a fixed ground point rather than at a formation.

A task ends when the battle stops, the unit dies, or that unit is given another
order. Its sensor goes with it, and whatever it was holding in view returns to
fog.

### The reconnaissance drone's sensor

One more sensor is registered from outside `ReconOrderSystem` entirely: the
**reconnaissance drone** in the UAV STRIKES menu (docs/19-UAV-STRIKES.md). It is
not an order given to a formation — there is no unit involved at all — but it
speaks to the fog through exactly the same interface.

| | |
|---|---|
| Registered by | `UavStrikeSystem.RunReconnaissance` → `FogOfWarSystem.AddSensor` |
| Radius | **10 km**, fixed (`UavDef.reconRadiusKm`) |
| Registered when | The drone **arrives** on station, not when the mission is tasked |
| Removed when | It turns for home, five scenario minutes later |

The timing is the point. The ground is uncovered because something is over it
looking: a footprint that appeared the moment the mission was ordered would be
intelligence the player has not paid for yet, and one that lingered after the
drone left would be intelligence nobody is gathering.

What survives the sortie is what reconnaissance actually leaves behind. The
terrain it uncovered stays **explored** in the blanket (§2a), and every enemy
formation it saw becomes a **last-known contact** with the scenario time stamped
on it, whose ring then grows exactly as any other stale contact's does. The live
view goes home with the drone.

Like every sensor here, it does nothing unless fog is armed *and* a battle is
running — `InEffect`. Flying one in the editor is a nine-second animation and no
intelligence, which is what the rail section warns.

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

## 2b. The mission area

A single-player mission can name the ground it is fought over — a closed polygon
on the mission record, drawn in the editor's MISSIONS panel. See
docs/22-MISSIONS.md §1a for how it is authored and stored; what it does to the
fog is here.

**Outside the area is a fourth tier, above all three others.**

| Tier | Opacity | Cleared by |
|---|---|---|
| Watched | clear | Anything of the player's, in bounds |
| Explored | 0.52 | Having been watched earlier this battle |
| Unexplored | 0.94 | Being watched |
| **Out of bounds** | **0.97** | **Nothing** |

**It applies whether or not fog is armed.** A bounded mission blacks out its
surroundings in battle even with `fogOfWar = false`: the boundary is what the
scenario *is*, not an intelligence setting. `FogOfWarSystem` splits the two
questions accordingly —

| Property | True when | Governs |
|---|---|---|
| `InEffect` | fog armed **and** battle running | Hiding formations, contacts |
| `BlanketInEffect` | (fog armed **or** area set) **and** battle running | Whether the blanket is laid at all |

With an area but no fog the blanket runs in **mask-only** mode: everything inside
is simply clear, and the blanket is doing nothing but framing the battlefield.

**Three further consequences in the sweep:**

- A **watcher outside the area reveals nothing.** It is dropped from the watcher
  list before the sweep runs — otherwise something standing off the map would
  punch a hole in the dark from outside the battlefield.
- An **enemy outside the area is hidden outright**, and gets *no* contact ring. A
  contact says "this was seen and could still be somewhere"; that is the wrong
  claim about a formation that is out of the scenario.
- The grid is **laid over the mission's ground, not the units'**, and never
  re-fitted. It covers about 1.6 × the area's radius so the dark reaches the edge
  of the screen — a mask that stopped at the boundary would leave a lit ring of
  out-of-bounds terrain around it, saying the opposite of what it is for.

**Grid sizing.** The half-extent ceiling is 45 km when the blanket is following
the units and **200 km** when it is covering an area, or a 120 km scenario would
have its boundary drawn well inside itself — a mask that lies about where the
battlefield ends is worse than no mask. Vertices per side then follow the extent
(80 → 128, aiming at ~1.4 km cells) so a cell stays about the same size on the
ground: a fixed 80² grid over a 260 km theatre would step in four-kilometre
blocks.

Which vertices fall inside is computed **once**, when the grid is laid: six to
sixteen thousand point-in-polygon tests several times a second, for an answer
that cannot change during a battle, would be the most expensive thing the fog
does. Changing the area therefore has to re-lay the grid, which
`FogOfWarSystem.SetArea` does.

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
| Combat across the mission boundary | An out-of-bounds formation is hidden but still fights; `CombatSystem` does not read the area |

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
| `Assets/Scripts/Units/ReconTaskCatalog.cs` | The recon task in numbers — the table in §2 |
| `Assets/Scripts/Units/ReconOrderSystem.cs` | Task lifecycle: outbound, on station, patrol, UAV flight |
| `Assets/Scripts/Units/AxisArrow.cs` | The objective arrow (shared with attack orders) |
| `Assets/Scripts/Units/UnitActor.cs` | `HiddenByFog`, `SetHiddenByFog` |
| `Assets/Scripts/Units/RangeRing.cs` | Range volumes — line of sight, weapon range, contact rings |
| `Assets/Scripts/Units/FogBlanket.cs` | The dark over unobserved ground (§2a) and outside the mission area (§2b) |
| `Assets/Scripts/Data/MissionArea.cs` | The mission boundary: containment, extent, clamping (§2b) |
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
