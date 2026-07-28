# Regenerate unit icons

Run `python3 scripts/generate_icons.py` (install Pillow first if missing: `pip install pillow`).

Verify: 37 PNGs in `Assets/Resources/Icons/Friendly/`, 37 in `Assets/Resources/Icons/Enemy/`, 4 in `Assets/Resources/Icons/Affiliations/`. If a glyph looks wrong, adjust its branch in `draw_glyph()` and re-run. Icons are 256×256 transparent PNGs; friendly = blue rectangle frame, hostile = red diamond, per APP-6 style.
