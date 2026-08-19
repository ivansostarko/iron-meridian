# Publishing on Steam

Two halves, and they are not equally hard.

The **mechanical** half — integration, depot, upload, icons, versioning — is
built and mostly automated here. Run `make steam-check` and it tells you what is
still red.

The **other** half is three questions nobody can answer from inside this repo,
and one of them can make the release financially unviable rather than merely
late. Read §1 before you pay Valve anything.

```powershell
make steam-check      # the mechanical half, as a report
```

## 1. Three things that can stop this release

### 1a. Cesium ion — read this one twice

**The terrain is not in the build.** Every player, every session, streams tiles
from Cesium ion against *your* token and *your* account. That is fine for a
handful of developers and it is a completely different proposition on a store
that might sell ten thousand copies.

Two separate problems come out of that:

- **Cost.** Tile streaming is metered. Your bill scales with the number of
  players and how long they play — which means a *successful* launch costs more
  than a quiet one. Nothing in the game currently caps, caches or rations it.
- **Licence.** Cesium ion's free tier is for development and evaluation.
  Redistributing streamed assets inside a commercially sold product is a
  different agreement, and the imagery layer has its own terms on top of
  Cesium's own.

Neither is a "sort it out at launch" item. Before you set a date:

1. Talk to Cesium about a commercial plan for a distributed game, and get in
   writing what is permitted. Ask specifically about a paid product on a
   storefront and about how many end users the plan covers.
2. Model the cost per player — sessions × tiles × your plan's rate — and decide
   what a bad month looks like.
3. Confirm the current imagery layer's commercial terms separately from
   Cesium's, and keep the attribution overlay on either way
   (`docs/02-CESIUM.md`).

If the numbers do not work, the alternatives are real but each is a project of
its own: self-host the tiles, pre-bake a fixed set of mission regions into the
build so the game ships its own ground, or run a small service that vends a
short-lived token per session so the credential is not sitting in a text file on
every buyer's disk.

**Do not skip this because the game currently works.** It works because you are
one user on a development tier.

### 1b. The Unity licence

`m_ShowUnitySplashScreen` is on. Removing the splash — and commercially
releasing at all past Unity's revenue and funding thresholds — depends on your
Unity licence tier, and those thresholds have moved more than once. Check what
your account is entitled to before launch, not after.

`make steam-check` reports the splash as a warning for exactly this reason: it
is not a bug, it is a licence question wearing a settings checkbox.

### 1c. Third-party assets

Eighteen third-party folders sit under `Assets/` — vehicles, aircraft, effects,
props, editor utilities. Most Unity Asset Store licences do permit inclusion in
a released game, but "most" is not a defence, and several of these are
demo/free variants whose terms differ from their paid counterparts.

`docs/37-THIRD-PARTY.md` is the register to fill in — one row per pack, with
where it came from, its licence, whether commercial release is permitted and
whether attribution is required. The Credits screen already exists as the place
for the attributions that turn out to be required.

## 2. The Cesium token, specifically

`docs/34-INSTALLER.md` §2a covers this for the installer. Steam raises the
stakes, because the audience is buyers rather than testers, and because §1a
means the token is now a *billing* credential as much as a secret.

`scripts/steam-upload.ps1` therefore **refuses to run without an explicit
`-Token Include` or `-Token Exclude`**. There is no default, because both
options are bad in different ways and neither should happen because a script
guessed:

| | What the buyer gets | What you get |
|---|---|---|
| `-Token Exclude` | A game that starts and shows no ground until they paste in their own ion token | No bill, and a store page that has to explain itself |
| `-Token Include` | A game that works out of the box | Your token in a readable text file on every buyer's disk, and every one of their tile requests billed to you |

The honest resolutions are in §1a. Whichever you choose, say it plainly on the
store page — "requires an internet connection; terrain streamed from a
third-party service" is the minimum, and if the player must supply a key, that
belongs above the fold, not in a support FAQ.

## 3. The Steamworks integration

`Assets/Scripts/Core/SteamIntegration.cs` is the game's only contact point with
Steam. It is behind the `IRONMERIDIAN_STEAM` scripting define and compiles to
no-ops without it — so the project builds, runs and ships through the installer
whether or not the SDK is present, and **no call site needs a `#if` of its
own**.

To turn it on:

1. **Install Steamworks.NET.** Either the `.unitypackage` from
   [its releases](https://github.com/rlabrecque/Steamworks.NET/releases) or the
   UPM package. It brings `steam_api64.dll`, which must end up beside the built
   `.exe`.
2. **Add the define.** Edit → Project Settings → Player → Other Settings →
   Scripting Define Symbols → `IRONMERIDIAN_STEAM`.
3. **Set the app id** in `SteamIntegration.cs` *and* `steam/app_build.vdf`.
   `make steam-check` fails if they disagree — one app, one number.
4. **Verify it compiles**: `.\scripts\compile-check.ps1 -Define IRONMERIDIAN_STEAM`.

### 3a. Testing before you have an app id

App **480** is Spacewar, Valve's public test app, and it is what
`SteamIntegration.AppId` ships as. With Steam running:

```powershell
make build
make steam-appid      # writes steam_appid.txt beside the player
```

Launch the `.exe` directly and the overlay, the persona name and
`SteamIntegration.Running` all work. Achievements do nothing, because they are
Spacewar's, not yours. `steam_appid.txt` is git-ignored and excluded from the
depot: in a shipped build it would override what Steam says the app is.

### 3b. What is wired, and what is not

Wired: relaunch-through-Steam, init, the callback pump, clean shutdown, the
persona name, and `Achieve(apiName)`.

**Not** wired: `SteamIntegration.OverlayChanged` fires but nothing listens. Steam
expects a single-player game to stop when the overlay opens — hook it to
whatever pauses a battle before you ship. That is a deliberate loose end rather
than an oversight; it needs to know about your pause semantics, and this class
should not.

Achievements are created on the partner site first; the API name you type there
is the string you pass to `Achieve`. There is no catalogue in code yet because
there are no achievements yet — when there are, they belong in one, the way
every other list in this project does.

## 3c. DLC, and why there is no payment code

The main menu has a **DLC** screen (`Extras → DLC`, currently a placeholder), so
this is worth settling before someone reaches for a payments SDK.

**Steam DLC is not an in-app purchase, and Unity IAP cannot sell it.** Unity IAP
targets Google Play, the App Store and similar; it has no Steam backend. On
Steam:

1. Each piece of DLC is **its own app id**, created on the partner site under
   the base game.
2. The player buys it on the store, like the base game. Your build never sees a
   payment, a receipt or an SDK.
3. The game asks one question at runtime:

```csharp
if (SteamIntegration.OwnsDlc(1234570))   // the DLC's app id
    // unlock the campaign
else
    SteamIntegration.OpenStorePage(1234570);   // opens in the overlay
```

Both are in `SteamIntegration` and both are safe outside Steam —
`OwnsDlc` returns false, `OpenStorePage` does nothing. Whatever the DLC screen
becomes, it needs a path that degrades when the game is not running under
Steam, not a dead button.

Content itself has a choice: ship it in the base depot and gate it behind
`OwnsDlc` (simple, but the bytes are on every disk), or give the DLC its own
depot so only owners download it. For a campaign — JSON in `StreamingAssets`,
measured in kilobytes — gating is the sane option. For anything with models or
video, use a depot.

## 4. Uploading a build

`steam/` holds two VDF templates with `{{PLACEHOLDER}}` values.
`scripts/steam-upload.ps1` resolves them into `*.local.vdf` with absolute paths
(git-ignored) and runs `steamcmd`, which you supply — put `steamcmd.exe` in
`steam/steamcmd/`.

```powershell
# see what would upload, no login, nothing sent
.\scripts\steam-upload.ps1 -Token Exclude -Preview

# upload, but do not release it
.\scripts\steam-upload.ps1 -Token Exclude -User <steam-login>

# upload and put it live on a branch
.\scripts\steam-upload.ps1 -Token Exclude -User <steam-login> -Branch beta
```

Without `-Branch` the build uploads and sits there; you set it live from the
partner site's Builds page. That is the safer order, and it is the default for
that reason.

The depot excludes `*_DoNotShip*`, `*.pdb`, `*.log`, `steam_appid.txt` and
`cesium-token.txt`. The last is re-included by `-Token Include`, which rewrites
the exclusion in the generated local copy rather than the committed template —
so the repo never records a decision to ship the token.

There is no `make` target for uploading, on purpose. Every run of it is a
decision, and decisions take flags.

## 5. Steam Cloud

No code needed — Auto-Cloud handles it, because the game already keeps
everything the player owns in one folder outside the install directory.

On the partner site, under SteamPipe → Auto-Cloud, add a root path:

| Field | Value |
|---|---|
| Root | `WinAppDataLocalLow` |
| Subdirectory | `IvanSostarko/Iron Meridian` |
| Pattern | `*` |
| Recursive | yes — `Maps/` lives under it |

That covers saved scenarios, `missions.json`, `tuning.json` and the map saves
described in `docs/05-MAP-SAVES.md`. Set a quota comfortably above a heavy
player's `Maps/` folder.

## 6. Store assets

Valve's [store](https://partner.steamgames.com/doc/store/assets/standard) and
[library](https://partner.steamgames.com/doc/store/assets/libraryassets) asset
pages are authoritative; these are the current sizes, and note they are double
the older ones many templates still use:

| Asset | Size |
|---|---|
| Main capsule | 1232 × 706 |
| Header capsule | 920 × 430 |
| Small capsule | 462 × 174 |
| Vertical capsule | 748 × 896 |
| Page background | 1438 × 810 |
| Library capsule | 600 × 900 |
| Library header | 920 × 430 |
| Library hero | 3840 × 1240 |
| Library logo | 1280 wide and/or 720 tall |
| Screenshots | 1920 × 1080 minimum, 16:9 |

The small capsule is the one that matters most and the one most often got
wrong: at 462 × 174 in a crowded list, the game's name has to be readable and
the logo's tagline will not be. The emblem that
`scripts/generate_installer_art.py` cuts out of the logo is the right mark for
the small sizes — same reason it is the app icon.

A trailer is effectively required. `docs/32-VIDEO.md` covers the video assets
the game already carries.

**Capture both with Unity Recorder** (`com.unity.recorder`, already installed —
`docs/38-PACKAGES.md` §2a). It records the Game view at a fixed resolution and
frame rate rather than whatever the editor manages that second, which is the
difference between footage and a screen grab: lock it to 1920×1080 at 60 fps,
fly the camera over Lyon, and the output is trailer material and screenshots
from the same session.

## 7. The partner-side process

Short version, because Valve's own pages are authoritative and these change:

- **Steam Direct fee**: $100 per app, non-refundable, recouped once the app
  clears $1,000 in adjusted gross revenue.
- **First-time publishers** complete identity verification and bank details,
  then wait **30 days** before the first title can release. This is the long
  pole — start it early.
- **The Coming Soon store page must be public for at least two weeks** before
  release.
- Content survey, age ratings, and a build that passes review.

Realistically four to six weeks from creating the account to being *able* to
launch, before any of §1 is resolved. Do the paperwork while you work on §1a.

## 8. Preflight

```powershell
make steam-check
```

Checks the app id in both places and that they agree, the Steamworks define and
SDK, `steamcmd`, the app icon, the Unity splash, that `GameConfig.Version` and
`bundleVersion` agree and are not a `-dev` string, that a player build exists
without debug leftovers, what the Cesium token situation is, and lists the
third-party packs awaiting a licence decision.

It fails on things that are unambiguously wrong and warns on things only you can
judge. It cannot tell you the game is ready.

## 9. Launch checklist

Mechanical — `make steam-check` covers these:

- [ ] Real app id in `SteamIntegration.cs` and `steam/app_build.vdf`
- [ ] `IRONMERIDIAN_STEAM` defined and Steamworks.NET installed
- [ ] `.\scripts\compile-check.ps1 -Define IRONMERIDIAN_STEAM` clean
- [ ] App icon applied (`make installer-art`, then `make setup`)
- [ ] Version is a release version in `GameConfig.Version`
- [ ] Clean build: `.\scripts\build-windows.ps1 -Clean`, no `*_DoNotShip`, no `.pdb`
- [ ] Overlay pauses the game (§3b)
- [ ] Auto-Cloud path configured and round-tripped on a second machine

Judgement — only you:

- [ ] Cesium ion commercial agreement in writing, cost per player modelled (§1a)
- [ ] Token decision made and reflected on the store page (§2)
- [ ] Unity licence permits commercial release at your revenue (§1b)
- [ ] `docs/37-THIRD-PARTY.md` complete, attributions in the Credits screen (§1c)
- [ ] Store assets at the sizes in §6, trailer cut
- [ ] Steam Direct paid, 30-day wait elapsed, page public 2+ weeks (§7)

## See also

`docs/02-CESIUM.md` (the token) · `docs/34-INSTALLER.md` (the non-Steam build)
· `docs/35-TASKS.md` (the task runner) · `docs/37-THIRD-PARTY.md` (asset
licences) · `docs/05-MAP-SAVES.md` (what Cloud syncs)
