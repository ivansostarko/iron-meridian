#!/usr/bin/env python3
"""Generates the small row icons used by the in-game Unit Info panel table.

Unlike the APP-6 unit icons (colour-framed, dark stroke), these are plain
light-stroke glyphs on a transparent background, meant to sit directly on the
dark UI panel background at small size (~20-24px in-game).

Outputs 256x256 transparent PNGs to Assets/Resources/Icons/Stats/<name>.png

Requires: pip install pillow
Run:      python scripts/generate_stat_icons.py
"""
import os, math
from PIL import Image, ImageDraw

SIZE = 256
LINE = 16
STROKE = (225, 230, 235, 255)   # near-white, matches GameConfig.UiText

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Icons", "Stats")

C = SIZE / 2  # canvas centre


def canvas():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)


def star_points(cx, cy, r_outer, r_inner, n=5, rot=-90):
    pts = []
    for i in range(n * 2):
        r = r_outer if i % 2 == 0 else r_inner
        a = math.radians(rot + i * 360 / (n * 2))
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def g_type(d):  # generic unit silhouette
    d.ellipse([C - 34, 50, C + 34, 118], outline=STROKE, width=LINE)
    d.arc([C - 62, 120, C + 62, 244], 200, 340, fill=STROKE, width=LINE)
    d.line([C - 62, 182, C - 62, 130], fill=STROKE, width=LINE)
    d.line([C + 62, 182, C + 62, 130], fill=STROKE, width=LINE)


def g_team(d):  # flag on a pole
    d.line([90, 40, 90, 216], fill=STROKE, width=LINE)
    d.polygon([(90, 46), (190, 78), (90, 110)], outline=STROKE, width=LINE)


def g_affiliation(d):  # diamond
    d.polygon([(C, 40), (216, C), (C, 216), (40, C)], outline=STROKE, width=LINE)


def g_echelon(d):  # three stacked chevrons
    for i, y in enumerate((70, 128, 186)):
        d.line([(60, y + 30), (C, y), (196, y + 30)], fill=STROKE, width=LINE, joint="curve")


def g_status(d):  # heartbeat pulse
    d.line([(30, 140), (86, 140), (110, 70), (140, 210), (166, 140), (226, 140)],
           fill=STROKE, width=LINE, joint="curve")


def g_strength(d):  # heart
    r = 52
    d.ellipse([C - r * 2 + 20, 60, C - 20 + 4, 60 + r * 2 - 20], outline=STROKE, width=LINE)
    d.ellipse([C + 16, 60, C + r * 2 - 20 + 16, 60 + r * 2 - 20], outline=STROKE, width=LINE)
    d.polygon([(C - 96, 120), (C, 226), (C + 96, 120)], outline=STROKE, width=LINE)


def g_manpower(d):
    d.ellipse([C - 32, 36, C + 32, 100], outline=STROKE, width=LINE)
    d.arc([C - 70, 104, C + 70, 250], 195, 345, fill=STROKE, width=LINE)


def g_training(d):  # 5-point star
    d.polygon(star_points(C, C, 100, 42), outline=STROKE, width=LINE)


def g_morale(d):  # smiling face in circle
    d.ellipse([30, 30, 226, 226], outline=STROKE, width=LINE)
    d.ellipse([88, 96, 108, 116], fill=STROKE)
    d.ellipse([148, 96, 168, 116], fill=STROKE)
    d.arc([84, 110, 172, 190], 20, 160, fill=STROKE, width=LINE)


def g_organisation(d):  # simplified gear
    d.ellipse([C - 46, C - 46, C + 46, C + 46], outline=STROKE, width=LINE)
    d.ellipse([C - 16, C - 16, C + 16, C + 16], outline=STROKE, width=int(LINE * 0.7))
    for i in range(8):
        a = math.radians(i * 45)
        x0, y0 = C + 58 * math.cos(a), C + 58 * math.sin(a)
        x1, y1 = C + 82 * math.cos(a), C + 82 * math.sin(a)
        d.line([x0, y0, x1, y1], fill=STROKE, width=LINE)


def g_power(d):  # lightning bolt
    d.polygon([(146, 26), (76, 146), (122, 146), (108, 230), (190, 106), (140, 106)],
              outline=STROKE, width=int(LINE * 0.6))


def g_attack(d):  # sword
    d.line([C, 30, C, 176], fill=STROKE, width=LINE)
    d.line([C - 44, 150, C + 44, 150], fill=STROKE, width=LINE)
    d.line([C, 176, C, 226], fill=STROKE, width=int(LINE * 1.6))
    d.polygon([(C - 26, 20), (C + 26, 20), (C, 56)], fill=STROKE)


def g_hardattack(d):  # armour-piercing arrow through a plate
    d.line([50, C, 180, C], fill=STROKE, width=LINE)
    d.polygon([(160, C - 34), (226, C), (160, C + 34)], fill=STROKE)
    d.line([150, 60, 150, 196], fill=STROKE, width=int(LINE * 0.8))


def g_defence(d):  # shield
    d.polygon([(C, 30), (206, 76), (206, 140), (C, 226), (50, 140), (50, 76)],
              outline=STROKE, width=LINE)


def g_armour(d):  # hex plate
    pts = []
    for i in range(6):
        a = math.radians(60 * i - 90)
        pts.append((C + 96 * math.cos(a), C + 96 * math.sin(a)))
    d.polygon(pts, outline=STROKE, width=LINE)


def g_antiair(d):  # radar dome + up arrow
    d.arc([40, 70, 216, 246], 180, 360, fill=STROKE, width=LINE)
    d.line([C, 150, C, 40], fill=STROKE, width=LINE)
    d.line([C - 30, 70, C, 40], fill=STROKE, width=LINE)
    d.line([C + 30, 70, C, 40], fill=STROKE, width=LINE)


def g_weaponrange(d):  # crosshair rings
    d.ellipse([C - 90, C - 90, C + 90, C + 90], outline=STROKE, width=LINE)
    d.ellipse([C - 44, C - 44, C + 44, C + 44], outline=STROKE, width=LINE)
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        d.line([C + dx * 96, C + dy * 96, C + dx * 118, C + dy * 118], fill=STROKE, width=LINE)


def g_viewrange(d):  # eye
    d.arc([30, 40, 226, 216], 20, 160, fill=STROKE, width=LINE)
    d.arc([30, 40, 226, 216], 200, 340, fill=STROKE, width=LINE)
    d.ellipse([C - 34, C - 34, C + 34, C + 34], outline=STROKE, width=LINE)
    d.ellipse([C - 12, C - 12, C + 12, C + 12], fill=STROKE)


def g_speed(d):  # forward chevrons
    for off in (-40, 40):
        d.line([(70 + off, 60), (150 + off, C), (70 + off, 196)],
               fill=STROKE, width=LINE, joint="curve")


def g_ammo(d):  # cartridge
    d.rectangle([C - 34, 40, C + 34, 170], outline=STROKE, width=LINE)
    d.polygon([(C - 34, 170), (C + 34, 170), (C, 226)], outline=STROKE, width=LINE)


def g_fuel(d):  # droplet
    d.polygon([(C, 30), (C + 74, 150)], fill=None)
    d.pieslice([C - 74, 76, C + 74, 224], 0, 360, outline=STROKE, width=LINE)
    d.polygon([(C - 40, 110), (C, 26), (C + 40, 110)], fill=(0, 0, 0, 0), outline=STROKE, width=LINE)


def g_food(d):  # ration box
    d.rectangle([40, 90, 216, 206], outline=STROKE, width=LINE)
    d.line([40, 130, 216, 130], fill=STROKE, width=int(LINE * 0.7))
    d.arc([88, 46, 168, 126], 200, 340, fill=STROKE, width=LINE)


def g_position(d):  # map pin
    d.pieslice([C - 74, 30, C + 74, 178], 0, 360, outline=STROKE, width=LINE)
    d.polygon([(C - 40, 130), (C, 226), (C + 40, 130)], fill=(0, 0, 0, 0), outline=STROKE, width=LINE)
    d.ellipse([C - 24, 78, C + 24, 126], outline=STROKE, width=int(LINE * 0.7))


GLYPHS = {
    "type": g_type, "team": g_team, "affiliation": g_affiliation, "echelon": g_echelon,
    "status": g_status, "strength": g_strength, "manpower": g_manpower, "training": g_training,
    "morale": g_morale, "organisation": g_organisation, "power": g_power, "attack": g_attack,
    "hardattack": g_hardattack, "defence": g_defence, "armour": g_armour, "antiair": g_antiair,
    "weaponrange": g_weaponrange, "viewrange": g_viewrange, "speed": g_speed, "ammo": g_ammo,
    "fuel": g_fuel, "food": g_food, "position": g_position,
}


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, fn in GLYPHS.items():
        img, d = canvas()
        fn(d)
        img.save(os.path.join(OUT, name + ".png"))
    print(f"Stats: {len(GLYPHS)} icons -> {OUT}")


if __name__ == "__main__":
    main()
