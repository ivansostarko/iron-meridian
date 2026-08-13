# Weather and Sky Atmosphere

How the sky, the weather and the day/night cycle work: what each setting changes, how they combine, and what is simulated versus what is only shown.

---

## 1. Two axes, not one list

Weather in Iron Meridian is **two independent settings**:

| Axis | What it is | Options |
|---|---|---|
| **Sky phase** | The time of day — where the sun is and what colour the light is | Day, Sunset, Night |
| **Condition** | What is falling out of the sky and how far you can see through it | Clear, Overcast, Fog, Rain, Storm, Snow |

Any sky can be combined with any condition. That is the point: **a night storm and a midday storm are both real**, and folding light into a single flat weather list would make them mutually exclusive.

It is also what makes the automatic day/night toggle coherent. If "Night" were just another weather option, turning on auto day/night would have to fight whatever weather the player picked. Because the axes are separate, the clock can own the sky while the player keeps the condition.

```
        Day  ─┐
     Sunset  ─┼─ SKY  ×  CONDITION ─┬─ Clear / Overcast / Fog
      Night  ─┘   ▲                 └─ Rain / Storm / Snow
                  │
        auto day/night drives this from the scenario clock
```

---

## 2. Where things live

| Piece | File | Responsibility |
|---|---|---|
| The register | `Assets/Scripts/Weather/WeatherCatalog.cs` | Sky and condition tables; the day/night rule |
| The system | `Assets/Scripts/Weather/WeatherSystem.cs` | Applies lighting, fog, precipitation and ambience |
| The UI | `Assets/Scripts/UI/UnitPaletteUI.cs` → **WEATHER CONDITIONS** section | Sky buttons, auto toggle, condition list |
| Audio | `Assets/Scripts/Audio/AmbienceManager.cs` | The weather bed — see `docs/10-AUDIO.md` |
| Persistence | `Assets/Scripts/Data/MapSaveData.cs` | `skyPhase`, `weatherCondition`, `autoDayNight` |

Map editor → left rail → **WEATHER CONDITIONS** (opens the section panel).

---

## 3. Sky phases

Sun elevation is what sells time of day, so each phase is a sun angle, a light colour, an intensity and an ambient level.

| Phase | Sun angle (X, Y) | Sun colour | Intensity | Ambient | Reads as |
|---|---|---|---|---|---|
| **DAY** | 55°, −35° | Near-white | 1.35 | Cool grey | High sun, full observation |
| **SUNSET** | 8°, −60° | Warm orange | 1.05 | Dim warm | Low sun, long shadows, glare |
| **NIGHT** | −18°, 200° | Cold blue | 0.28 | Very dark blue | Darkness, movement under cover |

Night puts the light **below the horizon and facing the other way**, so what remains reads as moonlight rather than as a dim sun. Ambient is dropped hard as well: without that, night under cloud still looks like overcast noon, because ambient light alone would carry the scene.

The camera's clear colour is also set per phase. It is only visible where terrain has not streamed in yet — but that is exactly where a bright blue void would break the mood.

---

## 4. Conditions

| Condition | Precipitation | Light × | Fog | Ambience |
|---|---|---|---|---|
| **CLEAR** | — | 1.00 | off | — |
| **OVERCAST** | — | 0.62 | 0.000012 | — |
| **FOG** | — | 0.55 | 0.000075 | — |
| **RAIN** | Rain, 900/s | 0.55 | 0.000030 | `rain-background` |
| **STORM** | Rain, 2000/s | 0.34 | 0.000055 | `storm-background` |
| **SNOW** | Snow, 700/s | 0.70 | 0.000045 | `snow-background` |

**Light ×** multiplies the sky phase's sun intensity — cloud cover expressed as one number — and tints the sun toward the condition's grey. Ambient is scaled with it too, at 60 % strength, so heavy weather darkens the whole scene rather than only its lit faces.

**Fog** is Unity's exponential-squared fog, coloured from the sky's horizon so distance fades toward the horizon rather than toward an unrelated grey. Densities look tiny because the map is measured in metres: at 0.000075 (Fog) visibility collapses within a few kilometres.

Conditions carrying an audio bed are marked with a **♪** in the panel.

---

## 5. Precipitation

One particle system, reconfigured per condition rather than one per weather type — only one thing can be falling at a time, and rebuilding on every change would churn allocations for nothing.

Three details make it work at map scale:

- **World simulation space.** The camera moves *through* the weather; it does not carry it. Local space would make rain slide sideways with every pan.
- **The emitter follows the camera** each frame, so precipitation is always around the viewer no matter where on the globe they are.
- **Everything scales with camera altitude.** The strategic camera sits anywhere from ~100 m to tens of kilometres up. The emitter box is `altitude × 1.6` (clamped 300 m – 9 km) and drop size scales with it, and the whole rig **re-sizes when altitude changes by more than ~50 %**. Without that, rain chosen at ground level is invisible from 12 km up and vice versa.

Rain uses **stretched** billboards so drops read as falling streaks; snow uses round billboards with a noise field so it drifts. Snow also falls roughly 8× slower and lives 6 s instead of 2.2 s.

---

## 6. Automatic day / night

**AUTO DAY / NIGHT** in the weather panel. When on, the scenario clock drives the sky phase and the manual sky buttons go inactive.

The rule:

| Clock time | Phase |
|---|---|
| 05:00 – 06:00 | **Sunset** (dawn) |
| 06:00 – 22:00 | **Day** |
| 22:00 – 23:00 | **Sunset** (dusk) |
| 23:01 – 04:59 | **Night** |

So: **day from 05:00 to 23:00, night from 23:01 to 04:59**, with an hour of sunset either side of the daylight window so dawn and dusk are not hard cuts. Constants are `WeatherCatalog.DayStartHour`, `NightStartHour` and the sunset window.

The phase is re-evaluated four times a second — ample for something that changes on the hour, and it keeps `DateTime` work off the per-frame path.

**Picking a sky by hand turns auto off.** Silently ignoring the click would be worse than the toggle flipping, so `SetPhase` clears the flag.

Because the clock only runs during a battle (see `docs/13-DATE-AND-TIME.md`), auto day/night is effectively static in the editor — it shows the phase for the scenario's start time — and advances once the battle is running. A long battle started at 22:30 will roll into night by itself.

---

## 7. Editor preview vs battle mode

| What | In the editor | In battle |
|---|---|---|
| Sun angle, colour, intensity | ✅ applied immediately | ✅ |
| Ambient and camera clear colour | ✅ | ✅ |
| Fog | ✅ | ✅ |
| Precipitation | ✅ | ✅ |
| **Weather audio bed** | ❌ silent | ✅ plays |
| Auto day/night advancing | static at H-hour | advances with the clock |

Visuals preview in the editor because you cannot choose a sky you cannot see. Audio does not, because a rain loop droning while counters are being laid out is noise rather than atmosphere. `WeatherSystem.SetBattleRunning` is wired to `CombatSystem.RunningChanged`, and `OnDestroy` stops the bed so leaving the scene never leaves rain running under the menus.

---

## 8. Persistence

Weather saves with the map, so a scenario carries the conditions it is meant to be fought in.

```json
{
  "startDateTime": "1990-06-21 05:30",
  "skyPhase": "Night",
  "weatherCondition": "Storm",
  "autoDayNight": false
}
```

Both axes are stored separately, so a night storm round-trips correctly. `skyPhase` stores the **manually chosen** phase, not the derived one — persisting the derived phase while auto day/night was on would silently overwrite the player's choice with whatever the clock happened to say.

Unknown or missing values fall back to `Day` / `Clear` rather than throwing, so older saves and hand-edited JSON still load.

---

## 9. What is *not* simulated

**Weather is currently atmosphere, not mechanics.** Nothing in `CombatSystem`, `UnitMover` or the view/weapon ranges reads the weather or the sky. Fog at 0.000075 hides the map from the *player*, but a unit's `viewRangeKm` is unchanged.

The obvious next steps, in rough order of value:

1. **Visibility** — scale `viewRangeKm` by the condition (fog and night cut spotting hardest).
2. **Movement** — snow and storm reduce `speedKmh`; mud after rain.
3. **Air and indirect fire** — ground the drone units and degrade `canIndirectFire` accuracy in storms.
4. **Night operations** — a night modifier interacting with unit `training`.

Until then, treat the presets as scene-setting. The panel's wording is deliberately descriptive ("reduced observation") rather than promising a number.

---

## 10. Adding a condition or sky

1. Add the enum value in `WeatherCatalog` (`WeatherCondition` or `SkyPhase`).
2. Add its row to `Conditions` or `Skies` — light multiplier, tint, fog, precipitation, ambience.
3. If it needs audio, add an `AmbienceTrack` and an `AmbienceDef` in `AudioCatalog`, put the file under `Assets/Resources/Audio/weather/`, and **update `docs/10-AUDIO.md` §2.1a**.
4. If it needs a new precipitation look, extend `Precipitation` and `WeatherSystem.ApplyPrecipitation`.
5. The UI builds itself from the catalogue — no panel changes needed.
6. **Update this file** — the tables in §3/§4.

---

## Related

`docs/13-DATE-AND-TIME.md` (the clock that drives auto day/night) · `docs/10-AUDIO.md` (ambience channel) · `docs/08-PARTICLE-SYSTEMS.md` (the other particle system) · `docs/05-MAP-SAVES.md` (save schema)
