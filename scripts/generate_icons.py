#!/usr/bin/env python3
"""Generates APP-6 inspired unit icons for Iron Meridian.

Outputs 256x256 transparent PNGs:
  Assets/Resources/Icons/Friendly/<unit_id>.png   (blue rectangle frame)
  Assets/Resources/Icons/Enemy/<unit_id>.png      (red diamond frame)
  Assets/Resources/Icons/Affiliations/{friendly,hostile,neutral,unknown}.png

Requires: pip install pillow
Run:      python scripts/generate_icons.py
"""
import json, os
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
    # Arm width comes from the box rather than from LINE: the cross is also
    # drawn small (under a MEDEVAC/HOSP tag), where a fixed 18 px arm was wider
    # than the box and filled it in solid.
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    w = min(x1 - x0, y1 - y0) * 0.18
    d.rectangle([cx - w, y0, cx + w, y1], fill=STROKE)
    d.rectangle([x0, cy - w, x1, cy + w], fill=STROKE)

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

# ---- modifier layout ----
#
# With 117 unit types a lot of symbols are "the base arm, plus a qualifier":
# airborne infantry is infantry, an attack helicopter is a helicopter. Rather
# than invent 80 unrelated pictograms, those draw the base glyph in the upper
# band and a short APP-6 style letter modifier underneath. `gmain` and `gtag`
# are the two halves of that split, so every modified symbol lines up with
# every other one.

def gmain(box):
    """Upper band: where the base arm's glyph goes when a tag sits under it."""
    return sub(box, 0.02, 0.0, 0.98, 0.70)

def gtag(d, box, s):
    """Short letter modifier under the base glyph."""
    ctext(d, sub(box, 0.05, 0.66, 0.95, 1.06), s, 1.0)

def gparachute(d, box):  # airborne
    x0, y0, x1, y1 = box
    cx = (x0 + x1) / 2
    h = y1 - y0
    d.arc([x0, y0, x1, y1 + h * 0.7], 180, 360, fill=STROKE, width=LINE)
    cy = y0 + h * 0.5
    d.line([x0 + (x1 - x0) * 0.14, cy, cx, y1], fill=STROKE, width=LINE)
    d.line([x1 - (x1 - x0) * 0.14, cy, cx, y1], fill=STROKE, width=LINE)

def grotor(d, box):  # air assault: rotor disc on a mast
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, y0 + (y1 - y0) * 0.30
    d.line([x0, cy, x1, cy], fill=STROKE, width=LINE)
    d.line([cx, cy, cx, y1], fill=STROKE, width=LINE)

def gmountain(d, box):
    x0, y0, x1, y1 = box
    cx = (x0 + x1) / 2
    d.line([(x0, y1), (cx, y0), (x1, y1)], fill=STROKE, width=LINE, joint="curve")

def gwaves(d, box):  # maritime / amphibious
    x0, y0, x1, y1 = box
    h = y1 - y0
    for ry in (0.28, 0.72):
        cy = y0 + h * ry
        a = h * 0.18
        pts, n = [], 4
        for k in range(n + 1):
            x = x0 + (x1 - x0) * k / n
            pts.append((x, cy - a if k % 2 == 0 else cy + a))
        d.line(pts, fill=STROKE, width=max(LINE - 2, 3), joint="curve")

def ghelo(d, box):  # side-on helicopter
    x0, y0, x1, y1 = box
    cx, cy = (x0 + x1) / 2, y0 + (y1 - y0) * 0.66
    w, h = (x1 - x0) * 0.5, (y1 - y0) * 0.16
    d.ellipse([cx - w * 0.75, cy - h, cx + w * 0.5, cy + h], outline=STROKE, width=LINE)
    d.line([cx + w * 0.45, cy, x1, cy - h * 0.8], fill=STROKE, width=LINE)   # tail boom
    ry = y0 + (y1 - y0) * 0.18
    d.line([x0, ry, x1, ry], fill=STROKE, width=LINE)                        # rotor
    d.line([cx - w * 0.15, ry, cx - w * 0.15, cy - h], fill=STROKE, width=LINE)

def gplane(d, box):  # fixed wing, plan view
    x0, y0, x1, y1 = box
    cx, w, h = (x0 + x1) / 2, x1 - x0, y1 - y0
    d.line([cx, y0, cx, y1], fill=STROKE, width=LINE)                        # fuselage
    wy = y0 + h * 0.52
    d.line([(x0, wy + h * 0.16), (cx, wy - h * 0.10), (x1, wy + h * 0.16)],
           fill=STROKE, width=LINE, joint="curve")
    ty = y0 + h * 0.92
    d.line([(cx - w * 0.20, ty + h * 0.08), (cx, ty), (cx + w * 0.20, ty + h * 0.08)],
           fill=STROKE, width=LINE, joint="curve")

def gship(d, box):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    deck = y0 + h * 0.58
    keel = y0 + h * 0.92
    d.line([(x0, deck), (x0 + w * 0.14, keel), (x1 - w * 0.14, keel), (x1, deck), (x0, deck)],
           fill=STROKE, width=LINE, joint="curve")
    cx = (x0 + x1) / 2
    d.line([cx, deck, cx, y0 + h * 0.12], fill=STROKE, width=LINE)           # mast

def gsub(d, box):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    cx = (x0 + x1) / 2
    d.line([x0, y0 + h * 0.06, x1, y0 + h * 0.06], fill=STROKE, width=max(LINE - 2, 3))
    cy = y0 + h * 0.72
    d.ellipse([x0 + w * 0.04, cy - h * 0.20, x1 - w * 0.04, cy + h * 0.20],
              outline=STROKE, width=LINE)
    d.rectangle([cx - w * 0.06, y0 + h * 0.32, cx + w * 0.06, cy - h * 0.14], fill=STROKE)

# ---- multi-line glyphs that do not fit a lambda ----

def _loitering(d, box):
    gdrone(d, box, 0.3)
    x0, y0, x1, y1 = box
    cx = (x0 + x1) / 2
    d.line([cx, y0 + (y1 - y0) * 0.45, cx, y1], fill=STROKE, width=LINE)
    a = 16
    d.line([cx - a, y1 - a, cx, y1], fill=STROKE, width=LINE)
    d.line([cx + a, y1 - a, cx, y1], fill=STROKE, width=LINE)

def _mot_infantry(d, box):
    gx(d, box)
    for fx in (0.25, 0.5, 0.75):
        gdot(d, sub(box, fx - 0.02, 0.96, fx + 0.02, 1.04), 0.5, 9)

# ---- glyph per unit id ----
#
# A table rather than an if/elif chain: at 117 unit types the chain was longer
# than the rest of the file and every new unit made it worse. Each entry takes
# (draw, box) and paints inside the frame's content box.
#
# The families read top to bottom the way the catalogue does — infantry X,
# armour oval, artillery dot, air-defence dome, aircraft, ships, then the
# lettered support symbols. Where a type is a qualified version of another
# (airborne infantry, attack helicopter) the base glyph goes in `gmain` and the
# qualifier in `gtag` so the whole family lines up.
GLYPHS = {
    # --- infantry ---
    "infantry":              lambda d, b: gx(d, b),
    "light_infantry":        lambda d, b: (gx(d, gmain(b)), gtag(d, b, "LT")),
    # The qualifier sits clear of the X rather than beside its top corners —
    # overlapped, the two just read as a scribble at 44 px in the palette.
    "airborne_infantry":     lambda d, b: (gx(d, sub(b, 0.12, 0.40, 0.88, 1.0)),
                                           gparachute(d, sub(b, 0.28, 0.02, 0.72, 0.26))),
    "air_assault_infantry":  lambda d, b: (gx(d, sub(b, 0.12, 0.40, 0.88, 1.0)),
                                           grotor(d, sub(b, 0.18, 0.02, 0.82, 0.28))),
    "mountain_infantry":     lambda d, b: (gx(d, sub(b, 0.12, 0.0, 0.88, 0.60)),
                                           gmountain(d, sub(b, 0.30, 0.68, 0.70, 1.0))),
    "marine_infantry":       lambda d, b: (gx(d, sub(b, 0.12, 0.0, 0.88, 0.60)),
                                           gwaves(d, sub(b, 0.18, 0.66, 0.82, 1.02))),
    "special_forces":        lambda d, b: ctext(d, b, "SF"),

    # --- armour ---
    "armour":                lambda d, b: goval(d, b, 1.15),
    "combined_arms":         lambda d, b: (goval(d, gmain(b), 1.15), gtag(d, b, "CA")),
    "armoured_recon":        lambda d, b: (goval(d, gmain(b), 1.1), gslash(d, gmain(b))),
    "recon":                 lambda d, b: gslash(d, b),
    "anti_tank":             lambda d, b: gchevron_up(d, b),

    # --- mechanised ---
    "mech_infantry":         lambda d, b: (gx(d, b), goval(d, b, 0.9)),
    "mot_infantry":          _mot_infantry,

    # --- artillery ---
    "artillery":             lambda d, b: gdot(d, b, 0.5, 24),
    "self_propelled_artillery": lambda d, b: (goval(d, b, 1.15), gdot(d, b, 0.5, 18)),
    "towed_artillery":       lambda d, b: (gdot(d, gmain(b), 0.45, 22),
                                           gdot(d, sub(b, 0.16, 0.74, 0.30, 0.96), 0.5, 10),
                                           gdot(d, sub(b, 0.70, 0.74, 0.84, 0.96), 0.5, 10)),
    "rocket_artillery":      lambda d, b: (gdot(d, b, 0.72, 20),
                                           garrow_up(d, sub(b, 0, 0, 1, 0.75))),
    "mortar":                lambda d, b: (gdot(d, b, 0.85, 16),
                                           garrow_up(d, sub(b, 0, 0.05, 1, 0.9))),
    "surface_to_surface_missile": lambda d, b: (garrow_up(d, gmain(b)), gtag(d, b, "SSM")),
    "deep_precision_strike": lambda d, b: (garrow_up(d, gmain(b)), gtag(d, b, "DPS")),
    "coastal_defence_missile": lambda d, b: (garrow_up(d, gmain(b)),
                                             gwaves(d, sub(b, 0.14, 0.70, 0.86, 1.02))),
    "forward_observer":      lambda d, b: ctext(d, b, "FO"),
    "joint_fires":           lambda d, b: ctext(d, b, "JFC"),
    "jtac":                  lambda d, b: ctext(d, b, "JTAC"),
    "target_acquisition":    lambda d, b: gcrosshair(d, b),
    "counter_battery_radar": lambda d, b: (gcrosshair(d, gmain(b)), gtag(d, b, "CB")),

    # --- anti-aircraft ---
    "manpads":               lambda d, b: (gdome(d, gmain(b)), gtag(d, b, "MAN")),
    "air_defence":           lambda d, b: gdome(d, b),
    "shorad":                lambda d, b: (gdome(d, gmain(b)), gtag(d, b, "SHO")),
    # APP-6 would nest the AD dome inside an armour oval; at this size the two
    # curves merge into an almond. The dome plus a tag stays readable and keeps
    # it in line with the rest of the air-defence family.
    "spaag":                 lambda d, b: (gdome(d, gmain(b)), gtag(d, b, "SPG")),
    "mrad":                  lambda d, b: (gdome(d, gmain(b)),
                                           garrow_up(d, sub(b, 0, 0.08, 1, 0.70)),
                                           gtag(d, b, "MR")),
    "sam":                   lambda d, b: (gdome(d, b), garrow_up(d, sub(b, 0, 0.1, 1, 1))),
    "lrad":                  lambda d, b: (gdome(d, gmain(b)),
                                           garrow_up(d, sub(b, 0, 0.08, 1, 0.70)),
                                           gtag(d, b, "LR")),
    "missile_defence":       lambda d, b: (gdome(d, gmain(b)),
                                           garrow_up(d, sub(b, 0, 0.08, 1, 0.70)),
                                           gtag(d, b, "BMD")),
    "counter_uas":           lambda d, b: (gdrone(d, b, 0.42, 0.9), gx(d, b)),
    "ad_radar":              lambda d, b: (gdome(d, b),
                                           gzigzag(d, sub(b, 0.2, 0.25, 0.8, 0.75), 0.5, 0.3)),
    "air_surveillance_radar": lambda d, b: (gdome(d, gmain(b)),
                                            gzigzag(d, sub(b, 0.10, 0.72, 0.90, 1.0), 0.5, 0.34)),
    "air_defence_c2":        lambda d, b: (gdome(d, gmain(b)), gtag(d, b, "C2")),

    # --- air: manned ---
    "attack_helicopter":     lambda d, b: (ghelo(d, gmain(b)), gtag(d, b, "ATK")),
    "recon_helicopter":      lambda d, b: (ghelo(d, gmain(b)), gtag(d, b, "RCN")),
    "transport_helicopter":  lambda d, b: (ghelo(d, gmain(b)), gtag(d, b, "TPT")),
    "utility_helicopter":    lambda d, b: (ghelo(d, gmain(b)), gtag(d, b, "UTL")),
    # A letter tag rather than a cross: at the tag band's size the cross is a
    # blob, and it reads better as one of the five helicopters than as a
    # medical symbol that happens to have a rotor.
    "medevac_helicopter":    lambda d, b: (ghelo(d, gmain(b)), gtag(d, b, "MED")),
    "cas_aircraft":          lambda d, b: (gplane(d, gmain(b)), gtag(d, b, "CAS")),
    "strike_aircraft":       lambda d, b: (gplane(d, gmain(b)), gtag(d, b, "STK")),
    "fighter_aircraft":      lambda d, b: (gplane(d, gmain(b)), gtag(d, b, "FTR")),
    "isr_aircraft":          lambda d, b: (gplane(d, gmain(b)), gtag(d, b, "ISR")),
    "aewc":                  lambda d, b: (gplane(d, gmain(b)), gtag(d, b, "AEW")),
    "transport_aircraft":    lambda d, b: (gplane(d, gmain(b)), gtag(d, b, "TAL")),
    "aerial_refuelling":     lambda d, b: (gplane(d, gmain(b)), gtag(d, b, "AAR")),
    "ew_aircraft":           lambda d, b: (gplane(d, gmain(b)), gtag(d, b, "EW")),

    # --- air: unmanned ---
    "uas_operator":          lambda d, b: (gdrone(d, b, 0.34),
                                           ctext(d, sub(b, 0, 0.58, 1, 1), "OP", 0.9)),
    "recon_uas":             lambda d, b: (gdrone(d, b, 0.42), gslash(d, b)),
    "armed_uas":             lambda d, b: (gdrone(d, b, 0.34), gdot(d, b, 0.8, 15)),
    "loitering_munition":    _loitering,
    "fpv_attack_uas":        lambda d, b: (gdrone(d, gmain(b), 0.40), gtag(d, b, "FPV")),
    "deep_strike_uas":       lambda d, b: (gdrone(d, gmain(b), 0.40), gtag(d, b, "DS")),
    "interceptor_uas":       lambda d, b: (gdrone(d, gmain(b), 0.40), gtag(d, b, "ITC")),
    "ew_uas":                lambda d, b: (gdrone(d, gmain(b), 0.40), gtag(d, b, "EW")),
    "relay_uas":             lambda d, b: (gdrone(d, gmain(b), 0.40), gtag(d, b, "RLY")),
    "cargo_uas":             lambda d, b: (gdrone(d, gmain(b), 0.40), gtag(d, b, "CGO")),
    "decoy_uas":             lambda d, b: (gdrone(d, gmain(b), 0.40), gtag(d, b, "DEC")),

    # --- navy ---
    "surface_combatant":     lambda d, b: (gship(d, gmain(b)), gtag(d, b, "SC")),
    "submarine":             lambda d, b: (gsub(d, gmain(b)), gtag(d, b, "SS")),
    "patrol_craft":          lambda d, b: (gship(d, gmain(b)), gtag(d, b, "PC")),
    "mine_countermeasure_ship": lambda d, b: (gship(d, gmain(b)), gtag(d, b, "MCM")),
    "amphibious_ship":       lambda d, b: (gship(d, gmain(b)), gtag(d, b, "AMP")),
    "naval_logistics":       lambda d, b: (gship(d, gmain(b)), gtag(d, b, "LOG")),

    # --- logistics ---
    "logistics":             lambda d, b: ctext(d, b, "LOG"),
    "supply":                lambda d, b: ghline(d, b),
    "ammunition":            lambda d, b: ctext(d, b, "AMMO"),
    "fuel_pol":              lambda d, b: ctext(d, b, "POL"),
    "transport":             lambda d, b: ctext(d, b, "TPT"),
    "movement_control":      lambda d, b: ctext(d, b, "MC"),
    "maintenance":           lambda d, b: ctext(d, b, "MNT"),
    "recovery":              lambda d, b: ctext(d, b, "REC"),
    "ordnance":              lambda d, b: ctext(d, b, "ORD"),
    "medical":               lambda d, b: gcross(d, sub(b, 0.2, 0.05, 0.8, 0.95)),
    "medevac":               lambda d, b: (gcross(d, sub(b, 0.28, 0.02, 0.72, 0.62)),
                                           gtag(d, b, "EVAC")),
    "field_hospital":        lambda d, b: (gcross(d, sub(b, 0.28, 0.02, 0.72, 0.62)),
                                           gtag(d, b, "HOSP")),
    "water_supply":          lambda d, b: ctext(d, b, "H2O"),
    "field_services":        lambda d, b: ctext(d, b, "FS"),
    "personnel_support":     lambda d, b: ctext(d, b, "PERS"),

    # --- other: command, engineer, ISR, information ---
    "headquarters":          lambda d, b: ctext(d, b, "HQ"),
    "tactical_cp":           lambda d, b: (gflag(d, sub(b, 0.15, 0.05, 0.85, 0.95)),
                                           ctext(d, sub(b, 0.3, 0.45, 1, 1), "CP", 0.85)),
    "signals":               lambda d, b: gzigzag(d, b),
    "engineer":              lambda d, b: gengineer(d, b),
    "assault_engineer":      lambda d, b: (gengineer(d, gmain(b)), gtag(d, b, "ASLT")),
    "bridging":              lambda d, b: (gengineer(d, gmain(b)), gtag(d, b, "BRG")),
    "route_clearance":       lambda d, b: (gengineer(d, gmain(b)), gtag(d, b, "RTE")),
    "counter_ied":           lambda d, b: ctext(d, b, "CIED"),
    "mine_clearance":        lambda d, b: (gengineer(d, gmain(b)), gtag(d, b, "MCL")),
    "mine_laying":           lambda d, b: (gengineer(d, gmain(b)), gtag(d, b, "MINE")),
    "construction_engineer": lambda d, b: (gengineer(d, gmain(b)), gtag(d, b, "CON")),
    "geospatial_engineer":   lambda d, b: (gengineer(d, gmain(b)), gtag(d, b, "GEO")),
    "eod":                   lambda d, b: ctext(d, b, "EOD"),
    "cbrn":                  lambda d, b: ctext(d, b, "CBRN"),
    "smoke_obscuration":     lambda d, b: ctext(d, b, "SMK"),
    "camouflage_deception":  lambda d, b: ctext(d, b, "CAM"),
    "military_police":       lambda d, b: ctext(d, b, "MP"),
    "intelligence":          lambda d, b: ctext(d, b, "INT"),
    "humint":                lambda d, b: ctext(d, b, "HUM"),
    "counter_intelligence":  lambda d, b: ctext(d, b, "CI"),
    "geoint":                lambda d, b: ctext(d, b, "GEO"),
    "sigint":                lambda d, b: ctext(d, b, "SIG"),
    "battlefield_surveillance": lambda d, b: (gcrosshair(d, gmain(b)), gtag(d, b, "BSU")),
    "surveillance_radar":    lambda d, b: (gzigzag(d, gmain(b), 0.5, 0.30), gtag(d, b, "GSR")),
    "electronic_warfare":    lambda d, b: (ctext(d, sub(b, 0, 0, 1, 0.62), "EW"),
                                           gzigzag(d, sub(b, 0.1, 0.62, 0.9, 1), 0.5, 0.3)),
    "ew_unit":               lambda d, b: (gzigzag(d, sub(b, 0, 0.05, 1, 0.5), 0.5, 0.35),
                                           ctext(d, sub(b, 0, 0.5, 1, 1), "EW", 0.9)),
    "cyber_defence":         lambda d, b: ctext(d, b, "CYD"),
    "cyber_operations":      lambda d, b: ctext(d, b, "CYO"),
    "psyops":                lambda d, b: ctext(d, b, "PSY"),
    "cimic":                 lambda d, b: ctext(d, b, "CMC"),
    "public_affairs":        lambda d, b: ctext(d, b, "PA"),
    "meteorological":        lambda d, b: ctext(d, b, "MET"),
    "space_support":         lambda d, b: ctext(d, b, "SPC"),
}

def draw_glyph(uid, d, box):
    GLYPHS.get(uid, lambda dd, bb: ctext(dd, bb, "?"))(d, box)

def unit_ids():
    """
    The ids to draw, read from the shipped catalogue rather than kept in a list
    here. A hand-maintained list silently stops producing icons for units added
    to the generator, and a missing icon only shows up as a "?" placeholder in
    the palette — so the two are tied together instead.
    """
    path = os.path.join(ROOT, "Assets", "StreamingAssets", "Data", "units.json")
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    ids = [u["id"] for u in data["units"]]
    missing = [i for i in ids if i not in GLYPHS]
    if missing:
        raise SystemExit(
            "No glyph for: " + ", ".join(missing) +
            "\nAdd a GLYPHS entry for each before regenerating.")
    stale = [i for i in GLYPHS if i not in ids]
    if stale:
        print("warning: glyphs with no unit in units.json: " + ", ".join(stale))
    return ids

def main():
    ids = unit_ids()
    for folder, framer in (("Friendly", frame_friendly), ("Enemy", frame_hostile)):
        out = os.path.join(OUT, folder)
        os.makedirs(out, exist_ok=True)
        for uid in ids:
            img, d = new_canvas()
            box = framer(d)
            draw_glyph(uid, d, box)
            img.save(os.path.join(out, uid + ".png"))
        print(f"{folder}: {len(ids)} icons")

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
