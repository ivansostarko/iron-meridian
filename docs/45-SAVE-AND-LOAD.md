# Save and Load

Named saved games, where they live, and what a save actually contains.

> **Cloud saves are a preview.** The flow is wired end to end against a
> stand-in store; the files stay on this machine. Every surface that shows the
> cloud says so. See §4.

---

## 1. What there is

Three things write scenario state, and they are deliberately different tools:

| | Gesture | Writes | For |
|---|---|---|---|
| **Quick save** | `F5` / `F9` | The scenario's own map file | Laying a scenario out. Write it, keep working, read it back |
| **Mission save** | MISSIONS panel → **SAVE MISSION + MAP** | The mission record **and** its map file | Authoring a mission — see `docs/22-MISSIONS.md` |
| **Saved games** | Pause menu (`Esc`) → **SAVE** / **LOAD** | A named slot, local or cloud | Playing. Keep the position before an attack *and* after it |

### What the save browser replaced

SAVE and LOAD on the pause menu used to write and read one file with no name, no
list and no confirmation, and the status line said `Game saved.` whether or not
anything had been written. You could not keep two attempts at the same mission,
could not tell what a save was of without loading it, and could not tell a
failed save from a successful one. Everything below follows from wanting those
three things.

---

## 2. A save

`Save/SaveSlots.cs` — `GameSave`.

```jsonc
{
  "slot": "Bridge - 3rd try",
  "savedAtUtc": "2026-08-20T12:03:11.4Z",
  "missionId": "berlin",          // "" for a free-play editor map
  "missionName": "Berlin",
  "mapFile": "berlin.json",
  "unitCount": 41,
  "battleRunning": true,
  "map": { … }                    // the whole MapSaveData
}
```

**The header exists so the browser is cheap.** A save holds an entire order of
battle; a list of twelve would be twelve full parses to draw twelve rows.
Everything the list shows is duplicated at the top of the file and read from
there. The duplication is one-way — the header is written from the map, never
the other way round — so it cannot drift into being the truth.

**`map` is the same record the map editor writes**, which is what makes a save
a scenario rather than a second format with its own bugs. It is deep-copied
through JSON on the way in: the live `_save` keeps being edited afterwards, and
a slot holding a reference to it would quietly become a save of whatever
happened next.

### Loading one

| | |
|---|---|
| **The scenario comes from the slot** | It is the whole of what was saved |
| **The mission comes from the book** | Looked up fresh by `missionId` rather than carried in the file. The designer may well have edited the mission since, and the briefing, the boundary and the headquarters **as they are now** are what the player should get |
| **A save naming a deleted mission still loads** | It is a scenario, and the scenario is all of it that mattered |

---

## 3. The browser

`Assets/Scripts/UI/SaveLoadDialog.cs`.

**Save and load are the same screen in two modes**, not two screens. They ask
the same question — which of these — and the only difference is whether a new
name is allowed. Two dialogs would be two lists to keep looking the same.

| Behaviour | Why |
|---|---|
| **The whole row is the click target** | The row *is* the choice. A 640 px strip with a 16 px hit area is a dialog that feels broken before it feels precise |
| **The name field opens pre-filled** with the mission's name, deduplicated (`Berlin`, `Berlin 2`…) | SAVE is usually the whole of what the player wants, and a dialog that makes them type before they can press it has put a step in front of the verb |
| **Clicking an existing save in SAVE mode fills the name in** | The common reason to click one there is to write over it, and retyping the name to do that is what gets typed slightly wrong |
| **Overwrite asks first** | Through the same `ConfirmDialog` as every other irreversible action here |
| **The footer reports what happened**, and turns amber when it did not | The thing the old status line could not do |
| **Unscaled time, and it does not touch `Time.timeScale`** | The pause menu owns that. A dialog that resumed the game when it closed would resume it *under* the pause menu |
| **Escape closes the browser, not the pause menu behind it** | `PauseMenuUI.Update` stands down while `SaveLoadDialog.IsOpen` or `ConfirmDialog.IsOpen`. Without it one Escape would drop the player onto the map from two levels down |

`SaveLoadDialog.IsOpen` is in `GameController`'s selection input guard, so a
click that lands on the dialog cannot also land on the terrain.

### Slot names

Sanitised, not rejected: a player who types `Bridge — 3rd try` gets a save
called that, not an error about dashes. Letters, digits, space, `-` and `_`
survive; everything else becomes `-`; 40 characters maximum, because it is both
a file name on three operating systems and a list row.

**F5/F9 do not write slots.** They write and read the scenario's own map file,
which is the pair a designer uses while laying one out, and they keep doing
exactly that. Two mechanisms because they are two jobs — see §1.

---

## 4. Destinations

`SaveDestination.Local` and `SaveDestination.Cloud`, as tabs over one list —
they hold the same kind of thing, and the question "where is this save" should
not change what a save *is*.

| | Where |
|---|---|
| **Local** | `persistentDataPath/Saves/` |
| **Cloud** *(preview)* | `persistentDataPath/CloudSaves/` |

### The cloud is a mock, and says so

There is no backend and no account to sign into. What the mock does is keep its
files in a **separate folder on this machine** and report a simulated account,
so the whole flow — pick a destination, see what is in it, save, load, delete,
and the "these are different places" mental model — is real and testable while
the transport is not. The two destinations genuinely are separate stores with
separate contents, so every question the interface asks about them has a true
answer.

It is labelled everywhere it appears:

- the tab reads **CLOUD · PREVIEW**;
- the note under the tabs reads *PREVIEW · signed in as commander@example.com ·
  files stay on this machine*, in the warning colour;
- the footer says *saved to the cloud (preview)*.

Nothing on it ever claims a file has left the machine.

**Replacing the mock is one class.** Implement `SaveSlots.ICloudBackend` — four
verbs, `List` / `Write` / `Read` / `Delete`, plus an account label and an
availability flag — and hand it to `SaveSlots.SetCloudBackend`. Nothing in the
UI or the controller knows which one is installed; `SaveSlots.CloudIsMock` goes
false and the PREVIEW labels disappear with it.

Four verbs rather than a fuller API deliberately: four is what a save browser
asks for, and anything more would be this code guessing at a service nobody has
written yet.

---

## 5. Saving from the pause menu updates the mission

Pressing SAVE while a mission is open means *keep what I have done*. So
`GameController.OpenSaveBrowser` runs `WriteMissionAndMap()` **first, on the way
in** — before the browser is even shown — and the slot is an additional copy
rather than the only one. A player who then picked a name and cancelled would
reasonably expect their mission not to have been thrown away.

`WriteMissionAndMap()` is the single path every scenario write goes through —
`F5`, the pause menu, and the browser:

1. `CollectSave()` reads the live editor state. **All of it**: units, control
   measures, task markers, the FLOT's mode, the rear area, map objects, the
   barrier plan, stocks, the arrival schedule, the chain of command, teams and
   players, the view, the map style, buildings, H-hour, sky and weather.
2. When a mission is open: `MissionLibrary.ReadBackFrom` copies the view and
   weather onto the record, **fog of war is read off the live system** (there is
   one switch, in GENERAL — see `docs/16-FOG-OF-WAR.md`), and
   `MissionSeeder.Seed` fills in a boundary and two headquarters if they are
   still missing (`docs/22-MISSIONS.md`).
3. `MissionLibrary.SaveBook()` writes the record.
4. `SaveSystem.SaveMap()` writes the scenario.

It returns a one-line report, so the flash line says what actually happened
rather than a fixed string.

---

## 6. Web builds

`SaveSlots` calls `Core.WebStorage.Flush()` after every write and delete. In a
browser the file is only in memory until it is flushed to IndexedDB, and closing
a tab is not quitting — see `docs/41-WEB.md` §2.

---

## 7. Where the code lives

| File | Role |
|---|---|
| `Save/SaveSlots.cs` | Slots, both destinations, the mock cloud backend, `GameSave` |
| `Save/SaveSystem.cs` | The scenario file itself — shared with the map editor |
| `Save/MissionLibrary.cs` | The mission book |
| `Save/MissionSeeder.cs` | Boundary and HQ seeding on save |
| `UI/SaveLoadDialog.cs` | The browser |
| `UI/PauseMenuUI.cs` | Where SAVE and LOAD are reached from |
| `Core/GameController.cs` | `WriteMissionAndMap`, `FillGameSave`, `ApplyGameSave` |

---

## 8. Known gaps

- **No autosave.** Nothing writes a slot without being asked. The obvious hook
  is the start of each battle turn, and the obvious question is how many
  rolling slots to keep.
- **No screenshot on the row.** The list is text. A thumbnail of the map would
  make a list of six saves of the same mission far easier to tell apart, and
  `CaptureSystem` already knows how to take one — see `docs/39-CAPTURE.md`.
- **No save-format version field.** `MapSaveData` tolerates missing fields
  because `JsonUtility` leaves them at their initialisers, which has been enough
  so far; a genuine format break would need a version number and a migration.
- **The cloud is a mock** — §4.

---

## Related

`docs/22-MISSIONS.md` (the mission record, and what SAVE MISSION writes) ·
`docs/05-MAP-SAVES.md` (the scenario file) · `docs/41-WEB.md` (IndexedDB) ·
`docs/03-GAMEPLAY.md` (the pause menu)
