# Air Strikes

Tasked air strikes in the map editor: pick an airframe, place a target area, and ten seconds later the aircraft runs in and puts a stick of five weapons through it.

> **Keep this file current.** Every new airframe, burst effect, report or call site must be recorded here in the same change. See [Rules](#rules) at the bottom.

---

## 1. Airframe register

Rows live in `Assets/Scripts/Vfx/AirStrikeCatalog.cs`. The panel, the target marker and the bombing run are all driven from them — a new airframe appears in the UI by adding a row, not by editing the UI.

| Airframe | Target radius | Model | Burst | Smoke | Report |
|---|---|---|---|---|---|
| **B-2 Spirit** stealth bomber | 320 m | `stealth_bomber` | `AerialBombBurst` | `AerialBombSmoke` (26 s) | `AerialBomb` |

### B-2 flight profile

| Setting | Value | Meaning |
|---|---|---|
| `wingspanMeters` | 240 | Drawn size — see below |
| `altitudeMeters` | 1500 | Run-in height above the target's terrain |
| `approachKm` / `egressKm` | 4.5 / 4.5 | Ground distance flown before and after the release point |
| `approachSeconds` / `egressSeconds` | 5.0 / 4.0 | Time on each leg — 9 s of aircraft on screen |
| `releaseIntervalSeconds` | 0.34 | Spacing of the stick |
| `fallSeconds` | 1.15 | Release to impact |

Shared constants: `CountdownSeconds` = 10, `BombsPerStrike` = 5.

**The aircraft is drawn far larger than life.** A real B-2 spans 52 m; on a map whose unit icons are 260 m across and whose explosions are 300 m, that is a speck. `wingspanMeters` is 240 — the same deliberate exaggeration the icons themselves use, so the aircraft reads at the zoom the game is actually played at. Scaling is measured from the model's own renderer bounds at load, so it does not matter what units the FBX was authored in and a replaced model needs no re-tuning.

---

## 2. How a strike runs

```
Left rail → AIR STRIKE            UnitPaletteUI.BuildAirStrikeSection
  ↓ pick an airframe              AirStrikeSystem.Toggle          (CalledStrikeSystem)
  ↓ target area follows cursor    TargetAreaMarker (aiming instance)
  ↓ click the terrain             CalledStrikeSystem.Launch
  ↓ 10 s countdown                 GameHUD.SetFireMission + marker alarm
  ↓ aircraft runs in              BomberRun
  ↓ 5 weapons walk the target     BomberRun.ReleaseOne → AirStrikeSystem.Detonate
```

| Script | Role |
|---|---|
| `Vfx/CalledStrikeSystem.cs` | **Shared** arm / aim / countdown machinery |
| `Vfx/AirStrikeCatalog.cs` | The airframes in numbers — the single source of truth |
| `Vfx/AirStrikeSystem.cs` | The strike itself: launches the run, detonates the weapons |
| `Vfx/BomberRun.cs` | The flying aircraft and its bomb release |
| `Vfx/TargetAreaMarker.cs` | The 3D target-area volume (shared with artillery) |
| `UI/UnitPaletteUI.cs` | `BuildAirStrikeSection` |
| `UI/GameHUD.cs` | `SetFireMission` — the countdown banner (shared) |
| `Core/GameController.cs` | Builds the system, wires it, and arbitrates the banner |

### Shared with artillery

Everything up to the moment something lands is identical between a fire mission and an air strike — arming, the marker tracking the cursor, the ground checks, the countdown, the escalating marker, the HUD banner. That lives in `CalledStrikeSystem<TKey>`; `ArtilleryStrikeSystem` and `AirStrikeSystem` are its two subclasses and supply only their own numbers and their own `RunStrike`. See docs/17-ARTILLERY.md.

**One banner, two systems.** Both report their countdown every frame and there is one HUD banner, so left alone the idle system would blank it a frame after the busy one filled it. `GameController.RefreshStrikeBanner` gives each a slot and shows whichever strike is nearest to landing.

### What the countdown means

For artillery it is time to impact. Here it is **time until the aircraft is overhead** — the weapons land a couple of seconds later, after the run-in and the fall. That is the honest reading of a tasked strike, and it is why the target marker stays up until the run is finished rather than being dropped at zero.

### The flight is animated in code

The model is a static mesh — a flying wing has no moving surfaces, so there is nothing to rig and nothing to play. What makes it read as flight is the **track**:

- a real approach leg, so the aircraft is seen coming rather than appearing over the target;
- a held bank angle through the run;
- weapons that keep the aircraft's forward speed as they fall, so they land *ahead* of the release point and the blasts walk along the ground behind the aeroplane. Without that throw-forward the stick bunches under the flight path and the pass reads as the aircraft dropping straight down;
- a random attack heading per strike, so repeated strikes on the same ground do not all run in on the same line;
- the aircraft is **not** removed on the egress timer alone — it flies on in a straight line until its last weapon has landed. Destroying it on the timer would kill the fall coroutines with it and silently lose any weapon still in the air.

**Weapons are not modelled as objects.** A 3 m bomb falling from 1500 m is invisible at the zoom this map is played at; what the player reads is the aircraft passing and the stick walking through the target. A release therefore schedules its impact `fallSeconds` later, and the impact is where the burst happens.

### Degradation

If the model is not installed, `BomberRun.Launch` returns null after logging what to run, and `AirStrikeSystem` falls back to `FallbackStick` — the same five weapons on the same attack heading, with no aeroplane. **Losing a tasked strike to a missing art asset would be a far worse failure than one with nothing to look at.**

---

## 3. Effects and audio

Bursts and smoke are ordinary `VfxCatalog` rows — see **docs/08-PARTICLE-SYSTEMS.md**. `AerialBombBurst` is the largest blast in the game (560 m) and carries the highest priority in the catalogue: a tasked strike is a scheduled, watched event and must never be what the concurrency budget discards.

Reports are ordinary `EffectSound` values — see **docs/10-AUDIO.md**. The burst's report comes through its `VfxCatalog` row automatically. **`JetPass` does not**: it is played directly by `BomberRun` via `EffectAudio.PlayAt`, parented to the aircraft so it travels with it. It is the first gameplay sound in the project not carried by a particle effect, and it is recorded in §2.4 of the audio doc for that reason.

---

## 4. Known gaps

- **Strikes do no damage.** Like artillery, a strike is visual and audible only — it does not touch unit strength, morale or organisation. The hook is `AirStrikeSystem.Detonate`, which already knows each weapon's impact point.
- **Nothing can shoot it down.** The aircraft is invulnerable and ignores the `sam` and `air_defence` unit types entirely; there is no air-defence interaction of any kind.
- **No airbase, no sortie limit, no cooldown.** Strikes are unlimited and can be tasked as fast as they can be clicked.
- **Not saved.** A strike in the air is lost on save/load; the marker and the aircraft are runtime-only and are not part of the map schema.
- **`noseYawOffsetDeg` is unverified.** It is 0, meaning the model's nose is assumed to point down its local +Z. If the bomber flies sideways or backwards, that field on the catalogue row is the one number to change.

---

## Rules

1. **`AirStrikeCatalog` is the source of truth.** Add an airframe by adding a row — never special-case one in `UnitPaletteUI`, `AirStrikeSystem` or `BomberRun`.
2. **Models are reached through `UnitModelLibrary` by id**, never by a Resources path at the call site (golden rule 10). Register the mesh there and install it with `Install Unit Models`.
3. **Every airframe needs its own burst and smoke `VfxId`**, with rows in `VfxCatalog` and a procedural fallback, because asset packs must stay removable.
4. **A strike must still land if its model is missing.** Keep the fallback path working.
5. **Update this file, docs/08-PARTICLE-SYSTEMS.md, docs/09-3D-MODELS.md and docs/10-AUDIO.md in the same change** whenever an airframe, model, effect or report is added, removed or repurposed.

---

## Related

`docs/07-ARCHITECTURE.md` · `docs/08-PARTICLE-SYSTEMS.md` (effect register) · `docs/09-3D-MODELS.md` (model register) · `docs/10-AUDIO.md` (sound register) · `docs/17-ARTILLERY.md` (the shared strike machinery)
