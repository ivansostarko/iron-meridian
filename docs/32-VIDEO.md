# Video

The register of every film Iron Meridian plays — its file, where it plays, and what it is for.

> **Keep this file current.** Every video added to the game must be recorded in §2 with its path, where it plays and a description, in the same commit that introduces it. See [Rules](#rules) at the bottom.

---

## 1. Architecture

```
Assets/Scripts/Data/VideoCatalog.cs   the register in code: id → resource path, name, where it plays
Assets/Scripts/UI/IntroVideoUI.cs     the opening film (docs/11-GAME-MENU.md §3.1a)
Assets/Scripts/UI/VideoListUI.cs      DEVELOPMENT → VIDEOS: every film, with a transport
```

Same shape as the audio, model, effect and background catalogues: a screen names
an **id**, the catalogue owns the **path**, and one lab lists the lot.

| Rule | Reason |
|---|---|
| **Video files live under `Assets/Resources/`.** | The project builds every scene from code, so there is no serialised field anywhere to hold a `VideoClip`. `Resources.Load` is the only runtime lookup path — the same constraint that governs icons, audio, VFX prefabs and 3D models. |
| **Screens name a `VideoId`, never a path.** | One place to move a file. `IntroVideoUI` reads its path from the catalogue, so the VIDEOS lab is guaranteed to be listing the file the game actually plays. |
| **A missing file is not an error.** | It warns once and the screen carries on. An opening film that stopped a player reaching the menu would be worse than no film. |

### Playback

Unity's `VideoPlayer` decodes to a **`RenderTexture`** which is shown on a
`RawImage`. Two consequences worth knowing:

- **Aspect is fitted, not enveloped.** A background image may be cropped; a film
  frame is composed, so it is letterboxed against black instead
  (`AspectRatioFitter.FitInParent`).
- **Audio goes through an `AudioSource`**, not the player's Direct mode, so a
  film's sound obeys the master volume like everything else — see docs/10-AUDIO.md.

---

## 2. Video register

| Asset | Path | Resource path | Plays | Description |
|---|---|---|---|---|
| Game intro | `Assets/Resources/Videos/intro-video/game_intro.mp4` | `Videos/intro-video/game_intro` | Main menu, once per launch | The opening film, over black, before the menu is usable. Any input skips it; it always ends — see [11-GAME-MENU.md §3.1a](11-GAME-MENU.md#31a-the-opening-film). |

Ids in code: `VideoId.GameIntro`.

---

## 3. The VIDEOS lab

**DEVELOPMENT → VIDEOS** (`UI/VideoListUI.cs`) lists every registered film with:

| Column | What it says |
|---|---|
| Name | As the catalogue names it |
| Path | The resource path it was asked for |
| State | **INSTALLED** or **MISSING** — resolved, not assumed |

Selecting one loads it into the player on the right: **PLAY / PAUSE**,
**RESTART**, a scrub bar and a running time. The menu bed is stopped on the way
in — a film has its own sound, and a music bed under it would be the loudest
thing in the mix.

**It reports the truth, not the catalogue.** A catalogue naming a path is not the
same as the path resolving. That gap is the whole reason the screen exists, and it
is the same reason the AUDIO and 3D MODELS labs exist.

---

## 4. Adding a video

1. **Place the file under `Assets/Resources/Videos/`.** Anywhere else and it cannot be loaded at runtime.
2. **Add a `VideoId` value and a `VideoDef` row** in `VideoCatalog`, with its name, where it plays and a description.
3. **Play it through the id**, never a path — as `IntroVideoUI` does.
4. **Check it in DEVELOPMENT → VIDEOS**: the row must say INSTALLED and the film must play.
5. **Update §2 here** in the same commit.

Codec: Unity transcodes on import by default. A file it cannot decode reports
through `VideoPlayer.errorReceived`, which both the intro and the lab log rather
than swallowing.

---

## Rules

1. **This document is the register of every video in the game.** Adding, replacing or removing one is not done until §2 is updated in the same commit — with the file path, where it plays and a description.
2. Video files live under `Assets/Resources/`; nothing else is loadable at runtime.
3. **Screens name a `VideoId`.** No `Resources.Load` of a video at a call site.
4. **Every player must always dismiss.** A film that can trap the player behind it is worse than no film — completion *and* input *and* a timeout, as `IntroVideoUI` does.
5. A missing file warns once and is skipped, never throws.

---

## Related

`docs/11-GAME-MENU.md` (the opening film in context) · `docs/10-AUDIO.md` (the master volume a film's sound obeys) · `docs/12-LOADERS.md` (the other thing that makes a player wait) · `docs/07-ARCHITECTURE.md` (script map)
