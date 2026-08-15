# Artillery Strikes

Called fire missions in the map editor: pick a nature, place a target area on the map, and ten seconds later five rounds land inside it.

> **Keep this file current.** Every new nature, burst effect, report or call site must be recorded here in the same change. See [Rules](#rules) at the bottom.

---

## 1. Nature register

Rows live in `Assets/Scripts/Vfx/ArtilleryCatalog.cs`. The panel, the target marker and the impact sequence are all driven from them — a new calibre appears in the UI by adding a row, not by editing the UI.

| Origin | Class | Calibre | Real weapon | Beaten zone | Burst | Round spacing |
|---|---|---|---|---|---|---|
| NATO | Mortar | **60 mm** | M224 | 70 m | mortar | 0.20 s |
| NATO | Mortar | **81 mm** | L16 / M252 | 100 m | mortar | 0.28 s |
| NATO | Mortar | **120 mm** | M120 / RT-61 | 130 m | mortar | 0.42 s |
| NATO | Gun | **105 mm** | M119 / L118 | 140 m | light | 0.30 s |
| NATO | Gun | **155 mm** | M777 / PzH 2000 | 190 m | medium | 0.55 s |
| NATO | Gun | **203 mm** | M110 | 260 m | heavy | 0.85 s |
| Enemy | Mortar | **82 mm** | 2B14 Podnos | 105 m | mortar | 0.28 s |
| Enemy | Mortar | **120 mm** | 2B11 Sani | 135 m | mortar | 0.42 s |
| Enemy | Mortar | **160 mm** | M-160 | 185 m | heavy | 0.60 s |
| Enemy | Mortar | **240 mm** | 2S4 Tyulpan | 300 m | heavy | 1.10 s |
| Enemy | Gun | **122 mm** | D-30 / 2S1 Gvozdika | 150 m | medium | 0.35 s |
| Enemy | Gun | **130 mm** | M-46 | 175 m | medium | 0.45 s |
| Enemy | Gun | **152 mm** | 2S3 / 2S19 Msta-S | 200 m | heavy | 0.55 s |
| Enemy | Gun | **203 mm** | 2S7 Pion | 285 m | heavy | 0.95 s |

**Fourteen natures, four burst signatures.** Each nature does *not* get its own effect. What separates a 122 mm shell from a 152 mm one on a map three kilometres wide is how big the hole is, not what the flash looks like — so natures map onto four signatures (light burst, mortar soil column, standard HE burst, heavy blast) and are told apart by **beaten zone, burst scale and rate of fire**, all of which are real differences the player can see and use. Inventing fourteen near-identical particle effects would be fourteen things to keep in step for no gain. The same applies to the reports: four, mapped the same way.

**Mortars are not just small guns.** A mortar bomb arrives almost vertically and throws far more soil than fire, which is a genuinely different event on the map — hence `ArtilleryKind` and the separate `ArtilleryDirtColumn` signature. The two heaviest enemy-pattern mortars are exceptions that use the heavy blast: at 160 mm and 240 mm the round is a siege weapon and reads as one.

Shared constants, also in `ArtilleryCatalog`:

| Constant | Value | Meaning |
|---|---|---|
| `CountdownSeconds` | 10 | Time between the call for fire and the first round |
| `ShellsPerMission` | 5 | Rounds in one mission |

**Split by inventory, then ordered by calibre.** Fourteen natures will not fit in one column, and a scroll would bury the choice that matters, so the panel has **NATO / ENEMY** tabs — the first decision a player makes, and it halves the list. Within a page the natures run mortars then guns, ascending by calibre, so the beaten zone grows monotonically down the page and the trade-off is legible without reading a word.

**Why four signatures rather than one scaled explosion.** The four events genuinely do not look alike from a map camera: a light round is a bright crack with a flat shrapnel disc, a mortar bomb is a narrow column of soil, and a heavy shell is a fireball with a ground shock ring and arcing debris. One effect scaled four ways would make every nature the same event at four sizes. Within a signature, scale and rate of fire do the rest of the work — which is exactly what separates a 152 mm from a 203 mm.

---

## 2. How a mission runs

```
Left rail → ARTILLERY STRIKE      UnitPaletteUI.BuildArtillerySection
  ↓ pick a nature                 ArtilleryStrikeSystem.Toggle
  ↓ target area follows cursor    TargetAreaMarker (aiming instance)
  ↓ click the terrain             ArtilleryStrikeSystem.Fire
  ↓ 10 s countdown                 GameHUD.SetFireMission + marker alarm
  ↓ 5 rounds land                 ArtilleryStrikeSystem.RunSalvo
```

| Script | Role |
|---|---|
| `Vfx/CalledStrikeSystem.cs` | **Shared** arming, placement, countdown and HUD reporting |
| `Vfx/ArtilleryCatalog.cs` | The natures in numbers — the single source of truth |
| `Vfx/ArtilleryStrikeSystem.cs` | The natures and the salvo |
| `Vfx/TargetAreaMarker.cs` | The 3D target-area volume |
| `UI/UnitPaletteUI.cs` | `BuildArtillerySection` — origin tabs and the nature pages |
| `UI/GameHUD.cs` | `SetFireMission` — the countdown banner |
| `Core/GameController.cs` | Builds the system and wires it to the HUD and palette |

### Shared with naval, air, UAV and missile strikes

Everything up to the moment something lands is identical across all five, and lives in `CalledStrikeSystem<TKey>`. `ArtilleryStrikeSystem` supplies the natures and the salvo; `NavalStrikeSystem` the ships' guns and their faster, wider missions; `AirStrikeSystem` the airframes and the bombing run; `UavStrikeSystem` the drone and its dive (or its reconnaissance orbit); `MissileStrikeSystem` the launchers and the ballistic arc. All five share `TargetAreaMarker`, the strike allowance and the HUD banner — `GameController.RefreshStrikeBanner` shows whichever is nearest to landing. See docs/18-AIR-STRIKES.md, docs/19-UAV-STRIKES.md, docs/20-MISSILE-SYSTEMS.md and docs/21-NAVAL-GUNFIRE.md.

### The strike allowance

Every delivery system has **its own allowance of missions**, held as `missions`
on its catalogue row and counted by `Vfx/StrikeBudget.cs`. The figure is shown on
each button, under the beaten zone, in all five fire menus: two B-2 sorties,
twenty-four 60 mm fire missions, one DF-26.

**Why per system and not one pool.** It used to be a single pool of ninety-nine
shared by every strike in the game. That made every strike cost the same thing —
the next strike — so the choice between a 60 mm mortar mission and an Iskander
was free, and the only rational play was to spend the pool on whatever was
biggest. What makes a heavy weapon a real choice is that there are *two of them*.
An allowance attached to the weapon says what is scarce and what is plentiful,
says it on the button at the moment of choosing, and needs no explanation.

It also puts the number where the rest of a weapon's numbers already live:
`missions` sits beside the beaten zone and the countdown on the catalogue row, so
it is visible and tunable in **Development → Units List** like any other stat.

The artillery allowances, lightest to heaviest:

| Nature | Missions | | Nature | Missions |
|---|---|---|---|---|
| 60 mm mortar | 24 | | 82 mm mortar | 22 |
| 81 mm mortar | 20 | | 122 mm gun | 18 |
| 105 mm gun | 18 | | 120 mm mortar (enemy) | 16 |
| 120 mm mortar | 16 | | 130 mm gun | 14 |
| 155 mm gun | 12 | | 152 mm gun | 12 |
| 203 mm gun | 5 | | 160 mm mortar | 8 |
| | | | 240 mm mortar | 4 |
| | | | 203 mm gun | 5 |

**`StrikeBudget` only counts.** It does not hold the limits — the caller passes
each system's own figure in with the key, because the catalogues are the single
source of truth for what a weapon can do and a second copy of the limits would be
a second thing to keep in step.

The count is spent when a mission is **placed**, not when it lands: a mission
cannot be recalled once away, so that is the moment the player spent it. An
exhausted system refuses both arming and firing and stands its own tube down —
only its own; everything else in the menu is still available.

The readout turns amber at a third remaining and red at zero.
`StrikeBudget.Reset()` runs when the scene starts and on RESET — the state is
static and survives a scene load, and a fresh map opening with a spent bomber
would be inexplicable.

### What a mission leaves behind

The salvo is over in a few seconds; the mark on the ground is not. Every
completed fire mission plays `StrikeAftermath` at the aim point: **thirty
scenario minutes of fire, then two scenario hours of smoke**. One site per
mission, not one per round. See docs/08-PARTICLE-SYSTEMS.md §2.1 for why those
figures are on the operational clock rather than on a real one.

### The countdown is the feature

A strike that lands the instant you click is a paint tool. One that lands ten seconds later is a decision: the ground is committed to, the marker sits there advertising exactly where the rounds are going, and nothing can be done about it afterwards. That is why:

- **A mission cannot be recalled.** Right-click / Esc stands down the *tube* — it does not call back rounds already on the way.
- **The marker escalates.** It pulses faster, its colour runs toward warning red, and its sweep ring speeds up as the clock runs down, so the countdown is legible on the map without watching the HUD.
- **Missions are independent.** Several can be in the air at once, so fire can be walked across a position by placing the next before the last lands. The HUD banner shows whichever is nearest to impact.

### Unscaled time

The countdown, the marker animation and the salvo spacing all run on **unscaled** time. Tying them to game time would leave rounds hanging in the air indefinitely whenever the battle is paused — and the map editor spends most of its life paused. This matches the effect-placement reticle and the loading screens.

### Where the rounds fall

`ArtilleryStrikeSystem.ScatterPoint` places round *i* using the golden angle (137.508°) for the bearing and `sqrt(t)` for the radius, plus jitter on both.

The square root is what makes the scatter uniform **by area**. Without it every round crowds the centre and the sheaf looks nothing like a beaten zone; the golden angle stops successive rounds clumping on one bearing, and the jitter stops the pattern being recognisable between missions.

### Ground checks

Placement is ground-checked exactly like `EffectPlacementTool`: Cesium streams terrain in, and a click over tiles that have not arrived has no ground to put an impact on. Such a click is **refused with a message and leaves the tube armed** — losing a whole fire mission to the tile streamer would punish the player for something they did not do.


---

## Damage

Strikes are not visual only. Every round, weapon and warhead is resolved through
`Units/BlastDamage`, which is shared by **all five** strike types — artillery,
naval gunfire, air, UAV and missile — so they answer the question the same way.
This section is the canonical description of that model; the other strike docs
point here rather than repeating it.

Two radii, because a blast is not a switch:

| Radius | Effect |
|---|---|
| **Lethal** | Destroyed outright, whatever its strength was |
| **Blast** | Damage falls off with the **square** of the distance out to this edge |

Square falloff rather than linear because blast overpressure does. Linear makes
the rim of the circle as dangerous as the middle, which turns artillery into a
stamp-shaped area-denial tool instead of a weapon you have to aim.

### Range is measured to the formation, not to its map pin

This is what made strikes actually land, and it is the single most important
thing in this section.

A unit is stored as one lat/lon and drawn as one counter — but a battalion is
not a point, it is a kilometre or so of dispersed sub-units, vehicles and
positions. Measuring the range to the stored coordinate was asking whether the
round hit the formation's exact **centre**, which is a much harder question and
the wrong one. With a 155 mm blast radius of 132 m against a battalion, a fire
mission whose rounds visibly straddled the counter routinely did *nothing
whatever*. That was the model being wrong, not the player aiming badly.

Each formation now has a ground **footprint** by echelon
(`EchelonInfo.FootprintRadiusMeters`), and two thirds of it
(`BlastDamage.FootprintShare`) is subtracted from the measured range:

| Echelon | Footprint | Counted |
|---|---|---|
| Platoon | 110 m | 73 m |
| Company | 220 m | 145 m |
| Battalion | 550 m | 363 m |
| Brigade | 1300 m | 858 m |
| Division | 2400 m | 1584 m |

Not the whole footprint, because a formation is dispersed across its frontage: a
shell landing at the far edge of a brigade's ground is nowhere near most of the
brigade, and crediting the full radius would make a single 57 mm round on the
corner of a division a hit on the division. Two thirds is close to the ground the
fighting elements actually occupy and far enough short of the full extent that a
big formation is not a magnet.

The falloff is unchanged — a **direct hit still has to fall inside the lethal
radius measured from the edge of that footprint**, i.e. genuinely among the
sub-units, and damage still decays with the square of how far past it lands.
What changed is that the beaten zone the player is shown and the ground the
shells actually affect are now the same ground.

### What a mission reports

`BlastDamage.Apply` returns a `BlastResult` — formations hit, formations
destroyed, and total strength removed — and results add, so a caller accumulates
a salvo and reports the *mission* rather than its last round. A count of the dead
was not enough: most strikes destroy nothing and hurt several things, and
"0 formations destroyed" reads as *nothing happened*, which was both the
complaint and untrue.

> Rounds complete — 155 mm howitzer, 5 rounds. 3 formation(s) hit, 1 destroyed — 180 % combat strength lost.

Surviving formations also take **shock** — morale and organisation — at 55× the
strength damage, so being shelled and living through it still costs a formation
its composure. Near the blast edge that is the entire effect.

**It hits both sides.** A strike is placed on a piece of ground, and ground does
not check uniforms. Friendly fire is not a special case; it is what falls out of
doing the honest thing, and it is what makes placing a mission near your own line
a decision.

Artillery's numbers are **derived from calibre** rather than listed per nature:

| Quantity | Relation | 60 mm | 155 mm | 240 mm |
|---|---|---|---|---|
| Lethal radius | `calibre × 0.16` m | 9.6 m | 24.8 m | 38.4 m |
| Blast radius | `calibre × 0.85` m | 51 m | 132 m | 204 m |
| Max damage | `calibre / 700`, capped 0.06–0.40 | 0.09 | 0.22 | 0.34 |

Charge mass, and therefore lethal area, scales with the bore — so one relation
stays consistent by construction where fourteen hand-tuned triples would be
fourteen numbers to keep plausible against each other. A new nature gets sensible
values the moment its calibre is written down.

Each of the five rounds is resolved **where it actually lands**, not against the
target area as a whole. That is what makes the scatter matter, and why a wide
sheaf is not strictly better than a tight one.

---

## 4. The target-area marker

`TargetAreaMarker` draws the area as a **volume**, not a decal, because a flat ring painted on the imagery is unreadable on this map: the camera spends most of its time at a shallow pitch, where a circle on sloping ground foreshortens into a line and vanishes entirely behind a ridge.

| Element | Purpose |
|---|---|
| Ground disc | Faint fill so the area reads as a piece of ground, not just an outline |
| Rim band | Brightest element — the edge the eye actually uses to judge where rounds fall |
| Wall | Translucent cylinder, height = 0.6 × radius, fading to nothing at the top |
| Top ring | Closes the cylinder visually without capping it (a lid would hide the inside from above) |
| Cardinal ticks | Four radial bars — give the circle an orientation at shallow angles |
| Centre blades | Two crossed vertical quads marking the exact aim point from any bearing |
| Sweep ring | Dashed ring on its own transform, rotating; speeds up with the countdown |

It is one procedural mesh with vertex colours plus a second for the sweep — no texture to blur at 260 m across, and no material asset to ship. Triangles are emitted in **both windings** because the volume is looked at from outside, inside and directly overhead, and the fallback shaders in `RuntimeMaterials` do cull.

`Reshape` rebuilds it in place when the player switches nature, so the area resizes rather than blinking.

---

## 4. Effects and audio

Bursts and smoke are ordinary `VfxCatalog` rows — see **docs/08-PARTICLE-SYSTEMS.md** for the full effect register and the three artillery fallback builders (`ArtilleryAirBurst`, `ArtilleryDirtColumn`, `ArtilleryHeavyBlast`).

Reports are ordinary `EffectSound` values — see **docs/10-AUDIO.md**. Each burst row names its own report, and `VfxInstance` plays it positionally when the burst spawns; the artillery catalogue deliberately does **not** repeat the sound, so the two cannot drift apart.

Artillery smoke **loops** (`lifeSeconds = 0`) and is dispersed explicitly by `RunSalvo` via `VfxSystem.StopAfter`, the same way `PlayWreck` burns a wreck out. A one-shot lifetime would cut the particles off mid-air instead of letting them thin out.

---

## 6. Known gaps

- **No firing battery.** Rounds appear at the target; nothing on the map fires them, and no unit is consumed or required to call the mission.
- **No ammunition or cooldown.** Missions are unlimited and can be placed as fast as they can be clicked.
- **Not saved.** A mission in the air is lost on save/load; the marker is runtime-only and is not part of the map schema.

---

## Rules

1. **`ArtilleryCatalog` is the source of truth.** Add a nature by adding a row — never special-case a calibre in `UnitPaletteUI` or `ArtilleryStrikeSystem`.
2. **Every nature needs its own burst and smoke `VfxId`**, with rows in `VfxCatalog` and a procedural fallback, because asset packs must stay removable.
3. **The report belongs to the burst's `VfxCatalog` row**, not to the artillery row.
4. **Update this file, docs/08-PARTICLE-SYSTEMS.md and docs/10-AUDIO.md in the same change** whenever a nature, burst, smoke or report is added, removed or repurposed.

---

## Related

`docs/03-GAMEPLAY.md` · `docs/07-ARCHITECTURE.md` · `docs/08-PARTICLE-SYSTEMS.md` (effect register) · `docs/10-AUDIO.md` (sound register) · `docs/15-COMBAT-ORDERS.md`

---

### The target area is a kill zone

Every called strike resolves its **ring** once, at the aim point, the moment the
first ordnance lands (`StrikeImpact.Arrive`): anything whose counter is inside the
circle is destroyed outright, and a shockwave the size of that circle is drawn.

The circle a strike draws makes a promise — *everything in here dies* — and the
round-by-round model did not keep it. Each round has a lethal radius of a few tens
of metres scattered inside a target area of hundreds, so a formation could sit in
the middle of a strike and come out at 60 % strength. That reads as the weapon not
working, and no amount of tuning the falloff fixes it, because the falloff is not
what the circle is promising.

Centre rather than footprint edge, deliberately: a division clipped by the rim of a
105 mm target area should not evaporate, and requiring the counter itself to be
under the circle is what keeps *where to put it* a real decision.

The per-round passes still run afterwards and still matter — they are what damages
formations **outside** the ring, and what makes a wide sheaf different from a tight
one. Their outer reach is now `max(blastRadiusM, ring × 1.9)`, so damage can never
fall short of the circle the player was shown. See `Vfx/StrikeImpact.cs` and
`Units/BlastDamage.ApplyRing`.
