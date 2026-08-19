# Third-party content register

Eighteen folders under `Assets/` came from somewhere else. Selling the game
means every one of them is covered by a licence that permits commercial
distribution inside a compiled product, and that any attribution it requires is
actually present.

**This register is not filled in.** It cannot be filled in from the repo: most
of these packs ship a readme naming the author and nothing about terms, and the
terms depend on which store and which variant each came from — several are
`_demo` or `Free` editions whose licences differ from their paid counterparts.
Only the person who downloaded them can close this.

`make steam-check` lists the folders and warns; it does not and cannot verify
any of it. See `docs/36-STEAM.md` §1c.

## How to fill a row

- **Source** — where it came from. Unity Asset Store, a marketplace, a GitHub
  repo, a Sketchfab model. The store page URL is the useful thing to record.
- **Licence** — the actual terms. For the Asset Store that is the Standard or
  Extension Asset licence unless the publisher supplied their own.
- **Commercial** — may it be distributed inside a game that is sold? Asset
  Store Standard licences generally allow it; free and demo packs are where
  this goes wrong.
- **Attribution** — required by the licence? If yes, it goes in the Credits
  screen, and the row is not done until it is there.

Keep the evidence — an invoice, an order number, a screenshot of the licence —
somewhere outside this repo. A row that says "Asset Store, fine" with nothing
behind it is not much use if it is ever questioned.

## The register

| Folder | What it is | Source | Licence | Commercial | Attribution | Done |
|---|---|---|---|---|---|---|
| `ALSTRA INFINITE` | | | | | | ☐ |
| `Defensive_props` | Obstacle and fortification props | | | | | ☐ |
| `Hessburg - Stealth Bomber` | Aircraft model (`ReadMe.rtf`) | | | | | ☐ |
| `homing missile` | Missile model | | | | | ☐ |
| `JMO Assets` | War FX + Cartoon FX Easy Editor, © Jean Moreno | | | | | ☐ |
| `Kucher` | | | | | | ☐ |
| `LowPolySoldiers_demo` | **Demo edition** — check the paid pack's terms | | | | | ☐ |
| `M3A1 Scout Car` | WW2 scout car, 2 models (`readme.txt`) | | | | | ☐ |
| `Magic Pig Games (Infinity PBR)` | | | | | | ☐ |
| `Military Cargo Aircraft` | | | | | | ☐ |
| `Military vehicles (Sea)` | | | | | | ☐ |
| `MMAR` | Selection System (`readme.md`), bundles NaughtyAttributes | | | | | ☐ |
| `PVO` | Air defence models | | | | | ☐ |
| `QuickOutline` | Outline shader, © Chris Nolet 2018 — the upstream GitHub release is MIT, but this copy carries no licence file | | | | | ☐ |
| `Radar` | | | | | | ☐ |
| `RTS_Modern_Combat_Vehicle_Pack_Free` | **Free edition** — check what "free" permits | | | | | ☐ |
| `Vefects` | Shader/VFX pack | | | | | ☐ |
| `ZIL130_MilitaryTruck` | Truck model | | | | | ☐ |

Bundled inside `MMAR`, and worth their own rows if you keep them:

| Component | Notes |
|---|---|
| `NaughtyAttributes` | Inspector attributes, by Denis Rizov — upstream is MIT |

## Not bundled — but one decision away

**ffmpeg** encodes the video the CAPTURE section records (`docs/39-CAPTURE.md`).
Today it is **not distributed with the game**: the build looks for an ffmpeg the
player installed, and disables recording when there is none. That deliberately
keeps it off this register.

Putting `ffmpeg.exe` into `StreamingAssets` — which `CaptureSystem` will happily
use, and which would make recording work out of the box — changes that. It
becomes distribution, and ffmpeg's licence (LGPL 2.1+, or **GPL** when built
with libx264, which the encoder command uses) comes with it. If that is ever
done, it needs a row here, the licence text shipped, and the written offer for
source the licence requires.

## Things to look at first

- The two packs whose folder names say **`_demo`** and **`_Free`**. Free
  editions are frequently licensed for evaluation or non-commercial use, and
  they are the most likely single point of failure in this list.
- **`QuickOutline`** and **`NaughtyAttributes`** are code, not art, and are
  compiled into the shipped assembly. MIT is permissive but does require the
  copyright notice to travel with it — which means the Credits screen, not just
  a folder nobody sees.
- Anything that turns out to be from a **model marketplace rather than the
  Asset Store**. Those licences vary far more, and some forbid inclusion in
  interactive products outright.

## Removing one

Every model in the game is reachable through `UnitModelLibrary`, and every
effect through `VfxCatalog`, both of which have procedural fallbacks by design
(golden rules 10 and 11). A pack that cannot be licensed can be pulled and the
game will still run — degraded, not broken. That is the point of the
indirection, and it is the reason this list is a task rather than a crisis.

Check `docs/09-3D-MODELS.md` for which models a pack supplies before removing
it, so you know what falls back.

## See also

`docs/36-STEAM.md` §1c (why this matters for release) · `docs/09-3D-MODELS.md`
(model register) · `docs/08-PARTICLE-SYSTEMS.md` (effect register)
