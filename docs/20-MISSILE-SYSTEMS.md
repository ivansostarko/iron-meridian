# Missile Systems

The register of every missile system in Iron Meridian — what it reaches, what it does at the other end, and where it appears.

> **Keep this file current.** Every system added to `MissileCatalog` must be recorded in §2 in the same commit. See [Rules](#rules) at the bottom.

---

## 1. Architecture

```
Assets/Scripts/Vfx/
  MissileCatalog.cs       the register in code: id → role, radius, trajectory, effects
  MissileStrikeSystem.cs  arming, target area, countdown, impact  (CalledStrikeSystem<MissileSystemId>)
  MissileRun.cs           the flight: ballistic arc, code-built airframe, plume, motor
Assets/Scripts/UI/
  MissilePanelUI.cs       the right-hand board: NATO / ENEMY tabs, ten buttons
  UnitPaletteUI.cs        the MISSILE SYSTEMS rail row that opens it
```

Everything up to launch is `CalledStrikeSystem<TKey>`, shared with artillery (docs/17), air strikes (docs/18) and UAVs (docs/19): the arming, the target area tracking the cursor, the ground checks, the ten-second countdown, the escalating marker and the HUD banner. Adding a fourth caller cost none of that twice, which is what the base class is for.

### The ten-second countdown

The same ten seconds every other fires system uses, and for the same reason: **the ground is committed to before anything happens.** One countdown across four systems means the player learns it once. `MissileCatalog.CountdownSeconds`.

### The destruction radius

The circle that follows the cursor while a system is armed is `MissileSystemDef.radiusMeters`, drawn by the shared `TargetAreaMarker`. It is shown **before** the click, not after, so choosing a system is a decision made against the ground rather than against a number in a list.

**The figure means two different things and the panel says which.** For a surface-to-surface system it is the area the warhead covers. For an air-defence system it is the engagement footprint it can clear — a much larger circle making a much weaker claim. The panel groups the two roles under separate headings, each with the radius caption spelled out, because sorting them into one list would put a 3 km umbrella next to a 620 m warhead and invite exactly the wrong reading.

### The flight

`MissileRun` is a third flight alongside `BomberRun` and `DroneRun` because it is a third manoeuvre. An aircraft flies *through*; a loitering munition flies *at* the target from the next ridge; a missile arrives from **above**, having come from somewhere off the map.

| Phase | What happens |
|---|---|
| Climb (first 42%) | Off the horizon on the run-in bearing, accelerating to apogee |
| Descent (last 58%) | Over the top and down, nose following the trajectory |

Height is two parabolas meeting at apogee rather than one symmetric arc — it goes up under power and comes down under gravity, and those are not the same curve. Pitch is **derived from the trajectory's own slope**, not authored, so the missile always points where it is going: steeply up off the launcher, level at the top, near-vertical on the way in.

**The airframe is built in code** (`MissileRun.BuildBody`), like the kamikaze drone — see docs/09-3D-MODELS.md. At map scale a missile is a bright sliver with a plume behind it, the plume is doing most of the work, and a detailed mesh would be invisible behind it.

### Sound

Three cues, and the middle one is the point:

| Cue | Where it plays | Why |
|---|---|---|
| `MissileMotor` | On the missile, looping, travels with it | The launch is heard leaving |
| `MissileIncoming` | At the **target**, 1.6 s before impact | You hear it arrive before you see it hit |
| `MissileLight/Medium/Heavy` | At the impact point | The report, by weight class |

The whistle plays at the ground point rather than riding the airframe down: it is the sound of something arriving where you are looking, and carrying it on the missile would put it in the wrong place until the last instant.

---

## 2. System register

Ten systems, five per side. Radius is the ring drawn on the map.

### NATO

| System | Role | Radius | Weight | Flight | Notes |
|---|---|---|---|---|---|
| **MIM-104 Patriot** (PAC-3 MSE) | Air defence | 2.6 km | Medium | 5.0 s | Area air and missile defence, hit-to-kill |
| **SAMP/T NG** — Aster 30 | Air defence | 3 km | Medium | 5.4 s | European area defence, longest NATO reach short of THAAD |
| **NASAMS** — AMRAAM-ER | Air defence | 1.3 km | Light | 3.6 s | Point defence — protects one position well |
| **THAAD** | Air defence | 5.2 km | Heavy | 7.0 s | Exo-atmospheric intercept; the widest umbrella in the game |
| **HIMARS** — ATACMS / PrSM | Surface strike | 420 m | Medium | 6.2 s | Precision deep fires; one launcher, one target |

### Enemy

| System | Role | Radius | Weight | Flight | Notes |
|---|---|---|---|---|---|
| **S-400 Triumf** | Air defence | 4.2 km | Heavy | 6.2 s | The reference threat umbrella |
| **9K720 Iskander-M** | Surface strike | 620 m | Heavy | 7.4 s | Theatre ballistic strike, manoeuvring |
| **HQ-9B** | Air defence | 3.4 km | Medium | 5.6 s | Long-range area defence |
| **DF-26 Dongfeng** | Surface strike | 900 m | Heavy | 8.6 s | Intermediate-range; the heaviest warhead available |
| **Bavar-373** | Air defence | 1.9 km | Light | 4.4 s | Shorter reach, mobile |

**On the numbers.** Ranges, footprints and warhead weights for these systems are published in wildly inconsistent forms and most of the interesting figures are not public at all. What is in the catalogue is deliberately *game* tuning: the systems are in the right order relative to each other, the footprints are legible at map scale, and nothing claims more precision than that. **Treat the rows as a balance table, not as a reference work.**

### Blast

`BlastDamage.Apply(lat, lon, lethalRadiusM, blastRadiusM, maxDamage)` — the same function artillery, naval gunfire, air and UAV strikes use. Inside the lethal radius a formation is destroyed; beyond it damage falls off with the square of distance to the blast radius, and the range is measured to the formation's **footprint** rather than to its map pin. The canonical description of the model is **docs/17-ARTILLERY.md § Damage**.

The heaviest surface-to-surface systems reach `maxDamage = 1.0` (DF-26) and 220 m lethal — one of these lands on a battalion and there is no battalion. That is the intended difference between calling for fire and calling for a missile.

---

## 3. Effects

Three weights rather than one effect per system. Ten launchers firing the same effect at three sizes would be honest; ten distinct effects would be ten effects nobody could tell apart. The weight is what a player is actually choosing between.

| `VfxId` | Fallback | Scale | Used by |
|---|---|---|---|
| `MissileLightBurst` | `ArtilleryAirBurst` | 220 m | NASAMS, Bavar-373 |
| `MissileMediumBurst` | `ArtilleryHeavyBlast` | 420 m | Patriot, SAMP/T, HIMARS, HQ-9B |
| `MissileHeavyBurst` | `ArtilleryHeavyBlast` | 760 m | THAAD, S-400, Iskander-M, DF-26 |
| `MissileLightSmoke` / `MediumSmoke` / `HeavySmoke` | `Smoke` | 240 / 440 / 820 m | matched to the burst |
| `MissileTrail` | `Smoke` | 90 m | Attached to the missile, killed on impact |

Impacts also leave `GroundFire` burning for 10–45 s by system. **The fire outlives the smoke on purpose:** the smoke says something just happened here, the fire says the ground is still burning.

`MissileTrail` has the lowest priority in the catalogue (30). A trail is the first thing that should be dropped when the concurrent budget is reached, because losing it costs a flourish rather than an event.

See docs/08-PARTICLE-SYSTEMS.md and docs/10-AUDIO.md for the full registers.

---

## 4. The panel

The MISSILE SYSTEMS row in the left rail opens a board **on the left**, docked
against the rail exactly where the sliding section panel docks, and standing in
its place: only one of the two is ever up.

It used to open on the right. That gave it the width it needed and cost it the
thing that mattered more — clicking a row on the left to open a board on the
right reads as a mis-click, and the right-hand edge belongs to the unit info
panel and the front-line panel, so opening a fire menu had to drop the player's
selection to make room. Docked left it takes nothing down but the section panel.

It is still **wider than a section** (`UiTheme.MissilePanelWidth`, 320 px against
274) because it is doing a different job. The sections hold controls you set and
forget: a weather condition, a tile style. A missile system is chosen by
*comparing* it against nine others on numbers that matter — what it covers,
whether that number is a warhead or an umbrella, which side fields it. That
comparison needs a designation, a description and a radius on the same row.

Consequences, all deliberate:

- `UnitPaletteUI.OpenSection` special-cases `Section.Missiles`: it **closes** the sliding panel and raises `MissileSystemsRequested` rather than showing an empty section. Two boards docked at the same x would be one on top of the other.
- The nav row cannot use the section panel's own open state to light up, so `SetMissilePanelOpen` drives it. A nav row that never highlights reads as a button that did nothing.
- The on-map zoom cluster rides whichever left-hand board is up: `MissilePanelUI.LeftInsetChanged` moves it out while the board is open, and `UnitPaletteUI.ReassertMapInset` puts it back when the board closes — the slide animation that normally drives the inset is not running at that moment, so nothing else would take the width back off.
- Selecting a formation **no longer** closes the board. The two are at opposite edges now, so a launcher can stay chosen while a unit is inspected. Arming a draw tool still closes it, because you cannot draw a boundary and aim a missile with the same click.
- **Closing the board stands the launcher down.** Leaving a system armed behind a panel that is no longer on screen would turn the next click on the map into a missile strike nobody asked for.
- Each launcher carries its **own allowance** under its radius — DF-26 **2**, THAAD **3**, Iskander **4**, up through NASAMS and HIMARS at **8**. Deliberately small throughout: a theatre missile is an event, not a fire mission. See docs/17-ARTILLERY.md § *The strike allowance*. A missile impact also leaves the standard aftermath: thirty scenario minutes of fire, then two hours of smoke (docs/08-PARTICLE-SYSTEMS.md §2.1).

---

## Rules

1. **Add a system by adding a row to `MissileCatalog`**, never by special-casing one in the panel. The buttons, the ring, the flight, the countdown banner and the blast all read from the row.
2. **Record it in §2 in the same commit**, with its role, radius and weight.
3. A new effect or sound means a row in `VfxCatalog` / `EffectSound` **and** an entry in docs/08-PARTICLE-SYSTEMS.md / docs/10-AUDIO.md — those are the registers for their own domains and this file does not replace them.
4. Radius figures are balance, not reference. If a number changes, change it because the game plays better, and do not dress it up as research.

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
