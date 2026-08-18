# The task runner

Everything routine in this project is a script under `scripts/`, and until now
you had to remember which one and what to pass it. This is the index: one menu
listing every job, reachable three ways.

```powershell
make                      # the menu
make installer            # one job, named
.\scripts\menu.ps1        # the same menu, but pick from it — no make needed
```

All three run the same code. `Makefile` targets and the interactive picker are
both driven from the `$Jobs` table in `scripts/menu.ps1`, so they cannot
describe different work.

## 1. The jobs

| | Job | Does |
|---|---|---|
| **Build and ship** | `setup` | Generate the scenes and build settings (Unity, project closed) |
| | `build` | Build the Windows player into `Builds\Windows` |
| | `installer` | Build the player **and** package it as a setup `.exe` |
| | `package` | Package the player already in `Builds\Windows` |
| | `run` | Launch the built player |
| **Data and artwork** | `units` | Regenerate `units.json` |
| | `icons` | Regenerate the APP-6 unit icons |
| | `stat-icons` | Regenerate the unit info panel's stat glyphs |
| | `units-doc` | Rewrite the tables in `docs/04-UNITS.md` from `units.json` |
| | `installer-art` | Regenerate the installer icon and wizard bitmaps |
| | `data` | All four generators, in dependency order |
| **Unity tools** | `models` | **Install Unit Models** |
| | `vfx` | **Install VFX Prefabs** |
| | `packages` | **Import Bundled Packages** |
| **Steam** | `steam-check` | Release preflight — app id, icon, version, build, licences |
| | `steam-appid` | Write `steam_appid.txt` beside the player, to test Steam locally |
| **Project** | `check` | Compile the runtime C# with Roslyn, without opening Unity |
| | `doctor` | Check Unity, Python, Pillow, Inno Setup and the token |
| | `logs` | Tail the last Unity setup and build logs |
| | `clean` | Delete the packaged installers and the build logs |
| | `distclean` | Delete everything under `Builds\`, player included — asks first |

`data` runs its four in the order they depend on each other: `units.json` first,
because the icons are drawn one per unit id and the doc is rewritten from the
file. Run them out of order by hand and you get icons for units that no longer
exist.

### 1a. Start here

`make doctor` before anything else. It reports on each tool the other jobs need
and gives the one command that fixes each gap:

```
Toolchain
  Unity          ok   C:\Program Files\Unity\Hub\Editor\6000.5.5f1\Editor\Unity.exe
  Python         ok   C:\Users\...\python.exe
  Pillow         ok
  Inno Setup     ok   C:\Program Files (x86)\Inno Setup 6\ISCC.exe

Project
  Cesium token   ok   configured (not shown)
  Scenes         ok   16 generated
  Player         ok   iron-meridian.exe
```

It reports the token as present or absent and **never prints it** (golden rule
1). Nothing else in the runner reads it.

## 2. Options

The jobs are the common case with no arguments. Anything with options is the
script itself — make is not in the way:

```powershell
.\scripts\build-installer.ps1 -IncludeToken -Version 1.1
.\scripts\build-windows.ps1 -UnityPath "C:\...\Unity.exe" -ExeName Foo.exe
.\scripts\unity-run.ps1 -Method IronMeridian.EditorTools.ModelInstaller.Install
.\scripts\compile-check.ps1 -Define IRONMERIDIAN_STEAM
.\scripts\steam-upload.ps1 -Token Exclude -User <login> -Preview
```

`steam-upload.ps1` has no target at all, rather than one with defaults filled
in: it uploads to a storefront, and its `-Token` choice decides whether your
Cesium credential goes out with the build (`docs/36-STEAM.md` §2). A job that
could do that by accident should not exist.

That split is deliberate. A make target per flag combination is a second, worse
copy of an interface the scripts already have.

## 3. Installing make

GNU Make is not part of a Unity install and nothing here requires it:

```powershell
winget install --id ezwinports.make     # make 4.4.1
```

Without it, `.\scripts\menu.ps1` is the same menu with a picker, and every job
is a script you can run directly.

Recipes name the interpreter (`pwsh -NoProfile -File ...`) instead of setting
`SHELL`, so the Makefile behaves the same whether make hands recipes to
`cmd.exe`, `sh` or PowerShell. On Windows PowerShell 5.1 rather than PowerShell
7:

```powershell
make PWSH=powershell installer
```

That path is worth knowing about when editing `scripts/*.ps1`: Windows
PowerShell reads a script with no byte-order mark as ANSI, which turns a UTF-8
em dash into `â€"` — and the trailing `"` is a **smart quote, which PowerShell
accepts as a string delimiter**. One em dash inside one string literal is
enough to make the whole file fail to parse, several lines from where it looks
like the problem is. The scripts here are therefore saved as UTF-8 **with** a
BOM. Keep it that way, or keep them to ASCII.

## 4. Where Unity gets found

`scripts/unity-run.ps1` owns editor discovery for everything here — the newest
`6000.*` under `%ProgramFiles%\Unity\Hub\Editor`. It also runs a single
`-executeMethod` in batch mode, which is what the `setup`, `models`, `vfx` and
`packages` jobs are.

Batch mode cannot open a project the editor already has, so it checks for a
running Unity first and says so, rather than leaving you to find the lock-file
line thirty screens into a log. On failure it prints the tail of that log.

It searches `%ProgramW6432%` **before** `%ProgramFiles%`, which looks redundant
and is not. GNU Make for Windows is a 32-bit binary, so WOW64 rewrites
`ProgramFiles` to `C:\Program Files (x86)` for it and for everything it
launches. PowerShell 7 quietly corrects that on startup and Windows PowerShell
does not — so before this, `make doctor` found Unity and
`make PWSH=powershell doctor` reported it missing, on the same machine, from
the same line of code. `ProgramW6432` is the 64-bit Program Files in either
kind of process. Anything here that looks for an installed tool checks it
first; the Inno Setup lookup does the same.

Editors are then ordered by parsed version, not by name, so 6000.10 sorts above
6000.5. If the Hub was pointed somewhere else entirely, its
`%APPDATA%\UnityHub\secondaryInstallPath.json` is searched too.

## 5. Adding a job

One row in the `$Jobs` table in `scripts/menu.ps1` — group, key, one line of
description, and a script block:

```powershell
@{ Group = "Data and artwork"; Key = "portraits"; Text = "Regenerate commander portraits"
   Do = { Invoke-Python "generate_portraits.py" } }
```

Then add the key to the matching `*_JOBS` list in `Makefile`, and a line to the
comment block above the catch-all rule. The menu, the numbering and `make
<key>` all follow from those two edits — **and update the table in §1 in the
same change.**

## See also

`docs/06-WINDOWS-BUILD.md` (the player) · `docs/34-INSTALLER.md` (the setup
`.exe`) · `docs/01-GETTING-STARTED.md` (first run)
