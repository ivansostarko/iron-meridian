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
| Inspect the data behind it | **DEVELOPMENT** → Units List · Particles · Audio |

Six campaigns are laid in with ninety missions across real ground — Europe,
Africa, Asia, North America, South America and Australia, spanning 1990 to 2025.
Each carries its place, its date, its weather and a briefing, and opens on its
own terrain; the order of battle on each is authored in the map editor, which is
where the work is going next. See [docs/22-MISSIONS.md](docs/22-MISSIONS.md).

Building it yourself needs a free **Cesium ion token** — the terrain is streamed,
so nothing renders without one. See [Quick start](#quick-start-windows) below.

---

## Features

- **Main menu** with Testing, Settings (Video + Audio tabs) and Quit (with confirmation modal)
- **Cesium 3D world**: real terrain + satellite imagery + OSM buildings, default map centred on **Lyon, France**
- **2D / 3D switch** for both the game view and drawn lines
- **117 unit types** — organised into nine arms of service (Infantry, Armour, Mechanised, Artillery, Anti-Aircraft, Air, Navy, Logistics, Other) — each with manpower, training, morale, combat power, ammunition type & stocks, fuel and food
- **Two teams** (User = Blue, Enemy = Red) with APP-6 style **custom icons** for every unit of both teams, plus Friendly / Hostile / Neutral / Unknown affiliations
- **Full echelon ladder**: Team → Squad → Section → Platoon → Company → Battalion → Regiment → Brigade → Division → Corps → Army
- **Drag & drop deployment** from the left-side order-of-battle palette for both teams
- **Click-to-move** with smooth animated movement and destination markers (LMB select, RMB order)
- **Boundary & defensive line drawing** (2D or 3D, terrain-following)
- **Auto front line**: as units move, fight and die, the boundary between the teams updates automatically, weighted by combat power
- **Per-map JSON saves** storing every unit's position and full status

## Quick start (Windows)

1. Install **Unity Hub** and **Unity 6000.0 LTS** (Windows Build Support IL2CPP + Mono).
2. Clone: `git clone https://github.com/ivansostark/iron-meridian.git`
3. Open the folder in Unity Hub. First open resolves the **Cesium for Unity** package automatically.
4. **Add your Cesium ion token** — see [docs/02-CESIUM.md](docs/02-CESIUM.md). Short version: paste it into `Assets/StreamingAssets/cesium-token.txt`.
5. Run **Tools → Iron Meridian → Setup Project** (creates the scenes + build settings).
6. Press **Play**. Main menu → **DEVELOPMENT** → **MAP EDITOR** → you are over Lyon.
   Or **SINGLE PLAYER** → a campaign → a mission, to open one on its own ground.

Full guide: [docs/01-GETTING-STARTED.md](docs/01-GETTING-STARTED.md)

## Documentation

| Doc | Contents |
|---|---|
| [docs/01-GETTING-STARTED.md](docs/01-GETTING-STARTED.md) | Windows setup, first run |
| [docs/02-CESIUM.md](docs/02-CESIUM.md) | Cesium overview, ion account, **where to put the API token** |
| [docs/03-GAMEPLAY.md](docs/03-GAMEPLAY.md) | Screens, controls, teams, combat, lines |
| [docs/04-UNITS.md](docs/04-UNITS.md) | All units, attributes, icon system |
| [docs/05-MAP-SAVES.md](docs/05-MAP-SAVES.md) | Map save JSON format and locations |
| [docs/06-WINDOWS-BUILD.md](docs/06-WINDOWS-BUILD.md) | Building the Windows player |
| [docs/07-ARCHITECTURE.md](docs/07-ARCHITECTURE.md) | Code map and design decisions |

## AI-assisted development

The repo ships Claude-ready: [`CLAUDE.md`](CLAUDE.md) gives AI assistants the project context, and `.claude/` contains commands and skills for common tasks (adding units, regenerating icons, building). See [docs/07-ARCHITECTURE.md](docs/07-ARCHITECTURE.md).

## Project layout

```
Assets/
  Editor/               ProjectBootstrap (scene generation)
  Resources/Icons/      Generated APP-6 icons (Friendly, Enemy, Affiliations)
  Scripts/              All C# gameplay code (runtime-built UI, no binary scenes)
  StreamingAssets/
    Data/units.json     Unit catalogue (117 types, both teams)
    Maps/lyon_dev.json  Default Lyon scenario
    cesium-token.txt    <- YOUR CESIUM ION TOKEN GOES HERE
docs/                   Documentation
scripts/                Icon/unit generators (Python), Windows build script
.claude/                Claude Code commands & skills
CLAUDE.md               AI assistant project brief
```

## License & data attribution

Game code © Ivan Šoštarko. Map data streamed at runtime via [Cesium ion](https://cesium.com/) (Cesium World Terrain, Bing Maps imagery, OSM Buildings) — subject to Cesium ion terms and data attribution requirements.
