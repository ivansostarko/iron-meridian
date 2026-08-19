# Capture — stills and recordings

The map editor's left rail carries a **CAPTURE** section in both scenario and
battle mode. It takes a screenshot, or records the screen as a frame sequence,
and writes both into the player's own Pictures folder.

```
Map editor → left rail → CAPTURE
    SCREENSHOT        one PNG of the screen as it looks
    RECORD / STOP     a numbered JPG sequence at 30 fps
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
    Recordings\2026-08-19_14-31-12\frame_000000.jpg
                                   frame_000001.jpg
                                   …
```

Pictures rather than the save folder because these are the player's files, not
the game's — they are meant to be found, posted and edited without going
looking. A machine with no Pictures folder falls back to the save folder
(`docs/05-MAP-SAVES.md`); losing the shot entirely would be worse than putting
it somewhere less obvious.

Recordings get a timestamped subfolder each. Several hundred loose frames
dropped straight into Pictures would be hostile.

## 2. Recording is a frame sequence, not a video file

This is the part worth understanding before using it.

**Unity has no runtime video encoder.** Unity Recorder — which is installed in
this project (`docs/38-PACKAGES.md` §2a) — is an *editor* tool and cannot run in
a build. Producing an `.mp4` from the shipped game would mean bundling a native
encoder, which is a dependency and a licensing question for the sake of a
convenience the last step of any edit does anyway.

So `RECORD` writes numbered JPGs, which is:

- what a video editor wants as input,
- what Unity Recorder itself produces in image-sequence mode,
- and losslessly re-timeable, because the frames carry no timing of their own.

Turn a take into a video with ffmpeg:

```powershell
ffmpeg -framerate 30 -i frame_%06d.jpg -c:v libx264 -pix_fmt yuv420p take.mp4
```

### 2a. Why the game slows down while recording

Encoding and writing a frame takes longer than rendering one. Left alone, that
would produce a sequence with gaps in it — a recording that stutters exactly
where the game was working hardest.

`CaptureSystem` sets **`Time.captureFramerate`** instead, which is Unity's own
answer to this: time advances in fixed 1/30 s steps regardless of how long each
frame actually took. Every frame is captured, none is dropped, and the sequence
plays back perfectly smooth — at the cost of the game visibly running in slow
motion while the take is in progress.

That is the right trade for footage. It also means the battle itself advances in
lockstep with the capture, so a recorded fight is the fight that was recorded,
not a sped-up approximation of it.

### 2b. Limits

- **JPG at quality 90**, not PNG. A lossless 1080p frame costs several times the
  encode time and the disk, and the sequence is an intermediate rather than an
  archival still. Screenshots *are* PNG, because those are the artefact.
- **18,000 frames** (ten minutes) then it stops itself. A take nobody remembered
  to end should not quietly fill a disk.
- The file write is pushed to a background thread; the encode cannot be, because
  it needs the main thread's texture.

## 3. Both modes, deliberately

`Section.Capture` is in **both** `ScenarioSections` and `BattleSections`
(`UnitPaletteUI.cs`). Most sections belong to one or the other — the rail in
battle is deliberately short — but a still of a scenario being laid out and a
recording of the battle that follows are the same job, and removing the controls
the moment the fight starts would take them away exactly when there is something
worth recording.

## 4. Interface is included

Both capture the screen as it is, HUD and rail and all. There is no "hide the
interface" pass, because there is already a control for that: the rail's own
close button, and `SetChromeVisible(false)` for a mission. Adding a second,
capture-only way to hide the same things would be two mechanisms for one idea.

For store screenshots — which want no interface at all — close the section
panel first. See `docs/36-STEAM.md` §6.

## 5. Extending it

The system is deliberately small and static: `TakeScreenshot()`,
`ToggleRecording()`, `OpenFolder()`, plus `Recording`, `FrameCount`,
`RecordedSeconds` and `LastOutput` for a panel to read. It raises `Changed` on
start, on stop and once a second while running, so a UI is driven rather than
polled.

**`Changed` is static and the palette is per-scene**, so anything subscribing to
it must unsubscribe — `UnitPaletteUI.OnDestroy` does, and a new subscriber that
forgets will fire into a destroyed object on the next take.

A higher/lower frame rate is `CaptureSystem.RecordFps`; both the capture clock
and the readouts derive from it.

## See also

`docs/36-STEAM.md` §6 (store screenshots and the trailer) ·
`docs/38-PACKAGES.md` §2a (Unity Recorder, the editor-side alternative) ·
`docs/03-GAMEPLAY.md` (the rail and its sections)
