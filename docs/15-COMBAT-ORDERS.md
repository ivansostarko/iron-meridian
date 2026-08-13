# Combat Orders

The register of every order a unit can be given in **battle mode**. This is the
human-readable version of `AttackTaskCatalog.cs` and `DefenceOrderSystem.cs` —
keep it in step with them in the same change.

Orders live on the bottom **order bar** (`UnitActionBarUI`), which appears only
while a battle is running and exactly one unit is selected. In scenario mode the
bar is hidden and every order below is refused: the editor places counters, it
does not fight.

```
ORDERS — 1ST INFANTRY BATTALION
┌──────────┬──────────┬──────────┐
│   MOVE   │  ATTACK  │ DEFENCE  │
└──────────┴──────────┴──────────┘
                │           │
        offensive task   defensive task
        submenu (×5)     submenu (×3)
```

---

## 1. Move

Arms a pending order; the next click on the map is the destination. The unit
marches there along a planned route — see
[03-GAMEPLAY.md](03-GAMEPLAY.md#movement).

---

## 2. Attack — the five offensive tasks

Pick a task, then **click an enemy formation**. Attack orders want a *unit*, not
a point on the ground; clicking bare terrain is a miss and leaves the order
armed, so a slightly-off click costs one more click rather than the whole order.
`Esc` or right-click cancels.

### What happens next

1. An **attack arrow** is drawn from the attacker to the target, dashes marching
   toward what is about to be hit, coloured by task.
2. If the target is already inside the task's engagement range, the unit opens
   fire at once and the arrow fades after a couple of seconds.
3. Otherwise the unit **marches to a firing position** — on the bearing from the
   target back toward itself, just inside engagement range — routed over the
   terrain like any other move, with the usual movement trail. **The arrow fades
   the moment it arrives**, and the engagement's own muzzle flashes, impacts and
   fires carry the story from there.
4. If the target withdraws out of range, the order returns to the approach phase
   and follows it. An attack does not quietly lapse because the enemy moved.

### The tasks

| Task | Closes to | Damage | Shock | Return fire | Advances | Opening |
|---|---|---|---|---|---|---|
| **ATTACK** — close and destroy | 85% of weapon range | ×1.0 | ×1.0 | ×1.0 | yes | — |
| **ASSAULT** — close right up | 22% | ×1.85 | ×1.4 | **×1.45** | yes | ×1.25 |
| **SUPPRESS** — pin from max range | 100% | ×0.40 | **×2.6** | ×0.55 | yes | — |
| **AMBUSH** — hold concealed | 75% | ×1.15 | ×1.8 | ×0.85 | **no** | **×2.4, free** |
| **COUNTERATTACK** — strike a committed enemy | 70% | ×1.35 | ×1.5 | ×0.9 | yes | ×1.6 |

- **Damage** scales strength loss. **Shock** scales the morale and organisation
  damage that stops a formation functioning without killing anyone — which is
  why suppression barely dents a target's strength and wrecks its ability to act.
  A target whose organisation falls below 25 is marked `Suppressed`.
- **Return fire** is what the target hits back with, and only if the attacker is
  inside the *target's* weapon range. An assault is decisive precisely because
  both sides are fully exposed at that distance.
- **Advances = no** is the whole of AMBUSH: it sits where it is and lets the
  target walk into range. Its arrow stays up marking the ground it is watching,
  and its opening volley is doubled *and* draws no reply — surprise is worth a
  great deal once and nothing afterwards.
- **Opening** multipliers apply to the first volley of an order only.

### Precedence over automatic combat

`CombatSystem` normally has every opposing pair in weapon range exchange damage
each tick. A unit acting on an explicit order is **skipped by that sweep** and
fires only at what it was told to — otherwise it would shoot twice a tick, once
at its objective and once at whatever else was in reach. Units with no order
still engage anything they can reach, which is what keeps a front line fighting
without micromanaging every formation.

The ordinary exchange's damage clamp is untouched; the task multiplier is applied
on top of the clamped value, under an outer ceiling so no single order can delete
a formation in one tick.

### Not saved

Attack orders are live combat state, not map data, and are deliberately **not**
written to the save file. A scenario describes a situation; reloading one should
not resume half-finished engagements. Stopping the battle clears every order and
abandons any approach march in progress.

---

## 3. Defence — the three defensive tasks

| Task | What it does |
|---|---|
| **DEFEND** | Lays a bowed **defence line** across the threat axis, captioned `DEFENCE LINE — <unit>`, with a closed **battle position** behind it. Subordinates are distributed evenly along the frontage and marched to their slots facing the threat; the commander sits back inside the position. |
| **HOLD** | Pins the unit where it stands, stops any march, turns it onto the threat, and marks the position with a yellow `HOLD` marker. |
| **GUARD** | Pushes the unit forward onto a guard position between the force it protects and the threat, and marks it with a green `GUARD` marker. |

Unlike attack orders, everything defence produces **is** map data — `defence-*`
lines and markers that round-trip through the save file. See
[05-MAP-SAVES.md](05-MAP-SAVES.md#defensive-tasks).

---

## 4. Effects and audio each order triggers

Every effect below goes through `VfxSystem` and carries its catalogue sound —
see [08-PARTICLE-SYSTEMS.md](08-PARTICLE-SYSTEMS.md) and
[10-AUDIO.md](10-AUDIO.md), which are the registers.

| Moment | Effect | Sound | Where |
|---|---|---|---|
| Attacker fires | `WeaponFire` | — | `CombatSystem.ResolveAttack` → `UnitActor.NotifyFiring` |
| Rounds land on the target | `ImpactBurst` | Impact | `UnitActor.ApplyDamage` |
| A volley takes ≥1.8% strength | `Explosion` | **Explosion** | `AttackOrderSystem.Engage`, throttled to one per 2.4 s per order |
| ASSAULT opens | `GroundFire` on the objective, 20 s | **Fire** | `AttackOrderSystem.BeginEngagement` |
| SUPPRESS opens | `SmokeScreen` on the target, for the order's life | **Smoke** | `AttackOrderSystem.BeginEngagement` |
| Target drops below 45% strength | `FireSmall`/`Medium`/`Large` attached | Fire | `UnitActor.RefreshBurning` |
| Target destroyed | `PlayWreck` — explosion, fire, smoke plume | Explosion + Fire + Smoke | `UnitActor.Die` |

`GroundFire` resolves to the imported **Free Fire VFX** floor-fire prefab where
the render pipeline can draw it, and to the procedural stand-in otherwise — the
same rule every effect in this project follows. No effect and no sound in the
table needs an asset to be installed: everything degrades to a procedural
build. See §4 of [08-PARTICLE-SYSTEMS.md](08-PARTICLE-SYSTEMS.md) for why the
authored pack usually falls back.

---

## 5. Where the code lives

| File | Role |
|---|---|
| `Assets/Scripts/Data/Enums.cs` | `AttackTask`, `MarkerKind` |
| `Assets/Scripts/Units/AttackTaskCatalog.cs` | The five tasks in numbers — the table in §2 |
| `Assets/Scripts/Units/AttackOrderSystem.cs` | Order lifecycle: approach, wait, engage |
| `Assets/Scripts/Units/AttackArrow.cs` | The axis-of-attack arrow on the map |
| `Assets/Scripts/Units/CombatSystem.cs` | Tick loop, `ResolveAttack`, order precedence |
| `Assets/Scripts/Lines/DefenceOrderSystem.cs` | Defend / Hold / Guard |
| `Assets/Scripts/UI/UnitActionBarUI.cs` | The order bar and both submenus |
| `Assets/Scripts/Units/SelectionManager.cs` | Arming an order and picking the target |

## Adding an offensive task

1. Add a value to `AttackTask` in `Data/Enums.cs`.
2. Add its row to `AttackTaskCatalog.Defs` — name, one-liner, ranges, multipliers,
   opening effect, arrow colour.
3. Nothing else. The submenu is built from the catalogue, and `AttackOrderSystem`
   runs the same loop for every task.
4. **Update §2 of this file**, and §3 of `08-PARTICLE-SYSTEMS.md` if the task
   triggers an effect at a new moment.
