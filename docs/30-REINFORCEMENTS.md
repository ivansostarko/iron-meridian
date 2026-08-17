# Reinforcements

Formations that are not on the map at H-hour, and the clock that brings them on.
This is the human-readable version of
`Assets/Scripts/Units/ReinforcementSystem.cs` — **keep it in step with that file
in the same change.**

Left rail → **REINFORCEMENTS**.

---

## 1. Why a scenario needs them

Everything deployed in the editor is present when the battle starts, which makes
every fight a single roll of everything both sides own. Reinforcement is what
turns that into a shape:

- a counter-attack that arrives at H+40,
- a reserve battalion the defender is waiting for,
- an enemy echelon the player knows is coming and has to be ready for.

It is the cheapest possible way of adding **time** to a game that otherwise only
has space.

---

## 2. The panel

**Deliberately the same panel as UNITS**, control for control: blue/red team
tabs, a search box, and the same branch accordion over the same 117 unit types. A
commander calling a battalion forward is doing exactly what a designer does when
they deploy one, and making them learn a second way to pick a unit would be
inventing a difference that is not there. The accordion's headings — INFANTRY,
ARMOUR and the rest — are inset 25 px so an arm's name reads as a heading over
its cards rather than as another card.

The one thing that *is* different is the verb:

| UNITS | REINFORCEMENTS |
|---|---|
| Drag a type onto the ground | **Click** a type |
| Where the cursor was | In its side's **deployment zone** |

**Click and it is there.** The panel used to schedule: an ARRIVES AT stepper set
a time, a SCHEDULED tab held the queue, and the formation appeared at H+n. That
is an authoring tool, and this row is on the rail's **battle** mode — a commander
asking for a reserve wants it committed, not diarised. So a card places the
formation immediately, scattered off whatever it placed before it, and the panel
carries no clock at all.

**The schedule itself has not gone.** `ReinforcementSystem` still holds one
loaded from the map file (§5) and still brings it on during a battle (§3). What
went is the panel for typing one in; a scenario that wants timed arrivals writes
them into the map's `reinforcements` list.

---

## 3. Timing (a schedule loaded from the map file)

Scheduled in **scenario minutes after the battle starts**, not at an absolute
clock time. A designer thinks in "forty minutes in", the figure survives changing
H-hour, and it reads the same whatever speed the battle is watched at — the clock
is the operational one, so ×60 brings the reserve on sixty times sooner in real
seconds and at exactly the same moment in the fight.

**Starting the battle re-arms every arrival.** A schedule that kept running
across a stop would be unusable in the editor, where a battle is started and
stopped a dozen times while a scenario is tested: the second run would begin with
its reserves already spent.

---

## 4. Where they arrive

Both routes — a card clicked on the panel and an entry coming due on the
schedule — land the same way, through the same `Place` call. A second placement
rule would mean the deployment zone meant one thing to the designer's schedule
and another to the commander's reserve.

The mission's **deployment zone** for that side (docs/22-MISSIONS.md §1c) —
scattered over it on the same golden-angle disc the artillery sheaf uses, so an
echelon of six battalions arrives as a laydown rather than as six counters on one
point.

**With no zone named**, arrivals appear ~8 km behind their own side's centre of
mass, away from the enemy. That is the honest fallback — a reinforcement comes
from the rear, and the rear is wherever the army already is — but it is not a
choice anybody made, which is the argument for naming a zone.

Arrivals come on through the **same spawn path as a hand-placed unit**
(`GameController.OnPaletteDrop`), so they get the undo record and the deploy
effect that every other unit gets.

---

## 5. Saving

Written to the map file as `reinforcements`:

```json
"reinforcements": [
  { "defId": "mech_infantry_bn", "team": "User",
    "echelon": "Battalion", "arrivalMinutes": 40 }
]
```

`arrived` is **not** saved: a scenario file is a starting state, and a reserve
that had already come on when the file was written must still come on when it is
played. Empty on an older map, which reads as "everything this scenario has is
already on it".

---

## 6. Where the code lives

| File | Role |
|---|---|
| `Units/ReinforcementSystem.cs` | The schedule, the countdown, `DeployNow`, and where an arrival lands |
| `Data/MapSaveData.cs` | `ReinforcementEntry` and the `reinforcements` list |
| `Data/MissionData.cs` | `MissionZone` — the deployment zones (docs/22 §1c) |
| `UI/UnitPaletteUI.cs` | `BuildReinforcementSection` — the panel, and the deployment-zone block in MISSIONS |
| `Core/GameController.cs` | Wiring, the spawn path, `DeploymentZoneFor` |

---

## 7. Known gaps

- **No arrival warning.** The enemy's schedule is as invisible to the player as
  their own is visible; a recon or intelligence hint that something is coming
  would be the natural next step.
- **No conditional triggers.** Arrivals are timed and nothing else — "when the
  FLOT is breached" or "when this objective falls" would need the trigger model
  that docs/28-FLOT.md §13 also wants.
- **Echelon is fixed at battalion**, the same default the deploy palette uses.
- **No way to author a schedule from the UI** since the panel became immediate.
  The map file still carries one and the system still plays it; only hand-editing
  puts one there.
- **Nothing routes them forward.** They arrive in the zone and stand there until
  ordered; a scenario that wants them marching on arrival has to be given that
  order by hand.
