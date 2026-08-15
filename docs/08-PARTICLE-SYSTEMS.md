# Particle Systems

How Iron Meridian draws fire, smoke, explosions and dust — what exists, where each effect is used in the game, and how to add a new one.

> **Keep this file current.** Every new particle effect, every new call site, and every change to the catalogue must be reflected here in the same commit. See [Rules](#rules) at the bottom.

---

## 1. Architecture

All particle effects go through one system. Gameplay code never builds a `ParticleSystem` itself and never touches a prefab directly.

```
Assets/Scripts/Vfx/
  VfxCatalog.cs     VfxId enum + the catalogue row for each effect (prefab, scale, life, priority)
  VfxSystem.cs      the only entry point: resolve → anchor → scale → budget
  VfxInstance.cs    handle for a live effect; owns screen-size culling
  ProceduralVfx.cs  code-built fire/smoke/explosion/impact/dust/artillery fallbacks
  EffectPlacementTool.cs  arm an effect, click the terrain to place it
  CalledStrikeSystem.cs   shared arm/aim/countdown behind all three strike types
  ArtilleryCatalog.cs     the fourteen artillery natures — see docs/17-ARTILLERY.md
  ArtilleryStrikeSystem.cs  call for fire: countdown, then a five-round salvo
  NavalCatalog.cs         the nine naval guns — see docs/21-NAVAL-GUNFIRE.md
  NavalStrikeSystem.cs    naval gunfire: countdown, then a fast, wide mission
  AirStrikeCatalog.cs     the three strike airframes — see docs/18-AIR-STRIKES.md
  AirStrikeSystem.cs      tasked air strike; BomberRun.cs flies the aircraft
  UavCatalog.cs           the unmanned types — see docs/19-UAV-STRIKES.md
  UavStrikeSystem.cs      tasked UAV sortie; DroneRun.cs flies an attack,
                          ReconDroneRun.cs flies the reconnaissance orbit
  AirTarget.cs            the air picture: a track a flight puts itself on
  InterceptorRun.cs       the surface-to-air missile — see docs/24-AIR-DEFENCE.md
  DroneFall.cs            a drone coming down after being hit
  StrikeAftermath.cs      what a strike leaves: 30 min of fire, then 2 h of smoke
  StrikeBudget.cs         missions flown per delivery system (docs/17-ARTILLERY.md)
  RotorSpinner.cs         spins rotors/propellers on unrigged models
  TargetAreaMarker.cs     the 3D target-area volume a strike is placed with
Assets/Editor/
  VfxInstaller.cs   Tools > Iron Meridian > Install VFX Prefabs
```

Call sites use exactly three methods:

```csharp
// One-shot at a geodetic position, sitting on the terrain.
VfxSystem.Play(VfxId.Explosion, lat, lon, scaleMultiplier);

// Looping effect parented to a moving object; dies with its parent.
VfxInstance fire = VfxSystem.Attach(VfxId.FireMedium, unitTransform);
fire.Stop();

// Composite: detonation + a wreck that burns and smokes, then goes out.
VfxSystem.PlayWreck(lat, lon, severity01);

// One-shot in the AIR, at a height above the terrain rather than on it.
VfxSystem.PlayAloft(VfxId.AirInterceptBurst, lat, lon, heightAboveGround, scaleMultiplier);
```

`PlayAloft` exists because everything the game blew up used to be standing on the ground, so `Play` sampling the terrain and clamping to it was the only behaviour worth having. An air-defence interception is not: the warhead goes off where the drone is, and a burst that dropped four hundred metres to the ground would be reporting a crash rather than a kill. See docs/24-AIR-DEFENCE.md.

`VfxSystem` is a no-op when it has not been initialised, so effects are safe to call from any scene and non-game scenes cost nothing.

### Design rules the system enforces

| Rule | Why |
|---|---|
| **Positions are geodetic.** World-anchored effects get a `CesiumGlobeAnchor` at lat/lon + sampled terrain height. | Same as units and lines (`docs/07-ARCHITECTURE.md`). An effect placed in Unity world space drifts off the globe as the origin shifts. |
| **Effects are authored at ~1 world unit**, then scaled by `VfxDef.scaleMeters`. | Effect packs are built at human scale. A 2 m camp fire is sub-pixel when the strategic camera sits 20 km up. Fires here are 100–280 m across. |
| **`ParticleSystemScalingMode.Hierarchy` on every system.** | Makes the root transform scale drive particle *size and velocity* together, so one number converts author scale to map scale. |
| **Never hard-code particle values at the call site.** | The catalogue is the single source of truth; tuning happens in one file. |
| **Fallback always exists.** | The project ships no binary prefabs of its own; the game must look correct with zero asset dependencies. |

### Procedural fallback builders

`VfxFallback` picks which builder in `ProceduralVfx` stands in when no prefab is available.

| Fallback | Shape | Used by |
|---|---|---|
| `Explosion` | Flash, fireball, smoke column, ground dust ring | `Explosion` |
| `Impact` | Small one-shot puff | `ImpactBurst`, `WeaponFire` |
| `Fire` | Flame cone with a noise field, plus a smoke crown | all `Fire*`, `GroundFire`, `StrikeAftermathFire` |
| `Smoke` | Rising, churning column | `SmokePlume`, `SmokeScreen`, all artillery smoke, `StrikeAftermathSmoke`, `ReconMarker` (pale tint) |
| `Dust` | Flat ring across the ground plane | `Dust` |
| `ArtilleryAirBurst` | White-hot flash, **flat fast shrapnel disc**, small core | `ArtilleryLightBurst`, `UavWarheadBurst` |
| `ArtilleryDirtColumn` | **Narrow vertical soil column** under gravity, small flash, low skirt | `ArtilleryMortarBurst` |
| `ArtilleryHeavyBlast` | Fireball, **ground shock ring**, arcing debris, dust | `ArtilleryMediumBurst`, `ArtilleryHeavyBurst`, `AerialBombBurst` |

The three artillery builders exist because the events do not look alike from a map camera — what separates them is the *shape of the throw*, not the size. 155 mm and 203 mm deliberately share `ArtilleryHeavyBlast` and differ only by their rows' scale and lifetime, because that genuinely is the difference between them.

---

## 2. Effect catalogue

Defined in `VfxCatalog.cs`. `scaleMeters` is the on-map diameter; call sites pass a multiplier on top (typically 0.6–1.5, scaled by formation size).

| `VfxId` | Meaning | Scale | Life | Priority | Source |
|---|---|---|---|---|---|
| `Explosion` | Detonation — unit destroyed, ammo dump hit | 320 m | 2.6 s | 100 | procedural |
| `ImpactBurst` | Rounds landing on a unit under fire | 110 m | 1.1 s | 40 | procedural |
| `WeaponFire` | Firing signature at the shooter | 80 m | 0.7 s | 20 | procedural |
| `FireSmall` | Company/battalion burning | 100 m | loops | 60 | `VFX_Fire_01_Small_Smoke` |
| `FireMedium` | Brigade burning, struck vehicle park | 170 m | loops | 70 | `VFX_Fire_01_Medium_Smoke` |
| `FireLarge` | Division-scale conflagration, fuel/ammo fire | 280 m | loops | 80 | `VFX_Fire_01_Big_Smoke` |
| `GroundFire` | Burning ground — wreck site, torched terrain | 230 m | loops | 55 | `VFX_Fire_Floor_01_Smoke` |
| `SmokePlume` | Column of smoke off a wreck or fire | 300 m | loops | 50 | procedural |
| `SmokeScreen` | Deliberate obscuration (artillery / smoke generators) | 620 m | loops | 65 | procedural |
| `Dust` | Kicked up by movement or a deployment drop | 140 m | 1.5 s | 10 | procedural |
| `ArtilleryLightBurst` | 105 mm round landing — sharp, bright, little soil | 210 m | 2.2 s | 120 | procedural |
| `ArtilleryMortarBurst` | 120 mm mortar bomb landing — narrow column of earth | 190 m | 2.8 s | 120 | procedural |
| `ArtilleryMediumBurst` | 155 mm round landing — standard HE burst | 300 m | 3.0 s | 125 | procedural |
| `ArtilleryHeavyBurst` | 203 mm round landing — heavy fireball with debris | 430 m | 3.6 s | 130 | procedural |
| `ArtilleryLightSmoke` | Thin pale smoke off a 105 mm burst | 180 m | loops | 45 | procedural |
| `ArtilleryMortarSmoke` | Brown soil haze off a mortar bomb | 200 m | loops | 45 | procedural |
| `ArtilleryMediumSmoke` | Grey-black smoke off a 155 mm burst | 280 m | loops | 48 | procedural |
| `ArtilleryHeavySmoke` | Heavy oily column off a 203 mm burst | 380 m | loops | 52 | procedural |
| `AerialBombBurst` | Air-dropped weapon landing — the largest blast in the game | 560 m | 4.2 s | 140 | procedural |
| `AerialBombSmoke` | Black column off an air-dropped weapon | 460 m | loops | 56 | procedural |
| `UavWarheadBurst` | Loitering-munition warhead — the smallest strike blast | 150 m | 2.0 s | 135 | procedural |
| `UavWarheadSmoke` | Thin smoke off a drone warhead | 150 m | loops | 42 | procedural |
| `ShahedWarheadBurst` | Shahed-class warhead — a heavy one-way drone, not a shell | 300 m | 3.0 s | 150 | procedural |
| `ShahedWarheadSmoke` | Oily black column off a Shahed warhead | 320 m | loops | 46 | procedural |
| `ShahedWreckFire` | Burning ground left where a one-way drone went in | 180 m | loops | 60 | procedural |
| `MissileLightBurst` | Interceptor / short-range missile impact | 220 m | 2.4 s | 152 | procedural |
| `MissileMediumBurst` | Theatre missile impact — the standard heavy warhead | 420 m | 3.4 s | 158 | procedural |
| `MissileHeavyBurst` | IRBM impact — the largest detonation in the game | 760 m | 4.6 s | 165 | procedural |
| `MissileLightSmoke` | Smoke off a light missile impact | 240 m | loops | 44 | procedural |
| `MissileMediumSmoke` | Smoke off a medium missile impact | 440 m | loops | 48 | procedural |
| `MissileHeavySmoke` | Towering column off a heavy missile impact | 820 m | loops | 52 | procedural |
| `MissileTrail` | Exhaust plume trailing a missile in flight | 90 m | loops | 30 | procedural |
| `StrikeAftermathFire` | Ground burning where a strike landed — **30 scenario minutes** | 200 m | loops | 35 | `VFX_Fire_Floor_01_Smoke` |
| `StrikeAftermathSmoke` | Smoke over a burnt-out impact site — **2 scenario hours** | 260 m | loops | 32 | procedural |
| `ReconMarker` | Objective a reconnaissance drone is working — pale motes inside the search ring | 900 m | loops | 38 | procedural |
| `InterceptorLaunch` | Surface-to-air missile leaving the rail — flame and back-blast at the launcher | 130 m | 1.2 s | 118 | procedural |
| `InterceptorTrail` | Motor plume behind an interceptor in flight | 55 m | loops | 30 | procedural |
| `AirInterceptBurst` | The kill — a warhead against a drone, **in the air** | 120 m | 1.8 s | 145 | procedural |
| `DroneFallTrail` | Burning airframe coming down after being hit | 70 m | loops | 58 | procedural |
| `TaskAreaDefend` | Ground a formation is defending, holding or guarding | 260 m | loops | 22 | procedural |
| `TaskAreaAttack` | Ground a formation is attacking onto | 260 m | loops | 24 | procedural |
| `TaskAreaRecon` | Ground a formation is searching | 260 m | loops | 22 | procedural |
| `TaskAreaMove` | A move objective, a withdrawal line or a rally point | 260 m | loops | 22 | procedural |

The eight artillery rows are the four burst signatures and their smoke, shared across all fourteen natures in **docs/17-ARTILLERY.md**. They outrank a plain `Explosion` on priority because a called fire mission is the thing the player is watching and must never be what the concurrency budget throws away; their smoke ranks *below* the fires, because if the budget has to give, it should give up lingering smoke rather than a round landing.

Artillery smoke loops and is dispersed explicitly by `ArtilleryStrikeSystem` via `VfxSystem.StopAfter` — the same pattern `PlayWreck` uses to burn a wreck out. A finite `lifeSeconds` would cut the particles off mid-air rather than letting them thin out.

Each row also carries a **sound** (`VfxDef.sound`), played as 3D positional audio parented to the effect — so a burning unit takes its crackle with it. Fire effects loop `EffectSound.Fire`, `Explosion` fires a one-shot, the smoke effects loop `EffectSound.Smoke`, and `ImpactBurst` fires `EffectSound.Impact`. `WeaponFire` and `Dust` are deliberately silent: at one puff per firing formation they would turn a front line into a rattle. Clips come from `Resources/Audio/effects/` when present and are otherwise synthesised — full table in `docs/10-AUDIO.md` §2.3.

**Priority** decides who dies when the concurrent-effect budget is full: lowest priority is evicted first, oldest among equals. Dust is deliberately the cheapest thing on the map, an explosion the most protected.

`VfxCatalog.FireForScale(scale01)` picks Small / Medium / Large from a 0..1 formation size, so a burning squad and a burning army do not look the same.

### 2.1 Strike aftermath — the two rows that are measured in *scenario* time

`StrikeAftermathFire` and `StrikeAftermathSmoke` both loop, and neither is dispersed by `VfxSystem.StopAfter`. They are owned by **`StrikeAftermath`**, which counts down in **operational minutes** through `GameClock.ScenarioDelta`:

| Phase | Length | Reads as |
|---|---|---|
| Fire | 30 scenario minutes | *this is burning now* |
| Smoke | 120 scenario minutes | *this burned* |

Why not `lifeSeconds`, and why not `StopAfter`: both are real-time, and the operational clock runs anywhere from x1 to x300. Thirty real minutes of fire is either half an hour of the battle or four days of it depending on a setting that has nothing to do with ordnance. Thirty *scenario* minutes is thirty minutes either way — half an hour of watching at x1, thirty seconds at x60, and frozen while the battle is paused, because the world is stopped and a fire burning out over a motionless map would be the one thing disagreeing about that. In the editor, where the clock never runs, scenario time ticks at x1 so an effect placed while laying a scenario out still expires.

The bursts every strike plays last two to four seconds — right for a detonation, wrong for its consequence. Ten seconds after a battery had put five rounds into a position the map showed nothing at all. What ordnance leaves is the part that is visible for hours, and on an operational map that mark is real information: which positions have been worked over, and roughly how long ago.

**One site per mission, at the aim point** — not one per round or per weapon. A five-round salvo is a single event on the ground; five overlapping fires would cost five times as much for a worse picture. `StrikeAftermath.MaxSites` (20) caps the concurrent sites and retires the oldest first, so long-lived loops can never crowd out the bursts of the strikes still landing; both rows carry a deliberately low priority for the same reason.

---

## 3. Where each effect is used in the game

This is the complete list of call sites. **Add a row here whenever you add one.**

### Combat

| Case | Effect | Trigger | File |
|---|---|---|---|
| A unit shoots | `WeaponFire` at the attacker | Every resolved exchange, throttled to one per `GameConfig.VfxWeaponFireCooldownSeconds` (2.6 s) per unit | `CombatSystem.Exchange` → `UnitActor.NotifyFiring` |
| A unit takes damage | `ImpactBurst` at the defender | Every `ApplyDamage`, throttled to one per `GameConfig.VfxImpactCooldownSeconds` (1.8 s) per unit | `UnitActor.ApplyDamage` |
| A unit is badly mauled | `FireSmall`/`Medium`/`Large` **attached** to the unit | Strength drops to `GameConfig.VfxBurningStrength` (0.45) or below; cleared if it recovers above | `UnitActor.RefreshBurning` |
| A unit is destroyed | `PlayWreck`: `Explosion`, then `Fire*` + `SmokePlume` | On death; burns for 14–32 s scaled by echelon, then goes out | `UnitActor.Die` |
| A saved unit loads below strength | `Fire*` attached immediately | On spawn — damage is part of the map, not only something that happens live | `UnitActor.Build` |

Throttling matters: combat ticks once a second against **every** opposing unit in range, so an unthrottled effect per exchange would blanket the front line within seconds.

### Movement

| Case | Effect | Trigger | File |
|---|---|---|---|
| A formation on the march | `Dust` | One puff every 500 m of ground covered **along its planned route**, not along the straight line (distance-based, so puff spacing is speed-independent and survives the route's bends) | `UnitMover.Update` |

### Hand placement

| Case | Effect | Trigger | File |
|---|---|---|---|
| Player places a fire | `FireMedium` | **EFFECTS** panel → FIRE armed, then click the terrain | `EffectPlacementTool` |
| Player places an explosion | `PlayWreck` (explosion + burning wreck) | **EFFECTS** panel → EXPLOSION armed, then click | `EffectPlacementTool` |
| Player places smoke | `SmokePlume` | **EFFECTS** panel → SMOKE armed, then click | `EffectPlacementTool` |

The tool ground-checks every placement with `MapManager.RaycastGround`: Cesium streams terrain in, and a click over tiles that have not arrived has no ground to sit on. Those clicks are refused with a message rather than burying the effect inside the globe. A reticle tracks the real ground point while armed, so what you see is where it lands; the tool stays armed so a line of fires can be laid in one go, and right-click or Esc puts it away. Works in both editor and battle mode.

### Artillery strikes

| Case | Effect | Trigger | File |
|---|---|---|---|
| Round lands (105 mm) | `ArtilleryLightBurst` + `ArtilleryLightSmoke` | ×5 per mission, 0.30 s apart | `ArtilleryStrikeSystem.RunSalvo` |
| Round lands (120 mm) | `ArtilleryMortarBurst` + `ArtilleryMortarSmoke` | ×5 per mission, 0.42 s apart | `ArtilleryStrikeSystem.RunSalvo` |
| Round lands (155 mm) | `ArtilleryMediumBurst` + `ArtilleryMediumSmoke` | ×5 per mission, 0.55 s apart | `ArtilleryStrikeSystem.RunSalvo` |
| Round lands (203 mm) | `ArtilleryHeavyBurst` + `ArtilleryHeavySmoke` | ×5 per mission, 0.85 s apart | `ArtilleryStrikeSystem.RunSalvo` |

**ARTILLERY STRIKE** panel → pick a nature → click the terrain → a 10 s countdown runs in the HUD → five rounds land scattered across the target area. Ground-checked exactly like hand placement above, and a refused click leaves the tube armed. Full detail in **docs/17-ARTILLERY.md**.

### Naval gunfire

| Case | Effect | Trigger | File |
|---|---|---|---|
| Round lands (57 / 76 mm) | `ArtilleryLightBurst` + `ArtilleryLightSmoke` | ×10–12 per mission, 0.16–0.22 s apart | `NavalStrikeSystem.RunStrike` |
| Round lands (100 / 127 mm) | `ArtilleryMediumBurst` + `ArtilleryMediumSmoke` | ×8–9 per mission, 0.30–0.38 s apart | `NavalStrikeSystem.RunStrike` |
| Round lands (130 / 155 mm) | `ArtilleryHeavyBurst` + `ArtilleryHeavySmoke` | ×6–10 per mission, 0.30–0.62 s apart | `NavalStrikeSystem.RunStrike` |

**NAVY STRIKE** panel → NATO NAVY / ENEMY NAVY → pick a gun → click the terrain → a 10 s countdown → the mission lands. It **deliberately reuses the calibre-matched artillery effects**: a 127 mm shell landing is a 127 mm shell landing whoever fired it, and nine near-identical particle effects would be nine more rows to keep in step for a difference nobody could see. What makes it read as naval is the mission — more rounds, much faster, over a wider beaten zone. Full detail in **docs/21-NAVAL-GUNFIRE.md**.

### Air strikes

| Case | Effect | Trigger | File |
|---|---|---|---|
| Weapon lands | `AerialBombBurst` + `AerialBombSmoke` | ×5 per pass, released 0.34 s apart along the aircraft's track | `BomberRun.ReleaseOne` → `AirStrikeSystem.Detonate` |

**AIR STRIKE** panel → pick an airframe (B-2, strike fighter or attack helicopter) → click the terrain → a 10 s countdown → the aircraft runs in and walks a stick of five through the target. The blasts follow the aeroplane rather than landing in a heap, because a released weapon keeps the aircraft's forward speed as it falls. If the aircraft model is not installed the weapons still land, on the same attack heading, with no aeroplane. Full detail in **docs/18-AIR-STRIKES.md**.

### UAV strikes

| Case | Effect | Trigger | File |
|---|---|---|---|
| Kamikaze warhead | `UavWarheadBurst` + `UavWarheadSmoke` | Once, where the drone reaches the ground | `DroneRun.Update` → `UavStrikeSystem.Detonate` |
| Shahed warhead | `ShahedWarheadBurst` + `ShahedWarheadSmoke` + `ShahedWreckFire` | Same, for the heavier one-way type | `UavStrikeSystem.Detonate` |
| Reconnaissance drone's objective | `ReconMarker` under the search ring | Played when the sortie is flown, stopped when the drone has gone home | `UavStrikeSystem.RunReconnaissance` |

### Task areas

| Case | Effect | Trigger | File |
|---|---|---|---|
| Defend / hold / guard placed | `TaskAreaDefend` at the objective | Once, when the order is given; stopped when it is cancelled | `TaskAreaSystem.Show` |
| Attack onto ground | `TaskAreaAttack` | as above | `GameController.OrderAreaAttack` |
| Recon area placed | `TaskAreaRecon` | as above | `GameController.OrderRecon` |
| Move / withdraw / retreat placed | `TaskAreaMove` | as above | `ManoeuvreOrderSystem.Order` |

**Attached to the area, not played at it.** A one-shot puff says something
happened; a task area is a standing state, so the motes loop for as long as the
order does. They are the lowest-priority effects in the catalogue and are
silent: an order stands until it is cancelled, so these are the longest-lived
things on the map, and if the budget has to give it must give up a marker rather
than a round landing. Full detail in **docs/15-COMBAT-ORDERS.md** §1a.

### Air defence

| Case | Effect | Trigger | File |
|---|---|---|---|
| Interceptor leaves the rail | `InterceptorLaunch` at the launcher | Two seconds after a battery acquires a drone | `InterceptorRun.Launch` |
| Interceptor in flight | `InterceptorTrail` attached to the missile | For the whole flight; killed on intercept | `InterceptorRun.Launch` / `Update` |
| Drone shot down | `AirInterceptBurst`, **aloft**, at the drone's own altitude | Once, on intercept | `AirDefenceSystem.Fire` → `InterceptorRun.Intercept` |
| Wreck coming down | `DroneFallTrail` attached to the falling airframe | From the hit to the ground | `DroneFall.Begin` |
| Wreck lands | `UavWarheadBurst` at 0.6 scale + `StrikeAftermath` at 0.5 | Once, where it hits the terrain | `DroneFall.Impact` |

Everything here is deliberately **small**: an interception is a precise event a long way up, competing on screen with the strikes landing below it. The wreck's burst is a *fraction* of the warhead the drone was carrying, because a loitering munition shot down short of its target has not delivered its attack. Full detail in **docs/24-AIR-DEFENCE.md**.

**UAV STRIKES** panel → pick a type → click the terrain → a 10 s countdown → the drone launches and flies in. The kamikaze drone is deliberately the smallest blast of any strike here; the Shahed is closer to a 155 mm shell and leaves the ground burning. The **reconnaissance drone** has no warhead at all: it orbits the point for five operational minutes with `ReconMarker` on the ground under it, lifts the fog off a 10 km circle, and flies home. Full detail in **docs/19-UAV-STRIKES.md**.

### Strike aftermath

| Case | Effect | Trigger | File |
|---|---|---|---|
| Any artillery fire mission completes | `StrikeAftermathFire` → `StrikeAftermathSmoke` at the aim point | Once per mission, when the salvo ends | `ArtilleryStrikeSystem.RunStrike` |
| Any naval gunfire mission completes | as above, at the aim point | Once per mission | `NavalStrikeSystem.RunStrike` |
| Any air strike completes | as above, at the target area | Once per pass | `AirStrikeSystem.RunStrike` |
| Any UAV attack completes | as above, at the objective | Once per sortie; the recon type leaves nothing | `UavStrikeSystem.RunAttack` |
| Any missile impacts | as above, at the aim point | Once per mission | `MissileStrikeSystem.RunStrike` |

Thirty scenario minutes of fire, then two scenario hours of smoke — see §2.1 for why those figures are on the operational clock and not on a real one.

### Missile systems

| Case | Effect | Trigger | File |
|---|---|---|---|
| Missile impact | `Missile{Light,Medium,Heavy}Burst` + matching smoke + `GroundFire` | Once, where the missile reaches the ground | `MissileRun.Update` → `MissileStrikeSystem.Detonate` |
| Missile in flight | `MissileTrail` | Attached to the missile at launch, killed on impact | `MissileRun.Launch` |

**MISSILE SYSTEMS** rail row → the right-hand board → pick a system → click the terrain → a 10 s countdown → a missile comes over the horizon on a ballistic arc and the warhead lands inside the ring. Three weights rather than one effect per system: ten distinct effects would be ten effects nobody could tell apart. Full detail in **docs/20-MISSILE-SYSTEMS.md**.

`MissileTrail` is **attached** rather than played at a point — a trail spawned at the launch site would sit there while the missile left it behind — and carries the lowest priority in the catalogue (30), because when the concurrency budget has to give, losing a plume costs a flourish rather than an event.

### Deployment

| Case | Effect | Trigger | File |
|---|---|---|---|
| Unit dragged from the palette onto the map | 3D ring wall + ground disc + marker column + dust and embers | `GameController.OnPaletteDrop` | `DeployEffect` |
| Units pasted (Ctrl+V) | as above | `GameController.PasteClipboard` | `DeployEffect` |

`DeployEffect` predates `VfxSystem` and owns its own meshes, which the catalogue has no equivalent for. It is a migration candidate, not a second effects system — do not add new effects to it.

It has four layers because a flat expanding ring disappears the moment the camera tilts, which is most of the time in 3D: a **ring wall** (a cylinder of light standing on the ground, expanding and flattening as it spreads — the part that reads at any angle), a **ground disc** under it (what reads from directly overhead), a **marker column** at the exact drop point (so the eye is told *where*, not merely *near here*), and **particles** — dust thrown outward along the ground plus team-coloured embers rising through the column.

`DeployEffect.Play` **refuses and returns false** if the terrain at the drop point cannot be sampled. The palette has already refused the drop in that case, and an effect floating at a guessed height would be the only thing on screen suggesting it worked.

### Movement

| Case | Effect | Trigger | File |
|---|---|---|---|
| Motes off the head of a marching unit's trail | Team-coloured motes, world-simulated so they stay where they were shed | Continuous while the order stands | `MoveTrail` |

### Ordered attacks

Offensive tasks add three call sites on top of the automatic exchange above. See
[15-COMBAT-ORDERS.md](15-COMBAT-ORDERS.md) for what each task does.

| Case | Effect | Trigger | File |
|---|---|---|---|
| A volley takes ≥1.8% strength off the target | `Explosion` at the target | Throttled to one per 2.4 s **per order**, so a long engagement marks its heavy blows instead of carpeting the map | `AttackOrderSystem.Engage` |
| An **assault** goes in | `GroundFire` on the objective, burning for 20 s | Once, when the engagement opens. The ground an assault crosses stays lit whether or not the order survives | `AttackOrderSystem.BeginEngagement` |
| **Suppressive fire** opens | `SmokeScreen` on the target | Once, when the engagement opens; stopped when the order ends. This is the obscuration case the effect was defined for | `AttackOrderSystem.BeginEngagement` |

### Blast arrival — the shockwave and the debris

Two rows added with the strike-damage rework, played by `StrikeImpact.Arrive` at the aim point of **every** called strike:

| Effect | What it is | Scaled to |
|---|---|---|
| `BlastShockwave` | A flat ring of particles racing outward along the ground, decelerating as it goes. Nothing rises — a shockwave that rose would read as another puff of smoke, and its one job is to state a **radius** | The strike's own target area, exactly |
| `BlastDebris` | Soil and fragments thrown out on ballistic arcs, stretched billboards because a tumbling fragment seen from 2 km up is a streak | 55 % of the ring — debris comes from the impact, not from the whole beaten zone |

Both carry `scaleMeters = 100` as a **reference** size rather than a real one: the call site passes `ringRadius / 100` as the multiplier, which is what puts the ring exactly on the circle the player drew. `BlastShockwave` has the highest priority in the catalogue (170) — it is the frame that answers the only question the player asked, and losing it to the concurrency budget would be losing the answer.

Light warheads skip the debris: `StrikeImpact.Arrive(..., heavy: false)` for a quadcopter grenade and for artillery under 120 mm. A grenade that fountained soil like a 203 mm shell would be lying about its size.

### Defined but not yet triggered

Nothing. Every catalogue row now has at least one call site.

### The effects lab

**DEVELOPMENT → PARTICLES** (`EffectsListUI` + `VfxPreview`) plays every row of §2 in 3D, on a ground plane, looping, with the sound it carries.

It exists because the catalogue is the register of what the game can draw, and the only other way to see an entry was to make the event that triggers it happen on the map — which for half these rows means calling a fire mission and watching a 300 m burst from 20 km up.

Two things about it are deliberate:

- **Effects are shown at their authored size** — roughly one world unit — not at `scaleMeters`. A 760 m missile burst rendered at 760 units is a wall of orange; the lab is for seeing an effect's *shape*, and the metre figure is written beside it in words.
- **It resolves prefabs and audio the way the game does**, through `VfxSystem.LoadPrefab` and `EffectAudio`, and reports which one it got. A row that falls back to a procedural stand-in in the lab is one that falls back on the map — which given §4 is the single most useful thing the screen says.

The preview rig is the same device `ModelPreview` uses: a parked container far below the scene, its own camera, a `RenderTexture` on a `RawImage`. Sound is played 2D from the screen rather than through `EffectAudio.PlayAt`, whose kilometre rolloff would be silent at that distance.

---

## 4. Render pipeline — read this before using the authored pack

**The imported pack ([Free Fire VFX URP](https://assetstore.unity.com/packages/p/free-fire-vfx-urp-266226), `Assets/Vefects/`) is URP-only, and this project runs the built-in render pipeline.**

Its shaders declare `Tags { "RenderPipeline"="UniversalPipeline" }`, include `com.unity.render-pipelines.universal/...` HLSL, and set `Fallback Off`. Under the built-in pipeline they have no matching sub-shader, so the particles draw magenta.

`VfxSystem` detects this: it checks `Shader.isSupported` on every material of a loaded prefab and, if any fails, logs one warning and falls back to `ProceduralVfx` for that effect. `VfxInstaller` reports the same at install time. **So the game looks correct today — it just isn't using the pack yet.**

Three ways forward:

1. **Stay procedural** (current state). No dependency, no pipeline change; the look is stylised rather than photoreal.
2. **Move the project to URP.** Cesium for Unity supports URP. This is a real migration: every runtime material goes through `RuntimeMaterials` (`Sprites/Default` etc.), which would need URP equivalents, and lighting/post would need re-tuning. Not a change to make casually — see `Assets/Scripts/Core/RuntimeMaterials.cs`.
3. **Re-target the pack's materials** to built-in shaders (`Particles/Standard Unlit`). Cheapest path to using the pack's textures and particle timing, but the Amplify-authored distortion, erosion and heat-haze effects are lost.

Also note the pack has **no explosion, standalone smoke or dust prefab** — it is a fire pack. Those catalogue rows are procedural regardless of pipeline. Its audio (`SFX_FireBig/Medium/Small_L.wav`) is not wired up; fire audio via `AudioManager` is an open item.

### Installing authored prefabs

Scenes and prefabs are generated in this project, so there is no serialised field anywhere to reference an asset — `VfxSystem` resolves everything through `Resources.Load`.

Run **Tools → Iron Meridian → Install VFX Prefabs**. It copies the prefabs named in the catalogue into `Assets/Resources/VFX/`. `AssetDatabase.CopyAsset` preserves GUID references, so each copy is a single file that still points at the pack's own materials and textures — the pack is not duplicated. Effects with no installed prefab silently use the procedural fallback, so running this is optional.

---

## 5. Performance

The strategic camera can show a whole front, so effect count is bounded rather than trusted.

| Guard | Value | Where |
|---|---|---|
| Concurrent effect cap, with priority eviction | `GameConfig.VfxMaxConcurrent` = 48 | `VfxSystem.MakeRoom` |
| Looping effects stop emitting when sub-pixel | `GameConfig.VfxMinApparentSize` = 0.005 (0.5 % of screen height), re-checked 4×/s | `VfxInstance.Update` |
| Per-unit throttles on impact and firing effects | 1.8 s / 2.6 s | `GameConfig` |
| Wreck fires burn out rather than persisting | 14–32 s | `GameConfig.VfxWreck*` |
| Concurrent strike-aftermath sites, oldest retired first | 20 | `StrikeAftermath.MaxSites` |
| Missions per delivery system, per scenario | 2–24, per catalogue row | `<Def>.missions`, counted by `StrikeBudget` |
| One shared material for all procedural effects | — | `ProceduralVfx.PuffMaterial` |

Effects are **not pooled** — each spawn allocates a `GameObject`. The cap and throttles keep the churn low enough that this has not mattered; if profiling says otherwise, pooling belongs in `VfxSystem.Populate`.

World-anchored effects (wrecks, aftermath sites) deliberately outlive their unit, so `GameController.LoadMap` and `ResetEditor` call `VfxSystem.StopAll()` on reload — and `StrikeAftermath.ClearAll()` with it, so the bookkeeping does not outlive the effects it is tracking and try to swap a dead fire for smoke.

---

## 6. Adding a new particle effect

1. **Add a `VfxId`** in `VfxCatalog.cs`, named for what it *means* in the game, not for the asset that draws it.
2. **Add its catalogue row**: prefab path (or `null`), fallback kind, `scaleMeters`, `lifeSeconds` (`0` = loops until stopped), tint, priority.
3. **Add a procedural fallback** in `ProceduralVfx` if none of the existing kinds fits. Author at ~1 world unit; `VfxSystem` handles the scale.
4. **Add tuning constants** to `GameConfig` if the effect needs thresholds or cooldowns — never magic numbers at the call site.
5. **Call it** via `VfxSystem.Play` / `Attach` / a composite helper. Throttle anything that can fire per combat tick.
6. **If it uses an authored prefab**, put the prefab name in the catalogue and run **Tools → Iron Meridian → Install VFX Prefabs**.
7. **Update this file** — the catalogue table in §2 *and* the usage table in §3.
8. **Check it in the lab** — DEVELOPMENT → PARTICLES. The new row appears there automatically; confirm it plays, that SOURCE says what you expect, and that its sound is the one you meant.

---

## Rules

1. **This document is the register of every particle effect in the game.** Adding, removing or repurposing an effect, or adding a new call site, is not done until §2 and §3 here are updated in the same commit.
2. Gameplay code goes through `VfxSystem`. No `new GameObject().AddComponent<ParticleSystem>()` outside `Assets/Scripts/Vfx/`.
3. Effect tuning lives in `VfxCatalog` and `GameConfig`, never at the call site.
4. Every authored effect needs a procedural fallback — the game must run with the asset packs removed.
5. Anything that can be triggered by a combat tick must be throttled.

## Related

`docs/07-ARCHITECTURE.md` (script map) · `docs/03-GAMEPLAY.md` (combat model) · `docs/02-CESIUM.md` (georeferencing)
