# UAV Strikes

Unmanned strikes in the map editor: pick a type, place an objective, and ten seconds later the drone launches, flies in, dives on the point and is expended on it.

> **Keep this file current.** Every new UAV type, effect, sound or call site must be recorded here in the same change. See [Rules](#rules) at the bottom.

---

## 1. Type register

Rows live in `Assets/Scripts/Vfx/UavCatalog.cs`.

| Type | Target radius | Model | Burst | Smoke | Fire | Sounds |
|---|---|---|---|---|---|---|
| **Kamikaze drone** | 90 m | `kamikaze_drone` (built in code) | `UavWarheadBurst` | `UavWarheadSmoke` (12 s) | — | `UavWarhead`, `DroneBuzz` |
| **Shahed drone** | 160 m | `shahed_drone` | `ShahedWarheadBurst` | `ShahedWarheadSmoke` (22 s) | `ShahedWreckFire` (40 s) | `ShahedWarhead`, `ShahedEngine` |

### Flight profiles

| Setting | Kamikaze | Shahed | Meaning |
|---|---|---|---|
| `spanMeters` | 90 | 130 | Drawn size — exaggerated, as every aircraft here is |
| `cruiseAltitudeMeters` | 420 | 520 | Height of the level run-in |
| `approachKm` | 1.8 | 6.0 | Ground covered from launch point to objective |
| `cruiseSeconds` / `diveSeconds` | 5.5 / 2.2 | 8.5 / 3.0 | Seconds of drone on screen |
| `diveAngleDeg` | 62 | 38 | Nose-down attitude through the terminal dive |
| `CountdownSeconds` | 10 | 10 | Tasking to launch |

### Why the two are not one type at two sizes

They are opposite instruments. The kamikaze drone is **tactical**: it comes over
the next ridge on a 1.8 km run-in, tips almost vertically onto one target, and
carries a warhead smaller than a 60 mm mortar bomb. The Shahed is **operational**:
it arrives from six kilometres out on a shallow glide, covers a 160 m circle, and
leaves the ground burning for forty seconds afterwards.

That difference shows in three places on screen, and each one is deliberate:

- **The engine.** `DroneBuzz` is a stack of clean detuned tones — four electric
  motors holding station. `ShahedEngine` is a sawtooth with a wavering firing
  rate — a small two-stroke under load, which is the sound the class is named
  for. It is the first cue telling you which one is coming.
- **The dive.** 62° is a munition tipping onto a point; 38° is an airframe
  gliding onto a target.
- **What is left behind.** The tactical drone leaves a scorch. The Shahed leaves
  a fire, because fifty-odd kilograms of warhead with the airframe's fuel behind
  it does. `UavDef.wreckFire` / `wreckFireSeconds`; set `wreckFireSeconds = 0`
  for a type that leaves nothing.

### The kamikaze drone's model is built in code

Its source pack was removed from the project, and rather than borrow another
quadcopter the game now owns its loitering munition outright:
`ProceduralModels.BuildKamikazeDrone` builds a delta body, a warhead nose, swept
wings, twin fins and a pusher propeller, with a runtime `AnimationClip` that
turns the propeller and rocks the airframe. See docs/09-3D-MODELS.md.

It carries **no `rotors` spec**, unlike the Shahed: the clip already turns the
propeller, and a `RotorSpinner` on top of that would be two things driving one
transform with the loser decided by script execution order.

---

## 2. Why this is its own system

A loitering munition is not a small aeroplane. An aircraft **passes over** a target and releases something that carries on without it; a loitering munition **is** the weapon and arrives at the impact point itself. That difference is the whole character of the thing on screen, and it is why `DroneRun` is a separate flight from `BomberRun` rather than a configuration of it:

- there is no egress leg — the flight ends where the explosion starts;
- there is no stick walking across the ground — one warhead, one point;
- the target area is small, because this is aimed at a *thing*, not at a beaten zone.

It is also a separate **rail section** from AIR STRIKE, because it asks a different question of the player: an airframe comes back and a drone does not, so the two are drawn from different stocks and are not alternatives to each other in the way two aircraft are.

### The blast is deliberately small

`UavWarheadBurst` is 150 m — the smallest strike effect in the game, below even a 60 mm mortar bomb. A loitering munition carries a few kilograms of warhead, not a shell. Drawing it as artillery-sized would make it strictly better than artillery at everything, which is both wrong and boring; drawn honestly it is a precision tool.

### Two phases

```
launch ──── cruise (level, at altitude, 82% of the ground) ──── nose over ──── dive ──── impact
```

The nose tips down over the last quarter of the cruise so the dive is *entered* rather than snapped into, and altitude falls on a squared curve during the dive because a diving munition gains speed all the way in.

**The propeller buzz is cut before the warhead, not after.** A drone still humming after its own explosion is the kind of detail that makes everything around it look fake.

---

## 3. Where the code lives

| Script | Role |
|---|---|
| `Vfx/CalledStrikeSystem.cs` | **Shared** arm / aim / countdown machinery |
| `Vfx/UavCatalog.cs` | The types in numbers — the single source of truth |
| `Vfx/UavStrikeSystem.cs` | The strike: launches the drone, detonates the warhead |
| `Vfx/DroneRun.cs` | The flight: cruise, nose-over, terminal dive |
| `Vfx/RotorSpinner.cs` | Spins the propellers (shared with the helicopter) |
| `Vfx/TargetAreaMarker.cs` | The 3D target-area volume (shared) |
| `UI/UnitPaletteUI.cs` | `BuildUavStrikeSection` |
| `Core/GameController.cs` | Builds the system and arbitrates the HUD banner |

**Three systems, one banner.** Artillery, air and UAV strikes all report a countdown every frame; `GameController.RefreshStrikeBanner` shows whichever is nearest to landing.

### Degradation

If the model is not installed, `DroneRun.Launch` returns null after logging what to run, and `UavStrikeSystem` waits out the flight time and detonates anyway. Losing a tasked strike to a missing art asset would be a far worse failure than one with nothing to watch.


## Damage

Strikes are no longer visual only. Every round, weapon and warhead is resolved
through `Units/BlastDamage`, which is shared by all three strike types so they
answer the question the same way.

Two radii, because a blast is not a switch:

| Radius | Effect |
|---|---|
| **Lethal** | Destroyed outright, whatever its strength was |
| **Blast** | Damage falls off with the **square** of the distance out to this edge |

Square falloff rather than linear because blast overpressure does. Linear makes
the rim of the circle as dangerous as the middle, which turns artillery into a
stamp-shaped area-denial tool instead of a weapon you have to aim.

Surviving formations also take **shock** — morale and organisation — at 55× the
strength damage, so being shelled and living through it still costs a formation
its composure. Near the blast edge that is the entire effect.

**It hits both sides.** A strike is placed on a piece of ground, and ground does
not check uniforms. Friendly fire is not a special case; it is what falls out of
doing the honest thing, and it is what makes placing a mission near your own line
a decision.

The kamikaze drone's warhead is **18 m lethal, 70 m blast, 0.30 max damage** —
the smallest of any strike here, and smaller than a 60 mm mortar bomb. It kills
what it lands on and leaves the formation beside it standing, which is what makes
it a precision instrument rather than cheap artillery.

---

## 5. Known gaps

- **Nothing can shoot it down**, and no air-defence unit interacts with it.
- **No stock, cooldown or operator unit** — tasking is unlimited.
- **Not saved.** A strike in the air is lost on save/load.
- **`noseYawOffsetDeg` and the Shahed's rotor axis are unverified.** The
  procedural drone is authored nose-along-`+Z` so its offset is correct by
  construction, but the Shahed's comes from an imported FBX: if it flies sideways
  or its propeller spins in the wrong plane, `noseYawOffsetDeg` and
  `rotors[0].axis` on its catalogue row are the fix.

---

## Rules

1. **`UavCatalog` is the source of truth.** Add a type by adding a row.
2. **Models are reached through `UnitModelLibrary` by id**, never by a Resources path (golden rule 10).
3. **Every type needs its own burst and smoke `VfxId`** with a procedural fallback, because asset packs must stay removable.
4. **A strike must still land if its model is missing.** Keep the fallback path working.
5. **Update this file, docs/08-PARTICLE-SYSTEMS.md, docs/09-3D-MODELS.md and docs/10-AUDIO.md in the same change.**

---

## Related

`docs/07-ARCHITECTURE.md` · `docs/08-PARTICLE-SYSTEMS.md` · `docs/09-3D-MODELS.md` · `docs/10-AUDIO.md` · `docs/17-ARTILLERY.md` · `docs/18-AIR-STRIKES.md`
