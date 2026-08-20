# Reinforcements

Formations that are not on the map at H-hour, and the clock that brings them on.
This is the human-readable version of
`Assets/Scripts/Units/ReinforcementSystem.cs` — **keep it in step with that file
in the same change.**

Written in **scenario mode**: left rail → UNITS → right-click a type → **ADD TO
REINFORCEMENT**, then tuned on the **REINFORCEMENT** tab.
Read in **battle mode**: left rail → **REINFORCEMENTS**.

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

## 2. Writing a schedule (scenario mode)

**Right-click a type on the UNITS page.** A card in the AVAILABLE list carries
three verbs now, and the two that are not "tell me about it" are on its
right-click menu:

| Entry | What it does |
|---|---|
| **ADD TO MAP** | Arms the placement ring. The next click on the ground puts that formation there — right-click or `Esc` cancels. See §2a |
| **ADD TO REINFORCEMENT** | Puts the type on this side's arrival schedule at H+30, and shows the REINFORCEMENT tab so you can see it land |

The side is the palette's own FRIENDLY / ENEMY tab, read at the moment of the
action — the same rule the drag already followed.

### 2a. ADD TO MAP — the placement ring

A drag onto the ground is still the direct gesture and stays the primary one. It
is also the wrong gesture when the ground you want is halfway across a map you
have to pan to reach, and it is not a gesture a menu entry can make at all. Armed
from the menu, the same footprint ring the drag previews with follows the cursor;
the next click drops the formation, and it refuses ground the terrain has not
streamed in yet rather than leaving a counter buried in a ridge.

While the ring is armed the map's own click handling stands down, so the click
that places the formation cannot also select whatever it landed on.

### 2b. The REINFORCEMENT tab

Called ARRIVING until it was renamed. The old caption named the *moment* rather than the thing — everything on a scenario board arrives at some point — and the word that says what the list actually is, is the one the rest of the game already uses for it: the card menu says ADD TO REINFORCEMENT, and this file is `30-REINFORCEMENTS.md`.

Third tab on the UNITS page, beside AVAILABLE and DEPLOYED — the three questions
in order: what is there, what have I put down, what is still to come. It lists
**this side's** schedule, earliest first.

| Control | What it does |
|---|---|
| **ARRIVES** ◄ ► | Moves the row five minutes earlier or later |
| **HOW MANY** ◄ ► | 1–24 formations of that type, arriving together |
| **✕** | Takes the row off the schedule |

**A row is an order, not a record**: everything on it can be changed from the
row, because a schedule that could only be added to would make the first mistake
permanent. What it deliberately cannot do is place the formation — that is what
the map is for.

**Adding the same thing twice bumps the count rather than adding a row.** Asking
for four battalions is four presses of the same card, and four identical rows
would be a list nobody can scan and a removal nobody can aim. "Identical" means
every field a designer chose — type, side, echelon and minute — so two rows that
differ in any of them stay two rows.

---

## 3. Reading it (battle mode)

**Left rail → REINFORCEMENTS** shows the schedule the scenario laid on, for the
side the tabs are set to — icon, how many, echelon, the minute it is due, and its
state.

| State | Means |
|---|---|
| `H+40` | The battle has not started; this is when it will come |
| `IN 12 MIN` | Counting down. Amber inside five minutes |
| `DUE` | Its minute has passed and it is arriving |
| `ARRIVED` | On the map. The row stays, greyed |

**NOW** on a pending row brings it on at once. A commander who can see a reserve
due at H+40 and wants it at H+12 is making a choice the scenario left them;
spending an arrival early is not the same as inventing one, and the row goes to
ARRIVED either way, so it cannot be spent twice.

**It shows the schedule, not a catalogue.** This page used to be all 117 types
with a DEPLOY on every card, which made a battle a shop: anything either side
owned could be conjured into the deployment zone at any moment, and the schedule
the designer had written was a separate thing nobody could see. Showing only what
the scenario laid on is what makes a reinforcement a plan rather than a resource.

An arrived row stays because it is the record of what the fight has been given so
far, which is exactly what a commander counting their reserves is asking.

---

## 4. Timing

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

## 5. Where they arrive

Both routes — a row coming due and a row called forward with NOW — land the same
way, through the same `Place` call. A second placement rule would mean the
deployment zone meant one thing to the designer's schedule and another to the
commander's reserve.

The mission's **deployment zone** for that side (docs/22-MISSIONS.md §1c) —
scattered over it on the same golden-angle disc the artillery sheaf uses, so a
row of six battalions arrives as a laydown rather than as six counters on one
point. A row's formations are scattered against its own position in the schedule,
so two rows due in the same minute do not land on each other.

**With no zone named**, arrivals appear ~8 km behind their own side's centre of
mass, away from the enemy. That is the honest fallback — a reinforcement comes
from the rear, and the rear is wherever the army already is — but it is not a
choice anybody made, which is the argument for naming a zone.

Arrivals come on through the **same spawn path as a hand-placed unit**
(`GameController.OnPaletteDrop`), so they get the undo record, the deploy effect
and the generated formation name that every other unit gets.

---

## 6. Saving

Written to the map file as `reinforcements`:

```json
"reinforcements": [
  { "defId": "mech_infantry", "team": "User",
    "echelon": "Battalion", "arrivalMinutes": 40, "count": 3 }
]
```

`count` defaults to 1, so a file written before the field existed loads as the
single formation it was.

`arrived` is **not** saved: a scenario file is a starting state, and a reserve
that had already come on when the file was written must still come on when it is
played. Empty on an older map, which reads as "everything this scenario has is
already on it".

---

## 7. Where the code lives

| File | Role |
|---|---|
| `Units/ReinforcementSystem.cs` | The schedule, the countdown, `Add`/`StepCount`/`BringForward`, and where an arrival lands |
| `Data/MapSaveData.cs` | `ReinforcementEntry` (incl. `count`) and the `reinforcements` list |
| `Data/MissionData.cs` | `MissionZone` — the deployment zones (docs/22 §1c) |
| `UI/UnitPaletteUI.Units.cs` | The card right-click menu, and the REINFORCEMENT tab that edits the schedule |
| `UI/UnitPaletteUI.Deploy.cs` | `ArmPlacement` / `TickPlacement` — the click-to-place ring |
| `UI/UnitPaletteUI.Force.cs` | `BuildReinforcementSection` — the battle-mode read-only view, with NOW |
| `Core/GameController.cs` | Wiring, the spawn path, `DeploymentZoneFor` |

---

## 8. Known gaps

- **No arrival warning.** The enemy's schedule is as invisible to the player as
  their own is visible; a recon or intelligence hint that something is coming
  would be the natural next step.
- **No conditional triggers.** Arrivals are timed and nothing else — "when the
  FLOT is breached" or "when this objective falls" would need the trigger model
  that docs/28-FLOT.md §13 also wants.
- **Echelon is fixed at battalion**, the same default the deploy palette uses;
  the REINFORCEMENT row shows it but cannot change it.
- **Nothing routes them forward.** They arrive in the zone and stand there until
  ordered; a scenario that wants them marching on arrival has to be given that
  order by hand.
