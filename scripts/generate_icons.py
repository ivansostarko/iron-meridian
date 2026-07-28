#!/usr/bin/env python3
"""Generates APP-6 inspired unit icons for Iron Meridian.

Outputs 256x256 transparent PNGs:
  Assets/Resources/Icons/Friendly/<unit_id>.png   (blue rectangle frame)
  Assets/Resources/Icons/Enemy/<unit_id>.png      (red diamond frame)
  Assets/Resources/Icons/Affiliations/{friendly,hostile,neutral,unknown}.png

Requires: pip install pillow
Run:      python scripts/generate_icons.py
"""
import os, math
from PIL import Image, ImageDraw, ImageFont

SIZE = 256
LINE = 7

# APP-6 crystal colours
FRIENDLY_FILL = (128, 224, 255, 255)   # crystal blue
HOSTILE_FILL  = (255, 128, 128, 255)   # salmon red
NEUTRAL_FILL  = (170, 255, 170, 255)   # bamboo green
UNKNOWN_FILL  = (255, 255, 128, 255)   # canary yellow
STROKE        = (20, 20, 20, 255)

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "Icons")

def font(size):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
              "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf",
              "C:/Windows/Fonts/arialbd.ttf"):
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()

def new_canvas():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    return img, ImageDraw.Draw(img)

def frame_friendly(d):
    d.rectangle([24, 62, 232, 194], fill=FRIENDLY_FILL, outline=STROKE, width=LINE)
    return (44, 74, 212, 182)  # inner content box

def frame_hostile(d):
    d.polygon([(128, 14), (242, 128), (128, 242), (14, 128)],
              fill=HOSTILE_FILL, outline=STROKE)
    d.line([(128, 14), (242, 128), (128, 242), (14, 128), (128, 14)],
           fill=STROKE, width=LINE, joint="curve")
    return (78, 82, 178, 174)

def frame_neutral(d):
    d.rectangle([44, 44, 212, 212], fill=NEUTRAL_FILL, outline=STROKE, width=LINE)
    return (60, 60, 196, 196)

def frame_unknown(d):
    # quatrefoil: four overlapping circles + centre square
    r = 62
    for cx, cy in ((92, 92), (164, 92), (92, 164), (164, 164)):
        d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=UNKNOWN_FILL, outline=None)
    for cx, cy in ((92, 92), (164, 92), (92, 164), (164, 164)):
        d.arc([cx - r, cy - r, cx + r, cy + r], 0, 360, fill=STROKE, width=LINE)
    d.rectangle([92, 92, 164, 164], fill=UNKNOWN_FILL)
    return (78, 78, 178, 178)

def ctext(d, box, s, scale=1.0):
    x0, y0, x1, y1 = box
    fsize = int((y1 - y0) * 0.52 * scale)
    if len(s) >= 4: fsize = int(fsize * 3.4 / len(s))
    f = font(max(fsize, 16))
    bb = d.textbbox((0, 0), s, font=f)
    w, h = bb[2] - bb[0], bb[3] - bb[1]
    d.text(((x0 + x1) / 2 - w / 2 - bb[0], (y0 + y1) / 2 - h / 2 - bb[1]),
           s, font=f, fill=STROKE)

def gx(d, box):  # infantry X
    x0, y0, x1, y1 = box
    d.line([x0, y0, x1, y1], fill=STROKE, width=LINE)
    d.line([x0, y1, x1, y0], fill=STROKE, width=LINE)

def goval(d, box, scale=1.0):
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    w, h = (x1 - x0) * 0.46 * scale, (y1 - y0) * 0.34 * scale
    d.ellipse([cx - w, cy - h, cx + w, cy + h], outline=STROKE, width=LINE)

def gslash(d, box):
    x0, y0, x1, y1 = box
    d.line([x0, y1, x1, y0], fill=STROKE, width=LINE)

def gdot(d, box, ry=0.5, r=16):
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, y0 + (y1 - y0) * ry
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=STROKE)

def garrow_up(d, box, ry0=0.95, ry1=0.1):
    x0, y0, x1, y1 = box
    cx = (x0 + x1) / 2
    ya, yb = y0 + (y1 - y0) * ry0, y0 + (y1 - y0) * ry1
    d.line([cx, ya, cx, yb], fill=STROKE, width=LINE)
    a = 18
    d.line([cx - a, yb + a, cx, yb], fill=STROKE, width=LINE)
    d.line([cx + a, yb + a, cx, yb], fill=STROKE, width=LINE)

def gchevron_up(d, box):  # anti-tank
    x0, y0, x1, y1 = box
    d.line([x0, y1, (x0 + x1) / 2, y0], fill=STROKE, width=LINE)
    d.line([(x0 + x1) / 2, y0, x1, y1], fill=STROKE, width=LINE)

def gdome(d, box):  # air defence arc
    x0, y0, x1, y1 = box
    h = (y1 - y0)
    d.arc([x0, y0 + h * 0.15, x1, y1 + h * 0.9], 180, 360, fill=STROKE, width=LINE)

def gzigzag(d, box, ry=0.5, amp=0.22):
    x0, y0, x1, y1 = box
    cy = y0 + (y1 - y0) * ry
    a = (y1 - y0) * amp
    n, pts = 6, []
    for i in range(n + 1):
        x = x0 + (x1 - x0) * i / n
        pts.append((x, cy - a if i % 2 == 0 else cy + a))
    d.line(pts, fill=STROKE, width=LINE, joint="curve")

def ghline(d, box, ry=0.5):
    x0, y0, x1, y1 = box
    cy = y0 + (y1 - y0) * ry
    d.line([x0, cy, x1, cy], fill=STROKE, width=LINE)

def gcross(d, box):  # medical
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    w = LINE * 2.6
    d.rectangle([cx - w, y0, cx + w, y1], fill=STROKE)
    d.rectangle([x0 + 8, cy - w, x1 - 8, cy + w], fill=STROKE)

def gengineer(d, box):  # E-bridge symbol
    x0, y0, x1, y1 = box
    yb = y0 + (y1 - y0) * 0.72
    yt = y0 + (y1 - y0) * 0.28
    d.line([x0, yb, x1, yb], fill=STROKE, width=LINE)
    for fx in (0.06, 0.5, 0.94):
        x = x0 + (x1 - x0) * fx
        d.line([x, yb, x, yt], fill=STROKE, width=LINE)

def gdrone(d, box, ry=0.42, scale=1.0):  # APP-6 style flying wing
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, y0 + (y1 - y0) * ry
    w = (x1 - x0) * 0.44 * scale
    h = (y1 - y0) * 0.26 * scale
    d.polygon([(cx - w, cy), (cx, cy - h), (cx + w, cy), (cx, cy + h * 0.15)],
              fill=STROKE)

def gcrosshair(d, box):
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    r = min(x1 - x0, y1 - y0) * 0.32
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=STROKE, width=LINE)
    t = r * 0.7
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        d.line([cx + dx * r * 0.6, cy + dy * r * 0.6, cx + dx * (r + t), cy + dy * (r + t)],
               fill=STROKE, width=LINE)

def gflag(d, box):
    x0, y0, x1, y1 = box
    fx = x0 + (x1 - x0) * 0.2
    d.line([fx, y0, fx, y1], fill=STROKE, width=LINE)
    d.polygon([(fx, y0), (fx + (x1 - x0) * 0.4, y0 + (y1 - y0) * 0.16),
               (fx, y0 + (y1 - y0) * 0.32)], fill=STROKE)

def sub(box, fx0, fy0, fx1, fy1):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    return (x0 + w * fx0, y0 + h * fy0, x0 + w * fx1, y0 + h * fy1)

# ---- glyph per unit id ----
def draw_glyph(uid, d, box):
    if uid == "infantry": gx(d, box)
    elif uid == "mech_infantry": gx(d, box); goval(d, box, 0.9)
    elif uid == "mot_infantry":
        gx(d, box)
        for fx in (0.25, 0.5, 0.75): gdot(d, sub(box, fx - 0.02, 0.96, fx + 0.02, 1.04), 0.5, 9)
    elif uid == "armour": goval(d, box, 1.15)
    elif uid == "recon": gslash(d, box)
    elif uid == "special_forces": ctext(d, box, "SF")
    elif uid == "artillery": gdot(d, box, 0.5, 24)
    elif uid == "rocket_artillery": gdot(d, box, 0.72, 20); garrow_up(d, sub(box, 0, 0, 1, 0.75))
    elif uid == "mortar": gdot(d, box, 0.85, 16); garrow_up(d, sub(box, 0, 0.05, 1, 0.9))
    elif uid == "anti_tank": gchevron_up(d, box)
    elif uid == "air_defence": gdome(d, box)
    elif uid == "sam": gdome(d, box); garrow_up(d, sub(box, 0, 0.1, 1, 1))
    elif uid == "engineer": gengineer(d, box)
    elif uid == "eod": ctext(d, box, "EOD")
    elif uid == "cbrn": ctext(d, box, "CBRN")
    elif uid == "military_police": ctext(d, box, "MP")
    elif uid == "signals": gzigzag(d, box)
    elif uid == "electronic_warfare": ctext(d, sub(box, 0, 0, 1, 0.62), "EW"); gzigzag(d, sub(box, 0.1, 0.62, 0.9, 1), 0.5, 0.3)
    elif uid == "intelligence": ctext(d, box, "INT")
    elif uid == "headquarters": ctext(d, box, "HQ")
    elif uid == "logistics": ctext(d, box, "LOG")
    elif uid == "supply": ghline(d, box)
    elif uid == "transport": ctext(d, box, "TPT")
    elif uid == "maintenance": ctext(d, box, "MNT")
    elif uid == "medical": gcross(d, sub(box, 0.2, 0.05, 0.8, 0.95))
    elif uid == "uas_operator": gdrone(d, box, 0.34); ctext(d, sub(box, 0, 0.58, 1, 1), "OP", 0.9)
    elif uid == "recon_uas": gdrone(d, box, 0.42); gslash(d, box)
    elif uid == "armed_uas": gdrone(d, box, 0.34); gdot(d, box, 0.8, 15)
    elif uid == "loitering_munition":
        gdrone(d, box, 0.3)
        x0, y0, x1, y1 = box; cx = (x0 + x1) / 2
        d.line([cx, y0 + (y1 - y0) * 0.45, cx, y1], fill=STROKE, width=LINE)
        a = 16
        d.line([cx - a, y1 - a, cx, y1], fill=STROKE, width=LINE)
        d.line([cx + a, y1 - a, cx, y1], fill=STROKE, width=LINE)
    elif uid == "counter_uas": gdrone(d, box, 0.42, 0.9); gx(d, box)
    elif uid == "ad_radar": gdome(d, box); gzigzag(d, sub(box, 0.2, 0.25, 0.8, 0.75), 0.5, 0.3)
    elif uid == "ew_unit": gzigzag(d, sub(box, 0, 0.05, 1, 0.5), 0.5, 0.35); ctext(d, sub(box, 0, 0.5, 1, 1), "EW", 0.9)
    elif uid == "sigint": ctext(d, box, "SIG")
    elif uid == "forward_observer": ctext(d, box, "FO")
    elif uid == "target_acquisition": gcrosshair(d, box)
    elif uid == "joint_fires": ctext(d, box, "JFC")
    elif uid == "tactical_cp": gflag(d, sub(box, 0.15, 0.05, 0.85, 0.95)); ctext(d, sub(box, 0.3, 0.45, 1, 1), "CP", 0.85)
    else: ctext(d, box, "?")

UNIT_IDS = [
    "infantry", "mech_infantry", "mot_infantry", "armour", "recon",
    "special_forces", "artillery", "rocket_artillery", "mortar", "anti_tank",
    "air_defence", "sam", "engineer", "eod", "cbrn", "military_police",
    "signals", "electronic_warfare", "intelligence", "headquarters",
    "logistics", "supply", "transport", "maintenance", "medical",
    "uas_operator", "recon_uas", "armed_uas", "loitering_munition",
    "counter_uas", "ad_radar", "ew_unit", "sigint", "forward_observer",
    "target_acquisition", "joint_fires", "tactical_cp",
]

def main():
    for folder, framer in (("Friendly", frame_friendly), ("Enemy", frame_hostile)):
        out = os.path.join(OUT, folder)
        os.makedirs(out, exist_ok=True)
        for uid in UNIT_IDS:
            img, d = new_canvas()
            box = framer(d)
            draw_glyph(uid, d, box)
            img.save(os.path.join(out, uid + ".png"))
        print(f"{folder}: {len(UNIT_IDS)} icons")

    out = os.path.join(OUT, "Affiliations")
    os.makedirs(out, exist_ok=True)
    for name, framer in (("friendly", frame_friendly), ("hostile", frame_hostile),
                         ("neutral", frame_neutral), ("unknown", frame_unknown)):
        img, d = new_canvas()
        framer(d)
        img.save(os.path.join(out, name + ".png"))
    print("Affiliations: 4 frames")

if __name__ == "__main__":
    main()
