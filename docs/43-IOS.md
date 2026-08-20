# iOS

Iron Meridian on an iPhone or iPad: what carried over for free, what iOS needed
of its own, and what is still missing.

**Status: the port is prepared, not shipped.** Everything below is in the
repository and compiles, and nothing here has been run on a device or through
Xcode — see §9 before you promise it to anybody.

---

## 1. Most of this was already done

The unusual thing about the iOS port is how little of it is new. Three earlier
pieces of work carry over unchanged:

| Already handled | Because |
|---|---|
| **Touch gestures** — long press = right click, pinch = zoom, twist = orbit, drag = pan | `Core/TouchInput.IsTouchPlatform` has always included `IPhonePlayer`. See [40-ANDROID.md](40-ANDROID.md) §2 |
| **The interface scaled up for a hand** | Same flag drives `UIFactory.ReferenceResolution`, which is 1280×720 on any touch platform |
| **Video recording switched off** | `CaptureSystem.CanUseExternalEncoder` already excluded iOS: no child processes, so no ffmpeg |
| **Cesium terrain** | Cesium for Unity 1.25 ships `Plugins/iOS/libCesiumForUnityNative.a`. Nothing to add |

### 1a. StreamingAssets is a real directory here

Worth stating plainly, because it is the opposite of Android and the asymmetry
is surprising: on iOS `Application.streamingAssetsPath` is `<app>/Data/Raw`, a
**real folder inside the bundle**. `System.IO` opens it, `File.Exists` answers
honestly, and `Directory.GetFiles` enumerates.

So `Core/StreamingAssetsFile` takes its plain-file path — the same one desktop
takes — and none of the Android jar-reading or the WebGL preload applies.
`StreamingAssetsFile.IsDirectory` decides this by asking whether the path is a
URL rather than by naming platforms, which is exactly why iOS needed no change
to it at all.

The shipped map index (`Maps/index.json`) is still written and still read; it
costs nothing here and it is one fewer difference between platforms.

---

## 2. Three platforms, one input layer

For the record, since this is now the fourth port:

| Platform | Pointer | Read by |
|---|---|---|
| Desktop | mouse + keyboard | `UnityEngine.Input` directly |
| Android, **iOS** | touch | `Core/TouchInput` |
| Steam Deck | gamepad | `Core/GamepadInput` |

iOS shares Android's layer completely. There is no iOS-specific input code and
there should not be one.

---

## 3. The safe area is iOS's own problem

This is the part that genuinely needed building.

Every piece of chrome in this game measures from a screen edge: the top bar spans
the top, the rail holds the left edge for its full height, the unit inspector
holds the right, the zoom cluster sits in the bottom-left corner. On a device
whose screen is not a rectangle — a notch, a Dynamic Island, a home indicator —
"the edge" and "the edge you can see" are different places. In landscape, which
is the only orientation this game has, the cut-out eats a strip of exactly the
side the rail is on.

`UI/SafeAreaCanvas.cs` answers it once rather than thirty times: a full-canvas
child inset to `Screen.safeArea`, with everything parented into it. Panels then
measure from *its* edges, which are the edges that exist, and not one of them has
to learn what a notch is.

**It reparents, and that is a deliberate trade.** Everything in this project is
built at runtime into `canvas.transform` from around a hundred call sites, and
rewriting all of them would be a large change nobody could eyeball. Instead the
fitter moves strays into the safe rect as they appear — which is also what
catches the dialogs, context menus and tooltips created long after a screen was
built. Sibling order is preserved, so `SetAsLastSibling` still means what it did.

**On a rectangular screen the component is never even added.** `Screen.safeArea`
is the whole screen on every desktop, every browser and every phone without a
cut-out, so `SafeAreaCanvas.Inset` is false and `Attach` returns without doing
anything. The reparenting only ever happens on hardware that needs it, which is
what keeps the risk of it where the benefit is.

---

## 4. Player settings

Applied by `IosBuild.ApplySettings`, in code rather than in the `.asset` files,
so a fresh clone exports the same project.

| Setting | Value | Why |
|---|---|---|
| Bundle id | `me.ivansostarko.ironmeridian` | The same one Android uses. Changing it after release makes a different app |
| Backend / architecture | IL2CPP, ARM64 | Not a choice on iOS — it is the only combination Apple accepts |
| Minimum iOS | 15.0 | Cesium's native is built against a recent SDK, and older devices have neither the memory nor the bandwidth for streamed terrain |
| Graphics | Metal only | There is nothing else on iOS any more; a GLES entry would be a fallback to something the OS removed |
| Orientation | Both landscapes | A map is read across, and every panel is laid out against a landscape canvas |
| Status bar | Hidden | The map is the whole screen |
| Signing | Automatic, **no team id** | A team id belongs to whoever is shipping, not in a repository. Xcode picks it up from the machine that opens the project |

---

## 5. Building needs a Mac — but not for all of it

**Unity does not build an app here. It builds an Xcode project.** That is true on
every host Unity runs on, not a limitation of this script: the iOS target emits
`Unity-iPhone.xcodeproj`, and turning that into a signed `.ipa` needs Xcode,
which needs macOS.

```powershell
make ios                # or: .\scripts\build-ios.ps1 -Clean
```

**Running the export on Windows is still worth doing.** It is what catches a
missing module, an IL2CPP failure or a stripping error — the expensive, slow
failures — before a Mac is involved at all. What it cannot do is compile, sign or
submit.

Then, on a Mac:

1. copy `Builds\iOS` across, keeping the folder intact;
2. open `Unity-iPhone.xcodeproj`;
3. set your Team under **Signing & Capabilities**;
4. **Product → Archive**, then **Distribute App**.

`make doctor` reports whether iOS Build Support is installed.

> `-Clean` matters more here than elsewhere: Unity **appends** to an existing
> Xcode project rather than replacing it, so a stale one can carry settings that
> are no longer in the project.

---

## 6. Info.plist, and why not to edit it in Xcode

`IosBuild.OnPostprocessBuild` writes the keys Unity leaves to you:

| Key | Value | Why |
|---|---|---|
| `ITSAppUsesNonExemptEncryption` | `false` | Answers Apple's export-compliance question **in the build**, instead of a human clicking "no" on every upload. False is the truth: the game uses HTTPS and nothing else |
| `UIRequiresFullScreen` | `true` | Split View would hand a third of the map to another app and leave the rail overlapping the ground it sits beside |
| `UIViewControllerBasedStatusBarAppearance` | `false` | So the hidden status bar stays hidden |
| `NSLocalNetworkUsageDescription` | a sentence | Why the app wants the network, in the words Apple shows the user |

**Do not hand-edit `Info.plist` in Xcode.** The project is regenerated on every
export and the edit is silently lost — the failure surfaces weeks later as a
rejected submission. Add the key to `OnPostprocessBuild` instead.

The post-process is guarded on `UNITY_IOS`, not `UNITY_EDITOR`:
`UnityEditor.iOS.Xcode` lives in the iOS module's own assembly, and a machine
without that module would fail to compile the file at all.

---

## 7. ⚠️ The ion token

Same warning as everywhere else, at the same strength as Android:
`StreamingAssets` ships inside the `.app` as plain files, so an `.ipa` carries
`cesium-token.txt` in the clear. `build-ios.ps1` warns when it packs a real one.

It is not as bad as the **web** build, where the token is one fetch from anyone
who loads the page ([41-WEB.md](41-WEB.md) §6), and not better than Android
([40-ANDROID.md](40-ANDROID.md) §6). Issue a token restricted to asset read on
the tilesets in `docs/02-CESIUM.md`, and be ready to revoke it.

---

## 8. Where the code lives

| File | Role |
|---|---|
| `UI/SafeAreaCanvas.cs` | The notch, the island and the home indicator (§3) |
| `UI/UIFactory.cs` | `CreateCanvas` attaches it; `ReferenceResolution` already handled the scale |
| `Core/TouchInput.cs` | Every gesture, shared with Android (§2) |
| `Core/StreamingAssetsFile.cs` | Takes the plain-file path here (§1a) |
| `Editor/IosBuild.cs` | Player settings, the export, the plist (§4, §6) |
| `scripts/build-ios.ps1` | The export, and what to do on the Mac (§5) |

---

## 9. Known gaps

Honest list. None of this is done.

- **Nothing has been run on a device, or through Xcode.** The export has not been
  attempted on a Mac; the first archive will find things this list does not.
- **No App Store assets.** Icons, launch screen, screenshots at every required
  size, privacy nutrition labels, an age rating — none of it exists.
- **No `NSPrivacyAccessedAPITypes` manifest.** Apple has required a privacy
  manifest for new submissions since 2024. Unity ships one for its own SDK;
  whether Cesium's native needs entries has not been checked.
- **The map editor is a desktop interface.** Seventeen rail sections and a
  right-hand inspector on a phone is not a design anybody chose. An iPad is a
  more plausible home for it than an iPhone, and neither has been tried.
- **No on-screen keyboard handling.** iOS raises its own keyboard for a focused
  `InputField`, which is better than the Steam Deck's situation
  ([42-STEAM-DECK.md](42-STEAM-DECK.md) §8) — but it covers half a landscape
  screen, and nothing scrolls the rename field above it.
- **No thermal or battery handling.** Streaming terrain on a phone is a heat
  budget, and `Application.targetFrameRate` is never lowered.
- **No tile budget for mobile.** Cesium's memory and screen-space-error settings
  are still the desktop's — the same gap Android has.
- **`make check` does not compile for iOS.** Roslyn against Unity's reference
  assemblies catches C# errors, not IL2CPP, Xcode or native-linkage ones.
