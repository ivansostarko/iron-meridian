# Fog of War & Reconnaissance

Limited intelligence: the player sees the enemy only where something of theirs is
actually looking, and the reconnaissance tasks that extend that reach.

> Off by default, and armed from the map editor's **GENERAL** panel. Keep this
> file in step with `FogOfWarSystem.cs` and `ReconTaskCatalog.cs`.

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

## 3. Known leaks

Fog hides the **units**. Several things are still computed from the truth rather
than from what the player has seen, and will report enemy positions the player
has not earned:

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
| `Assets/Scripts/Units/RangeRing.cs` | The contact ring, with its caption override |
| `Assets/Scripts/UI/UnitPaletteUI.cs` | **GENERAL → INTELLIGENCE** toggle |
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
