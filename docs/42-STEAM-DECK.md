# Steam Deck

Iron Meridian on a handheld: what had to change, which build to ship, and what is
still missing.

**Status: the port is prepared, not shipped.** Everything below is in the
repository and compiles, and nothing here has been run on a Deck — see §8 before
you promise it to anybody.

---

## 1. A Deck is not "a small PC"

It is a 1280×800 seven-inch screen held at arm's length, an RDNA 2 APU on a 15 W
budget, and — the part that actually breaks a game like this one — **no
keyboard**.

Count what this game does with keys. WASD pans. Q/E rotate. R/F zoom. `C` faces a
formation. `Ctrl+Z` undoes. `F5`/`F9` save and load. `Tab` opens the casualty
list. `Esc`/`P` pause. On a Deck, a build that ignores all this is a build where
none of it exists — and the terrain is the *easy* part, because Cesium streams
over wifi and an APU renders it.

So the work splits three ways: **the pad** (§3), **the panel** (§4), and **which
binary you actually ship** (§2).

---

## 2. Proton or native?

Both work. They are not the same decision.

| | Windows build under Proton | Native Linux build |
|---|---|---|
| Effort | none — it is the build you already make | a second depot, a second build, a second thing to test |
| Layers | Win32 → Proton → Linux → Vulkan | Linux → Vulkan |
| Memory | Proton's overhead, on a machine with 16 GB shared with the GPU | less |
| Controller axes | XInput, emulated | native, and **numbered differently** — see §3a |
| Verification | plenty of Verified titles ship Windows-only | also fine |

**Ship the Windows build first.** For a game with no anti-cheat, no launcher and
no kernel driver, Proton usually just works, and one binary is one thing to keep
correct. `make linux` exists for when the second layer starts costing something
measurable — and because a native build is the only way to know whether it does.

Whichever you ship, the pad and panel work in §3 and §4 is the same, and is what
makes the difference.

---

## 3. The pad

`Core/GamepadInput.cs` defines the gestures once and the handful of places that
need them read it — the same shape as `Core/TouchInput.cs` for Android, and for
the same reason: thirty-odd `Input.GetKey` call sites should not each learn about
a controller.

| Keyboard / mouse | Pad | Read by |
|---|---|---|
| WASD pan | **Left stick** | `CameraRig` |
| Q / E rotate | **Right stick, horizontally** | `CameraRig` |
| Middle-drag tilt | **Right stick, vertically** | `CameraRig` |
| R / F zoom | **Triggers** | `CameraRig` |
| **Right click** | **B** | `SelectionManager` |
| `C` face a formation | **X** | `SelectionManager` |
| `Esc` cancel | **B** (also Unity's Cancel) | `SelectionManager` |
| `Tab` casualty list | **Back / View** | `GameController` |
| `Esc` / `P` pause | **Start / Menu** | `PauseMenuUI` |
| Confirm | **A** (also Unity's Submit, so uGUI answers already) | — |

**Start opens the pause menu, B does not.** B is Cancel everywhere else in this
game, and a Cancel that opened a menu would be the one control that surprised.

**Steam Input is not a reason to skip this.** A Deck can be told to send WASD and
a mouse, and for an unported game that is the whole answer — but it is a
per-player configuration, it shows the wrong button glyphs, and it turns the
right stick into a mouse that has to be dragged across a 7-inch screen to reach
the far side of the map. Reading the pad directly means the game works under the
*default* template, which is the one nearly everybody leaves alone.

**Sticks are dead-zoned radially, not per axis.** Testing each axis on its own
carves a cross out of the stick's range, so a diagonal push that clears the
threshold on neither axis reads as nothing while a straight one works. A Deck
that has been in a bag also has stick drift, which is why the zone is a generous
0.22 — a map that pans on its own is the most obvious possible fault.

### 3a. The axis numbers differ between Windows and Linux

The right stick and the triggers are the same physical controls and **different
axis numbers on each platform** — the legacy Input System's oldest wart. Worse,
XInput folds both triggers onto *one* axis (left positive, right negative, both
cancelling) where Linux reports them separately.

Both sets are declared in `ProjectSettings/InputManager.asset` —
`PadRightStickX` and `PadRightStickX_Linux` and so on — and `GamepadInput` picks
between them once, by platform, so nothing downstream ever learns about it. A
Deck running the *Windows* build takes the Windows set, because Proton presents
XInput.

> **Adding a pad control?** Add both axes, and read them through `GamepadInput`.
> A missing axis throws from `GetAxis` on every frame, which turns one missing
> setting into an unreadable console — so `GamepadInput.Axis` catches it and
> warns once instead.

---

## 4. The panel

`Core/SteamDeck.cs` detects the machine and applies what follows from it.

**Detection is by environment variable first.** Valve sets `SteamDeck=1` in the
game's environment on every Deck. It costs nothing and needs no SDK — which
matters here, because Steam integration is behind the `IRONMERIDIAN_STEAM` define
(`docs/36-STEAM.md`) and a build without it would otherwise have no way to know.
Where the SDK *is* compiled in, `SteamUtils.IsSteamRunningOnSteamDeck` is asked
as well.

`-handheld` on the command line forces the same defaults, for the devices that
are Decks in everything but name — a ROG Ally, a Legion Go, a small laptop with a
pad.

What it changes:

| | Value | Why |
|---|---|---|
| Resolution | 1280×800 borderless | The compositor scales anything else, and on a 7-inch panel that is the difference between readable type and a blur |
| UI reference | **1280×800**, not 1920×1080 | Laying out against the panel's own size puts the interface at 1:1 and makes every control half again as large — which is what a thumb and a trackpad need. 16:10, so nothing is cropped |
| Frame cap | 60, **seeded not forced** | A battery decision, and the LCD panel's rate |

**Seeded, not forced.** `DisplaySettings.SeedFrameCap` only writes a value the
player has never chosen. A port that overwrote the settings screen on every
launch would be a bug, not a default — and "they picked unlimited" and "nobody
has picked anything" are the same stored number and very different intentions,
which is what `HasFrameCap` is for.

Quality is deliberately **not** touched. That is the player's, and the Deck's own
per-game performance overlay is better at it than a guess in code.

---

## 5. Building

### 5a. What the machine needs

Unity Hub → Installs → the 6000.0 editor → **Add modules → Linux Build Support
(IL2CPP)** — only for the native build. `make doctor` reports whether it is
there.

### 5b. The job

```powershell
make linux              # or: .\scripts\build-linux.ps1 -Clean
```

IL2CPP, Vulkan first with OpenGL behind it, 1280×800 borderless by default.

**The executable bit cannot be set from Windows.** Steam sets it on install, so a
depot upload is fine; a build copied to a Deck by hand needs `chmod +x
IronMeridian.x86_64` before it will start.

---

## 6. Shipping it

The Linux depot template is `steam/depot_linux.vdf` — the Windows one with the
paths changed. `steam-upload.ps1` now takes a platform:

```powershell
.\scripts\steam-upload.ps1 -Platform Linux -Token Exclude -User <login> -Preview
```

`-SourceDir` defaults to `Builds\<Platform>`, so the two never get crossed.

Everything in `docs/36-STEAM.md` still applies, including the two things that
have to be settled before a release date exists. **The ion token question is
unchanged and unimproved**: a depot with the token in it hands your metered key
to every buyer, which is why `-Token` has no default (`docs/36-STEAM.md` §2).

### 6a. Deck Verified

Not applied for, and this port does not automatically earn it. Valve's checklist
wants, among other things: correct default controller glyphs, legible text at
1280×800, no compatibility warnings, and **the Steam on-screen keyboard for every
text field**. That last one this game fails today — see §8.

---

## 7. Where the code lives

| File | Role |
|---|---|
| `Core/SteamDeck.cs` | Detection, resolution, the seeded frame cap (§4) |
| `Core/GamepadInput.cs` | Sticks, triggers and buttons as the game's own signals (§3) |
| `Core/DisplaySettings.cs` | `HasFrameCap` / `SeedFrameCap` — a default that does not overwrite a choice |
| `Map/CameraRig.cs` | `TickPad` — pan, orbit, tilt and zoom on the pad |
| `Units/SelectionManager.cs` | B as right-click, X as `C` |
| `UI/UIFactory.cs` | `ReferenceResolution` — 1280×800 on a handheld |
| `ProjectSettings/InputManager.asset` | The `Pad*` axes, both platforms' numbering (§3a) |
| `Editor/LinuxBuild.cs` · `scripts/build-linux.ps1` | The native build (§5) |
| `steam/depot_linux.vdf` | The Linux depot (§6) |

---

## 8. Known gaps

Honest list. None of this is done.

- **Nothing has been run on a Deck.** The port compiles and the pieces are wired;
  the first real session will find things this list does not.
- **No on-screen keyboard.** The rename field and the UNITS search box need a
  keyboard, and a Deck has none unless the game asks Steam for one
  (`SteamUtils.ShowGamepadTextInput`, behind the `IRONMERIDIAN_STEAM` define).
  Today those fields are unusable on a Deck without the player summoning the
  keyboard themselves with **Steam + X**. This is also a Verified requirement
  (§6a).
- **No cursor for the pad.** Selecting a formation still needs a pointer, which
  on a Deck means the right trackpad through Steam Input. The left stick pans and
  nothing moves a cursor, so the pad is a *camera* controller, not yet a complete
  one.
- **No button glyphs anywhere.** Every hint line in the game names keys. On a
  handheld they name things that are not there.
- **No Steam Input configuration shipped.** A default `controller_config` uploaded
  through Steamworks is what makes the trackpads and back buttons sensible out of
  the box.
- **Quality is not tuned for 15 W.** Cesium's tile budget and screen-space error
  are the desktop's, and nobody has measured a frame on the actual hardware.
- **`make check` does not compile for Linux.** Roslyn against Unity's reference
  assemblies catches C# errors, not IL2CPP or native-plugin ones.
- **Battery, suspend and resume are untested.** A Deck sleeps mid-battle as a
  matter of routine, and Cesium holds network connections that will not survive
  it.
