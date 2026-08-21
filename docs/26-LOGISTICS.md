# Logistics

The register of every kind of logistic installation a scenario can be given,
and where they are laid out. This is the human-readable version of
`Assets/Scripts/Data/LogisticsCatalog.cs` — **keep it in step with that file in
the same change.**

---

> **Choosing a side.** The panel carries its own **FRIENDLY / ENEMY** selector at
> the top. It used only to report the side, which was chosen on the UNITS tab —
> so working on the enemy's logistic installations meant leaving this panel to switch, coming
> back, and remembering to switch again afterwards. It is the same side every
> other panel uses, and all of their tabs repaint together; there is one selected
> side in the editor, not one per panel.


## 1. What a logistic site is

A **place on the map that supports the force**: a depot, a supply point, or one
of the four function-specific points. It belongs to a side, it sits on the
terrain, and it is saved with the map.

It is deliberately **neither a unit nor a task marker**:

| | Unit | Task marker | Logistic site |
|---|---|---|---|
| Fights, moves, dies | yes | — | no |
| Belongs to | itself | the formation ordered | the scenario |
| Swept off the map when its owner goes | — | yes | never |
| Comes from | `units.json` | an order | the LOGISTICS panel |

That last row is why it has its own system rather than borrowing
`MarkerManager`: a task marker is removed the moment the formation that was
given the order leaves the map, and an ammunition point that vanished because
the battalion nearest it was destroyed would be exactly wrong.

---

## 2. The register

Six kinds, from the rear forward. The **service radius** is the ground the
installation covers. It is stated on the kind's button, drawn on the terrain
while the site is being placed and whenever its panel is open, and drawn for
every site at once from the panel's **SHOW SERVICE RINGS** switch — see §4a.

| Kind | Name | What it is for | Service radius | Issues | Serves | Glyph | 3D model |
|---|---|---|---|---|---|---|---|
| `SupplyDepot` | SUPPLY DEPOT | Rearms, refuels, repairs and treats | 25 km | 40 | General | Warehouse — pitched roof over a shed | `supply_depot_site` — warehouse, dock, container park |
| `SupplyPoint` | SUPPLY POINT | Forward supply — everything, at a reduced rate | 12 km | 20 | General | Two stacked crates | `supply_point_site` — canopies over pallets |
| `FuelPoint` | FUEL POINT | Refuel vehicles | 10 km | 12 | Fuel | Droplet | `fuel_point_site` — bunded tank and gantry |
| `AmmoPoint` | AMMO POINT | Replenish ammunition | 10 km | 12 | Ammunition | Two rounds | `ammo_point_site` — three revetted bays |
| `RepairPoint` | REPAIR POINT | Return deadlined vehicles to the road | 8 km | 12 | Repair | Crossed tools | `repair_point_site` — gantry over a stripped hull |
| `MedicalPoint` | MEDICAL POINT | Treat and evacuate casualties | 8 km | 12 | Medical | Cross | `medical_point_site` — hospital tents under a cross |

**What one issue puts back**, by service:

| Service | Restores | Ceiling |
|---|---|---|
| `Ammunition` | Half an establishment of rounds | full |
| `Fuel` | Half a tank | full |
| `Repair` | 15 % serviceability | full |
| `Medical` | 8 % strength | 75 % |
| `General` | all four, at 70 % of the above | as above |

**The register is a class, and it is tunable.** `LogisticsDef` used to be a
`readonly struct`. It is a class with writable fields now for one reason:
`TunableField` reflects over public instance fields and writes them, so
DEVELOPMENT → UNITS LIST can edit a record live and `TuningStore` can patch it on
load — and a readonly struct is a value copy the moment it is boxed, so an edit
would land on a copy and silently do nothing. Four fields are listed in the group's
`readOnlyFields` — **`kind`, `service`, `modelId` and `siteVfx`** — because they
are identity and wiring rather than tuning:

- `kind` decides which glyph, model and save name the row answers to. Editing it
  would rename an installation without moving any of them.
- `service` decides whether the installation does anything at all. Cycling a
  repair point's off `Repair` would stop every workshop on every map working, be
  written to `tuning.json`, and be re-applied on every future load — with nothing
  in the interface saying why.
- `modelId` and `siteVfx` are ids into other registers, where a typed-in value is
  a *missing* model or effect rather than a different one.

What is left is what is genuinely worth tuning: the name, the one-line detail,
the service radius and the default stock.

**Repair and medical are not the same job**, and that is why both exist. A
medical point restores **people** — `strength` — and cannot reconstitute a
battalion that has been destroyed, so it is capped at 75 %. A repair point
restores **equipment** — `serviceability` — and can put a recovered vehicle back
exactly as it was, so it is not capped at all.

Repair used to serve nothing, and the reason given was honest: vehicle state was
not modelled apart from a formation's strength, and quietly healing strength
there would have made the workshop a second hospital. Modelling it was the
prerequisite, and it is modelled now — see *Serviceability* under §2a.

**The radii are service ranges, not blast radii.** They say how far the
installation's ground extends, which is what makes a laydown judgeable: a depot
covering the whole sector is in the wrong place if it is inside the enemy's
reach, and a fuel point that reaches none of the armour is a fuel point in the
wrong valley.

**Six silhouettes, not six letters in a box.** NATO's own logistic symbology is
a rectangle with a letter in it, which is unreadable at 20 px on a rail button
and at whatever the camera makes of it on the map. Each kind is a different
*shape* instead — the one property that survives being small.

---

## 2a. Sites hold stock, and formations draw from it

`Logistics/ResupplySystem.cs`. **This is what a supply point is for.**

Until it existed a logistic installation was a symbol. A designer could lay out a
rear area, an aeroplane could push five bundles onto the objective, and nothing
on the map was any better supplied for it — which made the whole LOGISTICS panel
decoration and an air supply mission a firework. A formation that has run dry is
the most interesting state a unit can be in, and it needs somewhere to go.

### The rule

**A formation inside the site's service radius, on the same side, alive, is
topped up.** No convoy, no order, no draw request. This game is played at the
operational level, where *is it in the fuel point's area* is exactly the question
a staff officer asks — and modelling the truck run would be modelling something
the player has no control over anyway.

| | |
|---|---|
| **Battle mode only** | Nothing is being expended in the editor, and a cache that drained itself while a scenario was laid out would be a scenario that started wrong |
| **Both sides, on the same rule** | Every site serves formations of **its own side** standing inside it. The sweep walks every installation on the map, so the enemy's rear area works exactly as the player's does — and a strike on it costs them exactly what a strike on yours costs you |
| **Every two minutes** of scenario time, per formation per site | Long enough that a unit parked on a depot does not hoover it up in a few seconds of fast-forwarded clock; short enough that pulling a battalion back to refuel is worth doing rather than a wait you watch |
| **Half an establishment per issue** | A formation that arrives empty and leaves full in one draw makes the second draw meaningless |
| **Nothing is spent on a formation that needs nothing** | The usual reason a cache is not going down |
| **Medical recovers 8 % strength per issue, capped at 75 %** | A medical point treats casualties and returns the lightly wounded. It does not reconstitute a battalion that has been destroyed |
| **Repair recovers 15 % serviceability per issue, uncapped** | A workshop puts a recovered vehicle back exactly as it was. Faster than the hospital and with no ceiling, because those are the two ways the jobs genuinely differ |
| **A formation that walks draws nothing from a workshop** | And is charged nothing. A rifle battalion parked on a repair point has no equipment to recover, and a site quietly consumed by the wrong customers would be a site that was not there when the armour arrived |
| **A general site does all four at 70 %** | Which is what makes a forward SUPPLY POINT worth its shorter reach |

### Serviceability — what a repair point restores

`UnitState.serviceability`, 0–1: the fraction of a formation's **equipment** that
is running. See docs/04-UNITS.md.

It exists because the repair point had nothing to restore that was not already
somebody else's job. Strength is people and belongs to the medical point; ammo
and fuel belong to their own points. A tank with a thrown track is none of those:
it is not a casualty, it is a recovery job, and the place to take it is a
workshop.

| | |
|---|---|
| **Who has it** | A formation whose type burns fuel — `UnitActor.HasEquipment`. Six of the seven infantry types carry none, and for them serviceability reads 1 and is never asked about again. The catalogue already answers "does this run on vehicles", so a second hand-maintained flag would only be a chance for the two to disagree |
| **What it costs** | Combat power is scaled by `lerp(0.45, 1, serviceability)`. It never reaches zero: a battalion with every vehicle deadlined is still several hundred trained soldiers on ground they know |
| **What takes it** | 0.6 of every point of strength lost under fire — the share of a formation's losses that is recoverable rather than destroyed. And **1.6×** the strength a mine strike costs, because immobilising is what a belt is for (docs/31-OBSTACLES.md) |
| **What gives it back** | A REPAIR POINT, or a depot or supply point at 70 %. Nothing else on the map |
| **Speed is deliberately untouched** | A mobility kill really should slow a column, but every ETA the order feedback quotes comes from the catalogue's `speedKmh`, and a march that silently took half again as long as the panel promised is a worse lie than a formation that merely fights less well |

**Old saves load fully serviceable.** A file written before this existed has no
`serviceability` key, and `JsonUtility` leaves a missing field at its initialiser
— which is `1f`. That is the correct reading of a scenario that never recorded a
breakdown, and it is the same no-migration trick the `capacity` field uses.

### Stock is counted in issues

One issue is one formation's worth. It is the number a player can reason about —
*this cache is good for four more battalions* — and litres and rounds are not
comparable across the three loads.

**A bigger formation costs more**, scaled on **√(echelon manpower)**. A hundredfold
linear cost would make any cache useless to anything above a battalion; a flat
cost would make a division as cheap to supply as a company.

**Old saves are stocked on load.** A scenario written before installations held
anything arrives with `capacity` at zero, and a rear area that supplied nothing
would be a silent regression for every map that already exists — so the
catalogue's figure is filled in. A zero in a file is not a deliberately empty
depot; it is a file written before the field existed.

**An emptied airdropped cache is removed; an emptied depot is not.** A cache is a
pile of boxes and an empty pile of boxes is not a supply point. A depot that has
issued its last establishment is still a depot, still where the next convoy comes
to, and still something the designer put there — removing it would be the game
editing the scenario.

### The supply panel

**Click an installation on the map**, in **either mode** — laying a rear area out
and fighting over one both raise the same question about it.
`UI/SupplyPanelUI.cs`, on the right-hand edge with the unit inspector and the
front-line options.

**The click is measured against the plate, not the ground under it.** The marker
is a billboard standing above its ground point, by an offset that scales with
zoom and grows again when the site's 3D model is up. Picking used to test the
ground point, which left the top half of every plate dead and, with models
switched on, put the whole symbol outside its own hit area — you clicked the
symbol and nothing happened. `LogisticsSite.MarkerWorldPosition` is the position
that is drawn and `MarkerWorldRadius` sizes the pick to it, the same pair
`UnitActor` has always had for its counters. On this map the thing you click is
the counter, never the ground under it.

Clicking the thing you want to know about is the only discoverable way in, and it
is the question the player actually has standing over a rear area: *which of
these is nearly out, and is anything close enough to use it?* The LOGISTICS panel
lists what exists; this says what it is worth.

Three readings, in the order they are asked:

1. **How much is left** — a bar and a figure. The bar is green above half, amber
   below, red on the last issue: the same three-stage reading the strength bars
   use, so a rear area in trouble looks like a formation in trouble.
2. **What it reaches**, in kilometres — **and the ring is drawn on the ground
   while the panel is open.** The whole geometry of a rear area is whether the
   radius covers the formations that need it, and that is a question about
   ground rather than about a number. Clicking straight from one installation to
   the next hands the ring over; closing the panel drops it, unless the LOGISTICS
   panel's switch is holding every ring up (§4a).
3. **Who is in it right now** — one row per formation with what it is short of,
   as `AMMO 38 % · FUEL 91 % · STR 62 %`. Percentages rather than absolutes: 1 200
   rounds means nothing without the establishment beside it.

A formation already full is **listed and greyed**, not hidden. The usual answer
to "why is this cache not going down" is that everything near it is full, and a
list that silently omitted them could not say so.

A formation's row reads `AMMO 38 % · FUEL 91 % · STR 62 % · SVC 44 %`. A
formation that walks gets `SVC —` rather than a permanent 100 %: an unbroken
column of full serviceability across a rifle brigade would read as a figure being
tracked when it is a figure that does not apply.

The caption on the map carries the issues left as well, so the "which of these is
nearly out" question can be answered without opening anything.

### How much a site holds

**HOLDS − / + / FILL**, on the supply panel, in the editor.

The catalogue's figure is a sensible default, not a rule. A scenario in which
every depot holds exactly forty issues is a scenario whose rear area has no
shape, and a forward dump that is *meant* to run out halfway through the battle
is a design decision that needs somewhere to be made.
`LogisticsSiteData.capacity` has always been in the save file; until now nothing
could write it.

Lowering the limit takes the stock down with it — a depot recorded as holding
more than it can is a save that contradicts itself, and the bar would draw past
its own track.

**The control is hidden once the battle is running.** Topping a depot up
mid-fight is not a design decision, it is a cheat, and a control that is only
sometimes legitimate is better absent than present-and-disapproved-of.

---

## 3. Laying them out

**Left rail → LOGISTICS.** Six buttons, read straight off the catalogue, and
**two gestures on each of them** — exactly as a unit card carries them, for
exactly the same reasons.

**Drag one onto the map.** The direct statement: you are carrying the thing and
you put it down, and it is what makes a rear area quick to lay out. Press a
button, drag onto the terrain, release. Releasing back over the panel or the HUD
places nothing and says so, rather than dropping a depot onto whatever ground
happens to be behind that interface.

**Or click one and then click the ground.** The gesture a drag cannot make —
onto ground you have to pan to first, and from a session driven by a pad rather
than a mouse. The kind lights, and the next click on the terrain deploys it. The
tool **stays armed**, because a rear area is laid out several sites at a time;
right-click, `Esc` or **STOP DEPLOYING** puts it away.

**A drag released back over the button arms that kind**, and nothing is
deployed. uGUI suppresses the click after a drag only when the pressed object
and the dragged object differ, and here one handler is both — so releasing over
the button still raises `PointerClick`, and raises it *before* `EndDrag`. Arming
stands the drag down as it goes, so the `EndDrag` that follows finds nothing to
place and says nothing. A gesture that never reached the map therefore leaves the
tool armed, which is the useful reading of it. What cannot happen is one press
both deploying a site and arming the tool.

### What the preview says

Both gestures drive the same code (`LogisticsSystem.TrackGround`), so what you
see and where the site lands cannot disagree. It answers three questions,
because a rear area is judged on all three and a bare cursor answered none:

| | |
|---|---|
| The **plate** | *What* you are about to drop — the very marker the deployed site will wear, in the side's colour, not an approximation of it. With six kinds on one panel this is the first question |
| The **motes** | *Where* it will land. A rear area is laid out from a shallow camera angle, where a flat marker foreshortens into a line and a fold of ground hides it entirely; something rising out of the spot survives both. `LogisticsPlacementMotes`, docs/08-PARTICLE-SYSTEMS.md |
| The **service ring** | *What it will reach* — the kind's own radius, draped on the terrain, following the cursor. The whole geometry of a laydown, judged before you commit rather than after |

The ring is affordable while dragging because `RangeRing` re-samples the terrain
only once its centre has moved a few per cent of the radius.

Ground that has not streamed in yet is **refused with a message** rather than
guessed at — the same rule the effect tool and every strike follow. Two separate
questions are asked and both must answer yes: the raycast says the pointer is
over *something*, and a terrain sample says the ground under that point can
actually be measured. They come apart at a tile seam, where a site would be left
at the fallback height inside a ridge.

**The team tab decides the side.** A scenario has two rear areas and the
designer lays out both, so the panel follows the team already chosen in UNITS
rather than carrying a second side control. Whichever side you are deploying
formations for is the side you are deploying its supply for — and the panel says
which that is, in that side's colour, beside the DEPLOY ON MAP heading. A deploy
button whose side is decided on another page is a button you press to find out.

**SHOW SERVICE RINGS** draws the ground every installation covers, on the
terrain, both sides at once. Off by default; see §4a for why it is a switch
rather than a permanent feature of the map. It is the one control on this panel
that is about *judging* a laydown rather than making one.

**DEPLOYED** below it lists every site on the map with its coordinates, with
**◎** to fly to one and **✕** to remove it; **REMOVE ALL SITES** clears the
lot. The count line reads `DEPLOYED — n FRIENDLY · n ENEMY`.

**Or drop one from the air.** The strike dock's **AIR SUPPLY** menu tasks a
transport to parachute ammunition, fuel or medical stores onto a zone, and every
bundle that lands becomes one of these sites — captioned `AIRDROP · AMMO` and so
on, but otherwise identical to a hand-placed one. See docs/29-AIR-SUPPLY.md.

**Or remove one on the map**: right-click the site's marker and pick **REMOVE
SITE**. The panel is the right place when you are working through a laydown; the
map is the right place when you are looking at the thing you want gone. Sites are
picked in screen space against the marker you can see — see docs/03-GAMEPLAY.md
§ *The right-click menu*.

---

## 4. On the map

| Part | What it says | Always? |
|---|---|---|
| Ground ring | The owning **side** — blue or red — lying flat on the terrain. Dims as the site empties | yes |
| Marker plate | The **function**'s glyph in the kind's own tint, on a chamfered plate framed in the side's colour, standing up to face the camera | yes |
| Stock bar | How much is left, as a length: green above half, amber below, red on the last fifth | when the site tracks stock |
| Caption | The kind's name (or the site's own label), and the issues left | yes |
| Service ring | The ground it serves, draped on the terrain | §4a |
| 3D model | The installation's own buildings, with ambient motes | §4b |

**The plate billboards and the ring lies flat**, deliberately. A laydown is read
two ways: from overhead, where what matters is *where* the sites are relative to
the units they serve, and from a working camera angle, where what matters is
*which* site is which. A flat ring answers the first at any tilt and a
billboarded symbol answers the second, so the marker keeps both.

**Why the symbol sits on a plate.** It used to be a bare white silhouette drawn
straight onto the terrain — legible over a field and gone over a town, a
snowfield or a river, which is the ground a rear area is most often on. Every
readable map symbol in the world solves this the same way: put the symbol on a
ground of its own. The plate carries the **side in its frame** and the
**function in its glyph**, over a near-black fill, so the marker's contrast stops
being a property of whatever imagery is behind it.

It is **chamfered** rather than round or square because those are taken: a disc
is a strike area and a unit's selection ring, and a plain square is a map
object's fill. Cutting the corners off a square gives the logistics family a
silhouette of its own that still reads at ten pixels, where the difference
between a circle and a rounded square does not.

The plate and the glyph are **one baked texture**, not two stacked quads. Two
quads would each need a depth offset and would still separate at a grazing camera
angle — precisely the angle the editor is worked at. There are twelve of them in
the game (six kinds × two sides), composed once and cached
(`UiIcons.MapMarkerFor`).

**The stock bar is the caption's number at a glance.** Standing over a rear area
the question is *which of these is nearly out*, and the answer has to survive
being read at a zoom where `12 / 40 ISSUES` is four pixels tall. It uses the same
three-stage green/amber/red the formation strength bars do, so a rear area in
trouble looks like a formation in trouble, and it retreats from the left rather
than shrinking into its own middle. A site that does not track stock — an old
save's inexhaustible depot — gets **no bar at all** rather than a full one, which
would be a claim about a quantity nobody recorded.

Markers are sized like task markers (constant apparent size, clamped), so a rear
area reads as part of the same map as the formations it supports rather than as
a separate layer of furniture. Everything is clamped to the terrain and
re-clamped until the ground under it has actually streamed in.

### 4a. The service ring

`RangeRing` — the same instrument a unit's weapon range and line of sight are
drawn with: a feathered band lying on the terrain with cardinal tick spurs and a
caption at its north point.

This used to be a known gap, and the reasoning that kept it one was half right. A
flat disc at the site's own altitude *would* be cheap and wrong — over a 25 km
radius it sinks into every hill and floats over every valley, which is worse than
not drawing it. What was wrong was the conclusion: the game already owns a ring
that drapes, and the honest fix was to use it. A draped band dips and rides with
the ground and states the distance truthfully.

**Shown on demand rather than always**, because the band costs ~200 terrain
samples to build and a rear area is a dozen sites:

| When | Which |
|---|---|
| A kind is being dragged or is armed | The **preview's** ring, following the cursor at that kind's radius |
| An installation's supply panel is open | That **one** site's ring |
| LOGISTICS panel → **SHOW SERVICE RINGS** | **Every** site's, both sides |

The switch is the moment a designer is actually judging coverage, which is the
moment the cost is worth paying — it is the one view that answers *does this rear
area cover the force behind it*.

**The switch and the panel do not fight, in either direction.** While the switch
is on, a panel opening or closing cannot pull a ring down; and when the switch is
turned off, the site whose panel is open **keeps** its ring rather than losing it
with the rest. The panel says its ring is drawn, and a control on another panel
must not quietly make that a lie. `LogisticsSystem` remembers which site the
panel selected for exactly this — `ShowRingFor` records it, and
`SetServiceRingsVisible` leaves that one standing.

**A ring is never built on ground that has not arrived.** `RangeRing` re-bakes
its draped heights only when its centre or radius moves, so one built while the
terrain was still streaming would be a flat disc buried in every ridge for the
rest of the session rather than a mistake that corrects itself. A site defers its
ring until its own terrain sample succeeds, and puts it up the moment it does.

Rings are built lazily, so a site nobody ever looks at never pays for one, and a
site whose terrain has not streamed in yet defers its ring rather than baking
heights off the fallback altitude.

### 4b. The buildings

**GENERAL → SHOW UNIT 3D MODELS** — the same switch the formations follow. Six
models, built in code (`ProceduralModels.Logistics.cs`, docs/09-3D-MODELS.md).

A battlefield where the formations are solid and the depots are decals is exactly
the inconsistency that switch exists to remove: you fly the camera down to a
supply point that supplies a brigade and find a decal. So `GameController` calls
`UnitActor.SetModelsVisible` and `LogisticsSystem.SetModelsVisible` from one
handler.

The marker does not go away — it shrinks and rides above the model, exactly as a
formation's counter does, so a site is still identifiable as ammunition or fuel
from a distance at which the buildings are a smudge.

While its model is up a site also carries `LogisticsSiteHaze`: sparse ambient
motes that say the place is *occupied* rather than that it is burning. They come
with the model rather than with the marker, because a map of counters should stay
a clean map of counters.

**An airdropped cache is the exception and ignores the switch.** A depot is a
*place* — what matters about it is which one it is and how far it reaches, which
is what a doctrinal symbol says and a crate cannot. A cache is *a thing somebody
just put there*: the player watched it come down under a canopy, and what they
want afterwards is to find it again on the ground where it landed. Its
`supply_bundle` is always drawn. See docs/29-AIR-SUPPLY.md.

### 4c. Strikes destroy them

`StrikeImpact.WreckSupplies`, so **every** called mission does it: artillery, air,
UAV, missile and naval gunfire all funnel through one place.

Until this existed a 203 mm mission could land squarely on an ammunition point
and leave it issuing rounds — which made the rear area the one part of the map
that could not be fought over, and made *finding* the enemy's logistics pointless
since there was nothing to do about it. The most valuable target on an
operational map is now a target.

| | |
|---|---|
| **Centre-in-ring** | The same test `BlastDamage.ApplyRing` uses on formations. An installation is a point on the map, and the promise the circle makes is that what is inside it is gone |
| **Both sides** | Ground does not check uniforms. A strike called near your own rear area is a decision precisely because it can cost you the rear area |
| **Resolved before the units** | So a formation and the cache it was sitting on go in the same instant, rather than the cache surviving the round that killed the people guarding it |
| **Explosion + wreck fire** | How the player learns from across the map that the strike found something worth finding |

---

### 4d. Where else the six appear

The catalogue is the single source, so the installations show up wherever the
game's data is browsable — without either screen being taught what a logistic
installation is.

| Screen | What it shows |
|---|---|
| **Extras → UNITS → LOGISTICS** | The reader's board. Six rows — name, what it serves, reach, stock, purpose — with the kind's glyph, its 3D model on the turntable, and a detail block covering what it does, what it looks like on the map and where its model comes from |
| **Development → UNITS LIST → LOGISTICS** | The editable table. The same six as records: every field reflected, sortable by reach and by stock, tunable live and saved to `tuning.json` as a sparse patch |
| **Development → 3D MODELS** | Each installation model, with the installation named as its user. A model worn by no *formation* is not a model worn by nothing |

**Why the encyclopaedia carries them.** A reader asking "what does this side
field" is asking about the rear area too, and a book covering 117 formation types
that omits the six things keeping them supplied is a book with a hole in it. The
board entry is flagged `installations` because an installation is not a formation
— it does not fight, move or die, and it has none of a unit's numbers — so it
cannot be a branch filter over `UnitDatabase`.

The installations table is deliberately **not** the formation columns with blanks
in them. An installation has no attack, no armour and no speed, and a row of
dashes under those headings would state six values that do not exist. What it has
is a reach, a stock and a service, which is exactly what distinguishes one from
another.

---

## 5. Saving

Sites are written to the map file as `logistics`, one `LogisticsSiteData` each:

```json
"logistics": [
  { "id": "log-3f9a21c4", "kind": "FuelPoint", "team": "User",
    "label": "", "latitude": 45.75, "longitude": 4.85, "heightMeters": 214.0 }
]
```

Empty on a map saved before logistics existed, which reads correctly as "this
scenario has no rear area". `JsonUtility` leaves missing fields at their
initialiser values, so old maps load without a migration step.

---

## 6. Where the code lives

| File | Role |
|---|---|
| `Assets/Scripts/Data/LogisticsCatalog.cs` | **The register** — the six kinds in numbers |
| `Assets/Scripts/Data/MapSaveData.cs` | `LogisticsSiteData` and the `logistics` list; `UnitState.serviceability` |
| `Assets/Scripts/Logistics/ResupplySystem.cs` | **The rule** — who draws what from where, and what it costs the site |
| `Assets/Scripts/Units/UnitActor.cs` | `HasEquipment`, `Serviceability`, the combat-power factor, and what damage deadlines |
| `Assets/Scripts/Logistics/LogisticsSystem.cs` | Owns the sites, both placement gestures, the shared preview, the rings and models switches, save/load |
| `Assets/Scripts/Logistics/LogisticsSite.cs` | The map graphic: ground ring, marker plate, stock bar, caption, service ring, 3D model |
| `Assets/Scripts/Models/ProceduralModels.Logistics.cs` | The six installations' 3D models, built from primitives |
| `Assets/Scripts/UI/UiIcons.cs` | The six glyphs and `GlyphFor(LogisticsKind)`; `MapMarkerFor` composes the map plate |
| `Assets/Scripts/Units/ProceduralTextures.cs` | `MarkerPlate` / `MarkerPlateWithGlyph` — the chamfered plate itself |
| `Assets/Scripts/UI/UnitPaletteUI.Terrain.cs` | The LOGISTICS section, generated from the catalogue, and the drag handlers |
| `Assets/Scripts/UI/SupplyPanelUI.cs` | What one installation holds and who can reach it |
| `Assets/Scripts/Data/GameCatalogs.cs` | The `Logistics` group — what puts the six in DEVELOPMENT → UNITS LIST |
| `Assets/Scripts/UI/UnitLibraryUI.cs` | The LOGISTICS board in EXTRAS → UNITS |
| `Assets/Scripts/Core/GameController.cs` | Wiring, the DEPLOYED list's actions, the models switch, save/load |

---

## 7. Adding a kind

1. A value on `LogisticsKind` and **a row in `LogisticsCatalog.All`** — name,
   one-line detail, service radius, default stock, tint, model id, and the
   `SupplyService` it hands out.
2. A glyph in `UiIcons` and a case in `UiIcons.GlyphFor`.
3. A builder in `ProceduralModels.Logistics.cs`, its id on
   `ProceduralModels`, and an `Installation(...)` row in `UnitModelLibrary` —
   **or** pass an existing model id if the new kind genuinely looks like one that
   already exists.
4. A new **service** — as opposed to a new kind handing out an existing one —
   also needs a value on `SupplyService`, a restore step in
   `ResupplySystem.Draw`, and a case in each of the four switches that read it:
   `SupplyPanelUI.RefreshService`, `SupplyPanelUI.Wants`,
   `UnitLibraryUI.ServesText` and `UnitLibraryUI.RestoresText`. They are
   switches with a default arm, so a missing case is a silent wrong answer
   rather than a compile error — this is the one part of adding a kind that the
   catalogue does not drive for you.
5. **Update the table in §2 of this file, and the register in
   docs/09-3D-MODELS.md §1.**

Nothing else. The panel's buttons, both placement gestures, the preview, the map
marker, the service ring, the save file, the DEVELOPMENT table, the EXTRAS board
and the 3D MODELS screen are all driven from the catalogue, so a seventh kind
appears in every one of them without any of them being touched.
