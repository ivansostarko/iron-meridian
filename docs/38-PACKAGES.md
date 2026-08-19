# Package register

Every package in `Packages/manifest.json`, why it is there, and what it costs.

A Unity package is not free just because it compiles. Some pull in whole
dependency trees, some run code at startup whether you use them or not, and a
few do both — which is how a project ends up shipping a subsystem nobody wrote
a line against. This register exists so that adding one is a decision with a
written reason, and so the answer to "what is this for?" is not "it was in the
manifest".

**Rule: a package added to `manifest.json` gets a row here in the same change.**
`make steam-check` reports packages it cannot find a use for.

## 1. In use

| Package | Version | Why |
|---|---|---|
| `com.cesium.unity` | 1.25.0 | The whole point — 3D Tiles, the WGS84 globe, terrain and imagery streaming. Scoped registry `unity.pkg.cesium.com`. `docs/02-CESIUM.md` |
| `com.unity.ugui` | 2.0.0 | Every screen. All UI is built at runtime by `UIFactory` (golden rule 2) |
| `com.unity.ai.navigation` | 2.0.14 | NavMesh components for ground movement |
| `com.unity.modules.*` | 1.0.0 | Built-in engine modules — physics (PhysX), particles, audio, video, terrain, UI, web request. These are part of the engine, not add-ons |

## 2. Editor-only, no runtime cost

These never reach a player. They are a matter of taste, not of build health.

| Package | Version | What it is | Verdict |
|---|---|---|---|
| `com.unity.collab-proxy` | 2.12.4 | Unity Version Control (formerly Plastic SCM) integration | **Redundant.** This project is on git (`.gitattributes`, `.gitignore`, and a history). Nothing in the repo uses it. Harmless if you like the window; remove it if you do not |
| `com.unity.connect.share` | 4.2.4 | WebGL Publisher — builds for WebGL and uploads to Unity Play | **No path to use here.** Cesium does ship WebGL native libraries, so a WebGL build is not categorically impossible — but this game targets Windows (build settings, the installer, Steam), and Unity Play is not a storefront. Nothing in `Assets/` refers to it |
| `com.unity.sysroot.linux-x86_64` | 2.0.10 | Cross-compilation toolchain, pulled in by the **Linux Build Support (IL2CPP)** editor module | Build-time only, never in a player. Appeared on 2026-08-18 alongside the others. Harmless — but note that if a Linux build is actually intended, that is a decision with its own consequences for Steam (a native Linux depot versus letting Proton run the Windows build) and it is not covered anywhere yet |
| `com.unity.multiplayer.playmode` | — | Virtual players for testing networking | **Not installed.** Intended on 2026-08-18 but present in neither `manifest.json` nor `packages-lock.json` |

Neither has any **game source** surface at all: they add editor windows and menu
items, not APIs a game calls. There is nothing to integrate.

## 2a. Genuinely useful, once you need it

### `com.unity.recorder` 5.1.7 — Recorder

Editor-only capture: video and image sequences straight out of the Game view,
at a fixed resolution and frame rate rather than whatever the editor happened to
manage.

**This one has a job waiting for it.** `docs/36-STEAM.md` §6 needs a trailer and
1920×1080 screenshots for the store page, and this is the tool for both — a
scripted camera move over Lyon recorded at a locked 60 fps looks like a trailer;
a screen capture of the editor does not.

No game source involvement: it is a window and a set of recorder tracks. Nothing
to integrate, and nothing that reaches a player.

## 3. Installed, unused — kept on purpose

The list below is packages nothing in `Assets/` references. Most are inert
dead weight; the ones that are not are called out.

> **Where this stands, 2026-08-19.** Three rounds of installs took the project
> from **46 to 90 resolved packages**, and from 6 non-module direct
> dependencies to 24. Not one of the packages added in those rounds required — or received — a single line of game
> code, and several arrived with dependencies nobody asked for.
>
> One of them, `com.unity.recorder`, has a real job here (§2a).
>
> **Decision, 2026-08-19: strip them.** An earlier decision on 2026-08-18 was to
> keep them and accept the cost. That was revisited once the cost became visible
> in the editor rather than theoretical in a manifest — extra menus, a Netcode
> overlay across the Scene view, Package Manager errors in the console. §5a is
> the tool.
>
> The reasoning below is kept because it is *why*, and because the same
> questions apply to the next package that gets installed (§6).

### `com.unity.physics` 1.4.7 — Unity Physics (DOTS)

This is the one to look at.

**It is not an upgrade to the physics the game uses.** Unity Physics is the
ECS/DOTS stack: it simulates entities in a `World`, not `GameObject`s with
`Rigidbody` and `Collider`. The game's physics is `com.unity.modules.physics`
(PhysX), which is still installed and still what everything runs on. The two do
not interoperate — one is not a replacement for the other, they are parallel
worlds.

**What it dragged in.** Declaring it added a dependency tree, all of which now
compiles into the project:

```
com.unity.physics 1.4.7
└── com.unity.entities 1.4.8
    ├── com.unity.burst 1.8.29
    ├── com.unity.collections 2.6.8
    ├── com.unity.mathematics 1.3.2
    ├── com.unity.serialization 3.1.5
    └── com.unity.nuget.mono-cecil, scriptablebuildpipeline, profiling.core, …
```

**What it costs at runtime.** Entities is not inert. It contains:

```csharp
// Unity.Entities.Hybrid/Injection/AutomaticWorldBootstrap.cs
#if !UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_RUNTIME_WORLD
    static class AutomaticWorldBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            DefaultWorldInitialization.Initialize("Default World", false);
        }
    }
#endif
```

So **every build creates a Default World and its default system groups before
the first scene loads**, whether or not a single line of ECS exists. In this
project there is not a single line: no `using Unity.Entities`, no
`using Unity.Physics`, no `SystemBase`, no `IComponentData`, no `[BurstCompile]`
anywhere under `Assets/`.

**What integrating it would actually mean.** Rewriting the game — units,
movement, combat, selection — from `MonoBehaviour` to entities and systems.
That is not a package adoption; it is a different project. Nothing about the
current architecture (runtime-built uGUI, geodetic positions, tick-based
battles) asks for it.

**Recommendation: remove it.**

```
Window → Package Manager → Unity Physics → Remove
```

Entities and its tree go with it, since nothing else depends on them.

**If it stays**, at minimum stop the unused world from starting. Player Settings
→ Scripting Define Symbols:

```
UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP
```

That is the only "source" change Unity Physics warrants in a project that does
not use ECS — and it is a setting, not code.

### The Unity Gaming Services group

Four packages, and a dependency tree that is larger than all of them.

| Declared | Version | What it is |
|---|---|---|
| `com.unity.remote-config` | 4.2.5 | Fetch config values from a UGS dashboard at runtime |
| `com.unity.services.push-notifications` | 4.0.2 | Push notifications — **Android and iOS only** |
| `com.unity.multiplayer.widgets` | 1.0.5 | Drop-in UI for UGS lobbies/matchmaking |
| `com.unity.multiplayer.center` | 1.0.0 | Editor window that recommends multiplayer packages |

What they brought with them, none of it requested:

```
com.unity.services.core 1.18.0          ← everything below hangs off this
com.unity.services.analytics 6.3.0      ← a data-collection SDK
com.unity.services.authentication 3.7.3
com.unity.services.multiplayer 1.1.0    (lobby, relay, matchmaking)
com.unity.services.qos / wire / deployment
com.unity.transport 2.7.2
com.unity.mobile.notifications 2.4.3    ← Android/iOS notification bindings
com.unity.nuget.newtonsoft-json 3.2.2
```

**`com.unity.services.analytics` is the one to know about.** Nobody asked for
it: it is a dependency of `push-notifications`, which is a mobile package on a
Windows-only game.

**It is dormant, and it is worth being precise about why.** UGS packages
register themselves with `CoreRegistry` at `BeforeSceneLoad`, but registration
is not initialisation — nothing connects, transmits or persists an identifier
until something calls `UnityServices.InitializeAsync()`. Nothing under
`Assets/` calls it, or references `AnalyticsService`, or `RemoteConfigService`.
So the SDK is compiled in and asleep.

The reason to care anyway: it is **one line from awake**, in a project that is
being prepared for sale. If anyone ever adds `InitializeAsync()` — to make
Remote Config work, say — analytics comes up with it, and that is the point at
which a Steam store page needs a privacy disclosure and the build acquires
obligations under data-protection law. Better to know that now than to discover
it from a store review.

Also note **push notifications cannot function here at all**: `mobile.notifications`
binds Android and iOS APIs, and this game ships to Windows.

**Remote Config specifically** overlaps something the project already has.
Runtime tuning is `tuning.json`, a sparse patch written by the in-game Units
List and applied over the generated catalogues (golden rule 3, `Save/TuningStore.cs`).
That works offline, ships with the game and is already wired to a UI. Remote
Config would replace it with a mechanism that needs a linked UGS project, a
network round-trip and an account — for a single-player game. If there is a
reason to prefer it, it should be written down before the switch, not after.

**What they wrote into the project.** Installing them was not read-only:

| File | Consequence |
|---|---|
| `Assets/Resources/pushNotificationsSettings.asset` | **This one ships.** `Assets/Resources` is the runtime-loadable folder (golden rules 8–10) — everything in it goes into the player. It is a Firebase-keyed settings object, with empty keys, in a Windows game that cannot receive a push notification |
| `ProjectSettings/NotificationsSettings.asset` | Editor-side, harmless |
| `ProjectSettings/EntitiesClientSettings.asset` | Editor-side, from §3's Entities |
| `ProjectSettings/Packages/` | Per-package editor settings |
| `cloudProjectId` in `ProjectSettings.asset` | The project is now **linked to a Unity Cloud organisation** (`projectName: Iron Meridian`). That is the prerequisite that makes the services above *able* to start. Nothing starts them yet — but the gap between dormant and live just got one step smaller |

`make steam-check` watches `Assets/Resources` for stray package settings for
exactly this reason.

### `com.unity.multiplayer.tools` 2.2.10, `.widgets`, `.center`

These are companions to **Netcode for GameObjects**, which is *not installed*:

```
com.unity.netcode.gameobjects     ← absent
com.unity.multiplayer.playmode    ← absent (intended, but never resolved)
```

`multiplayer.tools` profiles network traffic there is none of.
`multiplayer.widgets` supplies UI for UGS lobbies the game never creates.
`multiplayer.center` is a window that recommends the packages.
`multiplayer.playmode` — virtual players for testing networking — was meant to
be installed and is in neither `manifest.json` nor `packages-lock.json`; see the
note in §4 about the editor and the manifest.

And the destination is a stub: `MultiplayerUI` is a `PlaceholderScreenUI`
subclass (`Assets/Scripts/UI/PlaceholderScreenUI.cs`) — a screen that says the
feature is not built yet.

None of this is wrong to want. But multiplayer starts with a networking model
and a decision about authority, state and what a "tick" means across a wire —
for a game whose battles are tick-based and whose positions are geodetic, that
is a design problem, not a package problem. The tools are worth installing the
day there is traffic to profile.

### `com.unity.ml-agents` 4.1.0 — ML-Agents

Brought in `com.unity.ai.inference` 2.6.1 (the neural inference runtime).

ML-Agents is not a package you switch on. It is a training pipeline: you write
`Agent` subclasses that expose observations and accept actions, define a reward
function, then run a **Python** training process against the editor for hours or
days, and ship the resulting model file to run under inference.

The game already has an opponent with three settings — `Difficulty.Recruit`,
`Regular`, `Veteran` (`Assets/Scripts/Data/PlayerData.cs`), each with an
authored description of how it fights. Replacing that with a learned policy is a
research project whose hard part is the reward function for an operational
wargame, not the package.

Nothing references `Unity.MLAgents`.

### The third round, 2026-08-18

Eight more, taking the project from 74 resolved packages to **89**. Two of them
point at things the project genuinely might want, so they get a real answer
rather than a line in a table.

#### `com.unity.localization` 1.5.12 — the one worth thinking about

Steam store pages list supported languages, and translation is one of the
cheapest ways to widen a market. This is a legitimate thing to want.

It is also the largest piece of work on this page, for a reason specific to
this project: **all UI is built at runtime in code** (golden rule 2). There is
no scene full of `Text` components to attach a `LocalizeStringEvent` to. Every
label is a C# string literal passed to `UIFactory` — roughly **1,460 of them
across 43 files** in `Assets/Scripts/UI` alone, before menus, briefings,
mission text, unit names in `units.json` or the campaign blurbs in
`MissionData.cs`.

It also brought **`com.unity.addressables` 2.9.1**, which is a second asset
system alongside the `Resources.Load` that golden rules 8–11 are all built on.
Localization uses Addressables to load string tables; adopting it means both
systems in the build.

None of that is an argument against localizing. It is an argument that
localizing is **a project with a design step** — route every user-facing string
through one lookup, then decide what backs the lookup — and that step is worth
doing before the package, not after. A key-based indirection through `UIFactory`
would make the eventual backend (Localization, or a plain JSON table) an
implementation detail.

Nothing references `UnityEngine.Localization` today.

#### `com.unity.purchasing` 5.4.2 — the wrong tool for the DLC screen

There is a **DLC** placeholder screen, so this looks like a deliberate aim. It
will not work: Unity IAP targets Google Play, the App Store and Amazon. **It has
no Steam backend.**

Steam DLC is a separate app id, bought on the store, checked at runtime with one
call. `SteamIntegration.OwnsDlc()` and `OpenStorePage()` now exist for exactly
this, and `docs/36-STEAM.md` §3c has the pattern. The game handles no money and
needs no payment SDK.

The package also pulls `com.unity.modules.androidjni` and another copy of the
UGS core stack.

#### The rest

| Package | Version | Why it cannot help here |
|---|---|---|
| `com.unity.services.economy` | 3.5.4 | UGS virtual currency and player inventories, for live-service games. This is a single-purchase single-player wargame; there is no economy to run and no server to run it on |
| `com.unity.mobile.android-logcat` | — | Reads Android device logs. Windows target |
| `com.unity.device-simulator.devices` | — | Phone and tablet screen profiles for the Device Simulator. Windows target |
| `com.unity.2d.animation` | 10.2.3 | Skeletal animation for **sprites**. The game is 3D geospatial; models use legacy `Animation` (golden rule 10). Brought `2d.common` and `2d.sprite` |
| `com.unity.formats.fbx` | — | *Exports* FBX from Unity. The pipeline imports FBX and generates prefabs via `ModelInstaller` (`docs/09-3D-MODELS.md`); nothing here produces geometry to export |
| `com.unity.asset-manager-for-unity` | — | Editor client for Unity Cloud asset storage. Assets here live in git |

All editor-only or unreferenced. None has a game-source surface.

### `com.unity.timeline` 1.8.13 — Timeline

Installed as a direct dependency. Nothing in the project references
`PlayableDirector`, `TimelineAsset` or `UnityEngine.Playables`. The game has no
authored cutscenes — the intro and briefing films are video files played through
`VideoCatalog` (`docs/32-VIDEO.md`), which does not use Timeline.

Editor-heavy, harmless at runtime while unused. **Remove unless you are planning
authored in-engine sequences.**

## 4. To remove

### `com.unity.logging` 1.3.10 — decided against, 2026-08-18

Installed, never referenced, and **due to be removed**.

It was the only one of the recent additions that could have been adopted
without restructuring the game — it is a logging API, not an architecture, and
its configurable file sink would have given players something to attach to a bug
report. It was dropped anyway, because:

- Unity already writes `Player.log` beside the save folder for free.
- The package is built for Burst-compiled jobs, and this project has none.
- Adoption would have meant **71 call sites across 35 files** (22 `Debug.Log`,
  33 `Debug.LogWarning`, 16 `Debug.LogError`) plus startup configuration —
  a real change that deserved to be its own decision, not a consequence of a
  package having been installed.

`Debug.Log` remains the right tool here. If a player-facing log file is wanted
later, the shape is a small `Logging` wrapper in `Core/` that call sites go
through, and the sink behind it is then an implementation detail — with or
without this package.

Its dependencies, Burst and Collections, will **not** leave with it: they are
still pulled in by Entities (§3).

**Remove it from the editor, not from the file:**

```
Window → Package Manager → In Project → Unity Logging → Remove
```

> **`Packages/manifest.json` cannot be hand-edited while Unity is open.** The
> editor holds the resolved package set in memory and rewrites the file from
> it, so a line deleted in a text editor reappears within seconds and the
> removal looks like it silently failed. This was tried on 2026-08-18 and
> reverted exactly that way.
>
> Either use Package Manager in the running editor, or close Unity first and
> then edit the file. The same applies to any script or tool that edits the
> manifest — including `make` jobs, which is why there is no target for it.

## 5. What none of them need

None of the packages in §2 or §3 requires a single line of game source to be
"properly installed":

- `collab-proxy` and `connect.share` add editor windows, not APIs.
- `physics` would require rewriting the game to ECS to use at all.
- `timeline` needs authored assets, not code.
- `logging` offers an API the project could adopt, but has no obligation to.

An unused package is a build-size and startup question, not an integration
task. The only genuine source-level action on this list is the
`UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP` define in §3 — and only if Unity
Physics stays.

## 5a. Cleaning up

`scripts/packages-reset.ps1` strips the manifest back to what the project
references, plus Recorder. It reports by default and writes nothing:

```powershell
make packages-audit                       # what would go
.\scripts\packages-reset.ps1 -Apply       # do it - Unity must be closed
```

It **refuses to write while Unity is running**, for the reason in §4: the editor
rewrites the manifest from its own resolved state, so the edit would revert
within seconds and look like a silent failure. Close the editor, apply, reopen —
Unity re-resolves and `packages-lock.json` shrinks with it.

The manifest is backed up to `manifest.json.bak` first. To undo, copy it back
**before** opening Unity.

Keep something extra with `-AlsoKeep`:

```powershell
.\scripts\packages-reset.ps1 -AlsoKeep com.unity.collab-proxy -Apply
```

### What the clutter actually costs

The editor is the visible half. Three rounds of installs added a `Services`,
`Jobs`, `Publish` and `GDK` menu, a Unity Version Control tab, a Background
Tasks window, and a **Netcode scene-view overlay** — the "Server/Host, Clients,
No Clients Connected" panel from `com.unity.multiplayer.tools`, sitting over the
Scene view of a game with no networking. It can be switched off from the Scene
view's **Overlays** menu (the ⋮ button, or `` ` ``), but it comes back with the
package.

The console noise is the other half:

```
ScriptableSingleton already exists. Did you query the singleton in a constructor?
UnityEditor.ScriptableSingleton`1<...PackageManagerProjectSettings>:.ctor ()
UnityEditor.ScriptableSingleton`1<...ServicesContainer>:.ctor ()
```

That one is **Unity's own Package Manager UI**, not project code — nothing under
`Assets/` can cause or fix it. It is benign, and it is the kind of thing that
shows up when the package set churns hard. It goes away with a clean re-resolve.

## 6. Adding a package

1. Have a use for it *before* installing it. "It sounded useful" is how §3
   happened.
2. Check what it drags in — `Packages/packages-lock.json` after resolving, or
   the package's `dependencies` on its Package Manager page. A `depth=1` entry
   you did not ask for is the thing to look at.
3. Check whether it runs anything at startup. `RuntimeInitializeOnLoadMethod` in
   the package source is the giveaway.
4. Add a row to §1, or explain in §2/§3 why it is here without being used.
5. `make check` — the compile still has to pass.

## See also

`docs/02-CESIUM.md` (the one package that matters) · `docs/36-STEAM.md`
(release preflight, which reads this situation) · `docs/07-ARCHITECTURE.md`
(why the project is MonoBehaviour-shaped)
