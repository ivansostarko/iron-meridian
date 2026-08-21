# Air Supply

The register of every air-dropped load, the aircraft that flies it and what it
leaves on the ground. This is the human-readable version of
`Assets/Scripts/Vfx/AirSupplyCatalog.cs` — **keep it in step with that file in
the same change.**

Strike dock → **AIR SUPPLY** (the parachute, next to AIR STRIKE).

---

## 1. The one mission that gives

Every other entry in the fire menus exists to take something away — a battery, a
bomber, a drone, a missile, a naval gun. This one arrives on the same machinery,
through the same countdown, and **leaves a supply point standing on the map**.

That is the whole design. A drop is not an effect that plays: every bundle that
touches down becomes a real `LogisticsSystem` site of the matching kind, with the
icon, the DEPLOYED row, the save entry and the right-click **REMOVE SITE** that
any hand-placed one has (docs/26-LOGISTICS.md). A drop that produced a special
marker only this system understood would be a second logistics system with one
member.

It sits next to AIR STRIKE in the dock deliberately: the two are flown by the
same kind of thing and tasked in exactly the same way, and the pairing is the
clearest statement that an aircraft overhead is not always bad news.

---

## 2. The register

| Kind | Button | Drop zone | Bundles | Missions | Leaves |
|---|---|---|---|---|---|
| `Ammo` | **AMMO SUPPLY** — rounds of every nature | 420 m | 5 | 4 | `AmmoPoint` — *AIRDROP · AMMO* |
| `Oil` | **OIL SUPPLY** — fuel and lubricants | 420 m | 4 | 3 | `FuelPoint` — *AIRDROP · FUEL* |
| `Medic` | **MEDIC SUPPLY** — casualty treatment stores | 360 m | 3 | 3 | `MedicalPoint` — *AIRDROP · MEDICAL* |

Loads carry the **same glyphs as the LOGISTICS panel's** ammunition, fuel and
medical points, because that is precisely what they become. Missions are counted
per load by `StrikeBudget`, so a spent ammunition drop leaves the medical one
flyable.

---

## 3. The sequence

```
pick a load          →  the drop zone marker follows the cursor
click the map        →  zone placed, allowance spent, 10 s countdown in the HUD
countdown expires    →  transport runs in low along a random heading
over the zone        →  bundles pushed out, 0.55 s apart
                     →  free fall, canopy opens, descent at 55 m/s with drift
each canopy lands    →  dust puff + a supply point appears
run ends             →  "Air supply delivered — n bundles on the ground"
```

Countdown: **10 seconds**, per the brief. Run-in altitude **700 m** — far lower
than a bomber's, because the canopies have to be watchable all the way down; from
3 km they would be two pixels for twenty seconds.

The release is biased **early** by a third of the fall time: a bundle pushed out
overhead lands well down-track, so releasing overhead would put the whole load
beyond the zone the player drew.

---

## 4. What flies, and what falls

Both models are **built in code** (`ProceduralModels`), for the same reason the
drones are: a supply run must not be able to lose its aeroplane to an asset pack
somebody removed. Registered in `UnitModelLibrary` like everything else — golden
rule 10, never a `Resources.Load` at a call site.

**Transport** (`transport_aircraft`, ~40 m span, scaled to 420 m on the map):
high wing, four turboprops, T-tail, upswept rear with a ramp. Every other
airframe in this game is something arriving to kill; a supply drop has to read as
the opposite from the first frame, and at map zoom the only thing the player can
see is the outline. Idle clip: four propellers turning, and a very slight roll —
a loaded airlifter on a drop run holds the steadiest line it can.

**Bundle** (`supply_bundle`, 90 m tall on the map): a palletised crate, four
rigging lines, and an open canopy tinted to the load. A **cone, not a dome** — at
this zoom both are twelve pixels, but the cone's silhouette has a point, which is
what makes it read as a parachute rather than a ball. Idle clip: a pendulum swing
on two out-of-phase periods, because a crate descending in a dead straight line
reads as a lift rather than a drop.

`ParachuteDrop` runs three phases: **free fall** (so the canopy has something to
open from), **deployment** (the canopy blooms over 0.45 s), then **descent** at a
constant rate with the down-track drift the release imparted. On touchdown the
canopy collapses onto the load rather than standing inflated in a field.

**Why the load is modelled at all**, when a bomb is not: a weapon at map zoom is
invisible and the burst is the event, whereas a canopy is deliberately large,
slow and white — watching it come down *is* the event.

---

## 4a. What a landed bundle leaves

Each bundle becomes a real `LogisticsSystem` site of the matching kind — same
list, same save entry, same right-click REMOVE as a hand-placed one. What is
different is that it is a **cache**, and three things follow from that.

**It carries what the sortie carried.** `issuesPerBundle` — 1.5 for ammunition
and fuel, 2 for medical — so an ammunition sortie puts 7.5 issues on the ground
across its five bundles. An airdrop is what gets a cut-off battalion through the
next few hours, not a rear area: **running out is the point of it.** A drop that
produced an inexhaustible supply point would make the fourth sortie meaningless
and the first one a cheat. See docs/26-LOGISTICS.md §2a for what an issue is.

**It is drawn as a 3D model**, `supply_bundle`, standing on the ground —
**always**, where a placed installation shows its buildings only when
GENERAL → SHOW UNIT 3D MODELS is on (docs/26-LOGISTICS.md §4b). They are different sorts of
object and the map should say so: a depot is a *place*, and what matters about it
is which one it is and how far it reaches, which is exactly what a symbol says
and a crate cannot. A cache is *a thing somebody just put there* — the player
watched it come down under a canopy, and what they want afterwards is to find it
again where it landed. The symbol does not disappear; it shrinks and rides above
the model, so a cache is still identifiable as ammunition or fuel from a distance
at which the model is a dot.

**It burns marker smoke** for three minutes — `VfxId.SupplyCacheSmoke`, pale
green, the one column of smoke on this map that is not something burning. What a
real DZ party puts out, and here it solves a real problem: the landing dust is
over in a second, and a bundle down behind a ridge is otherwise a cache you know
you have and cannot see.

**An emptied cache removes itself** and says so. See docs/26-LOGISTICS.md §2a.

## 4b. The drop zone is not a beaten zone

Every other mission on the strike dock is aimed at something, and its marker says
so: a bright volume standing on the ground with the alarm rising as the rounds
come in. A supply drop is the one that is not a threat, and borrowing the
artillery's marker for it made picking a DZ look exactly like calling fire on
your own position — which, on a control sitting beside five things that really do
call fire, is the one mistake the interface must not invite.

So `AirSupplySystem` overrides `CalledStrikeSystem.StyleMarker` and the zone is
marked the way a DZ is marked: **the volume knocked back to 22 %**, and a
**reticle painted flat on the ground** inside it, in the load's own colour. The
radius is unchanged — it is the ground the bundles will scatter across, which is
a fact about the sortie rather than a style.

---

## 5. Effects and sound

| | |
|---|---|
| `VfxId.SupplyLandingDust` | A bundle touching down. Half the generic `Dust`'s size and life — gentle is the point. docs/08-PARTICLE-SYSTEMS.md |
| `EffectSound.JetPass` | **Reused**, not new: it is an aircraft passing low overhead, which is what the cue has to say. A dedicated turboprop drone would be more correct and is a known gap. docs/10-AUDIO.md |

---

## 6. Failure

Every stage degrades rather than losing the mission — the same rule the air
strike follows:

- **No transport model** → the load still arrives, on the same schedule, spread
  over the same zone (`FallbackDrop`).
- **No bundle model** → that bundle is delivered after the time it would have
  taken to fall.
- **No logistics system** → the drop flies and lands and leaves nothing, which is
  the right failure for a decoration to have.
- **Terrain not streamed** → the click is refused with a message and the load
  stays armed.

---

## 7. Where the code lives

| File | Role |
|---|---|
| `Vfx/AirSupplyCatalog.cs` | **The register** — three loads and the flight numbers |
| `Vfx/AirSupplySystem.cs` | The mission: `CalledStrikeSystem<SupplyKind>`, and the supply point each bundle leaves |
| `Vfx/SupplyRun.cs` | The transport's pass and its release schedule |
| `Vfx/ParachuteDrop.cs` | One bundle: free fall, deployment, descent, landing |
| `Models/ProceduralModels.cs` | The transport and the bundle, with their idle clips |
| `Models/UnitModelLibrary.cs` | Their entries — docs/09-3D-MODELS.md |
| `UI/UnitPaletteUI.cs` | `BuildAirSupplySection` — the dock page, generated from the catalogue |
| `UI/StrikeDockUI.cs` | The parachute button beside AIR STRIKE |
| `Core/GameController.cs` | Wiring, the countdown banner slot, the side a drop belongs to |

---

## 8. Adding a load

1. A value on `SupplyKind` and **a row in `AirSupplyCatalog.All`** — zone radius,
   bundles, missions, colour, and the `LogisticsKind` it leaves.
2. A glyph case in `UnitPaletteUI.SupplyGlyph`.
3. **Update the table in §2 of this file.**

The panel's buttons, the marker, the countdown and the sites it leaves are all
driven from the catalogue.

---

## 9. Known gaps

- **The supplies are not consumed by anything.** A dropped point is a point on
  the map; nothing draws stock from it, because nothing draws stock from a
  hand-placed one either — see docs/27-SUSTAINMENT.md §7, which is where that
  connection belongs.
- **No turboprop sound** — see §5.
- **The drop always belongs to the palette's current team tab.** There is no
  notion of the enemy flying his own resupply.
