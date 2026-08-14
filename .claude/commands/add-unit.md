# Add a new unit type

Add the unit type "$ARGUMENTS" to Iron Meridian:

1. Add a `unit(...)` entry in `scripts/generate_units.py` with sensible, balanced stats (compare with similar existing units; keep company-level values). Choose a category (`CoreGround`, `Drone`, `Air` or `Naval` — how it behaves) and a branch (`Infantry`, `Armour`, `Mechanised`, `Artillery`, `AntiAircraft`, `Air`, `Navy`, `Logistics` or `Other` — the arm of service shown to the player).
2. Run `python3 scripts/generate_units.py` and confirm `Assets/StreamingAssets/Data/units.json` now contains the unit.
3. Add a `GLYPHS` entry for the new id in `scripts/generate_icons.py`, following APP-6 conventions where possible. Where the symbol is a qualified version of an existing arm, draw the base glyph through `gmain(box)` and the qualifier through `gtag(d, box, "…")`. There is no id list to update — the script reads them from `units.json` and refuses to run if any unit has no glyph.
4. Run `python3 scripts/generate_icons.py` and confirm both `Assets/Resources/Icons/Friendly/<id>.png` and `Assets/Resources/Icons/Enemy/<id>.png` exist.
5. No C# changes are needed — palette, Units screen, combat, saves and info panel are all data-driven. The one exception is a 3D model: if the type has equipment already imported, add an `Overrides` row in `UnitModelLibrary.cs` and record it in `docs/09-3D-MODELS.md`.
6. Run `python3 scripts/generate_units_doc.py` to regenerate the register in `docs/04-UNITS.md`.
