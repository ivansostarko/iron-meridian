# Combat Orders

The register of every order a unit can be given in **battle mode**. This is the
human-readable version of `MoveTaskCatalog.cs`, `AttackTaskCatalog.cs`,
`ReconTaskCatalog.cs`, `DefenceOrderSystem.cs`, `ManoeuvreOrderSystem.cs` and
`PlannerSystem.cs` — keep it in step with them in the same change.

Orders live on the bottom **order bar** (`UnitActionBarUI`), which appears only
while a battle is running and at least one living formation is selected. In
scenario mode the bar is hidden and every order below is refused: the editor
places counters, it does not fight.

**The same bar gives a group its orders** — see §1b. There is no second order
bar anywhere: the group panel on the right names and recalls groups, it does not
order them.

```
ORDERS — 1ST INFANTRY BATTALION
┌────────┬────────┬────────┬─────────┬──────────┬─────────┐
│  MOVE  │ ATTACK │ RECON  │ DEFENCE │ COMMANDS │ PLANNER │
└────────┴────────┴────────┴─────────┴──────────┴─────────┘
     │        │        │        │          │          │
   ×5 move   ×1     ×1 recon  ×3       ×3 standing  ×3 plans
   tasks    attack   task    defensive   switches
                             tasks
```

**Four of the six are tasks and two are not.** MOVE, ATTACK, RECON and DEFENCE
each take an objective: pick the task, then click the map. COMMANDS are standing
switches that apply the moment they are clicked. PLANNER draws intentions that
nothing executes.

---

## 1. Picking the ground

Every task on the bar works the same way, through one mechanism —
`SelectionManager.ArmGroundPick`. Pick the task, and the next click on the map
is its objective. `Esc` or a right-click cancels; leaving battle mode cancels.

A click on terrain that has not streamed in yet **leaves the order armed** and
says so, so a click on a tile the map is still fetching costs one more click
rather than the whole order.

It used to be one bespoke armed flag per order — one for move, one for attack,
one for recon — and each new order meant another flag, another branch in
`SelectionManager.Update` and another pair of resolve callbacks. A callback
carries everything that differed between them.

## 1a. What a placed order draws

Everything placed draws through **one system**, `Units/TaskAreaSystem.cs`, so a
defence, a recon objective and a rally point are read the same way. Three
shapes, and they answer three different questions:

| Shape | Question it answers | Used by |
|---|---|---|
| **Ring** | *How far from here* — a circle about the point, radius called out on the rim | HOLD, ATTACK, RETREAT, the three plain moves |
| **Line** | *Which line do I hold* — a bowed trace across the threat axis, named along its length | DEFEND, WITHDRAW |
| **Quadrants** | *Which ground do I cover* — four sectors, each labelled on its own border | GUARD, RECON AREA |

Quadrants for the two covering tasks because that is what screening and
searching actually are: responsibility divided up and allocated, not a place
somebody stands.

Each area also carries:

- **3D volume.** A `TargetAreaMarker` at the objective — the same volume a
  called strike is placed with — so the area reads in three dimensions rather
  than as a decal on the terrain.
- **Particles.** Looping motes tinted by intent: `TaskAreaDefend`,
  `TaskAreaAttack`, `TaskAreaRecon`, `TaskAreaMove` (docs/08-PARTICLE-SYSTEMS.md).
  Attached to the area rather than played at it, because a one-shot puff says
  something *happened* and a task area is a standing state.
- **Labels.** On the line for a line, on the rim for a ring, on each border for
  the quadrants — the task, the formation, and the size.
- **A select animation.** The volume swells over 0.45 s when the area is placed,
  and again whenever its formation is selected; the area's lines thicken from
  45 m to 110 m while it is selected. The whole map can be carrying orders at
  once, and without this a screen of overlapping areas says nothing about which
  one belongs to the formation being commanded.

The reveal runs on the volume's alarm channel rather than by rewriting the
lines: line width goes through `MapLine.RefreshStyle`, which rebuilds the
polyline's geometry, and doing that per frame for every order on the map would
be a rebuild storm.

Everything is ordinary map data — lines through `LineManager`, markers through
`MarkerManager` — so a task area survives a save/load. Ids are prefixed
`task-<unit>-`, clear of the `sector-` set that "clear tactical graphics"
regenerates.

---

## 1b. Orders given to a group

Select two or more formations and the bar is captioned
`GROUP ORDERS — <group name> · <count>`, or `— N FORMATIONS` when the selection
is not all one group. Naming a group that only half the selection belongs to
would be the bar lying about what it is going to act on.

All six buttons work, and **every formation in the selection carries the order
out**. That is the whole difference: a group is not a different kind of thing
from a formation as far as orders go, so it uses the same six verbs in the same
place.

### The frontage

One click is one objective, but six battalions cannot occupy one grid square.
Sending them all to the same coordinate piles six counters, six objective rings
and six defensive lines on top of each other, and the player has ordered
something no formation could carry out. So **the click sets the centre of a
frontage** and the formations are laid out across it, perpendicular to the axis
of advance — which is what a frontage is
(`GameController.ForSelectionOnGround`).

| | |
|---|---|
| **Axis** | From the selection's centre to the clicked point |
| **Spacing** | The formations' own mean weapon range × 0.35, clamped to 0.6–4 km. A group of mortar companies packs tighter than a group of rocket battalions, and each can still cover its neighbour |
| **Order of march** | Sorted by where they already stand across that axis, so the left-hand formation gets the left-hand slot and nobody is sent across the front of anybody else |

Which orders are spread, and which are not:

| Order | Given to the group as |
|---|---|
| **MOVE** (all five tasks) | A frontage — each formation gets its own destination and its own objective ring |
| **DEFENCE** (DEFEND / HOLD / GUARD) | A frontage — each holds its own stretch of the line, side by side |
| **RECON** | A frontage — each searches its own area, sized from its own sensor reach |
| **ATTACK on ground** | A frontage onto the objective — each formation attacks its own piece of it |
| **ATTACK on a formation** | **Not spread.** Everything selected attacks the formation that was clicked; a named target is a named target |
| **COMMANDS** (STOP / FREE MOVEMENT / AUTO ATTACK) | Applied to every formation, flipped from the lead's current state so a mixed selection ends up all one way |
| **PLANNER** | The lead formation only. A plan is one axis, not one per battalion |

## 2. Move — five tasks

Rows live in `Units/MoveTaskCatalog.cs`.

| Task | Speed | If caught moving | Objective | What it is |
|---|---|---|---|---|
| **MOVE** | ×1.0 | ×1.0 | Ring | March at the formation's own speed |
| **FAST MOVE** | ×1.65 | **×0.55** | Ring | Road march — quick, strung out, in no state to fight |
| **TACTICAL MOVE** | ×0.6 | ×1.15 | Ring | Bounding advance — slow, in contact formation |
| **WITHDRAW** | ×1.25 | ×0.75 | **Line** | Break contact **at 50% strength** |
| **RETREAT** | ×1.5 | ×0.45 | Ring | Fall back **at 30% strength** |

**Three are moves and two are plans.** The first three execute the moment they
are given and differ only in the trade between speed and readiness — the whole
of the choice is *how much of a hurry am I in, and what am I willing to be
caught as*. FAST MOVE is not simply the better option: a column at road-march
pace fights at half weight.

WITHDRAW and RETREAT are **not journeys the player is ordering now**. The
formation carries the objective and goes when its own strength falls to the
task's trigger. That is the point: a commander cannot decide what happens when a
battalion breaks *at the moment it breaks*, so they decide beforehand and the
formation carries it out. Giving both to a fresh formation is how you decide in
advance what happens when it is hurt and when it is finished.

Triggers are checked once a second by `ManoeuvreOrderSystem` — a formation's
state does not meaningfully change between two combat ticks.

`in transit` multipliers are held on the catalogue row and **are not read by the
damage model yet**; the figure belongs with the task rather than being invented
at the point it is finally needed.

---

## 3. Attack — one task

Pick ATTACK, then click **either an enemy formation or bare ground**.

**A click on terrain is an order, not a miss.** It used to be refused on the
grounds that an attack needs a target; but with fog of war on, the ground you
most want to attack is exactly the ground you cannot see a counter on. Clicking
terrain attacks the **area** — everything hostile inside it, and anything that
walks into it while the order stands.

| Clicked | What the order becomes |
|---|---|
| An enemy formation | Close to engagement range and destroy it. Ends when the target dies. |
| Bare ground | An objective ring of `weaponRangeKm × 0.5` (0.6–8 km). The attacker closes to a firing position and engages whatever is inside; when that dies it **re-acquires** the next thing on the objective, and holds when there is nothing. |

**Out of range is not a refusal.** The attacker marches to a firing position by
itself — along the line to the objective, stopping a little inside its own
engagement range — and opens fire on arrival. `engageRangeFraction` on the
catalogue row is what "in range" means: 0.85 of the formation's own weapon
range, so it closes rather than sniping from the very edge.

**One task, deliberately.** There used to be five — attack, assault, suppress,
ambush, counterattack — separated by numbers the player could not see. What a
commander is deciding at this level is *where* to attack. The def keeps every
field the five used (shock, return fire, opening volley, obscuration) because
those are what a second task would be *made of*; the table having one row is a
statement about the menu, not about the model underneath it.

### Precedence over automatic combat

A unit acting on an explicit attack order is skipped by `CombatSystem`'s
automatic sweep, so it fires once a tick at what it was told to rather than
twice — once at its objective and once at whatever else is in range.

### Not saved

Orders are live state, not map data. A save records where formations are, not
what they were told; reloading a battle leaves every unit idle on the ground it
was standing on. The *graphics* an order drew do survive, because those are
ordinary lines and markers.

---

## 4. Recon — one task

**RECON AREA.** Pick it, click the centre of the ground to search. Four
quadrants are drawn, sized to what the formation will actually see —
`viewRangeKm × sensorRangeFactor`, so the area flatters a surveillance radar and
not a scout car — and the formation moves there and searches it.

The other four recon tasks (route, observe, UAV, combat patrol) are gone from
the menu. `ReconTaskDef` keeps the fields they used — scanning on the move, an
airborne sensor, patrolling — and `ReconOrderSystem` still honours every one of
them; the table simply has nothing that sets them. Full behaviour in
[16-FOG-OF-WAR.md §2](16-FOG-OF-WAR.md).

---

## 5. Defence — three tasks

All three are now **placed**: the player picks the ground, and the task is laid
out around that point rather than around wherever the formation happened to be
standing. Aiming a defence used to mean moving the formation first and giving
the order again.

| Task | Draws | What it does |
|---|---|---|
| **DEFEND** | Line + doctrinal defence line + battle position | Lays the line across the threat axis through the chosen ground, encloses the depth behind it, and **distributes the commander's subordinates along the frontage** so the line is manned rather than merely drawn. The commander sits back inside the position. |
| **HOLD** | Ring | Puts the formation on the ground and pins it there, facing the threat. Radius is what the formation can actually hold. |
| **GUARD** | Quadrants | Screens a sector in four. Wider than a hold: a screen covers ground rather than occupying it, so the same formation is thinner on all of it. |

Orientation always comes from the enemy — the threat axis is the bearing to the
centre of the opposing force. With no enemy on the map the unit's own facing
stands in, so the tasks still work while a scenario is being built up.

Subordinates are the commander's **group** if it has one, otherwise the smaller
friendly formations within 12 km. Grouping is explicit in the editor, so it wins;
proximity is the fallback that makes the order useful on a map nobody has
grouped.

---

## 6. Commands — three standing switches

Not orders: switches on how the formation behaves when nothing else is telling
it what to do. They apply the moment they are clicked — there is no ground to
pick — and the two toggles carry a **lamp** showing their current state, because
a switch you cannot read the state of is a switch you press twice to find out.

| Command | Default | What it does |
|---|---|---|
| **STOP** | — | Cancels the march, the contingency and every graphic either put on the map. Does **not** touch the two switches: stop means "stop what you are doing", not "forget what you are". |
| **FREE MOVEMENT** | **Off** | When idle, roam within **50 km** of where it was released. Off by default: a formation that wandered off the ground the player put it on, because they did not know a switch existed, would be the game losing their scenario for them. |
| **AUTO ATTACK** | **On** | Engage anything that comes into range without being told. On by default, because that is what every formation did before this existed. Turning it off is how a screen or a reconnaissance element is kept out of a fight it cannot win. |

**Free movement is the lowest-priority thing a formation does.** It only runs
when the unit is not marching, not in contact and has no contingency waiting — a
unit that wandered off mid-fight because a switch was on would be the switch
overriding the battle. The radius is anchored to where it was switched on, so
the formation works in the ground it was given rather than drifting across the
map one hop at a time.

**Auto attack off does not take the unit out of the battle.** It is still in
contact and still takes what is coming; it simply does not open fire of its own
accord. An explicit attack order is unaffected — that is the player telling it
to shoot, not the sweep deciding for it.

Both are saved with the unit (`UnitState.freeMovement`, `automaticAttack`),
because "this battery does not shoot at what wanders past" is a property of the
scenario as much as its position is. Commands are given to the **whole
selection**, flipped from the lead formation's state so a mixed selection ends
up all one way.

---

## 7. Planner — three entries

**Nothing here executes.** Every other control on the bar makes a formation do
something now; an operation is not a sequence of those. It is a main effort, the
supporting efforts that make it possible, and the line everything falls back to
if it does not work — decided before any of it is ordered, and useful precisely
because it is written down where it can be looked at while the fighting is
happening.

| Entry | Draws |
|---|---|
| **MAIN ATTACK** | A heavy 140 m arrow from the formation to the picked ground, in attack orange, dashed |
| **SUPPORTING** | A 70 m arrow in a lighter amber |
| **RETREAT LINE** | Calls `MOVE → RETREAT` — the same order, not a copy of it |

The weight difference is the whole point: two identical arrows would be two
arrows, a weighted pair is a plan. Both are drawn `planned`, so they render
broken — that is what a control measure that has not happened yet looks like
everywhere else on this map.

The retreat line is **not** a separate planner feature. It is a movement
contingency, because unlike the two axes it is something a formation actually
carries out; two controls that looked like planning and behaved differently
would be worse than one control in two places.

Axes are built as **outlines**, not filled meshes, because everything on this
map is a draped polyline — an arrow that was a mesh would be the one graphic
that did not follow the terrain, and over a ridge that difference is the whole
picture. Ids are prefixed `plan-`.

---

## 8. Where the code lives

| Script | Role |
|---|---|
| `UI/UnitActionBarUI.cs` | The six-button bar and its submenus |
| `Units/SelectionManager.cs` | `ArmGroundPick` — one mechanism for every placed order |
| `Units/TaskAreaSystem.cs` | Ring / line / quadrant areas, labels, motes, select pulse |
| `Units/MoveTaskCatalog.cs` | The five movement tasks in numbers |
| `Units/ManoeuvreOrderSystem.cs` | Movement orders, the two contingencies, the standing commands |
| `Units/AttackTaskCatalog.cs` | The offensive task in numbers |
| `Units/AttackOrderSystem.cs` | Order lifecycle: approach → engage; `OrderArea` for ground attacks |
| `Units/ReconTaskCatalog.cs` | The reconnaissance task in numbers |
| `Units/ReconOrderSystem.cs` | Recon lifecycle and the sensors the fog reads |
| `Lines/DefenceOrderSystem.cs` | Defend / hold / guard: frontage, threat axis, subordinate distribution |
| `Units/PlannerSystem.cs` | The two drawn axes |
| `Units/AxisArrow.cs` | The live attack/recon axis arrow — unit to target |
| `Core/GameController.cs` | Wires all of it, owns the objective-sizing rules and lays a group's orders out across a frontage (`ForSelectionOnGround`) |

---

## Adding a task

1. **A row in the right catalogue** — `MoveTaskCatalog`, `AttackTaskCatalog`,
   `ReconTaskCatalog`. The menu is read off the catalogue, so the caption and
   the one-liner cannot drift from the behaviour.
2. **Name a `TaskAreaShape`** if it is placed. Do not build a graphic at the
   call site; `TaskAreaSystem` draws all three shapes.
3. **A `MarkerKind`** if it pins a point, plus its colour in `TaskMarker.Tint`.
4. **A `VfxId` + catalogue row** if it needs its own motes, with a procedural
   fallback (golden rule 11).
5. **Update this file**, and docs/08-PARTICLE-SYSTEMS.md if an effect was added.

## Related

docs/03-GAMEPLAY.md (the map editor and movement) · docs/16-FOG-OF-WAR.md (what
recon is for) · docs/08-PARTICLE-SYSTEMS.md (the task-area effects) ·
docs/23-COMMANDERS.md (who a defence's subordinates are)
