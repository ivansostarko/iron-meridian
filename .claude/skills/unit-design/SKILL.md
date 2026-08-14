---
name: unit-design
description: Designing or balancing unit types, stats, echelons, icons and combat tuning. Use when editing generate_units.py, generate_icons.py, units.json or CombatSystem.
---

# Unit design & balance

## Data pipeline

`scripts/generate_units.py` → `Assets/StreamingAssets/Data/units.json` (117 types; company-level values). `scripts/generate_icons.py` → `Assets/Resources/Icons/{Friendly,Enemy}/<id>.png` (APP-6 style; blue rectangle / red diamond frames). Both teams share the catalogue; team/affiliation/echelon are chosen at deployment. **Never edit units.json by hand** — edit the generator and re-run, so regeneration stays lossless.

## Classification

Two independent fields, and mixing them up is the mistake to avoid:

- `category` — **how it behaves**: `CoreGround` (holds terrain, gets a ground model), `Drone`, `Air`, `Naval` (none of those do either). This is what gameplay code branches on.
- `branch` — **the arm of service**: Infantry, Armour, Mechanised, Artillery, AntiAircraft, Air, Navy, Logistics, Other. Display only — the Units screen's filter and column, and the palette's grouping. `Other` is a real answer for support that belongs to no arm.

The generator asserts both, and `generate_units_doc.py` refuses to run on an unknown branch.

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

New id ⇒ new `GLYPHS` entry. There is no id list to maintain: the script reads them from `units.json` and refuses to run while any unit has no glyph. Follow APP-6 conventions (X infantry, oval armour, dot artillery, dome AD, flying-wing UAS, plan-view airframe for crewed aircraft, side-on rotor for helicopters, hull for vessels). Where a symbol is a qualified version of an existing arm, use `gmain(box)` for the base glyph and `gtag(d, box, "…")` for the letter modifier so the family stays aligned. Regenerate and visually check both team variants — the hostile diamond has ~40% less usable area than the friendly rectangle, and that is where a glyph breaks first.
