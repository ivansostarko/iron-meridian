# Gameplay Guide

## Screen flow

```
Main Menu ── SINGLE PLAYER ..... "Under development"
    ├── MULTIPLAYER ....... "Under development"
    ├── EXTRAS ............ "Under development"
    ├── TESTING ──┬── DEV ................ game screen (Lyon)
    │             └── MAP EAST FRANCE .... "Under development"
    ├── SETTINGS ──┬── VIDEO SETTINGS (resolution, window mode, v-sync)
    │              └── AUDIO SETTINGS (master volume for the whole game)
    └── QUIT ...... confirmation modal

**Play modes first, tools second.** SINGLE PLAYER, MULTIPLAYER and EXTRAS are
placeholders — each is a real screen with its own background and music entry, a
plain "under development" statement and a way back — but they sit above TESTING
because that is the order the menu will read in when they are built, and moving
them later would retrain the player for nothing. QUIT stays last, where it cannot
be hit by accident.

Each placeholder **names its own artwork and its own track** in
`BackgroundCatalog` / `AudioCatalog`, and falls back to the shared menu image and
bed until those files exist. Dropping a file in at the path the catalogue row
names is the whole of the work — no code change. See docs/11-GAME-MENU.md and
docs/10-AUDIO.md.
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

- The **rail** is always there: the emblem, the section nav, and the tool strip along the bottom. (There is no "ORDER OF BATTLE" caption — ten labelled nav rows already say what the rail is, and the heading cost a row of vertical space the sections needed more.)
- The **section panel** slides out from behind the rail carrying that section's controls.

| Nav row | What the panel shows |
|---|---|
| **GENERAL** | Tactical graphics — generate / clear sectors, auto-update — plus **line of sight** and **fog of war** |
| **UNITS** | Team, search, and the AVAILABLE / DEPLOYED lists (scrollbar on the right) |
| **CONTROL MEASURES** | The five kinds of line that can be drawn by hand — picking one opens its options on the right |
| **EFFECTS** | Hand-placed fire, explosion and smoke |
| **ARTILLERY STRIKE** | Call for fire — NATO / Russian tabs, 14 natures. See docs/17-ARTILLERY.md |
| **AIR STRIKE** | Task an airframe — bomber, fighter or helicopter. See docs/18-AIR-STRIKES.md |
| **UAV STRIKES** | Task a loitering munition — see docs/19-UAV-STRIKES.md |
| **WEATHER CONDITIONS** | Sky phase, auto day/night, weather condition |
| **MAP** | Tile style, 2D/3D, layers, unit-label size |
| **DATE AND TIME** | Scenario H-hour and presets |

**Control measures are set up in two places on purpose.** The *kind* is chosen in
the rail, because it changes how the line should be laid on the ground — a rear
boundary runs parallel to the front, a lateral one runs into it — so it is
decided first and then left alone. The *styling* (side, colour, width, planned or
actual, caption) lives in a **docked panel on the right**, because those are
fiddled with until they look right against the terrain.

That panel used to be a modal dialog, which was the wrong shape twice over: it
blocked the map while collecting settings that are all about *where* the line
will go, and it had to be dismissed before drawing, so changing your mind about a
colour meant cancelling the line and starting again. Docked, the terrain stays
visible and clickable behind it, and a change applies to the next line without a
round trip. Opening it drops the unit selection, since two panels cannot share
the right-hand edge — and you are drawing now, not inspecting a formation.

Click a row to open it; click the **same** row again, or the **✕** in the panel's header, to close the panel and hand that strip of screen back to the map. Only one section is open at a time, and the active row is marked with an accent bar. The on-map zoom cluster rides the panel's edge, so it is never buried underneath it.

### Deploying units (drag & drop)

Open **UNITS** in the left rail — the panel lists all 37 unit types with their icons.

**There is no echelon picker.** Units deploy at **battalion**, which is the echelon an operational map is actually drawn at — brigades are too coarse to manoeuvre and companies too many to command. A dropdown listing every size from section to army, sitting above a list of 37 types, made deploying one unit a two-control operation and put the rarely-wanted choice in front of the always-wanted one; a formation's size is changed after the fact from the info panel, where the rest of its details are edited anyway.


1. Pick the team tab (**FRIENDLY** / **ENEMY**). Affiliation follows from it — friendly units are Friendly, enemy units Hostile — so there is no separate picker to contradict the tab.
2. **Drag** a unit card onto the terrain — it deploys where you drop it, at battalion strength.

The drop is ground-checked twice: the cursor must be over the terrain **and** the ground under that point must be measurable. The two come apart at a tile seam, where a unit would otherwise land at the fallback height — floating over a valley or buried in a ridge.

The cards carry drag handlers, which means dragging one *deploys* rather than
scrolls the list. Use the wheel or the scrollbar on the right of the list to
reach the units past the fold.

### Commanding units (mouse)

| Input | Action |
|---|---|
| **Left-click** a unit icon | Select it — pulsing team-colour ring, **ground arrow showing its heading**, and full data panel on the right |
| **Right-click** terrain | Reposition (scenario mode) or march order (battle mode) — see *Movement* below |
| **Shift + right-click** terrain | **Adds a waypoint** to the end of the current march instead of replacing it (battle mode) |
| `C` | Aim the selection's facing: move the mouse to swing every selected unit onto a bearing. The heading arrows brighten and the status line reads the live bearing. LMB/Enter confirms, `Esc` cancels |
| `Esc` | Deselect |
| **Hover** a unit icon | Tooltip beside the cursor: side, type, echelon, strength bar, status, morale/organisation/ammo/fuel and both ranges |

### Connection alerts

The map is **streamed**. Losing the network does not produce an error — it
produces a map that quietly stops filling in, which looks like a hang. A banner
appears at the bottom of the screen for **five seconds** and then fades:

| Trigger | Message |
|---|---|
| Network route lost | *No internet connection — new map tiles and imagery will not load.* |
| Network route back | *Connection restored — map tiles will resume loading.* |
| A tileset request fails | *Map data failed to load — check your connection.* |

Two signals, because neither is sufficient alone. `Application.internetReachability`
knows whether a route *exists* — a cable in the socket, a carrier on the radio —
but not whether anything at the other end answers, so a router with no upstream
reads as reachable. `MapManager.LoadError` catches exactly that case, when a real
request fails. Between them, "no route" and "route but no service" are both
covered.

Starting the editor with no network does **not** fire the alert: the first poll
establishes a baseline rather than announcing it, and the loading screen's own
failure path covers that case with a better message.

**Hovering costs nothing.** Identifying a counter used to mean selecting it,
which replaces the current selection, closes whatever is open on the right and
cancels any order being aimed. Reading the map should not have side effects, so
the tooltip answers "what is that?" without a click. It is shown in both scenario
and battle mode — the information is as useful laying a scenario out as fighting
it — and never appears for a formation the fog is hiding, since the icon is gone
precisely so its position is unknown.

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

**Waypoints.** `Shift` + right-click adds an objective to the end of the route
rather than replacing it, so a column can be sent up a valley, along a ridge and
into a town in one order. Each waypoint is planned over the terrain **from the
previous one**, not from where the unit currently stands — which is what stops
the second leg driving through the hill the first leg went around. Fuel is
charged for the added ground as it is ordered. In scenario mode `Shift` does
nothing to a right-click: there is no march to extend, so it is just a
reposition.

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

**Trail.** While marching, the unit leaves a fine team-coloured trail over the
ground it has covered and a faint dashed thread over the route still ahead, both
clamped to the terrain, with **arrowheads marching forward along the thread** so
the direction of travel is stated rather than inferred — and each arrow points
the way the unit will actually be travelling when it gets there, so a route that
bends has arrows that bend with it. Motes lift off the head of the trail while
the order stands.

The lines are deliberately thin. They were three times this width, which on a
corps-scale advance turned the map into a bundle of ribbons wide enough to hide
the ground being fought over — and a route line's job is to be followed, not to
dominate. What was lost in presence is given back by the arrows and the motes,
which read at a glance without covering any terrain.

The trail fades out a few seconds after the unit arrives, and exists only in
battle mode.

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

### Line of sight

Selecting a unit draws a ring at its view range with the distance **in metres**
on the ring — in scenario mode as well as battle. **Left rail → GENERAL → LINE OF
SIGHT** toggles it; on by default. The weapon-range ring is separate and always
shown.

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
