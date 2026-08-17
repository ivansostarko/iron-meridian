# The FLOT

The forward line of own troops, as a **gameplay object**: per-side forward
edges, segments with states, breach events, territory queries, modes and
history. This is the human-readable version of
`Assets/Scripts/Lines/FrontlineSystem.cs` and
`Assets/Scripts/Units/FlotEligibility.cs` — **keep it in step with them in the
same change.**

The rule everything below serves:

> **The FLOT represents effective control by combat formations, not the
> physical position of the most advanced unit.**

---

## 1. The pipeline

```
units
  → eligibility        FlotEligibility — who gets a vote
  → clustering         mutually supporting groups, per side (12 km link)
  → outlier filtering  lone probes dropped; cut-off groups stop voting
  → one engagement     each side's remaining clusters merged into one body;
                       the two bodies' centres ARE the operational direction
  → forward edges      per band, one edge per side (influence field)
  → smoothing          Chaikin + stability damping
  → terrain            MapLine drapes and grounds every flat kind
  → segments           states, breach detection, territory, history
```

Not "connect all friendly units" — each stage exists to throw something out.

**Exactly two lines are ever published: ours (`flot-user`) and theirs
(`flot-enemy`)**, or the drawn trace (`flot-manual`) in MANUAL mode. The solver
used to pair every friendly cluster with the enemy cluster it faced and publish
an edge per battle, plus a closed ring round every pocket — so a dispersed order
of battle put four or five identically-coloured traces on the map and left the
player working out which of them was the front. The FLOT answers one question, so
it is one line per side.

Nothing downstream lost anything to the merge: territory, breach detection,
manning and history all read the engagement's **nodes**, not the published
geometry. A scenario saved before this carries the old `flot-user-0`,
`flot-pocket-…` ids in its line list; the first solve sweeps them off the map.

## 2. Eligibility

Only **frontline-capable, combat-effective** formations move the line
(`FlotEligibility.Weight`):

| Gets a vote | Weight | No vote |
|---|---|---|
| Infantry | ×1.0 | Artillery, air defence — they shape the front from behind it |
| Mechanised | ×1.1 | Logistics, support (`isSupport`) — they live behind it |
| Armour | ×1.25 | Air, drones, naval — over it, not on it |

Combat-effective means: alive, **strength ≥ 25%** (the rout threshold), and not
`Routed`/`Destroyed`. A shattered battalion's position is not a front.

Derived from what a unit *is* (category, branch, support flag) rather than a
per-unit field in units.json — one mapping in one place, nothing to keep in
step.

## 3. Outliers and cut-off groups

Units are clustered by mutual support (transitively within 12 km). A cluster
more than 30 km from its side's main body is on its own:

- **Under 5% of the side's power** → an outlier. Dropped from the solve entirely
  — a recon car deep in enemy ground must not drag the whole front with it.
- **Real combat power** → **isolated**. It stays in the order of battle but is
  left out of the merged body, so a cut-off battalion does not pull its side's
  front back across the map to reach it.

A side that is *only* isolated clusters still gets a line, built from them: being
surrounded is a shape of front rather than the absence of one, and the
alternative is a side with no front at all.

## 4. Two edges, one engagement, direction

Each side's surviving clusters are merged into a single body — the union of their
units, at a power-weighted centre. The engagement between the two bodies solves
**both sides' forward edges** across banded laterals with the same influence
field as before (power × Gaussian along the front × exponential toward the
enemy), one edge per side. The ground between the edges is **contested**.

The axis comes from the two bodies' centres. A front that curves comes out curved
because the influence field is solved per band, not because the map is carrying
several axes at once.

## 5. Stability

- Recompute every `GameConfig.FrontlineUpdateSeconds` (3 s), and on registry
  changes.
- Mean movement **under 50 m** → the previous geometry is kept (no shaking).
- Under 3 km → the line **blends halfway** per solve instead of snapping.
- Chaikin smoothing and band resolution are the panel's settings, as before.

## 6. Segment states

Compared solve to solve, shown in the panel readout and on `FlotSegment.State`:

```
STABLE · ADVANCING · RETREATING · CONTESTED · BREACHED · COLLAPSING
```

Advancing/retreating at ±0.1 km per solve; collapsing at −1 km; contested when
the mean gap between the two edges is under 1 km (or they interpenetrate);
breached while an intrusion is active. (`ISOLATED` is still on the enum; nothing
publishes it now that pockets do not get a segment of their own.)

## 7. FLOT_BREACH

`FrontlineSystem.Breach` fires when an enemy **cluster** — not a lone unit —
stands more than **2 km** behind a side's forward edge with at least **8% of
the victim's power**. Once per intrusion, not per solve; the intrusion clearing
re-arms it. Current consumer: the HUD alert. Reserves, counterattacks and
victory conditions are the intended next consumers.

## 8. Territory

`TerritoryAt(lat, lon)` → `Blue | Red | Contested`: behind the blue edge blue,
behind the red edge red, between them contested; off every engagement's span,
the nearer force decides. This is an **API** — combine it with objective
ownership and sector control when victory logic exists; the FLOT alone is not a
victory condition.

## 9. Modes

| Mode | Behaviour |
|---|---|
| **AUTO** | Solved from the force, as above |
| **MANUAL** | The designer draws the trace — panel → DRAW FLOT ON MAP: LMB adds a point, Enter/RMB finishes (min 2), Backspace undoes, Esc cancels. The drawn line feeds the same breach/territory machinery (both edges = the trace) |
| **HYBRID** | Manual until the battle starts; automatic once it runs |

The mode is saved on the map (`flotMode`); the manual trace is an ordinary
line (`flot-manual`) and survives reloads.

## 10. Fog of war

Two enemy lines: the **actual** one (used by breach detection, territory and
states — the simulation is not the player) and the **estimated** one the player
sees, computed only from enemy formations currently visible and drawn
**broken** (`planned`), captioned `ENEMY FLOT (EST)`. Nothing seen → nothing
drawn. *Gap: the estimate does not yet age — last-known contacts are not fed
in, and stale intelligence should blur the line.*

## 11. History

A snapshot of both main edges every **5 scenario minutes** (48 kept ≈ 4 hours),
on the operational clock. The panel reads it out as "moved N km"; full AAR
replay is a known gap.

## 12. Where the code lives

| File | Role |
|---|---|
| `Units/FlotEligibility.cs` | Who votes, and how much |
| `Lines/FrontlineSystem.cs` | The whole pipeline: modes, segments, breach, territory, history |
| `UI/FrontlinePanelUI.cs` | Mode switch, DRAW FLOT, settings, segment-state readout — opened by clicking any FLOT line |
| `Lines/MapLine.cs` | Flat, draped, ground-plane rendering of every published line |
| `UI/MiniMapUI.cs` | Draws every segment in its side's colour |
| `Core/GameController.cs` | Breach alert, GROUPS → FLOT manning (`PointsForManning`), mode save/load |

## 13. Known gaps

- **Nothing consumes territory or breach beyond the alert** — victory
  conditions do not exist yet (docs/22 §6); the events and queries are the API
  they will stand on.
- **Sector responsibility** (a segment belonging to a formation) is limited to
  the holding-group caption; per-sector AI orders need an AI to give them to.
- **The estimated enemy line does not age** — see §10.
- **History keeps means, not full traces** — enough for movement readouts, not
  for a drawn AAR replay.
