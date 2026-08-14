# Artillery Strikes

Called fire missions in the map editor: pick a nature, place a target area on the map, and ten seconds later five rounds land inside it.

> **Keep this file current.** Every new nature, burst effect, report or call site must be recorded here in the same change. See [Rules](#rules) at the bottom.

---

## 1. Nature register

Rows live in `Assets/Scripts/Vfx/ArtilleryCatalog.cs`. The panel, the target marker and the impact sequence are all driven from them — a new calibre appears in the UI by adding a row, not by editing the UI.

| Calibre | Target radius | Burst effect | Smoke effect | Smoke lasts | Round spacing | Marker colour |
|---|---|---|---|---|---|---|
| **105 mm** light howitzer | 140 m | `ArtilleryLightBurst` | `ArtilleryLightSmoke` | 9 s | 0.30 s | pale yellow |
| **120 mm** heavy mortar | 120 m | `ArtilleryMortarBurst` | `ArtilleryMortarSmoke` | 11 s | 0.42 s | tan |
| **155 mm** medium howitzer | 190 m | `ArtilleryMediumBurst` | `ArtilleryMediumSmoke` | 15 s | 0.55 s | orange |
| **203 mm** heavy howitzer | 260 m | `ArtilleryHeavyBurst` | `ArtilleryHeavySmoke` | 22 s | 0.85 s | deep red |

Shared constants, also in `ArtilleryCatalog`:

| Constant | Value | Meaning |
|---|---|---|
| `CountdownSeconds` | 10 | Time between the call for fire and the first round |
| `ShellsPerMission` | 5 | Rounds in one mission |

**Ordered by calibre, not by the order they were requested.** A munitions list that runs 105 → 120 → 155 → 203 is scannable, and the target radius grows monotonically down the panel, so the trade-off between the natures is legible without reading a word.

**Why each nature has its own effects rather than one scaled explosion.** The three events genuinely do not look alike from a map camera: a 105 mm round is a bright crack with a flat shrapnel disc, a 120 mm mortar bomb is a narrow column of soil, and a heavy shell is a fireball with a ground shock ring and arcing debris. Scaling one effect four ways would make every nature the same event at four sizes, which defeats the point of having four buttons. 155 mm and 203 mm *do* share a signature (`ArtilleryHeavyBlast`) and differ by scale and lifetime — because that is what actually separates them.

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
| `Vfx/ArtilleryCatalog.cs` | The natures in numbers — the single source of truth |
| `Vfx/ArtilleryStrikeSystem.cs` | Arming, placement, countdown, impact sequence |
| `Vfx/TargetAreaMarker.cs` | The 3D target-area volume |
| `UI/UnitPaletteUI.cs` | `BuildArtillerySection` — the four buttons |
| `UI/GameHUD.cs` | `SetFireMission` — the countdown banner |
| `Core/GameController.cs` | Builds the system and wires it to the HUD and palette |

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

## 3. The target-area marker

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

## 5. Known gaps

- **Strikes do no damage.** A mission is currently visual and audible only — it does not touch unit strength, morale or organisation. This was left deliberately: the feature sits in the map editor next to the other hand-placed effects, and choosing a damage model (how much, falloff with distance, which teams, how it interacts with `CombatSystem`'s tick) is a balance decision rather than a rendering one. The hook is `ArtilleryStrikeSystem.RunSalvo`, which already knows the impact point and radius of every round.
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
