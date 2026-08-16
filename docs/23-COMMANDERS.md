# Commanders

The order of battle **above** the units: who commands what, on both sides, and what it costs when a headquarters goes down.

> **Keep this file current.** Every change to the rank ladders, to the command bonus, or to how commanders are stored must be recorded here in the same change. See [Rules](#rules) at the bottom.

---

## 1. Why command is modelled at all

Without it a scenario is a bag of counters that all fight equally well whatever happens to the ones behind them. With it:

- a formation belongs to somebody,
- that somebody belongs to somebody,
- and knocking out a headquarters degrades everything under it.

That last line is the point. It is the reason armies are shaped like this, and the reason a deep strike on a divisional command post is worth flying.

**Unassigned is exactly neutral.** A scenario with no commanders fights precisely as it did before they existed — `CommandBonus` returns 1.0 for a formation with no `commanderId`. Adding commanders is opt-in, per scenario, and every map saved before this feature loads as "nobody is in command".

---

## 2. The model

`Data/CommanderData.cs` and `Units/CommanderRegistry.cs`.

| Type | Role |
|---|---|
| `RankDef` | One rank on one side's ladder: name, abbreviation, the echelon it typically commands, and its tier |
| `RankCatalog` | The two ladders, and the lookups over them |
| `CommanderState` | One officer: id, side, surname, rank, in-post flag, superior |
| `CommanderRegistry` | The live list, the chain walk, assignment, seeding, save/load and the combat bonus |

**A commander is a record, not a unit.** He is not on the map, cannot be shot at and occupies no ground. What he does is *own* formations, and own other commanders.

**Flat list, one parent pointer.** A commander owns formations through `UnitState.commanderId`, and may own other commanders through `CommanderState.superiorId`. A tree would have to be rebuilt on every reassignment; a flat list with one parent pointer is the same information and survives a subordinate being deleted.

**One commander, many units.** The pointer lives on the unit rather than as a list on the commander, because the inverse would need keeping in step on every reassignment and every deletion.

### The two rank ladders

Not one shared enum. NATO and the enemy do not have the same ranks, and folding them together would either invent a correspondence that does not exist — a Polkovnik is not quite a Colonel — or force one side to wear the other's insignia.

| NATO (friendly) | Enemy |
|---|---|
| Lieutenant · Captain · Major · Lieutenant Colonel · Colonel | Leytenant · Starshiy Leytenant · Kapitan · Mayor · Podpolkovnik |
| Brigadier General · Major General · Lieutenant General · General | Polkovnik · General-Mayor · General-Leytenant · General-Polkovnik · General Armii |

The enemy ladder is **transliterated, not translated**, for the same reason: giving the enemy its own words is most of what makes the two orders of battle read as two armies.

A commander's rank is stored as the rank's **name**, not an index, so a saved order of battle survives a ladder gaining an entry. An unrecognised name falls back to the foot of the ladder rather than throwing.

---

## 3. The chain of command

`ChainIntact(commander)` walks upward: the officer must be in post, and so must every officer above him.

**The walk is bounded, not recursive.** The superior pointer is editable and a cycle — A reports to B reports to A — is one mis-click away. A plain recursion would hang the game rather than degrade a bonus, which is not a trade worth making for four lines of code. Sixteen hops, then the chain is treated as broken, which is the safe reading: an order of battle that eats its own tail is not one anybody is being commanded through.

**Cycles are refused at the point of assignment.** `WouldCycle` is checked before a superior is set, so the list can never hold one. The panel's superior stepper skips illegal choices rather than erroring on them — an error message on every third press of a cycle button is not a warning, it is noise.

### What it is worth

| State | Multiplier on the formation's fire |
|---|---|
| No commander | **1.00** — neutral, not punished |
| In post, chain intact | **1.04 → 1.20**, scaled by the commander's rank on his own ladder |
| Out of action, or a broken chain above him | **0.88** |

Applied in `CombatSystem.ResolveAttack` as one more term in the existing modifier chain. Deliberately modest: command should decide close fights, not replace the fighting.

**Taking a commander out of action keeps his formations and his place in the chain.** That is the point of the switch — it models a headquarters being knocked out without deleting an order of battle that would then have to be rebuilt by hand.

---

## 4. Using it

Map editor → left rail → **COMMANDERS** (`UI/CommanderPanel.cs`).

| Control | What it does |
|---|---|
| **FRIENDLY / ENEMY** | Which side's order of battle the panel shows. Command never crosses the line |
| **the automatic seed** | Builds a chain of command for that side — see below |
| **CLEAR ALL** | Removes every officer on that side and releases their formations |
| Roster rows | Rank, surname, and how many formations he holds. The lamp is green in post with an intact chain, amber when the chain above him is broken, red when he is out of action |
| Name field | His surname |
| Rank stepper | Walks his own side's ladder |
| **REPORTS TO** stepper | Nobody, or another officer on the same side. Choices that would make a loop are skipped |
| **IN POST / OUT OF ACTION** | The switch, with a line under it saying what it currently costs |
| **ASSIGN SELECTED** | Puts every selected formation under him |
| **RELEASE ALL** | Releases everything he holds |
| **COMMANDS** list | The formations he holds; click to select them all on the map |
| **SUBORDINATE OFFICERS** | Who reports to him; click to walk down the chain |

**Assignment is a map gesture, not a form.** An order of battle is built by picking formations off the map and handing them to somebody — a drag-select and a button, rather than twenty rows of dropdowns. Formations from the other side are skipped rather than refused: a box drag across a front line catches both, and failing the whole assignment because of that would be the panel being pedantic about something it can simply do correctly.

### What SEED builds

Twenty officers per side, in a pyramid, each reporting to one above:

| Level | Count | Rank taken from the ladder |
|---|---|---|
| Army / front | 1 | top |
| Corps | 2 | one below |
| Division | 4 | two below |
| Brigade / regiment | 6 | three below |
| Battalion | 7 | four below |

A flat list of twenty peers would be twenty rows and no structure, and the structure is the whole point. Subordinates are spread evenly across the level above rather than hung off its first officer.

Surnames only — a full name would spend two thirds of a 250 px row on a first name nobody refers to an officer by.

---

## 5. Storage

Commanders live in the **map file**, in `MapSaveData.commanders`, alongside the units they command. They are part of a scenario, not a global roster: two scenarios on the same ground have different armies on it.

Loaded **after** the units, because the roster is referenced by id from formations that must already be down.

`commanderId` on `UnitState` defaults to `""`, so a map saved before commanders existed loads with nobody in command — which is the correct reading of it.

---

## 6. Where the code lives

| File | Role |
|---|---|
| `Assets/Scripts/Data/CommanderData.cs` | `RankDef`, `RankCatalog`, `CommanderState` |
| `Assets/Scripts/Units/CommanderRegistry.cs` | The live list, chain walk, assignment, seeding, save/load, `CommandBonus` |
| `Assets/Scripts/UI/CommanderPanel.cs` | The COMMANDERS section |
| `Assets/Scripts/UI/UnitPaletteUI.cs` | The nav row and the section's hooks |
| `Assets/Scripts/Core/GameController.cs` | `AssignSelectionToCommander`, `SelectCommandersUnits`, save/load wiring |
| `Assets/Scripts/Units/CombatSystem.cs` | Where the bonus is applied |
| `Assets/Scripts/Data/MapSaveData.cs` | `commanders`, and `UnitState.commanderId` |

---

## 7. Known gaps

- **No headquarters unit on the map.** A commander cannot be located, shelled or overrun — taking him out of action is a switch in the panel, not something that can happen to him during a battle. That is the obvious next step and the reason the flag exists.
- **Rank does not gate what a commander may hold.** A Lieutenant can be given a division. The ladder's `commands` echelon is used to seed and to suggest, not to enforce.
- **The AI does not use the chain.** Nothing reads `superiorId` to decide what the enemy does.
- **No losses, no replacement.** Officers do not become casualties and are not promoted.
- **Undo does not cover commander edits.** Ctrl+Z tracks unit placements, not the COMMANDERS panel.

---

## Rules

1. **`CommanderRegistry` is the only way to read or write the roster.** No screen touches the list directly.
2. **Unassigned must stay exactly 1.0.** A scenario with no order of battle has to fight identically to one from before this feature.
3. **Command never crosses sides.** `Assign` refuses it; so must anything added later.
4. **Every chain walk is bounded.** A cycle must degrade, never hang.
5. **New `CommanderState` fields must default harmlessly** — `JsonUtility` leaves missing fields at their initialiser values, and old maps must keep loading.
6. **Record ladder and bonus changes in §2 and §3**, in the same commit.

---

## Related

`docs/03-GAMEPLAY.md` (combat) · `docs/04-UNITS.md` (what is being commanded) · `docs/05-MAP-SAVES.md` (the file this lives in) · `docs/07-ARCHITECTURE.md` (script map)


---

## Seeding is automatic

There is no SEED button. Both sides are given a chain of command - one army
commander, two corps, four divisions, six brigades, seven battalions - **when a
map comes up with none of its own**, per side, in `GameController.EnsureCommanders`.

A chain of command is not an optional extra a player might press a button for:
every formation on the map belongs under somebody, and a scenario with an empty
roster was one where this whole panel had nothing to say and every unit read as
unassigned until someone found the button.

A saved scenario's own roster is never overwritten - including a deliberately
emptied one, which is why **CLEAR ALL** still means something. Reloading the map
rebuilds it.
