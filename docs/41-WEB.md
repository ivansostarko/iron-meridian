# Web (WebGL)

Iron Meridian in a browser: what had to change, what the build needs, and what is
still missing.

**Status: the port is prepared, not shipped.** Everything below is in the
repository and compiles, and nothing here has been run in a browser — see §9
before you promise it to anybody.

The good news first: **Cesium for Unity 1.25 ships a WebGL native**
(`Plugins/WebGL/libCesiumForUnityNative.a`, an Emscripten archive), so the
terrain is not the blocker. The blockers were elsewhere.

---

## 1. One thread changes everything

The Android port ([40-ANDROID.md](40-ANDROID.md)) already moved every read of a
shipped data file behind `Core/StreamingAssetsFile`, because on Android
`StreamingAssets` is an entry inside the APK rather than a folder. On WebGL it is
a **path on the web server** the build is hosted from — the same problem, so the
same class solves it.

What is *not* the same is how it is allowed to wait.

On Android the read spins on `UnityWebRequest.isDone`. That is safe there: Unity
services a `jar:` URL on a worker thread, so the fetch finishes without the main
loop coming back round, and it is a local decompression of a few milliseconds.

**In a browser the identical code hangs the tab, permanently.** There is one
thread. The fetch cannot progress unless the frame returns to the JavaScript
event loop, and a busy-wait never returns. Not slow — hung, with the only way out
being to close the page.

So WebGL does not read on demand at all:

| | Desktop | Android | **WebGL** |
|---|---|---|---|
| Path is | a directory | a jar URL | an http URL |
| Read | `File.ReadAllText` | fetch, spin until done | **preloaded before anything asks** |

`StreamingAssetsFile.PreloadRoutine` fetches every shipped file up front,
`ReadAllText` serves that cache, and `Ready` says whether it may be asked yet.

### 1a. What gets preloaded, and who waits

The list is `StreamingAssetsFile.CoreFiles` — the ion token, `units.json`,
`unit-names.json`, `missions.json` and the map index — plus every scenario the
index names, added in a second pass once the index itself has arrived.

**The list lives next to the reader**, not collected from the five classes that
read those files. A registry they had to add themselves to at startup would put
the whole thing at the mercy of static-constructor order, and the file that was
missed would be one that works everywhere except in a browser.

> **Adding a shipped data file?** Add it to `CoreFiles`. On desktop and Android
> it will work either way, which is exactly what makes forgetting easy.

Two things make the wait invisible:

1. It **starts before the first scene** — a `[RuntimeInitializeOnLoadMethod]` in
   `StreamingAssetsFile`. The main menu does not need shipped data, so the fetch
   runs while the player is looking at a screen that is doing something else.
2. **`SceneLoader` waits on it.** Every screen that *does* read shipped data is
   entered through it, and it already has a loading overlay with a bar and a
   timeout. It shows "Loading the catalogue…" and goes in anyway if the wait runs
   long — an overlay that never lifts is worse than a screen missing its unit
   list (golden rule 7), and the reader logs loudly enough to say which happened.

A read that arrives before the preload is finished is reported as an **error**,
not answered with null. On WebGL "not preloaded" means a bug in `CoreFiles` or an
ordering mistake, and silently returning null would look exactly like a missing
file.

---

## 2. Saves live in IndexedDB, and are lost without a flush

`Application.persistentDataPath` in a browser is an in-memory filesystem (IDBFS)
that is only written through to IndexedDB when something asks. Unity asks on
quit — and **closing a tab is not quitting**, so without an explicit flush a
scenario the player saved is gone the moment they navigate away.

`Core/WebStorage.Flush()` is that ask, through
`Assets/Plugins/WebGL/IronMeridianWeb.jslib`. Every write path calls it:

| Write | File |
|---|---|
| Saving a scenario | `Save/SaveSystem.cs` |
| Saving the mission book | `Save/MissionLibrary.cs` |
| Saving unit tuning | `Save/TuningStore.cs` |

It compiles to an empty method everywhere else, so the call costs nothing on
desktop and there is no `#if` at any call site.

> **Adding a write under `persistentDataPath`?** Call `WebStorage.Flush()` after
> it. There is no way to notice you forgot except in a browser, after a reload.

---

## 3. Right-click belongs to the browser until you take it

Right-click is the move order, the context menu and the cancel on every armed
tool in this game. In a browser it is also how the *browser's* menu opens — over
the canvas, swallowing the gesture. Unity's own templates do not suppress it.

`IronMeridian_SuppressContextMenu` in the `.jslib` binds a `contextmenu`
preventer to the game canvas, once, before the first scene. Bound to the canvas
rather than the document, so right-clicking the page *around* the game still
behaves like a web page.

---

## 4. What is switched off on the web

| Feature | Why |
|---|---|
| **Video recording** (`docs/39-CAPTURE.md`) | It shells out to ffmpeg and writes from a background thread. A browser build has neither processes nor threads, and nowhere to put an mp4. `CaptureSystem.CanRecord` is false, so the button says so |
| **Screenshots** | The file is written into the virtual filesystem rather than anywhere the player can find. It does not crash; it is close to useless. See §9 |
| **Steam** (`docs/36-STEAM.md`) | Behind the `IRONMERIDIAN_STEAM` define and never set for WebGL |

---

## 5. Building and serving

### 5a. What the machine needs

Unity Hub → Installs → the 6000.0 editor → **Add modules → WebGL Build Support**.
`make doctor` reports whether it is there.

### 5b. The jobs

| Command | What it does |
|---|---|
| `make web` | Build into `Builds\Web` |
| `make web-serve` | Build, then serve it on `localhost:8080` and open a browser |
| `make serve` | Serve whatever is already in `Builds\Web` |

**A WebGL build cannot be opened with `file://`.** The browser refuses to fetch
the `.wasm` and the StreamingAssets data across that origin, and the failure
looks like a corrupt build rather than a wrong URL. It has to be served over
http — which is what `serve` is for.

### 5c. The local server is not `python -m http.server`

`scripts/serve-web.py` is `SimpleHTTPRequestHandler` plus the headers a Unity
build actually needs. Plain `http.server` gets one thing wrong that matters: a
Brotli build ships `*.br` files, and without a `Content-Encoding: br` header the
browser hands the raw Brotli stream to the WASM loader, which fails with a
message about an invalid magic number and nothing about compression at all.

It also serves `.wasm` as `application/wasm` (which older Pythons do not know)
and sets `Cache-Control: no-store`, because the whole point of running it is to
look at a build you have just replaced.

### 5d. Compression, and the host

The build is **Brotli** by default. It is large — a Cesium native, a full IL2CPP
runtime and the game — and it is fetched over a network before anything happens,
so transfer size *is* the loading screen.

The cost is that the server must send `Content-Encoding: br` for `*.br`. On a
host you cannot configure that way (some static hosts, some CDNs):

```powershell
.\scripts\build-web.ps1 -Uncompressed
```

Bigger download, and it works, which a build the browser cannot decompress does
not.

---

## 6. ⚠️ The ion token is public in a web build

This is the one that matters most, and it is worse here than anywhere else.

StreamingAssets in a WebGL build is **served as plain files**. `cesium-token.txt`
is one fetch away from anyone who loads the page — no unpacking, no tooling, just
`view-source` and a URL. The Windows installer strips the token by default
(`docs/34-INSTALLER.md` §2a) precisely to avoid this; the web build has no such
strip and cannot have a useful one, because the game needs the token to run and
there is nowhere in a static page to hide it.

`build-web.ps1` prints a warning when it packs a real token. Treat a public build
as a **published token**:

- issue one restricted to *asset read* on the tilesets in `docs/02-CESIUM.md`,
  and nothing else;
- expect it to be used by people who are not playing your game, and watch the
  bill (`docs/02-CESIUM.md` § Selling the game);
- be ready to revoke it.

The real answer is a token endpoint — a small server that mints a short-lived
token per session — and that does not exist. §9.

---

## 7. Player settings

Applied by `WebBuild.ApplySettings`, in code rather than in the `.asset` files,
so a fresh clone builds the same thing.

| Setting | Value | Why |
|---|---|---|
| Compression | Brotli | The download is the loading screen (§5d) |
| IL2CPP config | Master | Size over speed: this game is bound by how fast tiles arrive, not by CPU |
| Stripping | High | Same argument |
| Exceptions | Explicitly thrown only | Full support costs size and speed; a development build turns it back up |
| Graphics | WebGL 2 (GLES3) | What Cesium's shaders need, and every browser that can run a build this size has had it for years |
| Heap | 512 MB | Cesium budgets tile memory itself; a tab that asks for a gigabyte up front fails to start on machines that would have run it |
| Data caching | On | IndexedDB keeps the build between visits, so the second load is not the first one again |

---

## 8. Where the code lives

| File | Role |
|---|---|
| `Core/StreamingAssetsFile.cs` | The preload, the cache, and `Ready` (§1) |
| `Core/SceneLoader.cs` | Waits for `Ready` behind the loading overlay (§1a) |
| `Core/WebStorage.cs` | `Flush()` and the context-menu suppressor (§2, §3) |
| `Plugins/WebGL/IronMeridianWeb.jslib` | The JavaScript both of those call |
| `Editor/WebBuild.cs` | Player settings and the batch build (§7) |
| `scripts/build-web.ps1` | Build, serve, and the token warning |
| `scripts/serve-web.py` | The local server with the right headers (§5c) |

---

## 9. Known gaps

Honest list. None of this is done.

- **Nothing has been run in a browser.** The port compiles and the pieces are
  wired; the first real build will find things this list does not.
- **No token endpoint.** §6. Every public build publishes your ion token, and the
  only real fix is server-side.
- **Screenshots go nowhere useful.** `CaptureSystem` writes into the virtual
  filesystem. Making the button actually hand the player a PNG means a
  `Blob` + download link through the `.jslib`.
- **The build size is unmeasured.** A Cesium native plus IL2CPP is not small, and
  nobody has looked at what it comes to over the wire or how long the first load
  takes.
- **No tile budget for the web.** Cesium's memory and screen-space-error settings
  are the desktop's, over a connection that is not.
- **`make check` does not compile for WebGL.** Roslyn against Unity's reference
  assemblies catches C# errors, not IL2CPP, Emscripten or `.jslib` linkage ones —
  a typo in a `DllImport` name is a runtime failure in the browser console.
- **The map editor is a desktop interface**, and a browser tab is usually a
  desktop — so this is less bad here than on a phone. But keyboard shortcuts
  compete with the browser's (`Ctrl+W`, `F5`, `Ctrl+Z` in a text field), and
  none of that has been checked.
- **No fullscreen or pointer-lock handling.** The camera's middle-mouse orbit and
  the browser's own drag behaviour have not been reconciled.
