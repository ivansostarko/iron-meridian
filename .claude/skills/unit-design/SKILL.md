---
name: unit-design
description: Designing or balancing unit types, stats, echelons, icons and combat tuning. Use when editing generate_units.py, generate_icons.py, units.json or CombatSystem.
---

# Unit design & balance

## Data pipeline

`scripts/generate_units.py` → `Assets/StreamingAssets/Data/units.json` (37 types; company-level values). `scripts/generate_icons.py` → `Assets/Resources/Icons/{Friendly,Enemy}/<id>.png` (APP-6 style; blue rectangle / red diamond frames). Both teams share the catalogue; team/affiliation/echelon are chosen at deployment. **Never edit units.json by hand** — edit the generator and re-run, so regeneration stays lossless.

## Stat conventions (company-equivalent)

- `attack`/`defence` 0–100 scale; `hardAttack` = anti-armour, `antiAir` = anti-drone/air.
- Line combat units: manpower 60–130, training 60–75. Elite (SF) training 90+. Support: `isSupport=true` (fights at 40%).
- Indirect fire (`canIndirectFire`) pairs with long `weaponRangeKm` (mortar 7, tube 24, MLRS 70, armed UAS 60).
- Sustainment realism: foot units `fuelUsePerKm=0`; vehicles 1.4–8.5 L/km; `ammoStock` in native rounds (`ammoType` documents the nature).
- Echelon scaling lives in `EchelonInfo.ManpowerMultiplier` — don't bake echelon into base stats.

## Balance levers (in order)

1. Unit stats in the generator (preferred — data over code).
2. Combat coefficients in `CombatSystem.Exchange` (base 0.010 dmg/tick, clamp 0.001–0.08, ammo-out ×0.25, support ×0.4, armour/drone modifiers).
3. Power formula in `UnitDefinition.PowerAt` (changes everything — touch last).

Sanity checks after tuning: armour beats infantry in the open but loses to anti-tank; counter-UAS/AD counters drone units; a battalion beats a company of the same type; out-of-ammo units collapse slowly, not instantly.

## Icon rules

New id ⇒ new `draw_glyph` branch + append to `UNIT_IDS`; follow APP-6 conventions (X infantry, oval armour, dot artillery, dome AD, flying-wing UAS). Regenerate and visually check both team variants.
