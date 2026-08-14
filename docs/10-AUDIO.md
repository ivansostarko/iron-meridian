# Audio

The register of every sound in Iron Meridian — its file, where it plays, and what it is for.

> **Keep this file current.** Every audio asset added to the game must be recorded in §2 with its path, the screens it plays on, and a description, in the same commit that introduces it. See [Rules](#rules) at the bottom.

---

## 1. Architecture

```
Assets/Scripts/Audio/
  AudioManager.cs     master volume (AudioListener) + procedural UI click
  AudioCatalog.cs     the register in code: track → resource path, level, loop
  MusicManager.cs     persistent music channel; survives scene loads
  AmbienceManager.cs  persistent weather channel; plays under the music
  EffectAudio.cs      3D positional effect sounds + voice budget
  ProceduralAudio.cs  synthesises effect sounds when no file is installed
```

### Channels

Three independent channels. The two looping beds are `DontDestroyOnLoad` singletons with their own `AudioSource`; effect sounds are short-lived world sources created on demand:

| Channel | Manager | Carries | Spatial | Fade |
|---|---|---|---|---|
| Music | `MusicManager` | The menu theme, on every screen | 2D | 1.5 s |
| Ambience | `AmbienceManager` | Weather beds (rain, storm, snow) | 2D | 1.0 s |
| Effects | `EffectAudio` | Fire, explosions, smoke, impacts | **3D, positional** | — |

Mixing them onto one source would make simultaneous playback impossible — a storm has to be audible *with* the music, and an explosion over both.

The effects channel is the only **3D** one: sources are placed in the world, so a fire on the far side of the map is quiet and one under the camera is not. That needs rolloff distances in hundreds of metres rather than Unity's defaults (1 m / 500 m, logarithmic), which are silent almost immediately at this scale. Each source uses **linear** rolloff with `minDistance` = the effect's own diameter and `maxDistance` = 26× that, so a 320 m explosion carries far further than a 100 m camp fire.

Four rules govern all audio:

| Rule | Reason |
|---|---|
| **Audio files live under `Assets/Resources/`.** | The project builds every scene and prefab from code, so there is no serialised field anywhere to hold an `AudioClip` reference. `Resources.Load` is the only runtime lookup path — the same constraint that governs icons, VFX prefabs and 3D models. |
| **Music goes through `MusicManager`, weather through `AmbienceManager`; never create a per-scene `AudioSource` for either.** | Both are `DontDestroyOnLoad` singletons. Every screen requests its track on load, and requesting the track already playing is a no-op — so the bed continues seamlessly across navigation instead of restarting on each screen. |
| **Levels live in `AudioCatalog`, not at the call site.** | One place to balance the mix. |
| **Ambience is driven by state, never by an event.** | `WeatherSystem` calls `AmbienceManager.Play(...)` whenever weather or battle state changes; the no-op-if-already-playing contract makes that safe to call repeatedly. |

### Volume chain

```
clip → AudioSource.volume (per-track level from AudioCatalog)
     → AudioListener.volume (master volume, Settings → Audio, persisted in PlayerPrefs "im.masterVolume")
```

There is currently **one** volume slider (master). Separate music/ambience/SFX buses would need a new `AudioManager` pref plus Settings rows — not implemented, though the channel split above is the groundwork for it.

### Playback behaviour

- **Fade-in:** music fades up over `AudioCatalog.MusicFadeInSeconds` (1.5 s) so it never starts abruptly.
- **Pause-safe:** fades run on `Time.unscaledDeltaTime`. The pause menu sets `timeScale = 0`, and music must not freeze mid-fade.
- **2D vs 3D:** music and ambience use `spatialBlend = 0`, unaffected by the map camera's `AudioListener` moving around the globe. Effect sounds use `spatialBlend = 1` and are placed in the world.
- **Missing clip:** logged once, never per scene load, and never throws.

---

## 2. Audio register

### 2.1 Music

| Asset | Path | Resource path | Screens | Level | Loop | Description |
|---|---|---|---|---|---|---|
| Menu theme | `Assets/Resources/Audio/main-menu/game_menu_background.mp3` | `Audio/main-menu/game_menu_background` | **All six**: Main Menu, Settings, Testing, Units List, East France, Game (map editor) | 0.45 | Yes | Ambient background bed for the whole game. Continues uninterrupted across screen navigation. |

Track id in code: `MusicTrack.MenuTheme`.

> The folder is named `main-menu` because that is where the track was first used; it is now the game-wide bed. Renaming it means updating `AudioCatalog.MenuTheme.resourcePath`.

### 2.1a Weather ambience

Looping environmental beds on the ambience channel. Selected in the map editor's **WEATHER CONDITIONS** section, and played **in battle mode only** — a rain loop droning while counters are being laid out is noise, not atmosphere. See `docs/14-WEATHER.md`.

| Asset | Path | Resource path | Plays when | Level | Description |
|---|---|---|---|---|---|
| Rain | `Assets/Resources/Audio/weather/rain-background.mp3` | `Audio/weather/rain-background` | Condition = **Rain**, battle running | 0.40 | Steady rainfall bed. |
| Storm | `Assets/Resources/Audio/weather/storm-background.mp3` | `Audio/weather/storm-background` | Condition = **Storm**, battle running | 0.50 | Wind and thunder bed; the loudest weather. |
| Snow | `Assets/Resources/Audio/weather/snow-background.mp3` | `Audio/weather/snow-background` | Condition = **Snow**, battle running | 0.30 | Muffled wind bed. Quietest by design — real snowfall is near-silent, and a loud loop reads as static. |

Track ids in code: `AmbienceTrack.Rain` / `.Storm` / `.Snow`. The Clear, Overcast and Fog conditions carry no bed (`AmbienceTrack.None`), which stops the channel.

### 2.2 UI sound effects

| Asset | Path | Screens | Description |
|---|---|---|---|
| Button click | *Generated in code* — `AudioManager.BuildClick()` | Every screen, on every `UIFactory.CreateButton` | 1.2 kHz sine with a 50 ms exponential decay, synthesised at runtime. No file: the project must run with no audio assets present. Wired automatically by the button factory — call sites do nothing. |

### 2.3 Particle effect sounds

Every effect in `VfxCatalog` can carry a sound; it is a field on the catalogue row, so an effect and its audio are defined in one place and cannot drift apart. Sources are 3D and parented to the effect, so a burning unit carries its crackle as it withdraws.

| Sound | Source | Used by | Loops | Description |
|---|---|---|---|---|
| Fire | **Synthesised** (`ProceduralAudio.FireLoop`) — or `Assets/Resources/Audio/effects/fire.*` if installed | `FireSmall`, `FireMedium`, `FireLarge`, `GroundFire` | Yes | Band-limited noise with sparse crackle pops. Loop is cross-faded at the wrap so it does not click. |
| Explosion | **Synthesised** (`ProceduralAudio.Explosion`) — or `Audio/effects/explosion.*` | `Explosion` | No | 90 Hz body falling to 28 Hz under a rolled-off noise crack, with a click transient and a 2.4 s tail. The pitch drop is what makes it read as a large blast rather than a pop. |
| Smoke | **Synthesised** (`ProceduralAudio.SmokeLoop`) — or `Audio/effects/smoke.*` | `SmokePlume`, `SmokeScreen` | Yes | Slow low hiss with a gentle swell. Deliberately near sub-audible. |
| Impact | **Synthesised** (`ProceduralAudio.Impact`) — or `Audio/effects/impact.*` | `ImpactBurst` | No | Short filtered thud for rounds landing. |
| Artillery 105 mm | **Synthesised** (`ProceduralAudio.Shell`) — or `Audio/effects/artillery_105.*` | `ArtilleryLightBurst` | No | Sharp high crack, short tail. Body 170 → 62 Hz, open crack filter. |
| Artillery 120 mm | **Synthesised** (`ProceduralAudio.Shell`) — or `Audio/effects/artillery_120.*` | `ArtilleryMortarBurst` | No | Dull thump — more earth than air. Body 120 → 46 Hz, closed crack filter, heavy rumble. |
| Artillery 155 mm | **Synthesised** (`ProceduralAudio.Shell`) — or `Audio/effects/artillery_155.*` | `ArtilleryMediumBurst` | No | The reference report: deep body, long tail. 92 → 30 Hz over 0.85 s. |
| Artillery 203 mm | **Synthesised** (`ProceduralAudio.Shell`) — or `Audio/effects/artillery_203.*` | `ArtilleryHeavyBurst` | No | Very low and slow to decay, with a rolling echo. 66 → 19 Hz over 1.35 s, 4.2 s clip. |
| Aerial bomb | **Synthesised** (`ProceduralAudio.Shell`) — or `Audio/effects/aerial_bomb.*` | `AerialBombBurst` | No | Deeper and longer than any tube: 58 → 15 Hz over 1.7 s, 5 s clip. The heaviest detonation in the game. |

Ids in code: `EffectSound.Fire` / `.Explosion` / `.Smoke` / `.Impact` / `.ArtilleryLight` / `.ArtilleryMortar` / `.ArtilleryMedium` / `.ArtilleryHeavy` / `.AerialBomb` (and `.JetPass`, which is not carried by an effect — see §2.4). `WeaponFire` and `Dust` carry no sound — at one puff per firing formation they would turn a front line into a rattle. Artillery *smoke* carries no sound of its own for the two lighter natures either; the heavier two reuse the smoke hiss.

**Why four artillery reports rather than one.** Calibre is audible in real life — a 105 mm round cracks, a 203 mm round is felt before it is heard — so a fire mission that sounds the same whatever was called for throws away the one cue that tells the player which battery answered. All four come from a single parameterised synthesiser, `ProceduralAudio.Shell`, layering a pitch-falling **body**, a filtered noise **crack** and a slow **rumble** bed; opening the crack filter and raising the body gives a light gun, closing it and dropping the body gives a heavy one. Each calibre uses a fixed seed, so a nature always sounds like itself between runs. See docs/17-ARTILLERY.md.

**Ordered attacks are the loudest thing on the map**, and all three of their sounds arrive through the effects above rather than through anything new — see [15-COMBAT-ORDERS.md §4](15-COMBAT-ORDERS.md):

| Order moment | Effect | Sound heard |
|---|---|---|
| A volley takes ≥1.8% strength | `Explosion` (throttled to one per 2.4 s per order) | Explosion |
| An **assault** goes in | `GroundFire` on the objective, 20 s | Fire |
| **Suppressive fire** opens | `SmokeScreen` on the target, for the order's life | Smoke |

The throttle is an audio decision as much as a visual one: unthrottled, a division-scale engagement would fire the explosion voice every tick and consume the whole 14-voice budget on one fight.

**Files beat synthesis.** `EffectAudio` looks in `Resources/Audio/effects/<name>` first and only synthesises when nothing is there, so dropping in recorded audio needs no code change. The synthesis exists so the game is audible with no audio assets at all — the same rule `ProceduralVfx` follows for the visuals.

**Voice budget:** 14 concurrent effect sources; past that the oldest is recycled. A corps-scale battle can have dozens of fires burning, and without the cap the mix turns to mud.

An artillery mission is the densest thing that hits this budget: five reports inside about two seconds, plus the smoke voices behind them. The round spacing in `ArtilleryCatalog` is what keeps it a salvo rather than a single mush — and it is why the heavier natures, whose reports are the longest, are also the most widely spaced.

### 2.4 Other gameplay sound effects

Sounds played directly rather than carried by a particle effect's catalogue row.

| Sound | Source | Played by | Loops | Description |
|---|---|---|---|---|
| Jet pass | **Synthesised** (`ProceduralAudio.JetPass`) — or `Assets/Resources/Audio/effects/jet_pass.*` | `BomberRun.Launch`, parented to the aircraft | No | Broadband roar swelling and fading over a turbine tone that slides 115 → 62 Hz. 6 s. |

`EffectSound.JetPass` is the first sound in the project **not** attached to a `VfxId`, because the thing making it is an aircraft rather than an effect. It is played with `EffectAudio.PlayAt(..., parent: aircraft)` so it travels with the aeroplane and is loudest as it passes overhead.

**The Doppler slide is baked into the clip**, not left to Unity. Effect sources run with `dopplerLevel = 0` — this map is kilometres across and the camera is not a listener in motion, so engine Doppler produces nothing useful — and without a slide a fast-moving aircraft sounds stationary.

Movement and unit orders are still silent.

### 2.5 Imported but not used

Tracked so the inventory stays honest and licensing stays traceable.

| Asset | Path | Source | Status |
|---|---|---|---|
| Fire loop (large) | `Assets/Vefects/Free Fire VFX URP/Audio/SFX_FireBig_L.wav` | [Free Fire VFX URP](https://assetstore.unity.com/packages/p/free-fire-vfx-urp-266226) | Not wired — fire currently uses synthesis (§2.3). To use it instead, copy it to `Assets/Resources/Audio/effects/fire.wav`; no code change needed. |
| Fire loop (medium) | `Assets/Vefects/Free Fire VFX URP/Audio/SFX_FireMedium_L.wav` | Free Fire VFX URP | Not wired. The effect system has one Fire sound for all sizes; per-size beds would need an `EffectSound` value each. |
| Fire loop (small) | `Assets/Vefects/Free Fire VFX URP/Audio/SFX_FireSmall_L.wav` | Free Fire VFX URP | Not wired, as above. |

> The positional-audio and voice-budget work these needed is now done (§2.3); using them is just a matter of copying a file into `Resources/Audio/effects/`.

---

## 3. Screen coverage

| Screen | Scene | Music | Ambience | Effects | UI SFX |
|---|---|---|---|---|---|
| Main Menu | `MainMenu` | Menu theme | — | — | Click |
| Settings | `Settings` | Menu theme | — | — | Click |
| Testing | `Testing` | Menu theme | — | — | Click |
| Units List | `UnitsList` | Menu theme | — | — | Click |
| East France | `EastFrance` | Menu theme | — | — | Click |
| Map editor (editing) | `Game` | Menu theme | — | Hand-placed effects (EFFECTS panel) | Click |
| Map editor (battle running) | `Game` | Menu theme | Weather bed, if the condition has one | Combat, ordered attacks (§2.3) + hand-placed effects | Click |

Each screen starts music in its bootstrap, next to `AudioManager.Apply()`:

```csharp
IronMeridian.Audio.AudioManager.Apply();
IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme);
```

Every scene has an `AudioListener` — menu scenes get one from `ProjectBootstrap`, the Game scene from `CameraRig`.

---

## 4. Import settings

The music file currently uses Unity's import defaults. Recommended settings for each class of audio:

| Class | Load type | Compression | Notes |
|---|---|---|---|
| Music bed (long, looping) | **Streaming** | Vorbis, ~70 % | Avoids holding several MB decompressed in memory. Enable *Load In Background*; disable *Preload Audio Data*. |
| Short UI SFX | Decompress On Load | ADPCM or PCM | Sub-second clips; decode cost matters more than size. |
| Looping gameplay SFX | Compressed In Memory | Vorbis | Many instances may play at once. |

Force To Mono is appropriate for positional gameplay SFX, not for the music bed.

---

## 5. Adding new audio

1. **Place the file under `Assets/Resources/Audio/<category>/`.** Anywhere else and it cannot be loaded at runtime.
2. **Set import settings** per §4.
3. **Music:** add a `MusicTrack` value and a `MusicDef` row in `AudioCatalog`, then call `MusicManager.Play(...)` from the screen's bootstrap.
   **SFX:** load through a small catalogue entry the same way — never `Resources.Load` at a call site.
4. **Balance the level** in `AudioCatalog`, not at the call site.
5. **Verify:** Play → walk every screen listed in §3 → music must not restart on navigation, and the master volume slider must govern it.
6. **Update this file** — the register in §2 *and* the coverage table in §3, with path, screens and description.
7. **Record the source** and its licence/Asset Store URL for any purchased or downloaded audio.

---

## Rules

1. **This document is the register of every sound in the game.** Adding, replacing or removing an audio asset, or playing one somewhere new, is not done until §2 and §3 here are updated in the same commit — with the file path, the screens it plays on, and a description.
2. Audio assets live under `Assets/Resources/`; nothing else is loadable at runtime.
3. Music goes through `MusicManager` and weather through `AmbienceManager`. No per-scene `AudioSource` for either — it would restart the track on every navigation, and would stop the two layering.
4. Levels and loop flags live in `AudioCatalog`. No magic volume numbers at call sites.
5. Audio must degrade silently: a missing clip warns once and the game keeps running.
6. Record the source and licence of every imported audio asset, including ones not yet used (§2.4).

## Related

`docs/07-ARCHITECTURE.md` (script map) · `docs/08-PARTICLE-SYSTEMS.md` (effects register) · `docs/09-3D-MODELS.md` (model register) · `docs/14-WEATHER.md` (what drives the ambience channel)
