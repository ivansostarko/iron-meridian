# Iron Meridian

**A real-terrain operational wargame built with Unity 6 and Cesium 3D geospatial maps.**

Deploy Blue (User) and Red (Enemy) forces on real 3D terrain streamed from Cesium ion, draw sector boundaries and defensive lines, then start the battle and watch different unit powers reshape the front line — HOI-IV style operational play at tactical map fidelity.

**Website: [iron-meridian.sostarko.me](https://iron-meridian.sostarko.me)** · Repository: `github.com/ivansostark/iron-meridian`

![Status](https://img.shields.io/badge/Status-In%20development-orange) ![Unity](https://img.shields.io/badge/Unity-6000.0%20LTS-black) ![Cesium](https://img.shields.io/badge/Cesium%20for%20Unity-1.24-blue) ![Platform](https://img.shields.io/badge/Platform-Windows%2064--bit-informational)

![Iron Meridian — the front line over real terrain](https://iron-meridian-storage.sostarko.me/screenshoots/gallery-1.png)

---

## Status — in development, and playable

**Iron Meridian is under active development.** Systems are being added and changed
regularly, saves and data files can change shape between builds, and parts of the
interface are ahead of the systems behind them.

**It can already be played and tested.** The map editor and the single-player
campaigns both run end to end: deploy an order of battle on real terrain, give
orders, call fire, and fight the battle out.

| You can | Where |
|---|---|
| Fight a scenario | Main menu → **SINGLE PLAYER** → a campaign → a mission |
| Build one | Main menu → **DEVELOPMENT** → **MAP EDITOR** |
| Inspect the data behind it | **DEVELOPMENT** → Units List · Particles · Audio · Videos · 3D Models |

Six campaigns are laid in with ninety missions across real ground — Europe,
Africa, Asia, North America, South America and Australia, spanning 1990 to 2025.
Each carries its place, its date, its weather and a briefing, and opens on its
own terrain; the order of battle on each is authored in the map editor, which is
where the work is going next. See [docs/22-MISSIONS.md](docs/22-MISSIONS.md).

Building it yourself needs a free **Cesium ion token** — the terrain is streamed,
so nothing renders without one. See [Quick start](#quick-start-windows) below.

---

## Features

**The world**

- **Cesium 3D terrain** — real elevation, satellite imagery and OSM buildings, streamed at runtime. Everything is positioned geodetically (WGS84), so a saved scenario is a place on Earth rather than a set of scene coordinates
- **2D / 3D switch** for the camera and for every control measure drawn on the ground
- **Operational clock** with H-hour, time compression, an automatic day/night cycle, and six weather conditions that change what can be seen and heard

**The forces**

- **117 unit types** across nine arms of service, each with manpower, training, morale, combat power, ammunition type and stocks, fuel and food — all data, never hard-coded
- **APP-6 style icons** generated for both sides, with Friendly / Hostile / Neutral / Unknown affiliations
- **Full echelon ladder**, Team through Army, with drag-and-drop deployment from the order-of-battle palette
- **Chain of command** — twenty officers a side in a pyramid; knocking out a headquarters degrades everything under it
- **Logistics and sustainment** — depots, supply, fuel, ammunition, repair and medical points, and the stocks a force burns through while it fights
- **Reinforcements** that arrive at H+n in their side's deployment zone

**Fighting**

- **Orders** — move and march, attack, defend, recon, with the ground each order puts down drawn on the map
- **Called fires** — artillery by nature and calibre, air strikes, UAV sorties, missile systems and naval gunfire, each with its own effects and report
- **Air defence** that engages drones and aircraft over the formations it covers
- **Fog of war** — the enemy is seen only where something of yours can see them, with contacts that decay into a growing ring of uncertainty
- **A front line that answers to the fighting** — the FLOT is derived from where the formations stand and what they are worth, not drawn by hand
- **Mines and obstacles**, laid as NATO barrier graphics
- **Map objects** — bridges, airfields, ports, rail yards and built-up areas, drawn on the terrain

**Around the game**

- **Six campaigns, ninety missions** on real ground from 1990 to 2025 — see [docs/22-MISSIONS.md](docs/22-MISSIONS.md)
- **A map editor that is the same scene as the game**, so a mission edited in it is the mission that is played
- **Settings** — video (resolution, quality, anti-aliasing, shadows, textures, frame rate), audio (master plus four channels), and a controls reference
- **In-game tuning** of every unit and weapon catalogue, saved as a patch over the shipped data so regenerating the catalogues never discards it
- **Per-map JSON saves** holding every unit's position and status

## Quick start (Windows)

1. Install **Unity Hub** and **Unity 6000.0 LTS** (Windows Build Support IL2CPP + Mono).
2. Clone: `git clone https://github.com/ivansostark/iron-meridian.git`
3. Open the folder in Unity Hub. First open resolves the **Cesium for Unity** package automatically.
4. **Add your Cesium ion token** — see [docs/02-CESIUM.md](docs/02-CESIUM.md). Short version: paste it into `Assets/StreamingAssets/cesium-token.txt`.
5. Run **Tools → Iron Meridian → Setup Project** (creates the scenes + build settings).
6. Press **Play**. Main menu → **DEVELOPMENT** → **MAP EDITOR** → you are over Lyon.
   Or **SINGLE PLAYER** → a campaign → a mission, to open one on its own ground.

Full guide: [docs/01-GETTING-STARTED.md](docs/01-GETTING-STARTED.md)

### Every routine job in one place

```powershell
make            # the menu: build, installer, data, icons, models, doctor, clean...
make doctor     # what's installed, what's missing, and the command that fixes it
```

No make? `.\scripts\menu.ps1` is the same menu with a picker, and every job is a
script you can run directly. See [docs/35-TASKS.md](docs/35-TASKS.md).

### Making a build others can install

```powershell
make installer          # or: .\scripts\build-windows.ps1 -Clean -Installer
```

Builds the player and packages it as `Builds\Installer\IronMeridian-<version>-Setup.exe`
— Start-menu and desktop shortcuts, a proper uninstaller that offers to keep
saves, and **no Cesium ion token** unless you ask for one. Needs
[Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install --id JRSoftware.InnoSetup`).
See [docs/34-INSTALLER.md](docs/34-INSTALLER.md).

## Documentation

Thirty-five documents under [`docs/`](docs/). Several are **registers** — the
human-readable half of a catalogue in code, and the rule is that they are updated
in the same change as the catalogue, never afterwards.

### Start here

| Doc | Contents |
|---|---|
| [01-GETTING-STARTED](docs/01-GETTING-STARTED.md) | Windows setup, first run |
| [02-CESIUM](docs/02-CESIUM.md) | Cesium overview, ion account, **where to put the API token** |
| [03-GAMEPLAY](docs/03-GAMEPLAY.md) | Screens, the editor's rail, controls, teams, combat |
| [07-ARCHITECTURE](docs/07-ARCHITECTURE.md) | Code map and design decisions — **read this first when changing code** |

### The game

| Doc | Contents |
|---|---|
| [04-UNITS](docs/04-UNITS.md) | All 117 unit types, their attributes, the icon system |
| [15-COMBAT-ORDERS](docs/15-COMBAT-ORDERS.md) | Every order a formation can be given in battle |
| [16-FOG-OF-WAR](docs/16-FOG-OF-WAR.md) | Limited intelligence, contacts and recon tasks |
| [22-MISSIONS](docs/22-MISSIONS.md) | The six campaigns and ninety missions, and how a mission is stored |
| [23-COMMANDERS](docs/23-COMMANDERS.md) | The chain of command above the units, and what breaking it costs |
| [25-PLAYERS](docs/25-PLAYERS.md) | Teams, players, and the computer's difficulty |
| [28-FLOT](docs/28-FLOT.md) | The front line as a gameplay object |
| [30-REINFORCEMENTS](docs/30-REINFORCEMENTS.md) | Formations that arrive after H-hour |

### Fires and effects

| Doc | Contents |
|---|---|
| [17-ARTILLERY](docs/17-ARTILLERY.md) | Called fire missions — the artillery nature register |
| [18-AIR-STRIKES](docs/18-AIR-STRIKES.md) | Tasked air strikes — the airframe register |
| [19-UAV-STRIKES](docs/19-UAV-STRIKES.md) | Unmanned sorties — the UAV type register |
| [20-MISSILE-SYSTEMS](docs/20-MISSILE-SYSTEMS.md) | The missile system register |
| [21-NAVAL-GUNFIRE](docs/21-NAVAL-GUNFIRE.md) | The naval gun register |
| [24-AIR-DEFENCE](docs/24-AIR-DEFENCE.md) | Automatic engagements against drones and aircraft |
| [29-AIR-SUPPLY](docs/29-AIR-SUPPLY.md) | Air-dropped loads |

### The rear area

| Doc | Contents |
|---|---|
| [26-LOGISTICS](docs/26-LOGISTICS.md) | Depots, supply, fuel, ammunition, repair and medical points |
| [27-SUSTAINMENT](docs/27-SUSTAINMENT.md) | What a force fights on, and what it burns |
| [31-OBSTACLES](docs/31-OBSTACLES.md) | Mines, wire, ditches and roadblocks |
| [33-MAP-OBJECTS](docs/33-MAP-OBJECTS.md) | **Map object register** — bridges, airfields, ports, built-up areas |

### Presentation

| Doc | Contents |
|---|---|
| [08-PARTICLE-SYSTEMS](docs/08-PARTICLE-SYSTEMS.md) | Fire, smoke, explosions and dust — **effect register** |
| [09-3D-MODELS](docs/09-3D-MODELS.md) | **Model register** — where each came from and where it is shown |
| [10-AUDIO](docs/10-AUDIO.md) | **Audio register** — music, weather beds, effect sounds, interface |
| [11-GAME-MENU](docs/11-GAME-MENU.md) | **Background register** — the menus and their artwork |
| [12-LOADERS](docs/12-LOADERS.md) | **Loader register** — everything that makes the player wait |
| [32-VIDEO](docs/32-VIDEO.md) | **Video register** — every film, and the lab that plays them |
| [13-DATE-AND-TIME](docs/13-DATE-AND-TIME.md) | The operational clock |
| [14-WEATHER](docs/14-WEATHER.md) | Sky phase, weather conditions and the day/night cycle |

### Files and builds

| Doc | Contents |
|---|---|
| [05-MAP-SAVES](docs/05-MAP-SAVES.md) | Map save JSON format and locations |
| [06-WINDOWS-BUILD](docs/06-WINDOWS-BUILD.md) | Building the Windows player |
| [34-INSTALLER](docs/34-INSTALLER.md) | Packaging that player as a Windows setup `.exe` |
| [35-TASKS](docs/35-TASKS.md) | The Makefile and task menu — every routine job in one place |

## AI-assisted development

The repo ships Claude-ready: [`CLAUDE.md`](CLAUDE.md) gives AI assistants the project context, and `.claude/` contains commands and skills for common tasks (adding units, regenerating icons, building). See [docs/07-ARCHITECTURE.md](docs/07-ARCHITECTURE.md).

## Project layout

```
Assets/
  Editor/                   ProjectBootstrap — generates every scene from code
  Scenes/                   Generated; no UI is authored in them (golden rule 2)
  Scripts/                  All C# — runtime-built UI, no binary prefabs
    Audio/                  music, weather beds, effect sounds, interface sounds
    Core/                   GameController, GameConfig, the clock, display settings
    Data/                   units, missions, commanders, players — the catalogues
    Lines/                  front line, boundaries, obstacles, mission areas
    Logistics/              installations and the rear area
    Map/                    Cesium georeference, camera rig, geodetic maths
    Models/                 3D model library and the installer
    Save/                   map saves, mission library, tuning patch
    UI/                     every screen; UIFactory builds all of it at runtime
    Units/                  actors, selection, combat, commanders
    Vfx/                    particle catalogue, strikes, air supply
    Weather/                sky phase and conditions
  Resources/                everything loaded at runtime (the only way, see docs)
    Audio/                  music, weather, interface sounds
    Graphics/               backgrounds, campaign artwork, logo, commander portraits
    Icons/                  generated APP-6 icons (Friendly, Enemy, Affiliations)
    Models/  VFX/  Shaders/  Videos/
  StreamingAssets/
    Data/units.json         unit catalogue — 117 types, both teams
    Data/missions.json      the mission book — 6 campaigns, 90 missions
    Maps/lyon_dev.json      the default Lyon scenario
    cesium-token.txt        <- YOUR CESIUM ION TOKEN GOES HERE (git-ignored)
installer/                  Inno Setup script + generated wizard artwork
docs/                       35 documents; several are registers — see above
Makefile                    task runner — `make` lists every job
scripts/                    menu.ps1 (the job table) · unity-run.ps1 ·
                            build-windows.ps1 · build-installer.ps1 ·
                            generate_units.py · generate_icons.py ·
                            generate_stat_icons.py · generate_units_doc.py ·
                            generate_installer_art.py
.claude/                    Claude Code commands and skills
CLAUDE.md                   AI assistant project brief and the golden rules
```

Third-party art and model packs sit in their own top-level folders under
`Assets/` (vehicles, aircraft, effects, props). They are optional: every model
resolves through `UnitModelLibrary` and every effect has a procedural fallback,
so the game runs with the packs removed — see
[docs/09-3D-MODELS.md](docs/09-3D-MODELS.md) and
[docs/08-PARTICLE-SYSTEMS.md](docs/08-PARTICLE-SYSTEMS.md).

The player's own files — saved maps, the mission book, unit tuning, settings —
live in `%USERPROFILE%/AppData/LocalLow/IvanSostarko/Iron Meridian/`, never in
the repository.

## License & data attribution

Game code © Ivan Šoštarko. Map data streamed at runtime via [Cesium ion](https://cesium.com/) (Cesium World Terrain, Bing Maps imagery, OSM Buildings) — subject to Cesium ion terms and data attribution requirements.
