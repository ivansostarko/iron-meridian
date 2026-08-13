# Audio

The register of every sound in Iron Meridian — its file, where it plays, and what it is for.

> **Keep this file current.** Every audio asset added to the game must be recorded in §2 with its path, the screens it plays on, and a description, in the same commit that introduces it. See [Rules](#rules) at the bottom.

---

## 1. Architecture

```
Assets/Scripts/Audio/
  AudioManager.cs    master volume (AudioListener) + procedural UI click
  AudioCatalog.cs    the register in code: track → resource path, level, loop
  MusicManager.cs    persistent music player; survives scene loads
```

Three rules govern all audio:

| Rule | Reason |
|---|---|
| **Audio files live under `Assets/Resources/`.** | The project builds every scene and prefab from code, so there is no serialised field anywhere to hold an `AudioClip` reference. `Resources.Load` is the only runtime lookup path — the same constraint that governs icons, VFX prefabs and 3D models. |
| **Music goes through `MusicManager`; never create a per-scene `AudioSource` for it.** | The manager is a `DontDestroyOnLoad` singleton. Every screen requests its track on load, and requesting the track already playing is a no-op — so the bed continues seamlessly across navigation instead of restarting on each screen. |
| **Levels live in `AudioCatalog`, not at the call site.** | One place to balance the mix. |

### Volume chain

```
clip → AudioSource.volume (per-track level from AudioCatalog)
     → AudioListener.volume (master volume, Settings → Audio, persisted in PlayerPrefs "im.masterVolume")
```

There is currently **one** volume slider (master). Separate music/SFX buses would need a new `AudioManager` pref plus a Settings row — not implemented.

### Playback behaviour

- **Fade-in:** music fades up over `AudioCatalog.MusicFadeInSeconds` (1.5 s) so it never starts abruptly.
- **Pause-safe:** fades run on `Time.unscaledDeltaTime`. The pause menu sets `timeScale = 0`, and music must not freeze mid-fade.
- **2D:** music uses `spatialBlend = 0`, so it is unaffected by the map camera's `AudioListener` moving around the globe.
- **Missing clip:** logged once, never per scene load, and never throws.

---

## 2. Audio register

### 2.1 Music

| Asset | Path | Resource path | Screens | Level | Loop | Description |
|---|---|---|---|---|---|---|
| Menu theme | `Assets/Resources/Audio/main-menu/game_menu_background.mp3` | `Audio/main-menu/game_menu_background` | **All six**: Main Menu, Settings, Testing, Units List, East France, Game (map editor) | 0.45 | Yes | Ambient background bed for the whole game. Continues uninterrupted across screen navigation. |

Track id in code: `MusicTrack.MenuTheme`.

> The folder is named `main-menu` because that is where the track was first used; it is now the game-wide bed. Renaming it means updating `AudioCatalog.MenuTheme.resourcePath`.

### 2.2 UI sound effects

| Asset | Path | Screens | Description |
|---|---|---|---|
| Button click | *Generated in code* — `AudioManager.BuildClick()` | Every screen, on every `UIFactory.CreateButton` | 1.2 kHz sine with a 50 ms exponential decay, synthesised at runtime. No file: the project must run with no audio assets present. Wired automatically by the button factory — call sites do nothing. |

### 2.3 Gameplay sound effects

*None yet.* Combat, movement and destruction are currently silent; only particle effects mark them. See §5.

### 2.4 Imported but not used

Tracked so the inventory stays honest and licensing stays traceable.

| Asset | Path | Source | Status |
|---|---|---|---|
| Fire loop (large) | `Assets/Vefects/Free Fire VFX URP/Audio/SFX_FireBig_L.wav` | [Free Fire VFX URP](https://assetstore.unity.com/packages/p/free-fire-vfx-urp-266226) | Not wired. Natural use: burning units / wrecks (`VfxId.FireLarge`) — see `docs/08-PARTICLE-SYSTEMS.md`. |
| Fire loop (medium) | `Assets/Vefects/Free Fire VFX URP/Audio/SFX_FireMedium_L.wav` | Free Fire VFX URP | Not wired. Pairs with `VfxId.FireMedium`. |
| Fire loop (small) | `Assets/Vefects/Free Fire VFX URP/Audio/SFX_FireSmall_L.wav` | Free Fire VFX URP | Not wired. Pairs with `VfxId.FireSmall`. |

Wiring these means positional 3D audio at map scale — rolloff distances measured in hundreds of metres, and a voice budget, since a front line can hold dozens of burning units at once. That is a design decision, not a mechanical one.

---

## 3. Screen coverage

| Screen | Scene | Music | UI SFX | Gameplay SFX |
|---|---|---|---|---|
| Main Menu | `MainMenu` | Menu theme | Click | — |
| Settings | `Settings` | Menu theme | Click | — |
| Testing | `Testing` | Menu theme | Click | — |
| Units List | `UnitsList` | Menu theme | Click | — |
| East France | `EastFrance` | Menu theme | Click | — |
| Map editor / game | `Game` | Menu theme | Click | — |

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
3. Music goes through `MusicManager`. No per-scene `AudioSource` for background music — it would restart the track on every navigation.
4. Levels and loop flags live in `AudioCatalog`. No magic volume numbers at call sites.
5. Audio must degrade silently: a missing clip warns once and the game keeps running.
6. Record the source and licence of every imported audio asset, including ones not yet used (§2.4).

## Related

`docs/07-ARCHITECTURE.md` (script map) · `docs/08-PARTICLE-SYSTEMS.md` (effects register) · `docs/09-3D-MODELS.md` (model register)
