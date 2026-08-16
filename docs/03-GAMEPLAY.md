# Gameplay Guide

## Screen flow

```
Main Menu ── SINGLE PLAYER ──┬── WEST EUROPE ...... Berlin, Oslo
    │                        ├── EAST EUROPE ...... Zagreb, Bjelovar, Budapest
    │                        └── NORTH AMERICA .... Denver, New York
    │                              └── a mission ── loader ── game screen
    ├── MULTIPLAYER ....... "Under development"
    ├── EXTRAS ............ "Under development"
    ├── TESTING ──┬── DEV ................ map editor (Lyon)
    │             ├── UNITS LIST ......... unit catalogue
    │             └── MAP EAST FRANCE .... "Under development"
    ├── SETTINGS ──┬── VIDEO SETTINGS (resolution, window mode, v-sync)
    │              └── AUDIO SETTINGS (master volume for the whole game)
    └── QUIT ...... confirmation modal
```

**Play modes first, tools second.** SINGLE PLAYER is the campaign browser — three
campaign boards, each holding missions authored in the map editor
(docs/22-MISSIONS.md). MULTIPLAYER and EXTRAS are still placeholders: each is a
real screen with its own background and music entry, a plain "under development"
statement and a way back. They sit above TESTING because that is the order the
menu reads in, and moving them later would retrain the player for nothing. QUIT
stays last, where it cannot be hit by accident.

Each placeholder **names its own artwork and its own track** in
`BackgroundCatalog` / `AudioCatalog`, and falls back to the shared menu image and
bed until those files exist. Dropping a file in at the path the catalogue row
names is the whole of the work — no code change. See docs/11-GAME-MENU.md and
docs/10-AUDIO.md.

**A mission and the map editor are the same scene.** A mission is a map with an
order of battle on it, so playing one opens the `Game` scene with the mission's
map, start point and settings; the HUD's identity block carries the mission's
name instead of MAP EDITOR, and BACK and the pause menu's EXIT return to the
campaign board rather than the main menu.

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

- The **rail** is always there: the emblem, the section nav, and the tool strip along the bottom. (There is no "ORDER OF BATTLE" caption — the labelled nav rows already say what the rail is, and the heading cost a row of vertical space the sections needed more.)
- The **section panel** slides out from behind the rail carrying that section's controls.

| Nav row | What the panel shows |
|---|---|
| **GENERAL** | Tactical graphics — generate / clear sectors, auto-update — plus **line of sight**, **max weapon range** and **fog of war** |
| **UNITS** | Team, search, and the AVAILABLE / DEPLOYED lists (scrollbar on the right) |
| **PLAYERS** | Who is fighting this scenario: teams, players, and the computer's difficulty. See [25-PLAYERS.md](25-PLAYERS.md) |
| **COMMANDERS** | The order of battle above the units. See [23-COMMANDERS.md](23-COMMANDERS.md) |
| **LOGISTICS** | The rear area: depot, supply, fuel, ammunition, repair and medical points, deployed by clicking the map. See [26-LOGISTICS.md](26-LOGISTICS.md) |
| **REINFORCEMENTS** | The same panel as UNITS, for formations that are not here yet: pick a type and it arrives at H+n in its side's deployment zone. See [30-REINFORCEMENTS.md](30-REINFORCEMENTS.md) |
| **MINES AND OBSTACLES** | The barrier plan: mines, minefields, AP/AT mines, wire, AT ditch, obstacles and roadblocks, laid as NATO graphics on the ground. See [31-OBSTACLES.md](31-OBSTACLES.md) |
| **SUSTAINMENT** | What the force fights on: fuel, ammunition natures, manpower, rations and the rest — with the burn rate its own order of battle implies. See [27-SUSTAINMENT.md](27-SUSTAINMENT.md) |
| **EFFECTS** | Hand-placed fire, explosion and smoke |
| **MISSIONS** | The single-player campaign: pick a campaign and a mission, edit its name, start point, altitude and briefing, and save the record and the map together. See docs/22-MISSIONS.md |
| **ENVIRONMENT** | Scenario H-hour and presets, sky phase, auto day/night, weather condition — **one section**, because they are one decision: a designer setting a night attack is choosing the hour *and* the sky in the same breath |
| **MAP CONFIG** | Tile style, 2D/3D, layers, unit-label size |
| **STATS** | *Empty.* Reserved — see *Reserved sections* below |
| **ZONES** | *Empty.* Reserved — see *Reserved sections* below |
| **OBJECTS** | *Empty.* Reserved — see *Reserved sections* below |
| **SUPPLIES** | *Empty.* Reserved — see *Reserved sections* below |
| **GROUPS** | *Battle mode only.* Every group on the map, and the one thing you can do to a group that is not an order: put it on the front line. See *Groups* below |

The rail is headed **SCENARIO MODE** or **BATTLE MODE** — the top bar's chip says the same thing, but the rail is where the player's hands are, and half these sections mean something different depending on the answer.

**Sixteen rows, and a seventeenth in battle.** The five fire menus moved to the strike dock at the top right (see below) and CONTROL MEASURES went altogether. What is left is the authoring nav, in the order a scenario is actually built: the ground rules, the forces, who is fighting, who commands, what supplies them and what they fight on, then the dressing, then the four reserved pages. **GROUPS** is added at the bottom when a battle starts and taken away when it stops — a group is something you command, not something you author, and a row that did nothing through the whole of a scenario's layout would be a row in the way.

**The nav scrolls.** Seventeen rows do not fit the rail on a 1280×720 screen, and a row that ran under the tool strip would be a section that could not be opened at all — the one failure a nav is not allowed. The emblem above and the tools below stay put; only the list between them moves, with a scrollbar down its right edge.

Click a row to open it; click the **same** row again, or the **✕** in the panel's header, to close the panel and hand that strip of screen back to the map. Only one section is open at a time, and the active row is marked with an accent bar. The on-map zoom cluster rides the panel's edge, so it is never buried underneath it.

### Reserved sections

**STATS**, **ZONES**, **OBJECTS** and **SUPPLIES** are a nav row and an empty page each, and nothing else yet. They are places to build in: the row, the section, the panel and the title are wired, so filling one is writing its controls and nothing else.

Each page says on its face that it is empty — a page that merely rendered blank would read as a section that had broken — and each names its nearest built neighbours, so the next person to fill one is told what already exists rather than building a second way of doing it:

| Section | Where the nearest existing thing lives |
|---|---|
| **STATS** | Casualties on **TAB** (the losses list); stocks under **SUSTAINMENT** |
| **ZONES** | Mission boundary, HQ zones and deployment zones, all under **MISSIONS** |
| **OBJECTS** | Barrier plans under **MINES AND OBSTACLES**; installations under **LOGISTICS** |
| **SUPPLIES** | Stocks under **SUSTAINMENT**, depots under **LOGISTICS**, air-dropped loads on the **AIR SUPPLY** fire menu |

They are built by `UnitPaletteUI.BuildEmptySection`, one shared page so the four cannot drift into four different ways of saying "empty".

**Hand-placed effects are permanent.** A fire, smoke column or explosion put down from the EFFECTS section burns until it is cleared — never evicted by the concurrent-effect budget, never given a lifetime, in battle mode or out of it. Two things used to end one and both looked like a bug: the budget is 48 effects and an incoming explosion outranks a standing fire, so the marker a player put down vanished exactly when the fighting got interesting; and a placed explosion left a wreck on a timer that went out by itself. Effects the *game* creates still burn out and are still evictable — otherwise a long battle ends up carpeted in permanent fires.

The tool strip along the bottom is down to three: the **cursor**, **generate sectors** and the **2D/3D toggle**. The pencil and the square drew control measures by hand.

### The strike dock

Six icons under the top bar's right-hand end: five ways of putting explosives on a piece of ground — **artillery, air, UAV, missile, naval** — and one of putting supplies on it, **air supply**, next to the air strike it is flown alongside. Click one to open its menu, docked on the right beneath the icons; click it again, or the **✕**, to close it.

**Why they left the rail.** They were five of the rail's fifteen rows, and the other ten are things you *set up* a scenario with. These are not that: they are things you *do* during one, they are all the same verb, and mixing them into the authoring nav made the rail read as a settings menu with weapons in it. Pulling them into their own cluster says what they have in common and gets them to one click from anywhere.

**The cluster is battle-mode only.** It appears when the battle starts and goes when it stops, together with the minimap below it. Calling for fire is something you do *during* a fight: in scenario mode no clock is running, nothing moves between the call and the impact, and a strike laid on a laydown that is still being drawn is a hole in a map rather than an event. Stopping the battle takes any open fire menu down with the icons, which also stands its launcher down.

**Within a battle the icons never hide.** Everything else that docks on the right — the unit inspector, the group panel, the front-line options — begins *below* the icon strip rather than under the top bar, so a fire menu can be reached with a formation selected. The panel itself still shares the right edge with them, because two panels cannot occupy one strip of screen: opening a fire menu drops the selection, and selecting a formation closes the fire menu. Closing a menu also stands its launcher down — leaving one armed behind a panel that is off screen would turn the next click on the map into a strike nobody asked for.

| Icon | Menu | Detail |
|---|---|---|
| Artillery piece | **ARTILLERY STRIKE** | NATO / Enemy tabs, 14 natures. docs/17-ARTILLERY.md |
| Flying wing | **AIR STRIKE** | Bomber, fighter or helicopter. docs/18-AIR-STRIKES.md |
| Parachute | **AIR SUPPLY** | Ammunition, fuel or medical stores, dropped by transport. The one menu here that *gives* — each bundle that lands becomes a supply point. docs/29-AIR-SUPPLY.md |
| Quadcopter | **UAV STRIKES** | Loitering munition, Shahed-class, reconnaissance. docs/19-UAV-STRIKES.md |
| Interceptor | **MISSILE SYSTEMS** | Ten launchers, NATO and enemy. docs/20-MISSILE-SYSTEMS.md |
| Warship | **NAVY STRIKE** | Nine mountings, NATO and enemy. docs/21-NAVAL-GUNFIRE.md |

### The minimap

Docked at the **top left**, under the command bar and riding the section panel's edge as that slides. **Battle mode only**, for the same reason the fire menus are: it is the picture of a fight.

It used to hang under the fire-menu cluster on the right. That edge is three panels deep already — the unit inspector, the group panel, the fire menus — so the minimap was either covering one of them or being covered by it, and on a 720p screen the two could not both fit. It then spent a while in the bottom left, which is where the zoom cluster and the order bar live. The top left is the one corner of the map this screen leaves empty, and a picture read at a glance belongs where the eye already starts.

The map is played at a few kilometres across while a scenario is tens of kilometres wide, so for most of a battle you are looking at one part of something whose shape you cannot see. Zooming out to find it costs the detail you were using; zooming back in costs the position you had. The minimap is the second, fixed-scale view that always shows the whole thing.

| Drawn | Notes |
|---|---|
| **Blips** | Every living formation, blue or red. **Selected formations carry a white centre** |
| **Front line** | Read straight off `FrontlineSystem`'s published line, so the two can never disagree |
| **Mission boundary** | When a mission is open — the same polygon the fog and the camera clamp use |
| **View box** | Where the camera is looking, sized from its field of view and standoff, with a tick out of the leading edge for the heading |
| **Grid** | At a round 1 / 2 / 5 / 10 … km spacing, so distances can be estimated. The width in km is printed underneath |

**North is up and stays up** — it is a map, not a repeat of the camera. **Click anywhere on it to fly the camera there.**

**It folds away.** The ▼ at the end of the header collapses the picture to its caption bar; ► brings it back. A minimap is ambient, and ambient chrome that cannot be put away is chrome you have to play around when the fight moves under it. The control stays where it was, so the way back is in the same place as the way out — and a folded minimap stops redrawing as well as stops drawing. The state survives the battle stopping and starting again.

**It obeys the fog.** An enemy formation hidden by fog of war is not drawn. A minimap showing the whole red laydown would be a way round the fog rather than a convenience — see `docs/16-FOG-OF-WAR.md`.

**No terrain imagery, deliberately.** At 244 px a satellite thumbnail is a brown-green smear that hides the blips, and the map itself is right there for anyone who wants to look at ground.

The picture is rasterised into a texture a few times a second rather than built from uGUI rects: a hundred formations would be a hundred `Image` components rebuilt on every move, and the front line is a polyline of several hundred vertices that uGUI cannot draw at all. See `Assets/Scripts/UI/MiniMapUI.cs`.

A single-player mission keeps the minimap — it is gameplay feedback, not editor chrome — where the editor's rail would have been.

**The right edge is free of it.** The four panels that dock there — unit inspector, group panel, front-line options, fire menu — all start below the fire-menu cluster and nothing else, now that the minimap is on the opposite side. One place still decides it for all four: `GameController.RefreshRightDockTop`. They share one width, `UiTheme.RightPanelWidth`, so a panel replacing another does not read as the map resizing.

### Missions per weapon, not per scenario

Every fire menu shows **two figures on each row**: the beaten zone, and how many missions that system has left. The allowance belongs to the weapon — two B-2 sorties, twenty 81 mm fire missions, one DF-26 — and running one system out does not touch any other.

It used to be a single pool of ninety-nine shared by every strike in the game, which made the choice between a 60 mm mortar mission and an Iskander free: they cost the same thing, so the only rational play was to spend the pool on whatever was biggest. What makes a heavy weapon a real choice is that there are two of them.

The figure is `missions` on the catalogue row, beside the beaten zone and the countdown, so it is visible and tunable in **Development → Units List** like any other stat. See `Vfx/StrikeBudget.cs`.

### Control measures

**Removed.** The editor no longer draws boundaries, phase lines or defensive lines by hand: the CONTROL MEASURES rail section, its right-hand options panel and the two drawing tools are all gone.

What survives is every control measure the game *derives* for itself, because those are the ones that carry information rather than decoration:

- the automatic **front line** (`FrontlineSystem`), clickable for its own settings;
- **GENERAL → GENERATE SECTORS**, which derives each side's lateral boundaries, FEBA and rear boundary from where its units stand;
- the lines and battle positions **DEFEND / HOLD / GUARD** put on the map as part of an order;
- a **mission's boundary**, drawn in the MISSIONS panel — the one click-to-draw tool left, and it belongs to the mission record rather than to the map.

Lines saved in an existing map still load and draw; nothing was removed from the line model itself.

### Deploying units (drag & drop)

Open **UNITS** in the left rail — the panel lists all 117 unit types with their icons as an **accordion**, one section per arm of service (Infantry, Armour, Mechanised, Artillery, Anti-Aircraft, Air, Navy, Logistics, Other). Click a heading to open it; click it again to close it.

**Everything starts closed.** 117 cards under nine headings is a list you scroll rather than one you read, and the arm is what you are actually choosing between first — *"I want an armoured battalion"* comes before *"which one"*. Collapsed, the whole order of battle fits on one screen and picking a category is one click. Each heading carries the number of types inside it, which is the answer to the question a closed section raises.

**Searching opens everything.** Typing a name is already a statement of which unit you want, so the hits are not made to wait behind a second click. An arm with no matches prints no heading at all, so a search never leaves a bare label behind.

**There is no echelon picker.** Units deploy at **battalion**, which is the echelon an operational map is actually drawn at — brigades are too coarse to manoeuvre and companies too many to command. A dropdown listing every size from section to army, sitting above a list of 117 types, made deploying one unit a two-control operation and put the rarely-wanted choice in front of the always-wanted one; a formation's size is changed after the fact from the info panel, where the rest of its details are edited anyway.


1. Pick the team tab (**FRIENDLY** / **ENEMY**). Affiliation follows from it — friendly units are Friendly, enemy units Hostile — so there is no separate picker to contradict the tab.
2. **Drag** a unit card onto the terrain — it deploys where you drop it, at battalion strength.

The drop is ground-checked twice: the cursor must be over the terrain **and** the ground under that point must be measurable. The two come apart at a tile seam, where a unit would otherwise land at the fallback height — floating over a valley or buried in a ridge.

The cards carry drag handlers, which means dragging one *deploys* rather than
scrolls the list. Use the wheel or the scrollbar on the right of the list to
reach the units past the fold.

### The DEPLOYED list

The other tab lists what is actually on the map — call sign, type, size, strength and status, with a side stripe down the card's left edge:

| Input | Action |
|---|---|
| **Click** a row | Selects that formation on the map: ring, outline, heading arrow and the info panel on the right |
| **Double-click** a row | Selects it **and flies the camera to it** — an eased travel to its position, not a cut |

A cut would leave you to work out where on the globe you had landed; watching the ground slide past carries the relationship between where you were and where you now are, which is the whole value of *"fly to this formation"* over *"show me this formation"*. Selecting first is what makes the arrival mean something — the camera stops over a counter that is already marked, rather than over a patch of terrain you then have to find the unit on. Any pan or zoom cancels the flight immediately: an animation that has to be waited out is a camera that has stopped answering.

A formation the fog has taken off the map is not listed here, since the list would hand back exactly what the fog is withholding.

### Commanding units (mouse)

| Input | Action |
|---|---|
| **Left-click** a unit icon | Select it — pulsing team-colour ring, **ground arrow showing its heading**, and full data panel on the right |
| **Right-click** terrain | Reposition (scenario mode) or march order (battle mode) — see *Movement* below |
| **Right-click** a friendly unit | Opens that formation's menu — **REMOVE UNIT**. See *The right-click menu* below |
| **Right-click** a logistic site | Opens that site's menu — **REMOVE SITE** |
| **Shift + right-click** terrain | **Adds a waypoint** to the end of the current march instead of replacing it (battle mode). Shift also bypasses the menu, so a counter in the way does not stop you extending a march over it |
| `C` | Aim the selection's facing: move the mouse to swing every selected unit onto a bearing. The heading arrows brighten and the status line reads the live bearing. LMB/Enter confirms, `Esc` cancels |
| `Esc` | Deselect |
| **Hover** a unit icon | Hover card beside the counter — see *Hovering costs nothing* below |

#### The right-click menu

Right-click **a friendly formation** or **a logistic site** and a small menu opens at the cursor, headed with that object's own name, carrying what can be done to it. Today that is one entry each — **REMOVE UNIT** and **REMOVE SITE** — and both are undoable with `Ctrl+Z` in the unit's case.

**Why a menu and not another shortcut.** Removing a counter used to mean finding it again in the rail's DEPLOYED list, or selecting it and reaching for the panel on the far side of the screen. Both are a trip away from the thing you are already pointing at. Right-click is the one gesture on this map that means "about *this*", and a menu is the affordance that can grow: a second and third entry cost nothing, where a second and third shortcut would each need learning.

| Rule | Why |
|---|---|
| **Friendly formations only** | An enemy counter is not yours to remove, so right-click on one still means the ground under it |
| **Formations win over sites** | A counter is the thing you are looking at; a depot underneath it is scenery by comparison. Same precedence left-click uses for units over control measures |
| **Both modes** | A right-click that produced a menu in battle and silently moved a formation in the editor would be a trap rather than a mode |
| **Bare ground is unchanged** | Right-click on terrain is still reposition-or-march, exactly as before |
| `Esc`, another right-click, or a click outside | Closes it. The dismissing click is swallowed, so it cannot also land on the terrain behind the menu |

Sites are picked in **screen space** rather than with a collider: a site's marker is drawn at a constant *apparent* size — the same number of pixels across at 500 m and at 50 km — so a collider would have to be resized every frame to keep matching it, and a pick that disagreed with what is on screen is worse than no pick at all.

### Groups

Select **two or more** formations — box-select by dragging, or shift-click them one at a time — and the right-hand panel becomes the **group panel** instead of the unit inspector. (One formation is a unit, not a group, so at a selection of one the inspector comes back.)

| Block | What it is |
|---|---|
| **GROUP · &lt;name&gt;** | Which group this selection is — or *NOT A GROUP YET* — and a reminder that its orders are on the bar below the map |
| **GROUP SELECTION** / **UNGROUP** | Makes the selection a group, or takes it out of whatever group it is in |
| **SELECTED UNITS** | A row per selected formation: icon, name, echelon, and the group it currently belongs to. **⇄** moves that one formation to another group; **✕** deletes it from the map |
| **EXISTING GROUPS** | Every group with a living member, with its size |

#### A group names itself

Press **GROUP SELECTION** and the group is formed and designated in one step — `GROUP 1`, `GROUP 2`, and so on, taking the lowest unused number so that deleting one and making another reuses it rather than climbing forever.

**There is no name field and no rename.** What a player needs of a group is a handle short enough to read on a row and on the order bar, and any handle will do as long as it is unique and stable. A text field, a RENAME button and the decision of what to type are three controls for something that exists to be pointed at; the space goes to the two lists instead, which are what is actually being read.

#### The group's orders are on the order bar

The panel used to carry its own MOVE / ATTACK / DEFEND bar, which was a mockup that did nothing, in a different place and a different shape from the order bar every other order is given on. **Group orders now live where a formation's orders live** — the bar at the foot of the map — captioned `GROUP ORDERS — <name> · <count>` (or `— N FORMATIONS` when the selection is not one group). All six orders apply, not three, and every formation in the selection carries them out. See `docs/15-COMBAT-ORDERS.md` for the frontage they are spread across.

#### The GROUPS panel — and putting a group on the front line

**Left rail → GROUPS**, battle mode only. The right-hand group panel describes
*the current selection*; this is the opposite question — what groups exist on
this map, and where are they? A commander asks that without having selected
anything, which is exactly when the right-hand panel is not there.

Each row carries the group's name, its size, a side stripe, and two controls:

| Control | Action |
|---|---|
| **The row** | Selects the group's formations |
| **◎** | Selects them and flies the camera so the whole group is framed |
| **FLOT** | Puts the group on the front line |

**FLOT is the one thing you can do to a group that is not an order.** The front
line is *derived* — it is where the fighting is, not a control measure anybody
drew — and until now nothing could be done with it except look at it. But "hold
the line" is the commonest order at this level, and giving it by hand meant
clicking DEFENCE once per battalion and eyeballing the spacing along a curve.

One click instead. The line is sampled at **equal arc lengths** — one point per
formation, at the centre of its share, so the outermost formations sit inside
the line rather than on its two tips — and each formation is ordered to defend
its point. Arc length rather than vertex index, because the line is smoothed and
its vertices bunch up wherever it bends.

**They are set back from the line, not on it.** The FLOT runs *between* the two
sides, so a formation placed exactly on it would be standing in the contact
itself. Each objective is offset 1.2 km toward the group's own rear, taken from
the two sides' centres of mass rather than from the line's local normal: the
line bends, and a per-point normal would send the flank formations off in
directions that have nothing to do with where their army is.

Once assigned, the line captions itself **FLOT — &lt;GROUP&gt;** at both ends and the
panel says who is holding it. **RELEASE** clears the assignment; the formations
keep the positions they were given, because taking a group off the line is a
statement about the line, not an order to abandon the ground.

#### Recalling a group

| Input on an EXISTING GROUPS row | Action |
|---|---|
| **Click** | Selects that group's members on the map |
| **Double-click** | Selects them **and flies the camera to them** |
| **＋** | Moves everything currently selected into that group |

The flight frames the whole group rather than using a fixed altitude: the standoff comes from the group's own radius about its centre, with a floor, so a brigade holding a thirty-kilometre frontage does not arrive with two of its units on screen and the rest off it. As with the DEPLOYED list, any pan or zoom cancels the flight.

#### Moving formations between groups

A group is a property of the units in it, not an object of its own, so *"move these units to that group"* is expressed as a selection plus a destination — and both are already on this panel. Two paths, because they answer two different questions:

- **＋ on a group row** takes the **whole selection** into that group. This is the bulk path — splitting a force between two axes is a statement about a set of formations, not about each of them in turn.
- **⇄ on a unit row** opens a chooser for that **one** formation, listing every group plus *(no group)*. This is how a battalion moves between brigades.

Both write straight to `UnitState.groupId` / `groupName`, so regrouping is saved with the map like any other unit property.

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
the hover card answers "what is that?" without a click. It is shown in both
scenario and battle mode — the information is as useful laying a scenario out as
fighting it — and never appears for a formation the fog is hiding, since the icon
is gone precisely so its position is unknown.

The card is placed **beside the counter, not under the cursor**: a counter is a
small target and the pointer is somewhere inside it, so a card hung off the mouse
covers the symbol being asked about and slides around under a hand that is
holding still.

**Everything on it is labelled by a picture.** It leads with the formation's own
APP-6 symbol — so the card and the counter are visibly the same thing — over its
name and its echelon / arm / side. Under that, each reading sits behind a glyph
in a fixed position:

| | Reading | |
|---|---|---|
| 🛡 | Strength | Bar and percentage, green → amber → red |
| ⚡ | Status | Coloured with the state: routed red, suppressed and engaging amber, moving blue |
| ⚑ MOR · ✎ ORG | Morale, organisation | Same three-colour thresholds as strength |
| ▪ AMMO · ● FUEL | Ammunition, fuel | Red at zero — out of ammunition is a state, not a low number |
| ◉ SEE · ⌁ REACH | View range, weapon range | |

It used to be four lines of prose with the readings run together as
`MOR 74 ORG 61 AMMO 3200 FUEL 480` — six numbers a player had to parse a caption
to identify, on a card that is up for about a second. Position and shape are what
a glance can use; a word is what a glance skips.

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

Select a unit while a battle is running and the bottom **order bar** appears —
six buttons, each opening a submenu. Full reference:
[15-COMBAT-ORDERS.md](15-COMBAT-ORDERS.md).

```
┌────────┬────────┬────────┬─────────┬──────────┬─────────┐
│  MOVE  │ ATTACK │ RECON  │ DEFENCE │ COMMANDS │ PLANNER │
└────────┴────────┴────────┴─────────┴──────────┴─────────┘
```

**Four are tasks and two are not.** MOVE, ATTACK, RECON and DEFENCE each take an
objective: pick the task, then click the map. COMMANDS are standing switches
that apply the moment they are clicked. PLANNER draws intentions that nothing
executes.

#### What a placed order draws

Everything placed draws through one system, so a defence, a recon objective and a
rally point are read the same way — a **ring** (*how far from here*), a **line**
(*which line do I hold*) or **four quadrants** (*which ground do I cover*). Each
area carries a 3D volume, looping motes tinted by intent, labels naming the task
and the formation, and a **select animation**: the volume swells when the order
is placed and again whenever its formation is selected, and the area's lines
thicken while it is. The whole map can be carrying orders at once, and without
that a screen of overlapping areas says nothing about which one is yours.

#### Move — five tasks

| Task | What it does |
|---|---|
| **MOVE** | March at the formation's own speed. |
| **FAST MOVE** | Road march at ×1.65 — and worth only half its combat power if caught on the way. Speed is not free. |
| **TACTICAL MOVE** | Bounding advance at ×0.6, in contact formation and slightly *better* than standing still if engaged. |
| **WITHDRAW** | Draws a **line** behind the formation. It goes there by itself **once it is down to 50% strength**. |
| **RETREAT** | Draws a **rally ring**. It goes there **at 30%**. |

The last two are not journeys you are ordering now — they are decided in advance
and executed by the formation, because a commander cannot decide what happens
when a battalion breaks at the moment it breaks.

#### Attack — one task

Pick ATTACK, then click **either an enemy formation or bare ground**. A click on
terrain is an order, not a miss: with fog of war on, the ground you most want to
attack is exactly the ground you cannot see a counter on. Clicking terrain draws
an objective ring and attacks the **area** — everything hostile inside it, and
anything that walks into it, re-acquiring each time the current target dies.

**Out of range is not a refusal.** The attacker marches to a firing position by
itself, routed over the terrain like any other move, and opens fire on arrival.
An **attack arrow** runs from attacker to target while the attack is pending and
fades once it is in position; from there the muzzle flashes and impacts carry it.
If the target withdraws, the attack follows.

A unit acting on an order fires only at what it was told to; unordered units keep
engaging anything in reach automatically. Orders are cleared when the battle
stops and are not saved — the graphics they drew are, because those are ordinary
map data.

#### Recon — one task

**RECON AREA.** Click the centre of the ground to search; four quadrants are
drawn, sized to what the formation will actually see, and it moves there and
searches it. The footprint is what the fog of war reads. Full table:
[16-FOG-OF-WAR.md](16-FOG-OF-WAR.md).

#### Defence — three tasks

All three are **placed** now: you pick the ground, and the task is laid out
around that point rather than around wherever the formation was standing.

| Task | What it does |
|---|---|
| **DEFEND** | Lays a bowed **defence line** across the threat axis through the chosen ground, with a closed **battle position** behind it. Subordinates are distributed evenly along the frontage and marched to their slots facing the threat; the commander sits back inside the position. |
| **HOLD** | A **ring** on the chosen ground. The formation moves onto it, faces the threat and is pinned there. |
| **GUARD** | **Four sectors** about the chosen ground — a screen covers ground rather than occupying it, so the same formation is thinner on all of it. |

- **Threat axis** is the bearing to the centre of the opposing force. With no
  enemy on the map yet, the unit's own facing stands in.
- **Subordinates** are the unit's group if it has one (see the group panel),
  otherwise the smaller friendly formations standing within 12 km.
- **Frontage** scales with echelon and with how many subordinates have to fit on
  the line (1.5–45 km).
- Re-tasking a unit replaces its graphics; it never stacks two defences.

#### Commands — three standing switches

Not orders: switches on how the formation behaves when nothing else is telling it
what to do. They apply at once, and the two toggles carry a **lamp** showing
their state.

| Command | Default | What it does |
|---|---|---|
| **STOP** | — | Cancels the march, the contingency and every graphic either left on the map. Does not touch the two switches. |
| **FREE MOVEMENT** | Off | When idle, roam within **50 km** of where it was released. Off by default — a formation that wandered off the ground you put it on, because you did not know a switch existed, would be the game losing your scenario for you. |
| **AUTO ATTACK** | On | Engage anything in range without being told. Turning it off keeps a screen or a recon element out of a fight it cannot win; it is still in contact and still takes what is coming. |

Free movement is the lowest-priority thing a formation does — never while
marching, in contact, or with a contingency waiting. Both switches are saved with
the unit and are given to the whole selection.

#### Planner — three entries

**Nothing here executes.** MAIN ATTACK draws a heavy arrow from the formation to
the picked ground; SUPPORTING draws a lighter one; RETREAT LINE calls the same
order the movement menu does. Both axes are drawn broken, because that is what a
control measure that has not happened yet looks like.

The weight difference is the point: two identical arrows would be two arrows, a
weighted pair is a plan.

### Line of sight and weapon range

Selecting a unit draws a ring at its view range with the distance **in metres**
on the ring — in scenario mode as well as battle. **Left rail → GENERAL → LINE OF
SIGHT** toggles it; on by default. The weapon-range ring is separate, in its own
colour, and toggled by **MAX WEAPON RANGE** beside it.

**Both rings are flat.** A range is a distance measured across the map, so it is
drawn on the ground: a feathered band lying on the terrain, bright along the true
radius and fading out on both shoulders, with four cardinal tick spurs so it
reads as an instrument rather than a halo. The band is clamped to the sampled
ground the whole way round, so it dips into valleys and rides over spurs exactly
as the terrain does. Only brightness breathes — the radius states a real distance
and is never animated.

It used to be a translucent wall of light rising out of the terrain along the
whole circumference. That was legible, and wrong: standing a range up hides the
very ground the reach is being judged against, and two rings on one formation
became a thicket of curtains. The band is wide in *metres* rather than pixels,
which is what keeps a flat ring visible from twenty kilometres up without it
becoming a painted stripe close in. Fog contact rings and air-defence contact
rings (`RangeRing`) are drawn the same way.

### Fog of war

**Left rail → GENERAL → FOG OF WAR.** Seeing has three tiers, not two:

| What you can see | When |
|---|---|
| The formation, drawn normally | It is inside a friendly unit's **view range**, or a recon sensor's footprint |
| A **live contact** — a ring, captioned `UNIDENTIFIED · IN CONTACT`, tracking as it moves | The two units' **view circles cross**, but it is outside yours. Something is out there and you know roughly where, but not what |
| Nothing at all | No overlap, and you have never seen it |

Lose sight of something you *had* seen and the map keeps a **last-known contact**: a ring on where it was, captioned with the scenario time of the sighting and growing to cover where it could have got to since. A formation you have never laid eyes on leaves no ring — inventing one would hand you the enemy order of battle on the first sweep of the battle.

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

### How lines are drawn

These rules apply to every line on the map — the derived front line, generated
sector boundaries, defensive positions and mission areas alike. Hand-drawing is
gone (see *Control measures* above); the drawing model is not.

- **Lines are draped, not strung.** Every segment is subdivided at ~220 m and each sample is clamped to the ground, so a boundary drawn with two clicks across ten kilometres lies on the terrain the whole way rather than flying over the valleys and vanishing inside the ridges between its two vertices.
- **STAND UP IN 3D** — for defensive lines and battle positions, which mark ground that is physically held, the flag chooses how far clear of the terrain the graphic floats (25 m in 3D, 140 m in 2D so it reads as an overlay from straight above). **Boundaries and phase lines ignore it and always read `DRAPED`**: a control measure is a line drawn on a map, not a fence standing in the world, and a wall of colour reaching into the sky hides the terrain the boundary is there to divide. Height is used for one thing only — putting the line on the ground.
- A line's `label` amplifier is drawn on the map — at both ends for a long line, at the midpoint for a short one — so `FEBA`, `PL BLUE` and `DEFENCE LINE — …` say what they are and keep saying it after a reload.

### Derived sector boundaries

**GENERAL → GENERATE SECTORS** derives each side's control measures from where
its units actually stand: lateral boundaries between laterally adjacent
formations, a FEBA through the forward ones, and a rear boundary behind them
(`SectorSystem`, following APP-6A / FM 101-5-1).

**A side gets one set per body of troops, not one per side.** The geometry
assumes a single front with a single frontage axis, which is true of a force
deployed in one place and false the moment a side is fighting in two: run over a
corps at Lyon and a brigade two hundred kilometres away, the principal axis lands
somewhere between them and the "boundary" is drawn through empty country between
two formations that are not adjacent to anything. So a side's units are first
split into groups by proximity — within 15 km of each other, transitively — and
each group gets its own frontage axis, its own lateral boundaries, its own FEBA
(captioned `FEBA 1`, `FEBA 2`, … when there is more than one) and its own rear
boundary. Each group is also oriented against the enemy formations *nearest to
it* rather than against the enemy's overall centre of mass, because with two
separated fronts those are different directions and only the first is "forward"
for that group. A group of fewer than two formations is left alone: it has no
adjacent pair to bound and no forward edge worth calling a FEBA.

### The front line (FLOT)

**The FLOT is a gameplay object, not a drawing** — full register in [28-FLOT.md](28-FLOT.md). The one-paragraph version:

Each side gets its **own forward edge**, solved every few seconds from its **combat formations only** — infantry, mechanised and armour that are still combat-effective. Logistics, artillery, air and broken units do not move the line, and an isolated group either becomes a **POCKET** ring (real combat power) or is ignored (a lone probe). The ground between the two edges is **contested**. Each stretch carries a live state — `STABLE · ADVANCING · RETREATING · CONTESTED · BREACHED · COLLAPSING · ISOLATED` — read out in the line's own panel, and an enemy force established more than 2 km behind an edge with real combat power raises a **FLOT BREACHED** alert.

Three modes, switched in the panel (click any FLOT line): **AUTO** (solved from the force), **MANUAL** (drawn by the designer, click by click), **HYBRID** (drawn first, solved once the battle starts). With fog of war on, the enemy edge you see is an **estimate** from visible formations only, drawn broken.

The lines are flat, draped and painted on the terrain in both view modes — the mechanics of that (and of why they can never climb into the sky) are unchanged from before and live in `MapLine`.

### Combat

Press **▶ START BATTLE**. Every second, opposing units within weapon range exchange damage automatically — there is nothing to order, and a formation that comes into range of the enemy is in a battle whether or not it was told to be.

- Damage scales with the ratio of **combat power** (attack/defence/hard-attack/anti-air × echelon × strength × training/morale).
- **Hard attack** matters vs armoured targets; **anti-air** matters vs drone units; support units fight at 40%.
- **Who commands the formation** is one more term: an intact chain of command is worth up to 20%, a broken one costs 12%, and an unassigned formation is exactly neutral. See [23-COMMANDERS.md](23-COMMANDERS.md).
- Units consume **ammunition** each tick (out of ammo → 25% damage) and lose morale as they take losses.
- Below 30% strength a unit **routs**; at 0% it is destroyed (fade-out animation) — and the front line updates.

**Range is measured edge to edge, not pin to pin.** Two thirds of each formation's footprint is subtracted from the gap, so a brigade whose leading elements are inside a battalion's weapon range is in range of it. Measuring counter to counter said otherwise by a kilometre or more, which is why formations could stand visibly overlapping without engaging.

**Contact stops the march.** A formation exchanging fire is fighting, not marching through the fight — its move order is *kept*, not cancelled, so breaking contact resumes it. Contact is mutual even when only one side can shoot: being under fire you cannot answer is still being in a battle, and walking away from rounds that are still landing is not a thing a column gets to do.

### Losses — TAB

Press **TAB** during a battle for the casualty list: both sides side by side, a row per unit type, heaviest first.

| Column | What it counts |
|---|---|
| **FORM** | Formations destroyed outright — the operational cost, what you have stopped being able to command |
| **MEN** | Manpower behind every point of strength lost, in **surviving** formations as well as dead ones — the human cost, and the number that keeps climbing during an exchange where nothing on the map has died yet |

The two halves of the page are deliberately identical, because the whole question it answers is a comparison: anything that made your side read differently from theirs would make them incomparable. Above each table is that side's total, and how many of its formations are still on the map — *"eleven destroyed"* means nothing without *"of thirty-four"*.

Losses are booked **as they are inflicted** rather than reconstructed by comparing the map with the save file, which would break the moment anything was reinforced or deployed mid-battle and could say nothing at all about the formations that are still alive but half gone. The ledger is per-scenario: loading, resetting or re-entering the map clears it. TAB again, or Escape, closes the page.

### Air defence

Anti-aircraft formations defend the airspace around themselves automatically — deploying the launcher **is** the order. A drone entering the envelope is tracked (a ring appears under it, and the HUD names the battery), a missile leaves the rail two seconds later, and it never misses. The drone comes down burning and its mission does not happen.

Today only *hostile* batteries have anything to shoot at, because every UAV sortie is called by the player. Full detail — which unit types qualify, why the envelope is absolute, and what the intercepted sortie reports — in [24-AIR-DEFENCE.md](24-AIR-DEFENCE.md).

### Marching

Movement runs on the **scenario clock at the formation's own speed**: a 45 km/h armoured battalion covers 45 km per hour of clock, and 12.5 m in the second you are watching. Ordering a march reports the distance, the speed and the ETA, and the unit info panel carries a live MARCH block while the unit is moving. To get somewhere sooner, **speed the clock up** — the ladder reaches 300×, which turns an hour of march into twelve real seconds. See docs/13-DATE-AND-TIME.md.

### Reading the map at range

Two things change as the camera pulls back, so an operational view stays a picture of the battle rather than a smear of counters:

- **Unit captions shrink and then drop out.** The icon holds its apparent size at every zoom; the caption on it does not. Full size below ~6 km of camera depth, tapering to a third by ~45 km, and gone past ~62 km. Identical in 2D and 3D — the attenuation is driven by camera depth, which is what actually decides apparent size in both.
- **Crowded formations fold into counted cluster markers** past ~22 km, **in battle mode only**. Same-side units within ~78 screen pixels of each other merge into one disc carrying the count, the largest echelon present and the head count; clicking it selects them all. Deliberately off in the scenario editor, where zooming out to see the whole map must not remove the counter you are trying to drag.

### Saving & loading

- **SAVE / LOAD** buttons or `F5` / `F9`.
- Each map is one JSON file with every unit's position and full status — see [05-MAP-SAVES.md](05-MAP-SAVES.md).
