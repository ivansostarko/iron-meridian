# Gameplay Guide

## Screen flow

```
Main Menu ── TESTING ──┬── DEV ................ game screen (Lyon)
    │                  └── MAP EAST FRANCE .... "Under development"
    ├── SETTINGS ──┬── VIDEO SETTINGS (resolution, window mode, v-sync)
    │              └── AUDIO SETTINGS (master volume for the whole game)
    └── QUIT ...... confirmation modal
```

## The game screen (DEV)

The map opens over **Lyon, France** on real Cesium 3D terrain with the default scenario (`lyon_dev.json`) already deployed: a Blue brigade west of the Rhône, a Red brigade to the east, an auto boundary between them and one Blue defensive line.

### Camera & view

| Input | Action |
|---|---|
| `W A S D` / arrows | Pan |
| Mouse wheel / `R` `F` | Zoom |
| `Q` / `E` | Rotate (3D mode) |
| Middle-mouse drag | Orbit / tilt (3D mode) |
| **VIEW: 3D / 2D** button | Toggle between free 3D view and top-down 2D map view. 2D hides 3D buildings and locks north-up. |

### Teams & affiliations

- **User Team = Blue** (APP-6 blue rectangles), **Enemy Team = Red** (red diamonds).
- Affiliations: **Friendly, Hostile, Neutral, Unknown** — stored per unit and shown in the info panel. New units take theirs from the team tab they were dragged from; there is no separate picker.

### The left rail and the section panel

The editor's left chrome is in two pieces:

- The **rail** is always there: the **ORDER OF BATTLE** emblem, the section nav, and the tool strip along the bottom.
- The **section panel** slides out from behind the rail carrying that section's controls.

| Nav row | What the panel shows |
|---|---|
| **GENERAL** | Tactical graphics — generate / clear sectors, auto-update — and **fog of war** |
| **UNITS** | Team, echelon, search, and the AVAILABLE / DEPLOYED lists (scrollbar on the right) |
| **EFFECTS** | Hand-placed fire, explosion and smoke |
| **WEATHER CONDITIONS** | Sky phase, auto day/night, weather condition |
| **MAP** | Tile style, 2D/3D, layers, unit-label size, boundary options |
| **DATE AND TIME** | Scenario H-hour and presets |

Click a row to open it; click the **same** row again, or the **✕** in the panel's header, to close the panel and hand that strip of screen back to the map. Only one section is open at a time, and the active row is marked with an accent bar. The on-map zoom cluster rides the panel's edge, so it is never buried underneath it.

### Deploying units (drag & drop)

Open **UNITS** in the left rail — the panel lists all 37 unit types with their icons:

1. Pick the team tab (**FRIENDLY** / **ENEMY**). Affiliation follows from it — friendly units are Friendly, enemy units Hostile — so there is no separate picker to contradict the tab.
2. Choose the **Echelon** (Team ... Army). Echelon scales manpower and combat power.
3. **Drag** a unit card onto the terrain — it deploys where you drop it.

The cards carry drag handlers, which means dragging one *deploys* rather than
scrolls the list. Use the wheel or the scrollbar on the right of the list to
reach the units past the fold.

### Commanding units (mouse)

| Input | Action |
|---|---|
| **Left-click** a unit icon | Select it — pulsing team-colour ring, **ground arrow showing its heading**, and full data panel on the right |
| **Right-click** terrain | Reposition (scenario mode) or march order (battle mode) — see *Movement* below |
| `C` | Aim the selection's facing: move the mouse to swing every selected unit onto a bearing. The heading arrows brighten and the status line reads the live bearing. LMB/Enter confirms, `Esc` cancels |
| `Esc` | Deselect |

The **heading arrow** is drawn flat on the ground ahead of the icon in the unit's
team colour, in both 2D and 3D and in both scenario and battle mode. It is the
same graphic the `C` facing tool aims, so what you set is what you see.

### Movement

Scenario mode and battle mode move units differently, and the mode chip in the
top bar says which set of rules is in force.

| Mode | Right-click / Move order | Animation |
|---|---|---|
| **Scenario** | Places the counter instantly at the clicked point | None — an edit is not a march |
| **Battle** | Orders a march along a planned route | Accelerate, corner, brake; trail behind |

**Routing.** A march is not a straight line. `RoutePlanner` lays a corridor
between start and objective, samples the terrain across it, and picks the
cheapest way through — punishing gradient hard and refusing anything steeper
than 25%, the way a road survey would. The result is a handful of legs
(A → B → C) that keep to valleys and follow contours instead of driving over a
ridge. There is **no road network to snap to** — OpenStreetMap is drawn as
raster imagery, not vector ways — so the terrain itself is what the route is
planned against. Identical in 2D and 3D: the view is a camera choice, the ground
underneath is the same.

The unit then drives that route like a vehicle column: it pivots onto its first
course, accelerates to its `speedKmh` (game-time accelerated), *slows through the
bends rather than stopping at them*, and brakes onto the objective. Fuel is
charged against the route actually driven, so going around the high ground costs
more than the straight-line distance would.

**Trail.** While marching, the unit leaves a solid team-coloured trail over the
ground it has covered and a faint dashed thread over the route still ahead. Both
are clamped to the terrain. The trail fades out a few seconds after the unit
arrives, and exists only in battle mode.

Stopping the battle abandons any march in progress and leaves every unit standing
where it actually is, handing the map back to the editor.

### Orders (battle mode)

Select a unit while a battle is running and the bottom **order bar** appears:
**MOVE**, **ATTACK**, **RECON**, **DEFENCE**. The last three each open a submenu
of tasks. Full reference: [15-COMBAT-ORDERS.md](15-COMBAT-ORDERS.md).

#### Attack — five offensive tasks

Pick a task, then **click an enemy formation** to target it (`Esc` or right-click
cancels; clicking bare ground is a miss and leaves the order armed).

| Task | What it does |
|---|---|
| **ATTACK** | Close to effective range and destroy the target. |
| **ASSAULT** | Close right up. Nearly double damage — and the heaviest return fire, because both sides are fully exposed. Sets the objective alight. |
| **SUPPRESS** | Fire from maximum range. Barely dents the target's strength and wrecks its morale and organisation, marking it `Suppressed`. Lays a smoke screen on it. |
| **AMBUSH** | Does **not** move. Sits concealed until the target walks into range, then strikes at ×2.4 with no reply — surprise is worth a great deal once. |
| **COUNTERATTACK** | Strike an enemy already committed to its own attack; heavy opening blow. |

If the target is out of range the unit **marches to a firing position** first,
routed over the terrain like any other move. An **attack arrow** in the task's
colour runs from the attacker to the target while the attack is pending, and
**fades the moment the unit reaches its firing position** — from there the muzzle
flashes, impacts, explosions and fires carry it. If the target withdraws, the
attack follows.

A unit acting on an order fires only at what it was told to; unordered units keep
engaging anything in reach automatically. Orders are cleared when the battle
stops and are not saved.

#### Recon — five reconnaissance tasks

Pick a task, then **click a point on the ground** (not a unit — recon exists to
look at ground you cannot see). Each task grants a detection footprint the fog of
war reads: **RECON AREA** searches the objective, **RECON ROUTE** scans the whole
way there, **OBSERVE** holds position and sees furthest, **UAV RECON** flies a
sensor out and back, **COMBAT PATROL** shuttles between start and objective ready
to fight. Full table: [16-FOG-OF-WAR.md](16-FOG-OF-WAR.md).

#### Defence — three defensive tasks

**DEFENCE** opens a submenu of the three defensive tasks:

| Task | What it does |
|---|---|
| **DEFEND** | Lays a bowed **defence line** across the threat axis, captioned `DEFENCE LINE — <unit>`, with a closed **battle position** enclosing the ground behind it. Subordinate units are distributed evenly along the frontage and marched to their slots facing the threat; the commander sits back inside the position. |
| **HOLD** | Pins the unit on the ground it is standing on, stops any march, turns it onto the threat, and marks the position with a yellow `HOLD` marker. |
| **GUARD** | Pushes the unit forward onto a guard position between the force it protects and the threat, and marks it with a green `GUARD` marker. |

- **Threat axis** is the bearing to the centre of the opposing force. With no
  enemy on the map yet, the unit's own facing stands in.
- **Subordinates** are the unit's group if it has one (see the group panel),
  otherwise the smaller friendly formations standing within 12 km.
- **Frontage** scales with echelon and with how many subordinates have to fit on
  the line (1.5–45 km).
- Re-tasking a unit replaces its graphics; it never stacks two defences.
- Everything produced is ordinary map data (`defence-*` lines and markers), so a
  defence survives save/load — see [05-MAP-SAVES.md](05-MAP-SAVES.md).

### Fog of war

**Left rail → GENERAL → FOG OF WAR.** With it on, enemy formations are drawn only
where a friendly unit's view range or a recon sensor reaches them. Lose sight of
one and the map keeps the **contact**: a ring on the last known position,
captioned with the scenario time of the sighting and growing to cover where that
formation could have got to since.

Battle mode only — the editor shows both sides so you can lay them out. Details
and known leaks: [16-FOG-OF-WAR.md](16-FOG-OF-WAR.md).

### On-map controls

- **Bottom-left cluster** — zoom, face north, 2D/3D, frame the order of battle, and an altitude readout. Every button has a hover caption naming it and its keyboard equivalent.
- **Bottom-right compass** — the rose turns so its N tick sits where north actually is; the fixed index at the top of the bezel reads against it, and that bearing is printed underneath. Click it to face north. It steps aside when the unit info panel opens.
- Both are opt-in from **MAP → LAYERS**.

### RESET

**RESET** in the top bar reloads the shipped scenario and puts every editor
setting back to its default — view mode, tile style, buildings, label size, fog
of war, on-map controls, clock speed. It asks first: units you have deployed,
lines you have drawn, defensive positions and orders in progress are all
discarded, and `Ctrl+Z` tracks individual edits rather than wholesale ones, so
there is no way back. It reloads the **shipped** map, not your last save — a
reset that restores your own save would not be a reset.

### Staying on the ground

Unit icons, control-measure lines, movement trails and task markers all clamp
themselves to the terrain in **both** 2D and 3D. Cesium streams tiles in, so the
first ground sample after a spawn or a load routinely finds nothing there yet;
each of these keeps retrying until real terrain is underneath, then refreshes
slowly. A miss never overwrites a height that was already good, so nothing can be
lost inside a ridge or left floating over a valley.

### Lines: boundaries & defensive lines

- **DRAW BOUNDARY** — yellow sector boundary separating the two teams.
- **DRAW DEFENSIVE LINE** — thick team-coloured fortification line.
- Left-click adds points, **right-click / Enter finishes**, `Esc` cancels.
- **LINES: 3D / 2D** — lines follow the terrain in both modes; the flag chooses how far they stand off it (25 m in 3D, 140 m in 2D so the graphics read as an overlay from straight above).
- A line's `label` amplifier is drawn on the map — at both ends for a long line, at the midpoint for a short one — so `FEBA`, `PL BLUE` and `DEFENCE LINE — …` say what they are and keep saying it after a reload.

**Auto front line:** the boundary marked `autoGenerated` is recomputed every few seconds from unit positions — the power-weighted midpoint between the closest opposing units. When units advance, rout or die, the line moves. A stronger side visibly pushes the front toward the weaker one.

### Combat

Press **▶ START BATTLE**. Every second, opposing units within weapon range exchange damage:

- Damage scales with the ratio of **combat power** (attack/defence/hard-attack/anti-air × echelon × strength × training/morale).
- **Hard attack** matters vs armoured targets; **anti-air** matters vs drone units; support units fight at 40%.
- Units consume **ammunition** each tick (out of ammo → 25% damage) and lose morale as they take losses.
- Below 30% strength a unit **routs**; at 0% it is destroyed (fade-out animation) — and the front line updates.

### Saving & loading

- **SAVE / LOAD** buttons or `F5` / `F9`.
- Each map is one JSON file with every unit's position and full status — see [05-MAP-SAVES.md](05-MAP-SAVES.md).
