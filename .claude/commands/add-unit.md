# Add a new unit type

Add the unit type "$ARGUMENTS" to Iron Meridian:

1. Add a `unit(...)` entry in `scripts/generate_units.py` with sensible, balanced stats (compare with similar existing units; keep company-level values). Choose category `CoreGround` or `Drone`.
2. Run `python3 scripts/generate_units.py` and confirm `Assets/StreamingAssets/Data/units.json` now contains the unit.
3. Add an icon glyph branch for the new id in `draw_glyph()` in `scripts/generate_icons.py`, following APP-6 conventions where possible, and append the id to `UNIT_IDS`.
4. Run `python3 scripts/generate_icons.py` and confirm both `Assets/Resources/Icons/Friendly/<id>.png` and `Assets/Resources/Icons/Enemy/<id>.png` exist.
5. No C# changes are needed — palette, combat, saves and info panel are data-driven.
6. Update the unit lists in `docs/04-UNITS.md`.
