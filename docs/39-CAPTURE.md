# Capture — screenshots and video

The map editor's left rail carries a **CAPTURE** section in both scenario and
battle mode. It takes a PNG screenshot, or records an **H.264 `.mp4`**, and
writes both into the player's own Pictures folder.

```
Map editor → left rail → CAPTURE
    SCREENSHOT        one PNG of the screen as it looks
    RECORD / STOP     an .mp4 at 30 fps
    OPEN FOLDER       reveals the output in the file browser
```

| | |
|---|---|
| The system | `Assets/Scripts/Core/CaptureSystem.cs` |
| The panel | `Assets/Scripts/UI/UnitPaletteUI.Capture.cs` — `BuildCaptureSection` |
| The glyph | `UiIcons.Camera` |

## 1. Where files go

```
%USERPROFILE%\Pictures\Iron Meridian\
    Screenshots\IronMeridian_2026-08-19_14-30-05.png
    Recordings\IronMeridian_2026-08-19_14-31-12.mp4
```

Pictures rather than the save folder because these are the player's files, not
the game's — they are meant to be found, posted and edited without going
looking. A machine with no Pictures folder falls back to the save folder
(`docs/05-MAP-SAVES.md`); losing the take entirely would be worse than putting
it somewhere less obvious.

**On Android and iOS** stills go to the app's own folder rather than the photo
library, because writing there needs a permission this game does not ask for, and
**recording is switched off entirely** — see §2.

**In a browser** neither really works: the still is written into a virtual
filesystem the player cannot reach, and recording is off for the same reason as
on Android. `docs/41-WEB.md` §4 and §9.

## 2. ffmpeg does the encoding

**Unity has no runtime video encoder.** `UnityEditor.Media.MediaEncoder` and
Unity Recorder are both editor-only and cannot run in a build, so the choice is
a native plugin or an external encoder. This uses **ffmpeg as a child process**:
no plugin in the project, no licence attached to the game binary, and it writes
a real `.mp4`.

The cost of that choice is that **it does not work on a platform with no child
processes**. An Android app cannot spawn an arbitrary executable, there is no
PATH to search, and `System.Diagnostics.Process` is not in the runtime that ships
in the APK; a browser build has no processes, no threads for the writer, and
nowhere to put an mp4. `CaptureSystem.CanUseExternalEncoder` is false on both, so
`CanRecord` is false, so the RECORD button is disabled and says why — rather than
the search coming back empty and the failure looking like a missing install.

Frames go over the pipe as **JPEG (quality 95), not raw RGBA**. Raw 1080p is
about 8 MB a frame — 250 MB/s at 30 fps — for a difference nobody can see once
x264 has finished. The exact command is:

```
ffmpeg -y -f image2pipe -framerate 30 -i -
       -vf "scale=trunc(iw/2)*2:trunc(ih/2)*2"
       -c:v libx264 -preset veryfast -crf 20 -pix_fmt yuv420p -movflags +faststart
       <output>.mp4
```

The `scale` filter is not decoration: `yuv420p` requires even dimensions, and a
window can be any odd size the player dragged it to. Verified against a 1279×721
input, which comes out 1278×720.

### 2a. Getting ffmpeg

The RECORD button is disabled, with the reason under it, when no encoder is
found. To install one:

```powershell
winget install --id Gyan.FFmpeg
```

`CaptureSystem.FfmpegPath` searches, once, in this order:

1. `StreamingAssets/ffmpeg/ffmpeg.exe` — so a build **can** ship its own copy
2. every directory on `PATH`
3. `C:\Program Files\ffmpeg\bin`, `/usr/bin`, `/usr/local/bin`

### 2b. If you bundle it — licensing

Putting `ffmpeg.exe` in `StreamingAssets` means **distributing ffmpeg**, and
that carries its licence into your release. It is not a blocker, but it is a
decision to make deliberately and record in `docs/37-THIRD-PARTY.md`:

- ffmpeg is **LGPL 2.1+**, or **GPL** if built with certain components —
  including **libx264**, the encoder in the command above.
- A GPL build distributed alongside a closed-source game is the case to be
  careful about. Running an ffmpeg the *user* installed, which is the default
  here, avoids the question entirely: you are not distributing it.
- If you do bundle, ship the licence text and the written offer for source that
  the licence requires.

This matters for a paid Steam release specifically — see `docs/36-STEAM.md` §1c.

## 3. Why the game slows down while recording

Encoding a frame takes longer than rendering one. Left alone that produces a
video with gaps in it — stuttering exactly where the game was working hardest.

`CaptureSystem` sets **`Time.captureFramerate`** instead, which is Unity's own
answer: time advances in fixed 1/30 s steps regardless of how long each frame
actually took. Every frame is captured, none is dropped, and the video is
correctly timed — at the cost of the game visibly running in slow motion while
the take is in progress. Ninety captured frames come out as exactly three
seconds of video.

The battle advances in lockstep with the capture, so a recorded fight is the
fight that was recorded, not a sped-up approximation of it.

## 4. How a take is wired

```
end of frame ─ CaptureScreenshotAsTexture ─ EncodeToJPG(95)
                                              │
                                     BlockingCollection (60 frames)
                                              │
                                       writer thread ─ ffmpeg stdin
```

The queue is **bounded on purpose**. With `Time.captureFramerate` set the game
is already off the wall clock, so blocking the capture until the encoder catches
up costs nothing but real seconds and guarantees no frame is ever dropped. An
unbounded queue would trade that for unbounded memory.

Only the write is off the main thread; the encode cannot be, because it needs
the texture.

### 4a. Ending a take cleanly

Order matters, and `FinishEncoder` keeps it: stop accepting frames → let the
writer drain → close stdin → wait for ffmpeg to write its trailer. **Killing
ffmpeg instead would leave an `.mp4` with no trailer, which nothing will play.**
`OnApplicationQuit` runs the same path, so quitting mid-take still produces a
playable file.

### 4b. What stops a take by itself

| Condition | Why |
|---|---|
| 18,000 frames (10 min) | A take nobody remembered to end should not fill a disk |
| The window was resized | `image2pipe` cannot follow a frame-size change; ending keeps the file playable instead of corrupting it from that frame on |
| ffmpeg exited | Detected next frame, reported in the panel |
| Encode threw | Caught — otherwise the coroutine dies with `Recording` still true, leaving a button that says STOP and a take that is not running |

## 5. Both modes, deliberately

`Section.Capture` is in **both** `ScenarioSections` and `BattleSections`
(`UnitPaletteUI.cs`), and is the only row that is. A still of a scenario being
laid out and a video of the battle that follows are the same job; removing the
controls the moment the fight starts would take them away exactly when there is
something worth recording.

## 6. Interface is included

Both capture the screen as it is, HUD and rail and all. There is no
"hide the interface" pass, because there is already a control for that: the
rail's own close button, and `SetChromeVisible(false)` for a mission. A second,
capture-only way to hide the same things would be two mechanisms for one idea.

For store screenshots and trailer footage — which want no interface — close the
section panel first. See `docs/36-STEAM.md` §6.

## 7. Extending it

The system is deliberately small and static: `TakeScreenshot()`,
`ToggleRecording()`, `OpenFolder()`, plus `Recording`, `CanRecord`,
`FrameCount`, `RecordedSeconds`, `LastOutput` and `LastError` for a panel to
read. It raises `Changed` on start, on stop and once a second while running, so
a UI is driven rather than polled.

**`Changed` is static and the palette is per-scene**, so anything subscribing
must unsubscribe — `UnitPaletteUI.OnDestroy` does, and a new subscriber that
forgets will fire into a destroyed object on the next take.

Frame rate is `CaptureSystem.RecordFps`; the capture clock, the encoder's
`-framerate` and the readouts all derive from it. Quality is the `-crf 20` in
`StartEncoder` — lower is better and bigger.

## See also

`docs/40-ANDROID.md` §3 (why recording is off on a phone) ·
`docs/41-WEB.md` §4 (and in a browser) ·
`docs/36-STEAM.md` §6 (store screenshots and the trailer) ·
`docs/38-PACKAGES.md` §2a (Unity Recorder, the editor-side alternative with
more output formats) · `docs/37-THIRD-PARTY.md` (where a bundled ffmpeg would be
recorded) · `docs/03-GAMEPLAY.md` (the rail and its sections)
