# Iron Meridian

**A real-terrain operational wargame built with Unity 6 and Cesium 3D geospatial maps.**

Deploy Blue (User) and Red (Enemy) forces on real 3D terrain streamed from Cesium ion, draw sector boundaries and defensive lines, then start the battle and watch different unit powers reshape the front line — HOI-IV style operational play at tactical map fidelity.

Repository: `github.com/ivansostark/iron-meridian`

![Unity](https://img.shields.io/badge/Unity-6000.0%20LTS-black) ![Cesium](https://img.shields.io/badge/Cesium%20for%20Unity-1.24-blue) ![Platform](https://img.shields.io/badge/Platform-Windows%2064--bit-informational)

---

## Features

- **Main menu** with Testing, Settings (Video + Audio tabs) and Quit (with confirmation modal)
- **Cesium 3D world**: real terrain + satellite imagery + OSM buildings, default map centred on **Lyon, France**
- **2D / 3D switch** for both the game view and drawn lines
- **37 unit types** — 25 core ground units and 12 drone-relevant units — each with manpower, training, morale, combat power, ammunition type & stocks, fuel and food
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
6. Press **Play**. Main menu → **TESTING** → **DEV** → you are over Lyon.

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
    Data/units.json     Unit catalogue (37 types, both teams)
    Maps/lyon_dev.json  Default Lyon scenario
    cesium-token.txt    <- YOUR CESIUM ION TOKEN GOES HERE
docs/                   Documentation
scripts/                Icon/unit generators (Python), Windows build script
.claude/                Claude Code commands & skills
CLAUDE.md               AI assistant project brief
```

## License & data attribution

Game code © Ivan Šoštarko. Map data streamed at runtime via [Cesium ion](https://cesium.com/) (Cesium World Terrain, Bing Maps imagery, OSM Buildings) — subject to Cesium ion terms and data attribution requirements.
