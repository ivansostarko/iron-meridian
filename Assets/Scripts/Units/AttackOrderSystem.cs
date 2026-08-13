using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Vfx;

namespace IronMeridian.Units
{
    /// <summary>
    /// The five offensive tasks from the battle order bar: pick a task, click an
    /// enemy formation, and the attacker carries it out.
    ///
    /// One loop serves all five (see <see cref="AttackTaskCatalog"/> for what
    /// separates them). An order runs through three phases:
    ///
    ///  • **Approaching** — the target is out of the task's engagement range, so
    ///    the attacker marches to a firing position on the bearing it came from,
    ///    routed over the terrain like any other move. The attack arrow is drawn
    ///    for exactly this phase; it fades the moment the unit arrives.
    ///  • **Waiting** — an ambush, which does not advance. It sits concealed
    ///    until the target walks into range, then springs.
    ///  • **Engaging** — resolved on every combat tick for as long as the target
    ///    stays in range and alive. Drift back out of range and the order returns
    ///    to Approaching, so an attack follows a withdrawing enemy instead of
    ///    quietly lapsing.
    ///
    /// **Battle mode only.** In the scenario editor there is nothing to attack
    /// with — the clock is not running, nothing is resolving — so orders are
    /// refused, and any order in flight is dropped the moment the battle stops.
    ///
    /// Orders are live combat state, not map data: they are deliberately *not*
    /// saved. A scenario file describes a situation, and reloading one should
    /// not resume half-finished engagements.
    /// </summary>
    public class AttackOrderSystem : MonoBehaviour
    {
        /// <summary>Firing position standoff, as a fraction of the task's engagement range.</summary>
        const double FiringPositionFraction = 0.85;
        /// <summary>Range slack so a unit idling on its firing position does not flip phase every tick.</summary>
        const double RangeHysteresisKm = 0.15;
        /// <summary>Seconds between the heavy detonation effects an engagement throws off.</summary>
        const float BlastCooldownSeconds = 2.4f;
        /// <summary>Strength lost in one tick above which the hit reads as a detonation rather than a puff.</summary>
        const float BlastDamageThreshold = 0.018f;
        /// <summary>Seconds an idle arrow lingers when the attacker never had to move.</summary>
        const float StationaryArrowSeconds = 2.2f;

        public System.Action<string> Flash;

        CombatSystem _combat;
        CesiumGeoreference _geo;
        readonly List<AttackOrder> _orders = new List<AttackOrder>();

        /// <summary>
        /// The delegate handed to <see cref="CombatSystem.HasAttackOrder"/>,
        /// kept so teardown can check it is still ours before clearing it — a
        /// method group cannot be compared against a delegate field directly.
        /// </summary>
        System.Func<UnitActor, bool> _hasOrderHook;

        /// <summary>Pending is the value a freshly built order carries before <see cref="Order"/> decides.</summary>
        enum Phase { Pending, Approaching, Waiting, Engaging }

        /// <summary>One live order. Named for the task rather than `Order` — that is the method that creates it.</summary>
        class AttackOrder
        {
            public UnitActor attacker, target;
            public AttackTaskDef def;
            public Phase phase;
            public AttackArrow arrow;
            public VfxInstance openingVfx;
            public bool openingSpent;
            public float engageRangeKm;
            public float nextBlast;
            public float arrowExpiry;      // unscaled time the idle arrow fades; 0 = not on a timer
        }

        public void Init(CombatSystem combat, CesiumGeoreference geo)
        {
            _combat = combat;
            _geo = geo;
            _hasOrderHook = HasOrder;
            _combat.Ticked += ResolveAll;
            _combat.HasAttackOrder = _hasOrderHook;
            _combat.RunningChanged += running => { if (!running) CancelAll("Battle stopped — attack orders cleared."); };
        }

        void OnDestroy()
        {
            if (_combat == null) return;
            _combat.Ticked -= ResolveAll;
            if (_combat.HasAttackOrder == _hasOrderHook) _combat.HasAttackOrder = null;
        }

        /// <summary>True while this unit is acting on an explicit attack order.</summary>
        public bool HasOrder(UnitActor unit)
        {
            if (unit == null) return false;
            foreach (var o in _orders) if (o.attacker == unit) return true;
            return false;
        }

        // ------------------------------------------------------- ordering

        /// <summary>
        /// Gives <paramref name="attacker"/> an offensive task against
        /// <paramref name="target"/>. Returns false with a reason flashed to the
        /// player when the order cannot stand.
        /// </summary>
        public bool Order(UnitActor attacker, UnitActor target, AttackTask task)
        {
            if (!CombatSystem.BattleRunning)
            {
                Flash?.Invoke("Attack orders need a running battle — press START BATTLE first.");
                return false;
            }
            if (attacker == null || !attacker.IsAlive)
            {
                Flash?.Invoke("Select a unit before ordering an attack.");
                return false;
            }
            if (target == null || !target.IsAlive)
            {
                Flash?.Invoke("Click an enemy formation to attack.");
                return false;
            }
            if (target == attacker || target.State.TeamEnum == attacker.State.TeamEnum)
            {
                Flash?.Invoke("Pick a target on the opposing side.");
                return false;
            }

            Cancel(attacker, null);

            var def = AttackTaskCatalog.Get(task);
            var order = new AttackOrder
            {
                attacker = attacker,
                target = target,
                def = def,
                engageRangeKm = Mathf.Max(0.05f, attacker.Def.weaponRangeKm * def.engageRangeFraction)
            };

            order.arrow = AttackArrow.Create(_geo, attacker, target, def.arrowTint);
            _orders.Add(order);

            double km = Separation(order);
            if (km <= order.engageRangeKm)
            {
                BeginEngagement(order);
                Flash?.Invoke($"{Name(attacker)} — {def.name} on {Name(target)}, in range, engaging.");
            }
            else if (!def.advances)
            {
                // An ambush that advances is not an ambush. It waits, and the
                // arrow stays up marking the ground it is watching.
                order.phase = Phase.Waiting;
                Flash?.Invoke($"{Name(attacker)} — {def.name} set, waiting for {Name(target)} to close to {order.engageRangeKm:0.#} km.");
            }
            else if (BeginApproach(order))
            {
                Flash?.Invoke($"{Name(attacker)} — {def.name} on {Name(target)}, closing to {order.engageRangeKm:0.#} km.");
            }
            else
            {
                order.phase = Phase.Waiting;
                Flash?.Invoke($"{Name(attacker)} cannot reach {Name(target)} — holding.");
            }

            return true;
        }

        /// <summary>Drops this unit's order, if it has one.</summary>
        public void Cancel(UnitActor attacker, string message = null)
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                if (_orders[i].attacker != attacker) continue;
                Retire(_orders[i]);
                _orders.RemoveAt(i);
                if (message != null) Flash?.Invoke(message);
            }
        }

        public void CancelAll(string message = null)
        {
            if (_orders.Count == 0) return;
            foreach (var o in _orders) Retire(o);
            _orders.Clear();
            if (message != null) Flash?.Invoke(message);
        }

        /// <summary>Tears down everything an order put on the map.</summary>
        void Retire(AttackOrder order)
        {
            if (order.arrow != null) order.arrow.Finish();
            // A smoke screen laid on the target is stopped with the order; the
            // ground fire an assault started is left to burn itself out, because
            // burning ground does not care whether the order is still standing.
            if (order.openingVfx != null && order.def.openingEffectSeconds <= 0f) order.openingVfx.Stop();
            // Only stop the march this order started, not one the player gave.
            if (order.phase == Phase.Approaching && order.attacker != null && order.attacker.Mover != null)
                order.attacker.Mover.Cancel();
        }

        // ------------------------------------------------------- phases

        /// <summary>
        /// Marches the attacker to a firing position: on the bearing from the
        /// target back toward the attacker, just inside engagement range. Going
        /// to the target's own position instead would walk the formation into
        /// the middle of the enemy, which is only what an assault wants — and
        /// its range fraction already puts the firing position on top of them.
        /// </summary>
        bool BeginApproach(AttackOrder order)
        {
            var a = order.attacker.State;
            var t = order.target.State;

            float backBearing = GeoUtils.BearingDeg(t.latitude, t.longitude, a.latitude, a.longitude);
            GeoUtils.Destination(t.latitude, t.longitude, backBearing,
                order.engageRangeKm * FiringPositionFraction, out double lat, out double lon);

            float face = GeoUtils.BearingDeg(lat, lon, t.latitude, t.longitude);
            if (!order.attacker.Mover.MoveTo(lat, lon, face)) return false;

            order.phase = Phase.Approaching;
            order.arrowExpiry = 0f;
            return true;
        }

        /// <summary>
        /// Opens fire. This is where the arrow's job ends — the unit is where it
        /// was told to be — and where the task's opening effect goes in.
        /// </summary>
        void BeginEngagement(AttackOrder order)
        {
            if (order.phase == Phase.Engaging) return;

            // The arrow marks the pending attack, so arriving at the firing
            // position is what retires it. An order that never had to move has
            // no arrival to retire it, so it gets a short linger instead —
            // otherwise clicking a target in range would flash an arrow for one
            // frame and the player would never see the order acknowledged.
            bool arrived = order.phase == Phase.Approaching;
            order.phase = Phase.Engaging;

            if (order.arrow != null)
            {
                if (arrived) { order.arrow.Finish(); order.arrow = null; }
                else order.arrowExpiry = Time.unscaledTime + StationaryArrowSeconds;
            }

            order.attacker.SetHeading(GeoUtils.BearingDeg(
                order.attacker.State.latitude, order.attacker.State.longitude,
                order.target.State.latitude, order.target.State.longitude));

            if (order.def.openingEffect.HasValue && order.openingVfx == null)
            {
                order.openingVfx = VfxSystem.Play(order.def.openingEffect.Value,
                    order.target.State.latitude, order.target.State.longitude,
                    Mathf.Lerp(0.8f, 1.6f, order.target.FormationScale01));

                // A fixed-life effect burns out on its own; a screen hangs until
                // the order that called for it ends.
                if (order.openingVfx != null && order.def.openingEffectSeconds > 0f && VfxSystem.Active != null)
                    VfxSystem.Active.StopAfter(order.openingVfx, order.def.openingEffectSeconds);
            }
        }

        // ------------------------------------------------------- resolution

        /// <summary>Runs every order forward one combat tick. Driven by <see cref="CombatSystem.Ticked"/>.</summary>
        void ResolveAll()
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                var order = _orders[i];

                if (order.attacker == null || !order.attacker.IsAlive)
                {
                    Retire(order); _orders.RemoveAt(i); continue;
                }
                if (order.target == null || !order.target.IsAlive)
                {
                    Retire(order); _orders.RemoveAt(i);
                    Flash?.Invoke($"{Name(order.attacker)} — target destroyed.");
                    continue;
                }

                Step(order);
            }
        }

        void Step(AttackOrder order)
        {
            double km = Separation(order);
            bool inRange = km <= order.engageRangeKm + RangeHysteresisKm;

            switch (order.phase)
            {
                case Phase.Approaching:
                    if (inRange) { BeginEngagement(order); break; }
                    // Arrived but the target has moved on, or the march never
                    // started: lay on a fresh approach rather than stalling.
                    if (!order.attacker.Mover.IsMoving && !BeginApproach(order)) order.phase = Phase.Waiting;
                    break;

                case Phase.Waiting:
                    if (inRange) { BeginEngagement(order); break; }
                    if (order.def.advances && BeginApproach(order)) break;
                    break;

                case Phase.Engaging:
                    if (!inRange)
                    {
                        order.phase = order.def.advances ? Phase.Approaching : Phase.Waiting;
                        if (order.def.advances) BeginApproach(order);
                        break;
                    }
                    Engage(order, km);
                    break;
            }
        }

        /// <summary>One tick of an ordered attack, plus whatever the target shoots back.</summary>
        void Engage(AttackOrder order, double km)
        {
            var def = order.def;
            bool opening = !order.openingSpent;
            order.openingSpent = true;

            float before = order.target.State.strength;
            float damage = def.damageMultiplier * (opening ? def.openingMultiplier : 1f);
            _combat.ResolveAttack(order.attacker, order.target, damage, def.shockMultiplier);

            // Routed is the worse state and belongs to the damage model; pinning
            // must not quietly promote a broken formation back up to suppressed.
            if (def.pins && order.target.IsAlive &&
                order.target.State.status != UnitStatus.Routed.ToString())
                order.target.State.status = UnitStatus.Suppressed.ToString();

            // A hit big enough to read as a detonation gets one. Throttled per
            // order, so a long engagement marks its heavy blows instead of
            // carpeting the map — see docs/08-PARTICLE-SYSTEMS.md.
            float lost = before - order.target.State.strength;
            if (lost >= BlastDamageThreshold && Time.time >= order.nextBlast)
            {
                order.nextBlast = Time.time + BlastCooldownSeconds;
                VfxSystem.Play(VfxId.Explosion,
                    order.target.State.latitude, order.target.State.longitude,
                    Mathf.Lerp(0.7f, 1.4f, order.target.FormationScale01));
            }

            // Return fire. Surprise buys the ambusher one free volley; after
            // that the target is fighting back like anyone else, and it can only
            // shoot back at all if the attacker is inside *its* weapon range.
            bool free = opening && def.openingIsFree;
            if (free || def.returnFireMultiplier <= 0f) return;
            if (!order.target.IsAlive || km > order.target.Def.weaponRangeKm) return;

            _combat.ResolveAttack(order.target, order.attacker, def.returnFireMultiplier);
        }

        static double Separation(AttackOrder order) => GeoUtils.DistanceKm(
            order.attacker.State.latitude, order.attacker.State.longitude,
            order.target.State.latitude, order.target.State.longitude);

        void Update()
        {
            // Retires the arrows that are on a linger timer rather than on an
            // arrival (see BeginEngagement).
            foreach (var o in _orders)
            {
                if (o.arrow == null || o.arrowExpiry <= 0f) continue;
                if (Time.unscaledTime < o.arrowExpiry) continue;
                o.arrow.Finish();
                o.arrow = null;
            }
        }

        static string Name(UnitActor u) =>
            u == null ? "unit"
            : string.IsNullOrEmpty(u.State.customName) ? u.Def.name : u.State.customName;
    }
}
