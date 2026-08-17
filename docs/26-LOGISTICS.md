# Logistics

The register of every kind of logistic installation a scenario can be given,
and where they are laid out. This is the human-readable version of
`Assets/Scripts/Data/LogisticsCatalog.cs` — **keep it in step with that file in
the same change.**

---

> **Choosing a side.** The panel carries its own **FRIENDLY / ENEMY** selector at
> the top. It used only to report the side, which was chosen on the UNITS tab —
> so working on the enemy's logistic installations meant leaving this panel to switch, coming
> back, and remembering to switch again afterwards. It is the same side every
> other panel uses, and all of their tabs repaint together; there is one selected
> side in the editor, not one per panel.


## 1. What a logistic site is

A **place on the map that supports the force**: a depot, a supply point, or one
of the four function-specific points. It belongs to a side, it sits on the
terrain, and it is saved with the map.

It is deliberately **neither a unit nor a task marker**:

| | Unit | Task marker | Logistic site |
|---|---|---|---|
| Fights, moves, dies | yes | — | no |
| Belongs to | itself | the formation ordered | the scenario |
| Swept off the map when its owner goes | — | yes | never |
| Comes from | `units.json` | an order | the LOGISTICS panel |

That last row is why it has its own system rather than borrowing
`MarkerManager`: a task marker is removed the moment the formation that was
given the order leaves the map, and an ammunition point that vanished because
the battalion nearest it was destroyed would be exactly wrong.

---

## 2. The register

Six kinds, from the rear forward. The **service radius** is the ground the
installation covers. It is stated on the kind's button and again in the
confirmation when a site is deployed; it is **not drawn on the map yet** — see
§4.

| Kind | Name | What it is for | Service radius | Glyph |
|---|---|---|---|---|
| `SupplyDepot` | SUPPLY DEPOT | Strategic supply location | 25 km | Warehouse — pitched roof over a shed |
| `SupplyPoint` | SUPPLY POINT | Forward supply location | 12 km | Two stacked crates |
| `FuelPoint` | FUEL POINT | Refuel vehicles | 10 km | Droplet |
| `AmmoPoint` | AMMO POINT | Replenish ammunition | 10 km | Two rounds |
| `RepairPoint` | REPAIR POINT | Recover and repair vehicles | 8 km | Crossed tools |
| `MedicalPoint` | MEDICAL POINT | Treat and evacuate casualties | 8 km | Cross |

**The radii are service ranges, not blast radii.** They say how far the
installation's ground extends, which is what makes a laydown judgeable: a depot
covering the whole sector is in the wrong place if it is inside the enemy's
reach, and a fuel point that reaches none of the armour is a fuel point in the
wrong valley.

**Six silhouettes, not six letters in a box.** NATO's own logistic symbology is
a rectangle with a letter in it, which is unreadable at 20 px on a rail button
and at whatever the camera makes of it on the map. Each kind is a different
*shape* instead — the one property that survives being small.

---

## 3. Laying them out

**Left rail → LOGISTICS.** Six buttons, read straight off the catalogue.

1. Click a kind. It lights, and a **ghost of that kind's own symbol** follows
   the ground under the cursor — with six kinds on one panel, "what am I about
   to drop" is the question the preview has to answer, so it is the symbol
   rather than a generic reticle.
2. Click the terrain. The site is deployed there.
3. The tool **stays armed**, because a rear area is laid out several sites at a
   time. Right-click, `Esc` or **STOP DEPLOYING** puts it away.

Ground that has not streamed in yet is **refused with a message** rather than
guessed at — the same rule the effect tool and every strike follow.

**The team tab decides the side.** A scenario has two rear areas and the
designer lays out both, so the panel follows the team already chosen in UNITS
rather than carrying a second side control. Whichever side you are deploying
formations for is the side you are deploying its supply for — and the panel says
which that is, in that side's colour, beside the DEPLOY ON MAP heading. A deploy
button whose side is decided on another page is a button you press to find out.

**DEPLOYED** below the buttons lists every site on the map with its coordinates,
with **◎** to fly to one and **✕** to remove it; **REMOVE ALL SITES** clears the
lot. The count line reads `DEPLOYED — n FRIENDLY · n ENEMY`.

**Or drop one from the air.** The strike dock's **AIR SUPPLY** menu tasks a
transport to parachute ammunition, fuel or medical stores onto a zone, and every
bundle that lands becomes one of these sites — captioned `AIRDROP · AMMO` and so
on, but otherwise identical to a hand-placed one. See docs/29-AIR-SUPPLY.md.

**Or remove one on the map**: right-click the site's marker and pick **REMOVE
SITE**. The panel is the right place when you are working through a laydown; the
map is the right place when you are looking at the thing you want gone. Sites are
picked in screen space against the marker you can see — see docs/03-GAMEPLAY.md
§ *The right-click menu*.

---

## 4. On the map

| Part | What it says |
|---|---|
| Ground ring | The owning **side** — blue or red — lying flat on the terrain |
| Symbol | The **function**, standing up to face the camera, in the kind's own tint |
| Caption | The kind's name, or the site's own label if it has one |

**The glyph billboards and the ring lies flat**, deliberately. A laydown is read
two ways: from overhead, where what matters is *where* the sites are relative to
the units they serve, and from a working camera angle, where what matters is
*which* site is which. A flat ring answers the first at any tilt and a
billboarded symbol answers the second, so the marker keeps both.

Markers are sized like task markers (constant apparent size, clamped), so a rear
area reads as part of the same map as the formations it supports rather than as
a separate layer of furniture. Everything is clamped to the terrain and
re-clamped until the ground under it has actually streamed in.

**Known gap — the service radius is a number, not a ring.** Drawing it properly
means a terrain-draped band per site, which is what `RangeRing` does at ~200
terrain raycasts each; a dozen depots would pay that on every georeference
shift. A flat disc at the site's own altitude would be cheap and wrong — over a
25 km radius it would sink into every hill and float over every valley, which is
worse than not drawing it. Until that is worth doing, the figure is stated
rather than shown.

---

## 5. Saving

Sites are written to the map file as `logistics`, one `LogisticsSiteData` each:

```json
"logistics": [
  { "id": "log-3f9a21c4", "kind": "FuelPoint", "team": "User",
    "label": "", "latitude": 45.75, "longitude": 4.85, "heightMeters": 214.0 }
]
```

Empty on a map saved before logistics existed, which reads correctly as "this
scenario has no rear area". `JsonUtility` leaves missing fields at their
initialiser values, so old maps load without a migration step.

---

## 6. Where the code lives

| File | Role |
|---|---|
| `Assets/Scripts/Data/LogisticsCatalog.cs` | **The register** — the six kinds in numbers |
| `Assets/Scripts/Data/MapSaveData.cs` | `LogisticsSiteData` and the `logistics` list |
| `Assets/Scripts/Logistics/LogisticsSystem.cs` | Owns the sites, the arm-then-click tool, save/load |
| `Assets/Scripts/Logistics/LogisticsSite.cs` | The map graphic: ring, symbol, caption |
| `Assets/Scripts/UI/UiIcons.cs` | The six glyphs, and `GlyphFor(LogisticsKind)` — one mapping read by the button, the ghost and the marker |
| `Assets/Scripts/UI/UnitPaletteUI.cs` | The LOGISTICS section, generated from the catalogue |
| `Assets/Scripts/Core/GameController.cs` | Wiring, the DEPLOYED list's actions, save/load |

---

## 7. Adding a kind

1. A value on `LogisticsKind` and **a row in `LogisticsCatalog.All`** — name,
   one-line detail, service radius, tint.
2. A glyph in `UiIcons` and a case in `UiIcons.GlyphFor`.
3. **Update the table in §2 of this file.**

Nothing else. The panel's buttons, the placement ghost, the map marker and the
save file are all driven from the catalogue, so a seventh kind appears in all
four without any of them being touched.
