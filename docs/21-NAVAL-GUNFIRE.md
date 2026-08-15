# Naval Gunfire

Naval gunfire support in the map editor: pick a gun, place the target area, and ten seconds later a ship over the horizon walks a mission of rounds across it.

> **Keep this file current.** Every new gun, effect, sound or call site must be recorded here in the same change. See [Rules](#rules) at the bottom.

---

## 1. Gun register

Rows live in `Assets/Scripts/Vfx/NavalCatalog.cs`. `LethalRadiusM`, `BlastRadiusM` and `MaxDamage` are derived from calibre, exactly as the artillery catalogue derives them, so a new gun gets consistent numbers the moment its bore is written down.

### NATO

| Gun | Calibre | Beaten zone | Rounds | Interval | Burst / smoke |
|---|---|---|---|---|---|
| **Mk 110** — Bofors, littoral combat ship | 57 mm | 120 m | 12 | 0.16 s | `ArtilleryLightBurst` / `ArtilleryLightSmoke` |
| **OTO Melara Super Rapid** — frigate main gun | 76 mm | 150 m | 10 | 0.22 s | `ArtilleryLightBurst` / `ArtilleryLightSmoke` |
| **Mk 45 Mod 4** — five-inch destroyer gun | 127 mm | 230 m | 8 | 0.38 s | `ArtilleryMediumBurst` / `ArtilleryMediumSmoke` |
| **Advanced Gun System** — Zumwalt class | 155 mm | 300 m | 6 | 0.62 s | `ArtilleryHeavyBurst` / `ArtilleryHeavySmoke` |

### Enemy — Russian pattern

| Gun | Calibre | Beaten zone | Rounds | Interval | Burst / smoke |
|---|---|---|---|---|---|
| **AK-176** — corvette mount | 76 mm | 155 m | 10 | 0.20 s | `ArtilleryLightBurst` / `ArtilleryLightSmoke` |
| **AK-100** — frigate main gun | 100 mm | 190 m | 9 | 0.30 s | `ArtilleryMediumBurst` / `ArtilleryMediumSmoke` |
| **AK-130** — twin automatic mount | 130 mm | 250 m | 10 | 0.30 s | `ArtilleryHeavyBurst` / `ArtilleryHeavySmoke` |

### Enemy — Chinese pattern

| Gun | Calibre | Beaten zone | Rounds | Interval | Burst / smoke |
|---|---|---|---|---|---|
| **H/PJ-26** — Type 054A frigate | 76 mm | 150 m | 10 | 0.22 s | `ArtilleryLightBurst` / `ArtilleryLightSmoke` |
| **H/PJ-38** — Type 052D destroyer | 130 mm | 265 m | 8 | 0.40 s | `ArtilleryHeavyBurst` / `ArtilleryHeavySmoke` |

Both enemy patterns share one **ENEMY NAVY** tab. The game's sides are User and Enemy (`Data.Team`), not nationalities — the same argument the artillery catalogue makes for its `Enemy` inventory. Which navy a mounting actually comes from is on its detail line, where it is reference rather than a claim about who is fighting whom.

---

## 2. Why this is not just more artillery

It is the same physics, and it deliberately uses **the same burst and smoke effects** a land gun of that calibre uses. A 127 mm shell landing is a 127 mm shell landing, whoever fired it; inventing nine near-identical particle effects would be nine more rows to keep in step for a difference no player could see. The same argument the artillery catalogue makes for mapping fourteen natures onto four signatures.

What differs is the **shape of the mission**, and that is where the character lives:

| | Field artillery | Naval gunfire |
|---|---|---|
| Rounds per mission | 5, always | **6–12, per gun** |
| Interval | 0.20–0.85 s | **0.16–0.62 s** |
| Beaten zone at 127–130 mm | ~175 m | **230–265 m** |

- **Rate of fire.** These are automatic mountings. A Mk 110 puts twelve rounds down in under two seconds; a battery's five take three. The strike reads as a hosing rather than as separate impacts, which is what naval gunfire looks like.
- **Dispersion.** The rounds come from a moving platform at a range no land gun in the game matches, so the beaten zone is wider for the same calibre. That is a real trade, not a drawback dressed up as one: every round is resolved **where it actually falls** (`NavalStrikeSystem.ScatterPoint`, the same golden-angle/√t construction the artillery salvo uses), so a wider sheaf genuinely spreads the damage thinner.
- **Availability.** It comes from a ship, so it does not care where the player's guns are — but it spends the same shared allowance every other called strike does.

### Marker colours are a different family

Artillery runs pale yellow → orange → red with increasing weight. Naval runs steel blue → indigo → violet. Both say "how big is the beaten zone", and the family says which menu it came from — so a naval target area on the map is identifiable as naval without reading the banner.

---

## 3. How a mission runs

```
pick a gun ── click the map ── 10 s countdown ── N rounds, one interval apart ── aftermath
                  │                  │                        │
            ring shows the      marker escalates        each round: burst + smoke,
            beaten zone         to full alarm           resolved where it lands
```

Everything up to the first round landing is `CalledStrikeSystem<NavalGun>` — the arming, the ring tracking the cursor, the ground checks, the countdown, the escalating marker, the HUD banner and the shared strike allowance. `NavalStrikeSystem` supplies the guns and the mission. That base class now has **five** subclasses; the fifth costing almost nothing is the entire reason it exists.

### The strike allowance

A naval mission spends one of the scenario's **99** called strikes, shared with artillery, air strikes, UAV sorties and missile systems — `Vfx/StrikeBudget.cs`, with the "STRIKES REMAINING" readout at the head of this section. Full rationale in docs/17-ARTILLERY.md § *The strike allowance*.

### What a mission leaves behind

`StrikeAftermath` at the aim point: **thirty scenario minutes of fire, then two scenario hours of smoke**. One site per mission, not one per round. See docs/08-PARTICLE-SYSTEMS.md §2.1.

### Ground checks

Placement is ground-checked like every other strike: Cesium streams terrain in, and a click over tiles that have not arrived has no ground to put an impact on. Such a click is refused with a message and **leaves the gun armed** — losing a whole mission to the tile streamer would punish the player for something they did not do.

---

## 4. Damage

Resolved through `Units/BlastDamage`, shared with every other strike so they all answer the question the same way. Two radii — lethal and blast, with square falloff between them — and range measured to the formation's **footprint**, not to its map pin. See docs/17-ARTILLERY.md § *Damage* for the full model and why the footprint matters.

For naval gunfire specifically: the wide beaten zone means most rounds land clear of any one formation, and the mission's effect comes from the *number* of rounds rather than from any one of them. A 57 mm Mk 110 mission will rarely destroy anything and will reliably suppress; a 155 mm AGS mission is six heavy shells and behaves like one.

The HUD reports the whole mission, not the last round: *"Rounds complete — 127 mm Mk 45 Mod 4, 8 rounds. 3 formation(s) hit, 1 destroyed — 140 % combat strength lost."*

---

## 5. Where the code lives

| Script | Role |
|---|---|
| `Vfx/CalledStrikeSystem.cs` | **Shared** arm / aim / countdown machinery |
| `Vfx/NavalCatalog.cs` | The guns in numbers — the single source of truth |
| `Vfx/NavalStrikeSystem.cs` | The mission: scatter, bursts, damage, aftermath |
| `Vfx/StrikeBudget.cs` | The 99 called strikes, shared with the other four menus |
| `Vfx/StrikeAftermath.cs` | The fire and smoke a mission leaves |
| `Vfx/TargetAreaMarker.cs` | The 3D target-area volume (shared) |
| `Units/BlastDamage.cs` | What a round does to what is under it (shared) |
| `UI/UnitPaletteUI.cs` | `BuildNavalStrikeSection` — fleet tabs and the gun rows |
| `UI/UiIcons.cs` | `Warship` — the rail row's glyph |
| `Core/GameController.cs` | Builds the system and arbitrates the HUD banner |

**One banner, five systems.** Artillery, air, UAV, missile and naval strikes all report a countdown every frame and there is one HUD banner. Each writes to its own slot and `GameController.RefreshStrikeBanner` shows whichever is nearest to landing.

---

## 6. Known gaps

- **No ship on the map.** The rounds arrive from over the horizon and there is nothing to see firing them, and no naval unit has to exist for a mission to be called. The `Naval` unit category is still unmodelled (docs/09-3D-MODELS.md).
- **No range limit.** Real naval gunfire support reaches 20–100 km inland depending on the mounting; here a mission can be placed anywhere on the map, however far from any water.
- **No shell types.** One HE nature per gun. Illumination, smoke and guided rounds (the AGS's whole reason for existing) are not modelled.
- **Not saved.** A mission in the air is lost on save/load.

---

## Rules

1. **`NavalCatalog` is the source of truth.** Add a gun by adding a row — the buttons, the ring, the salvo, the countdown banner and the blast all read from it.
2. **Record it in §1 in the same commit**, with its calibre, beaten zone and round count.
3. **Reuse the calibre-matched artillery burst effects** unless a gun genuinely looks different on the ground. A new effect means a row in `VfxCatalog` **and** an entry in docs/08-PARTICLE-SYSTEMS.md.
4. **Every mission spends one strike** from `StrikeBudget`, like every other called strike. Do not add a second pool.
5. Beaten-zone figures are balance, not reference. If a number changes, change it because the game plays better, and do not dress it up as research.

---

## Related

`docs/07-ARCHITECTURE.md` · `docs/08-PARTICLE-SYSTEMS.md` (effects register) · `docs/10-AUDIO.md` (audio register) · `docs/17-ARTILLERY.md` (the shared strike model and the damage model) · `docs/18-AIR-STRIKES.md` · `docs/19-UAV-STRIKES.md` · `docs/20-MISSILE-SYSTEMS.md`

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
