# Date and Time

How the operational clock works: when it runs, how fast, where H-hour is set, and how it is saved.

---

## 1. The model in one paragraph

Iron Meridian has **two modes and one clock**. In the map editor nothing advances — the editor is *timeless*, so a scenario can be laid out for as long as you like without burning game time. The moment **START BATTLE** is pressed, the clock begins running from the scenario's **start date and time** (H-hour), and the top bar shows it. Pausing the battle stops the clock; resuming continues from where it stopped.

```
Editor (timeless)  ──START BATTLE──▶  Battle (clock runs from H-hour)
       ▲                                        │
       └────────────── PAUSE BATTLE ────────────┘
```

---

## 2. Where things live

| Piece | File | Responsibility |
|---|---|---|
| The clock | `Assets/Scripts/Core/GameClock.cs` | Holds `Now`, `StartDateTime` and the speed; advances time; drives `Time.timeScale` |
| The readout | `Assets/Scripts/UI/GameHUD.cs` | Top-bar time / date / speed and the speed controls |
| The editor UI | `Assets/Scripts/UI/UnitPaletteUI.cs` → **DATE AND TIME** section | Current start, click to change, three presets |
| The picker | `Assets/Scripts/UI/DateTimeDialog.cs` | Modal for setting H-hour |
| Persistence | `Assets/Scripts/Data/MapSaveData.cs` → `startDateTime` | Saved with the map |

---

## 3. Time rate and speed

`GameClock.GameSecondsPerRealSecond = 60` — **one real second is one game minute** at 1×.

| Speed | Multiplier | One real second is | `Time.timeScale` |
|---|---|---|---|
| PAUSED | 0× | nothing | 0 |
| 1× | 1 | 1 game minute | 1 |
| 2× | 2 | 2 game minutes | 2 |
| 4× | 4 | 4 game minutes | 4 |
| 8× | 8 | 8 game minutes | 8 |

Speed is changed with the **«**, **❚❚**, **»** buttons in the top-bar clock.

**Speed drives `Time.timeScale`, not just the readout.** Slowing time genuinely slows unit movement and combat ticks; that is the point. Two consequences follow, and both are deliberate:

- The clock advances using `Time.unscaledDeltaTime × 60 × Speed`. `Time.deltaTime` is *already* scaled by `timeScale`, so using it would square the multiplier — 8× would run at 64×.
- Anything that must keep animating while paused uses **unscaled** time explicitly: range rings, the deploy burst, the placement reticle, particle culling, loading-screen fades and music fades.

The pause menu also forces `timeScale` to 0. `GameClock.Update` therefore checks `Time.timeScale <= 0` as well as its own `Paused` flag, so a menu pause freezes the clock even when the player's chosen speed is above zero. `PauseMenuUI` restores `GameClock.DesiredTimeScale` on close rather than blindly resetting to 1.

`GameClock.OnDisable` resets `timeScale` to 1, so a scene change never leaves the game frozen.

---

## 4. Setting H-hour

**Map editor → left panel → DATE AND TIME.**

### Current start

The panel shows the scenario's start as `HH:mm · dd.MM.yyyy`. Clicking it opens the picker.

### The picker

A modal with five stepper rows — Day, Month, Year, Hour, Minute — a live preview (`14:00 · Thursday 21 June 1990`), and Cancel / Apply. `Esc` cancels, `Enter` applies.

**Steppers rather than text fields, on purpose:** every reachable state is a valid date. A typed `31/02/1990` has to be parsed, rejected and explained; a stepper cannot produce it, because the day clamps to the length of the selected month as soon as the month or year changes. Ranges wrap (23:00 → 00:00), the year is clamped to 1900–2100, and minutes move in **5-minute steps**.

While the modal is open, map input is suppressed — `SelectionManager` and `CameraRig` both consult `DateTimeDialog.IsOpen`, because those read `UnityEngine.Input` directly and a raycast-blocking scrim alone would not stop the camera being dragged behind the dialog.

### Presets

Three one-click starts. Time of day is the operationally interesting variable — light, not the calendar, decides how a scenario plays — so the presets are one date at three points in the day:

| Preset | Time | Date | Intent |
|---|---|---|---|
| **DAWN ATTACK** | 05:30 | 21.06.1990 | First light — limited visibility |
| **MIDDAY ADVANCE** | 12:00 | 21.06.1990 | Full daylight — best observation |
| **NIGHT OPERATION** | 23:00 | 21.06.1990 | Darkness — movement under cover |

Add or change them in `UnitPaletteUI.StartPresets`.

> **Note:** time of day is currently **descriptive, not simulated** — visibility, spotting and combat do not yet read the clock. The presets set the scenario's clock and its framing; they do not change combat maths. Wiring time of day into `CombatSystem` and view ranges is an open item.

### Changing the start resets the clock

`GameClock.SetStart` sets both `StartDateTime` and `Now`. In the editor that is invisible — nothing was running. Doing it *during* a battle resets the running clock, which is the honest reading of "this scenario now starts at a different time".

---

## 5. The top-bar readout

Visible in battle mode only; hidden in the editor, where there is nothing to report.

```
┌──────────────────────────────────────────────────┐
│  14:00        21.06.1990  │  x1   «  ❚❚  »      │
└──────────────────────────────────────────────────┘
     time          date        speed   controls
```

Time and date sit **side by side on one line**. The bar is 40 px tall, and stacking two lines of type inside it left both too small to read at a glance. Time leads on the left at full size because it is what the player checks constantly; the date trails on the right, dimmed and right-aligned, because it rarely changes mid-battle — right alignment also keeps its edge fixed as the digits change width.

The readout refreshes whenever the clock panel is visible, not only while time is advancing, so it is correct the instant battle starts — including at speed 0.

Formats: `HH:mm` and `dd.MM.yyyy` (`GameClock.TimeText` / `DateText`).

---

## 6. Persistence

H-hour is saved with the map, so a scenario carries the time of day it is meant to be fought at.

```json
{
  "mapName": "Lyon Dev",
  "startDateTime": "1990-06-21 05:30",
  ...
}
```

Field: `MapSaveData.startDateTime`. Format: `yyyy-MM-dd HH:mm`, `InvariantCulture` — sortable, culture-independent, and readable when someone edits the JSON by hand. (The rest of the UI shows `dd.MM.yyyy`; the save format is deliberately different because a save file has different requirements from a readout.)

- **Saving** (`F5` / pause menu) writes `GameClock.StartToSaveString()`.
- **Loading** (`F9`, scene start) calls `GameClock.SetStartFromSaveString()`.
- A missing, empty or malformed value logs one warning and falls back to `GameClock.DefaultStart` (**1990-01-01 14:00**) rather than throwing. Saves made before this field existed still load — `JsonUtility` leaves the field at its default.

---

## 7. Gotchas

| Symptom | Cause |
|---|---|
| Clock does not advance | Not in battle mode (editor is timeless), speed is PAUSED, or the pause menu is open |
| Clock jumped back to H-hour | The scenario start was changed while a battle was running — see §4 |
| Start reverted after loading a map | The save's `startDateTime` won; it is applied in `ApplySave` |
| An effect keeps animating while paused | Intentional — see the unscaled-time list in §3 |
| Time changed but combat looks the same | Time of day is not simulated yet — see the note in §4 |

---

## Related

`docs/03-GAMEPLAY.md` (combat model) · `docs/05-MAP-SAVES.md` (save schema) · `docs/07-ARCHITECTURE.md` (script map)
