# Sustainment

The register of every stock a force is tracked on, and the rules that turn an
order of battle into a daily burn rate. This is the human-readable version of
`Assets/Scripts/Data/ResourceCatalog.cs` — **keep it in step with that file in
the same change.**

Left rail → **SUSTAINMENT**.

---

> **Choosing a side.** The panel carries its own **FRIENDLY / ENEMY** selector at
> the top. It used only to report the side, which was chosen on the UNITS tab —
> so working on the enemy's stocks meant leaving this panel to switch, coming
> back, and remembering to switch again afterwards. It is the same side every
> other panel uses, and all of their tabs repaint together; there is one selected
> side in the editor, not one per panel.


## 1. Why it is called that

*Resources* is what a strategy game calls the numbers in the corner of the
screen. *Sustainment* is what an army calls keeping a force in the field, and it
is the right word for a page about fuel, ammunition natures, replacements and
rations. It also keeps the two logistic sections distinct at a glance:

| Section | Question it answers |
|---|---|
| **LOGISTICS** (docs/26) | *Where* the supply is — depots and points on the map |
| **SUSTAINMENT** (this) | *How much of it there is*, and how long it lasts |

---

## 2. The register

Nine stocks, ordered by how quickly running out of one stops the fight.

| Kind | Name | Counted in | Consumed by |
|---|---|---|---|
| `Fuel` | FUEL | litres | Vehicles by the kilometre; everyone else at a flat rate per head |
| `LightAmmo` | LIGHT AMMUNITION | rounds | Every formation that is not armour, artillery or air defence |
| `TankAmmo` | TANK AMMUNITION | rounds | Armour |
| `ArtilleryAmmo` | ARTILLERY AMMUNITION | rounds | Anything with `canIndirectFire` — guns, mortars, rockets |
| `AirDefenceMissiles` | AIR DEFENCE MISSILES | missiles | The anti-aircraft branch |
| `Manpower` | MANPOWER | personnel | Replacements for casualties, per thousand on the field |
| `Rations` | RATIONS | man-days | One per person per day, by definition |
| `MedicalSupplies` | MEDICAL SUPPLIES | units | Per thousand personnel per day |
| `SpareParts` | SPARE PARTS | units | Anything with an engine |

**Which ammunition a formation eats is derived from what it is**, not from a
field somebody has to remember to set: indirect fire is artillery whatever the
calibre, the anti-aircraft branch is on missiles, armour fires tank natures, and
everything else is light. `ResourceCatalog.AmmoClassOf`.

---

## 3. Stocks are typed; burn rates are not

**This is the rule the whole page turns on.**

- **Stocks** are a designer's decision. They are edited in the panel, saved with
  the map as `resources`, and nothing changes them by itself.
- **Consumption** is arithmetic over the units actually on the map. Nobody can
  type it, so a scenario can never state a burn rate that disagrees with its own
  order of battle. Deploy a tank brigade and the fuel line moves; lose half of
  it and the line halves.

Every rate is read off the `UnitDefinition` sustainment fields the unit
catalogue already carries — `fuelUsePerKm`, `speedKmh`, `ammoStock`, `manpower`
— scaled by **echelon** (`EchelonInfo.ManpowerMultiplier`) and by **current
strength**.

The planning constants, all in `ResourceCatalog`:

| Constant | Value | Why |
|---|---|---|
| `MoveHoursPerDay` | 6 h | A day of operations is a few hours of movement, some fighting and a lot of waiting. Fuel figures built on a formation driving round the clock are wrong by an order of magnitude |
| `AmmoLoadsPerDay` | 0.35 | A formation carries roughly a day's *fighting*; a day of operations is not a day of fighting |
| `FuelPerPersonPerDay` | 1.5 l | Generators, cookers, command posts — what a foot formation still burns |
| `ReplacementsPerThousandPerDay` | 8 | A planning figure, not a measurement: manpower is the one line the force does not choose, because it is what the enemy is doing to it |
| `MedicalPerThousandPerDay` | 12 | |
| `PartsPerCompanyPerDay` | 2.5 | |

**These are a model, not a claim about a real army.** They are chosen to be
legible and to move in the right direction.

---

## 4. The panel

Per side — it follows the team tab in UNITS, and says which side it is showing
in that side's colour.

| Block | What it shows |
|---|---|
| **FORCE ON THE MAP** | Manpower **on field**, the formation count, the establishment figure, and the percentage between them |
| **CONSUMPTION PER** | DAY / WEEK / MONTH — switches every burn figure below. Tabs rather than three columns, because at 250 px three numbers side by side are three unreadable numbers instead of one legible one |
| **STOCKS** | One row per resource: name, an editable figure, and under it `<measure> · <burn> per <period>` and the days of supply left |
| The verdict line | *Sustained for N days — X runs out first.* Green over a week, amber under, red under two days |
| **STOCK 7 DAYS FROM FORCE** | Fills every stock with a week of this side's current burn |

**Manpower on field is counted, not stocked.** The *stock* is the pool of
replacements; the number that matters day to day is how many people are standing
on the map — each formation's establishment at its echelon, scaled by how much
of it is left.

**The verdict line is the point of the page.** A force is sustained for as long
as its *shortest* stock lasts, and nine figures without that sentence is nine
figures.

**A malformed number leaves the stock alone** rather than zeroing it: half-typed
input is not an instruction to empty a depot. Same rule as the mission fields.

---

## 5. Saving

Only stocks are written, as `resources` on the map file:

```json
"resources": [
  { "team": "User", "kind": "Fuel", "quantity": 84000.0 },
  { "team": "User", "kind": "ArtilleryAmmo", "quantity": 12400.0 }
]
```

Zero stocks are not written — zero is the default, and a file listing eighteen
zeroes says nothing. Empty on an older map, which reads as a force with nothing
behind it; **STOCK 7 DAYS FROM FORCE** fills it in one click.

---

## 6. Where the code lives

| File | Role |
|---|---|
| `Assets/Scripts/Data/ResourceCatalog.cs` | **The register** — the nine stocks and the planning constants |
| `Assets/Scripts/Data/MapSaveData.cs` | `ResourceStockData` and the `resources` list |
| `Assets/Scripts/Logistics/SustainmentSystem.cs` | Stocks, derived consumption, days of supply, the head count |
| `Assets/Scripts/UI/UnitPaletteUI.cs` | `BuildSustainmentSection` — the panel, generated from the catalogue |
| `Assets/Scripts/Core/GameController.cs` | Wiring, STOCK FROM FORCE, save/load |

---

## 7. Known gaps

- **Nothing is spent yet.** The page describes what the force *would* consume;
  the combat and movement systems do not draw it down, and running out has no
  effect. Wiring consumption to the clock is the obvious next step, and is
  deliberately not in this change — a stock that empties needs a resupply model
  (docs/26 § the depots) behind it or a scenario becomes unplayable at day two.
- **The logistic sites and the stocks do not know about each other.** A depot on
  the map does not hold any of these figures; when they are connected, a site's
  service radius (docs/26 §2) is the natural thing to charge against.
- **No per-formation readout.** The figures are per side; "which battalion is
  drinking the fuel" is not answerable from this page.

---

## 8. Adding a stock

1. A value on `ResourceKind` and **a row in `ResourceCatalog.All`** — name,
   measure, one-line detail, tint.
2. A case in `SustainmentSystem.DailyUse` saying what consumes it.
3. **Update the table in §2 of this file.**

The panel's rows, the save file and the verdict line are all driven from the
catalogue, so a tenth stock appears in all three without them being touched.
