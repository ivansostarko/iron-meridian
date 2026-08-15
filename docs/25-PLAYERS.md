# Players and Teams

Who is fighting a scenario, on which side, and how hard the computer is trying.

> **Keep this file current.** Any change to the roster model, the difficulty settings or the PLAYERS panel belongs here in the same commit. See [Rules](#rules).

---

## 1. The model

Two things, deliberately separate:

| | What it is | Where it lives |
|---|---|---|
| **Team** | A side of the fight. Units belong to it. | `TeamState` |
| **Player** | Who commands a team. Human or computer. | `PlayerState` |

Collapsing the two would make *"add a second commander to Blue"* impossible to express, and moving a player between sides would mean re-tagging every unit they had.

A team is a **label over one of the game's two hard sides** (`Team.User` / `Team.Enemy`), not a third side. Everything from the combat model to the icon set to the fog is built on that two-way split, so a team carries a `side` and the split underneath is untouched. Two teams on the same side are allies; five are a coalition.

Held by `Data/PlayerRegistry` — static, like `CommanderRegistry`, because the panel, the save system and (later) the AI all read it and none of them owns it.

## 1a. The default roster

Every scenario starts as **two teams and two players**:

| Team | Side | Player | Kind |
|---|---|---|---|
| Blue Force | Friendly | User | Human |
| Red Force | Hostile | Computer | Computer, Regular |

`PlayerRegistry.EnsureDefaults()` creates exactly that, and it only fills in what is **missing** — so a map that carries its own roster is left alone, and one saved before players existed opens with the arrangement it was always implicitly being played under rather than with an empty list.

## 2. Difficulty

Three settings, and no more. A slider of twenty would be twenty numbers nobody could tell apart; three names are what a player actually chooses between.

| Setting | Combat multiplier | What it means |
|---|---|---|
| **Recruit** | ×0.85 | Fights cautiously and reacts late. The setting to learn a scenario on. |
| **Regular** | ×1.00 | An even fight — no handicap either way. |
| **Veteran** | ×1.20 | Concentrates, counter-attacks and does not waste a formation. |

**Regular is exactly 1**, so a scenario played at the middle setting resolves precisely as it did before difficulty existed.

**The multipliers are in the data before there is an AI to use them.** The enemy does not issue orders of its own yet — see docs/07-ARCHITECTURE.md, *Known simplifications*. Putting the numbers here now means difficulty is a real, saved, inspectable property of a player from the moment the behaviour behind it exists, rather than a setting retro-fitted onto one. `DifficultyInfo.CombatMultiplier` is the single place a combat model should read.

Only a computer player has one. A human player has no difficulty — they are the difficulty.

## 3. The PLAYERS panel

Map editor → left rail → **PLAYERS**.

**Why a rail section and not a lobby screen.** Who plays which side is a property of the *scenario*, in the same way its weather and its H-hour are: it is saved with the map and decided while the map is being laid out. A lobby is what you need when two people are joining from different machines; a roster is what you need to author a scenario, and this is the authoring tool.

| Control | What it does |
|---|---|
| **ADD PLAYER** | A human, on the friendly side by default |
| **ADD COMPUTER** | A computer player at Regular, on the hostile side by default |
| **ADD TEAM** | A new team on whichever side has fewer, so it alternates rather than piling up on Blue |
| Team row | Rename in place; **FRIENDLY / HOSTILE** picks the side; **✕** removes it |
| Player row | Rename in place; **EDIT** unfolds the controls; **MAKE HUMAN / MAKE COMPUTER** switches kind; **✕** removes them |
| Player row → EDIT | Team picker, and for a computer the three difficulty buttons |

**Removing a team leaves its players unassigned rather than deleting them.** Losing a player because their team went would be destroying something the player did not ask to destroy; a roster row with no team is a visible, fixable state.

**The player controls are folded away behind EDIT.** Six rows each carrying a team picker and three difficulty buttons is a wall; the row being edited is the only one that needs them.

## 4. Persistence

`MapSaveData.teams` and `MapSaveData.players`, written by `GameController.CaptureSave` and read by `ApplySave` — the same path the commanders take. A map file with neither field (anything saved before this existed) loads and gets the default roster from §1a, which is why the fields are safe to add to the schema.

## 5. Known gaps

- **Nothing reads the difficulty yet.** There is no AI to slow down or sharpen up, and `CombatMultiplier` is not applied by `CombatSystem`. Wiring it in without an AI would make Veteran mean "the enemy's numbers are bigger", which is not what the word promises.
- **A player is not tied to input.** Nothing stops the person at the keyboard ordering the hostile team's formations; the editor is deliberately able to command both sides.
- **Teams do not filter anything.** Units carry a `Team` (the hard side), not a team id, so two friendly teams share one pool of formations.
- **No lobby, no network.** This is scenario authoring, not matchmaking.

## Rules

1. **`PlayerRegistry` is the source of truth.** Add a field to `PlayerState` / `TeamState` rather than tracking roster state anywhere else.
2. **A team is a label over `Team.User` / `Team.Enemy`,** never a third side.
3. **Regular must stay exactly ×1.0**, or a scenario's baseline stops being reproducible.
4. **Update this file in the same change** as anything touching the roster model or the panel.

## Related

docs/03-GAMEPLAY.md (the map editor's rail) · docs/23-COMMANDERS.md (the chain of command *inside* a side) · docs/05-MAP-SAVES.md (the save schema) · docs/22-MISSIONS.md (single-player, which will be the first consumer of a computer player)
