#!/usr/bin/env python3
"""Generate the application icon and the installer's artwork from the game logo.

Everything is derived from
``Assets/Resources/Graphics/Logo/game-logo.png`` so the icon, the wizard and the
game can never drift apart — regenerate instead of hand-editing.

    python scripts/generate_installer_art.py

Writes into ``installer/assets/``:

    iron-meridian.ico          16/24/32/48/64/128/256 px application icon
    wizard-large*.bmp          welcome/finished page panel (3 DPI sizes)
    wizard-small*.bmp          header strip on the inner pages (3 DPI sizes)

and into ``Assets/AppIcon/``:

    icon-<n>.png               the same mark at Unity's standalone icon sizes,
                               applied to the player by ProjectBootstrap

Requires Pillow (``pip install pillow``) — same dependency as
``generate_icons.py``.
"""

from __future__ import annotations

import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:  # pragma: no cover - dependency guidance
    sys.exit("Pillow is required: pip install pillow")

ROOT = Path(__file__).resolve().parent.parent
LOGO = ROOT / "Assets" / "Resources" / "Graphics" / "Logo" / "game-logo.png"
OUT = ROOT / "installer" / "assets"
# Outside Resources on purpose: these are build-time assets Unity copies into
# the executable's resources, not something the game loads at runtime.
ICON_OUT = ROOT / "Assets" / "AppIcon"

# UiTheme.AppBackground / UiTheme.Accent, so the installer reads as the game.
BACKGROUND = (10, 14, 20)
ACCENT = (46, 129, 240)

ICON_SIZES = [16, 24, 32, 48, 64, 128, 256]
# What PlayerSettings.GetIconSizes reports for a standalone player.
UNITY_ICON_SIZES = [16, 32, 48, 64, 128, 256, 512, 1024]
# Inno Setup 6 picks the closest size to the user's DPI (100% / 125% / 150%).
LARGE_SIZES = [(164, 314), (192, 386), (256, 481)]
SMALL_SIZES = [(55, 58), (64, 68), (92, 97)]


def load_logo() -> Image.Image:
    if not LOGO.exists():
        sys.exit(f"Logo not found: {LOGO}")
    return Image.open(LOGO).convert("RGBA")


def trim(image: Image.Image) -> Image.Image:
    """Crop fully transparent margins so the logo fills the space it is given."""
    bbox = image.getbbox()
    return image.crop(bbox) if bbox else image


def fit(image: Image.Image, box: tuple[int, int], margin: float) -> Image.Image:
    """Scale ``image`` to sit inside ``box`` with a proportional margin."""
    max_w = max(1, int(box[0] * (1 - margin * 2)))
    max_h = max(1, int(box[1] * (1 - margin * 2)))
    scale = min(max_w / image.width, max_h / image.height)
    size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    return image.resize(size, Image.LANCZOS)


def backdrop(box: tuple[int, int]) -> Image.Image:
    """Near-black panel with a subtle accent glow and a hairline rule."""
    w, h = box
    canvas = Image.new("RGB", box, BACKGROUND)
    draw = ImageDraw.Draw(canvas)

    # Vertical wash: a touch of the accent bled into the background at the top.
    for y in range(h):
        t = 1.0 - (y / max(1, h - 1))
        t = t * t * 0.22
        draw.line(
            [(0, y), (w, y)],
            fill=tuple(
                round(BACKGROUND[i] + (ACCENT[i] - BACKGROUND[i]) * t) for i in range(3)
            ),
        )

    # Hairline along the bottom edge, the same trick the in-game panels use.
    draw.line([(0, h - 1), (w, h - 1)], fill=(30, 43, 57))
    return canvas


def emblem(logo: Image.Image) -> Image.Image:
    """Split the emblem off the wordmark beside it.

    The full logo is nearly 3:1, and squeezed into a 16 px icon it reads as a
    smudge. The mark on its left is roughly square and survives the shrink, so
    cut at the first empty gutter wide enough to be the gap between the two.
    """
    # Averaging the alpha down to a single row gives one opacity per column.
    columns = list(logo.getchannel("A").resize((logo.width, 1), Image.BOX).tobytes())
    threshold = max(columns) * 0.01
    gutter = max(4, logo.width // 100)

    run = 0
    for x, weight in enumerate(columns):
        if weight <= threshold:
            run += 1
            # Only trust a gutter once past the mark itself, never a gap inside it.
            if run >= gutter and x > logo.height // 2:
                return trim(logo.crop((0, 0, x - run + 1, logo.height)))
        else:
            run = 0
    return logo  # One solid block — nothing to split, use it whole.


def square_icon(art: Image.Image, size: int) -> Image.Image:
    """Centre the mark on a square plate the colour of the game's own chrome."""
    canvas = Image.new("RGBA", (size, size), BACKGROUND + (255,))
    scaled = fit(art, (size, size), margin=0.06)
    canvas.alpha_composite(
        scaled, ((size - scaled.width) // 2, (size - scaled.height) // 2)
    )
    return canvas


def write_icon(logo: Image.Image) -> Path:
    path = OUT / "iron-meridian.ico"
    mark = emblem(logo)
    largest = square_icon(mark, max(ICON_SIZES))
    largest.save(path, format="ICO", sizes=[(s, s) for s in ICON_SIZES])
    return path


def write_wizard(logo: Image.Image, sizes, stem: str, margin: float) -> list[Path]:
    written = []
    for box in sizes:
        canvas = backdrop(box)
        art = fit(logo, box, margin)
        canvas.paste(art, ((box[0] - art.width) // 2, (box[1] - art.height) // 2), art)
        path = OUT / f"{stem}-{box[0]}x{box[1]}.bmp"
        canvas.save(path, format="BMP")
        written.append(path)
    return written


def write_app_icons(logo: Image.Image) -> list[Path]:
    """The player's own icon, one PNG per size Unity asks for.

    Unity wants textures in the project rather than an .ico, and picks the
    entry matching each size it needs. Same emblem as the installer, so the
    shortcut, the taskbar and the Steam library all show one mark.
    """
    mark = emblem(logo)
    written = []
    for size in UNITY_ICON_SIZES:
        path = ICON_OUT / f"icon-{size}.png"
        square_icon(mark, size).save(path, format="PNG")
        written.append(path)
    return written


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    ICON_OUT.mkdir(parents=True, exist_ok=True)
    logo = trim(load_logo())

    written = [write_icon(logo)]
    written += write_wizard(logo, LARGE_SIZES, "wizard-large", margin=0.10)
    written += write_wizard(logo, SMALL_SIZES, "wizard-small", margin=0.08)
    written += write_app_icons(logo)

    for path in written:
        print(f"  {path.relative_to(ROOT)}  ({path.stat().st_size // 1024} KB)")
    print(
        f"Wrote {len(written)} files to {OUT.relative_to(ROOT)} "
        f"and {ICON_OUT.relative_to(ROOT)}"
    )


if __name__ == "__main__":
    main()
