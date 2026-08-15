# Air Defence

Ground-based air defence: how a deployed anti-aircraft formation finds a drone flying over it, shoots it down, and what is left afterwards.

> **Keep this file current.** Any change to what counts as a launcher, to the engagement sequence, or to the air picture belongs here in the same commit. See [Rules](#rules).

---

## 1. What it does

Deploy an anti-aircraft formation. From that moment it defends the airspace around itself, with no orders and no interface of its own:

1. **Acquisition** — four times a second it measures every drone in the air against its own envelope: slant range inside both its weapon range *and* its view range, and clear line of sight through the terrain.
2. **The contact is shown** — a ring goes on the ground under the drone, captioned with what it is, and the HUD names the battery that has it.
3. **Two seconds later the missile leaves the rail** — an interceptor climbs off the launcher, arcs over and chases the track.
4. **It hits.** Always.
5. **The drone comes down** — tumbling, trailing fire, landing on the terrain and burning where it lands. Its mission does not happen: no warhead on the objective, no strike report.

**Deploying the launcher is the order.** Air defence is the one thing on the map that genuinely does not wait to be told — a battery's whole purpose is to engage what enters its envelope, in seconds, without reference to anything a commander is doing at the time. Asking the player to click on an incoming drone would be modelling a decision nobody makes.

## 1a. Which side shoots which drones

**A launcher engages the *other* side's aircraft.** Every drone in the game is flown by a called UAV sortie, and called sorties are the player's, so in practice:

| Deploy | Task a UAV sortie | Result |
|---|---|---|
| **Red (hostile)** anti-aircraft unit inside the sortie's flight path | any | The sortie is engaged and shot down |
| **Blue (friendly)** anti-aircraft unit | any | Nothing — a battery does not shoot its own drones |

So to see the system work in the map editor, deploy the launcher on the **ENEMY** side and fly a sortie past it. A blue battery is not broken; it is waiting for hostile air that the game does not yet generate — Red never tasks sorties of its own (see docs/07-ARCHITECTURE.md, *Known simplifications*).

## 2. What counts as a launcher

`AirDefenceSystem.IsAirDefence(def)` — a unit type qualifies when **all three** hold:

| Test | Why |
|---|---|
| `HoldsGround` | Ground formations only. Aircraft and ships carry anti-air ratings too, and air-to-air is a different engagement this system does not model. |
| `canCounterUas` | The catalogue's own statement that the type is meant to fight unmanned aircraft. |
| `antiAir >= 50` | And has the reach to do something about it. |

**Both flags are required, and that is the point.** `canCounterUas` alone would arm the electronic-warfare vans — they are filed as counter-UAS because jamming a drone is exactly what they do, and a missile leaving the roof of a jammer would be the model saying something false about *how* the drone was defeated. A high `antiAir` alone would arm the air-defence radar, which sees everything and shoots nothing.

The types that qualify today, all `AntiAircraft` branch (see docs/04-UNITS.md):

| Unit | `antiAir` | Weapon range | View range | **Effective envelope** |
|---|---|---|---|---|
| `spaag` | 78 | 4 km | 4 km | 4 km |
| `manpads` | 55 | 6 km | 5 km | 5 km |
| `air_defence` | 80 | 6 km | 5 km | 5 km |
| `counter_uas` | 70 | 8 km | 6 km | 6 km |
| `shorad` | 75 | 12 km | 8 km | 8 km |
| `sam` | 95 | 40 km | 8 km | 8 km |
| `mrad` | 92 | 50 km | 10 km | 10 km |
| `lrad` | 98 | 120 km | 12 km | 12 km |
| `missile_defence` | 96 | 150 km | 14 km | 14 km |

The envelope is the **smaller** of the two (§3.1), so today every launcher is limited by what it can see rather than by what it can reach — a long-range battery is a 12 km system against drones until it is given something that can find them further out. That is the honest consequence of not modelling an air-defence *network*: `ad_radar` exists in the catalogue precisely to extend the air picture for the shooters around it, and nothing yet reads it. Widening the envelope is a matter of raising `viewRangeKm` on the launchers in **Development → Units List**, or of building the sensor-sharing that `ad_radar` is waiting for.

Deliberately **not** launchers: `ad_radar` (`antiAir` 25 — it sees, it does not shoot), `electronic_warfare` and `ew_unit` (jammers).

Because the test is read off the catalogue, tuning a type in **Development → Units List** changes who defends: raise `antiAir` past 50 on a counter-UAS type and it starts firing missiles.

## 3. The engagement, in detail

### 3.1 Slant range, not map range

Range is measured along the **diagonal** to the drone, not across the ground to the point beneath it:

```
slant = √(groundKm² + (altitudeM / 1000)²)
```

A drone four hundred metres up and two kilometres away is 2.04 km from the launcher. A short-range system whose envelope ends at two would otherwise be handed a shot it does not have — and at MANPADS ranges the correction is a large fraction of the envelope.

The track must be inside **both** `weaponRangeKm` and `viewRangeKm`, so the effective envelope is the smaller of the two. A battery that can reach further than it can see does not get a free shot at something it has not found — see the last column of the table in §2 for what that means for each launcher today.

### 3.2 Line of sight

A raycast along the sight line, from the launcher's own height plus 20 m (a radar and a launch rail are above the ground the vehicle stands on) to the drone. **Only a `Cesium3DTileset` collider blocks it.** The scene is full of colliders that are not ground — unit icons carry one because they are click targets, and every control measure carries an invisible ribbon so the line can be picked — so a battery standing on a phase line would otherwise be unable to see anything at all. Testing positively for a tileset is the only way to be sure what was hit was the world.

**A raycast that hits nothing reads as clear.** Cesium streams its terrain, so the ground genuinely may not be loaded yet. That failure direction is deliberate: an engagement that should not have happened is visible and arguable, whereas a battery that silently never fires because the terrain under it has not finished downloading is neither.

### 3.3 The two seconds

`AirDefenceSystem.ReactionSeconds`. It is the one part of the engagement the player can act inside, and it is what makes an air-defence envelope read as a **hazard** rather than as an instant-death zone: a drone crossing a corner of the envelope at speed can get out the other side.

If the launcher is destroyed inside its own count, the commitment is **released** rather than dropped — another battery in range can pick the track up. A drone that flew home because the launcher that had it was killed mid-count would be an engagement quietly evaporating.

### 3.4 Why the missile cannot miss

A probabilistic interception would be more realistic and much worse to play against. The outcome of sending a drone into a defended sector would be unknowable, so the only rational play would be to send drones and see — which is not a decision.

Making the envelope absolute makes it **information**. A defended sector is a place your drones do not come back from; the counter is to find the launcher and kill it first; and both of those are decisions. The two-second reaction and the finite envelope are where the play is.

### 3.5 One missile per track, one track per launcher

A track is marked `Engaged` the moment a battery commits to it, so six launchers do not empty themselves into the same piece of sky. A battery already firing is not offered another track until its own is resolved. Each engagement costs the launcher **2 rounds** of its ammunition, and a battery at zero rounds is a spectator — the round count is what stops air defence being an infinite envelope.

## 4. The air picture

`Vfx/AirTarget.cs` is a component a flight attaches to itself. It carries the side, a label, the live geodetic position, the engagement flag and a `ShotDown` callback; `AirTarget.All` is every live track.

**Why a component and not a list of flights.** `DroneRun` and `ReconDroneRun` are unrelated classes with unrelated trajectories, and the air picture must not care which is which — a radar sees a track, not a class name. The position is *pushed* by the flight (which already computes it every frame to place its anchor) rather than read back off the `CesiumGlobeAnchor`, which would be a round trip through ECEF for a number the caller was holding a moment earlier.

**Adding something else that can be shot at** — a cruise missile, a helicopter — is one `AirTarget.Attach` call in its flight plus a `ShotDown` handler. Nothing in `AirDefenceSystem` needs to change. Today only the two drone flights are on the picture; called missile strikes (`MissileRun`) deliberately are not.

## 5. What the drone does when it is hit

`Vfx/DroneFall.cs`. The stricken flight hands over its **model** and destroys itself: an aircraft that is falling is not flying a mission any more, and teaching every flight a second mode it would then have to guard every line of its own `Update` against is worse than having no mission at all.

- The airframe's idle animation is **stopped** — a wreck with its propeller turning and its sensor turret still quartering the ground is the single detail that would give the whole thing away.
- It accelerates at 9.81 m/s² to a terminal 62 m/s, drifts on along its old heading at 22 m/s, and tumbles about all three axes at a random rate per wreck.
- `DroneFallTrail` burns on it the whole way down.
- On the ground: a small `UavWarheadBurst` at 0.6 scale and a `StrikeAftermath` site at half scale. **Deliberately a fraction of the warhead it was carrying** — a loitering munition shot down short of its target has not delivered its attack, and it does no damage for the same reason.

**The physics is authored, not simulated.** A rigid body dropped from four hundred metres onto Cesium terrain would need a collider on tiles that stream in and out underneath it, and would land whenever the physics happened to resolve. Integrating a real acceleration against the ground height sampled at the impact point lands it exactly on the terrain.

### What the sortie reports

| Sortie | Not intercepted | Intercepted |
|---|---|---|
| Attack | Warhead, damage, aftermath on the objective, *"Strike complete"* | *"…was shot down short of the objective — target untouched."* Nothing at all happens on the objective. |
| Reconnaissance | Fog lifted for its time on station, *"off station — the drone is returning"* | Sensor removed at once; *"…was shot down over the objective. What it had already seen stands."* |

Explored ground and last-known contacts the drone had already made are **kept**. Taking them back would be un-learning something the player was shown. Only the drone is lost — and the strike allowance it cost (`StrikeBudget`) is not refunded.

## 6. The interceptor

`Vfx/InterceptorRun.cs` — the fourth flight alongside `BomberRun`, `DroneRun` and `MissileRun`, and the only one that **chases**. The other three fly to a point decided when they launched; this one is aimed at something still flying.

There is no precomputed trajectory. Every frame it interpolates from the launch point to where the target *is now*, which produces the lead and the final tightening for free and cannot miss. Over the top of that straight line sits a loft of 18 % of the distance, peaking at the halfway point and gone by the intercept, so the shot arcs off the rail instead of ruling a line between two points. Nose attitude is derived from the path actually travelled, not authored.

Speed is 900 m/s, clamped to a 0.9–7 s flight so a point-blank shot is still watchable and a 120 km one does not look lost.

Drone altitude and missile altitude are both quoted *above the ground beneath them*, and over real terrain those are two different datums — several hundred metres apart in a valley. The flight interpolates between the launcher's ground height and the track's, which is what stops the missile passing under a drone holding station over a ridge.

**The airframe is built in code**, exactly as `MissileRun` builds its own: a body, a nose and four tail fins, unlit and bright so it stays visible against dark terrain from altitude. There is no `UnitModelLibrary` entry because there is no prefab and no pack — see docs/09-3D-MODELS.md.

## 7. Effects and sound

Four catalogue rows, all procedural — see docs/08-PARTICLE-SYSTEMS.md §2 for the full table.

| `VfxId` | Where | Scale | Life |
|---|---|---|---|
| `InterceptorLaunch` | At the launcher, as the missile leaves | 130 m | 1.2 s |
| `InterceptorTrail` | Attached to the missile, killed on intercept | 55 m | loops |
| `AirInterceptBurst` | The kill, **in the air** at the drone's altitude | 120 m | 1.8 s |
| `DroneFallTrail` | Attached to the falling wreck, killed on landing | 70 m | loops |

Everything here is deliberately **small**. An interception is a precise event a long way up, competing on screen with the strikes landing on the ground below it; a burst sized like a 155 mm round going off at four hundred metres would read as the sky exploding and would say the wrong thing about how much ordnance was involved.

`AirInterceptBurst` is played through `VfxSystem.PlayAloft`, which is new: everything the game blew up until now was standing on the ground, so `Play` sampling the terrain and clamping to it was the only behaviour worth having. A burst that dropped four hundred metres to the ground would be reporting a crash rather than a kill.

**No new sounds.** The launch and the burst reuse `EffectSound.MissileLight`, the trail is silent, the motor rides `EffectSound.MissileMotor` on the missile itself and the wreck's fire uses `EffectSound.Fire` — all already in docs/10-AUDIO.md.

## 8. Trying it in the map editor

1. **Development → Map Editor.**
2. Units panel → **ENEMY** side → open the **ANTI-AIRCRAFT** section → drag a **SAM** onto the map.
3. Left rail → **UAV STRIKES** → pick an attack type → click the ground **within about 8 km of the launcher** (the SAM's effective envelope — see §2). A drone only exists for its run-in — 1.8 to 9 km depending on the type (`UavDef.approachKm`) — so the *objective*, not just the flight path, has to be inside the envelope for a short-legged type.
4. Watch: the drone launches, a contact ring appears under it with its name, the HUD says which battery has it, two seconds later a missile leaves the launcher, and the drone comes down burning.

To see it *fail*, put the launcher behind a ridge from the objective — the line-of-sight test will not clear.

## Rules

1. **Update this file in the same change** as anything touching `AirDefenceSystem`, `AirTarget`, `InterceptorRun` or `DroneFall`.
2. **New effects go in `VfxCatalog` and in docs/08-PARTICLE-SYSTEMS.md**, never built at the call site (golden rule 11).
3. **Anything new that can be shot at attaches an `AirTarget`** rather than being special-cased in the defence system.
4. **The interceptor must not become probabilistic** without the argument in §3.4 being answered.

## Related

docs/19-UAV-STRIKES.md (the sorties this fights) · docs/04-UNITS.md (`antiAir`, `canCounterUas`) · docs/08-PARTICLE-SYSTEMS.md (the four effects) · docs/09-3D-MODELS.md (why the missile is built in code) · docs/16-FOG-OF-WAR.md (what a shot-down recon drone leaves behind) · docs/20-MISSILE-SYSTEMS.md (the *other* missiles, which are called rather than automatic)
