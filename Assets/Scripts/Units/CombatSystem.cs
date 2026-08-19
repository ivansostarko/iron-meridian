using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// Tick-based combat resolution. While the battle is running, every
    /// opposing pair inside weapon range exchanges damage each tick.
    ///
    /// Damage model (simple, tunable):
    ///   power     = definition.PowerAt(echelon, strength)
    ///   dmg/tick  = attackerPower / defenderPower * base * modifiers
    /// Modifiers: hard attack vs armour, anti-air vs drones, ammo state,
    /// support units fight at 40%. Ammo and food are consumed; units that
    /// run out of ammo deal 25% damage.
    ///
    /// **Ordered attacks take precedence.** A unit given an explicit task by
    /// <see cref="AttackOrderSystem"/> shoots what it was told to and is skipped
    /// by the automatic sweep below — otherwise it would fire twice a tick, once
    /// at its objective and once at whatever else happened to be in range. Units
    /// with no order still engage anything they can reach, which is what keeps a
    /// front line fighting without the player micromanaging every formation.
    /// </summary>
    public class CombatSystem : MonoBehaviour
    {
        public bool Running { get; private set; }
        public event System.Action<bool> RunningChanged;

        /// <summary>
        /// Raised after each tick's automatic exchanges, so ordered attacks
        /// resolve on the same clock rather than on their own timer.
        /// </summary>
        public event System.Action Ticked;

        /// <summary>
        /// Answers whether a unit is acting on an explicit attack order. Set by
        /// <see cref="AttackOrderSystem"/>; null means nothing is ordered.
        /// </summary>
        public System.Func<UnitActor, bool> HasAttackOrder;

        /// <summary>
        /// Whether a battle is running, readable without a reference to the
        /// system. Movement is a game-mode behaviour and units are spawned
        /// without knowing which controller owns them, so <see cref="UnitMover"/>
        /// asks here — the same reason <see cref="MapManager.Active"/> exists.
        /// </summary>
        public static bool BattleRunning { get; private set; }

        float _tickTimer;

        void Awake() => BattleRunning = false;      // a reloaded scene starts in the editor

        void OnDestroy()
        {
            if (Running) BattleRunning = false;
        }

        public void SetRunning(bool run)
        {
            Running = run;
            BattleRunning = run;
            RunningChanged?.Invoke(run);
        }

        public void Toggle() => SetRunning(!Running);

        /// <summary>
        /// Most catch-up ticks resolved in one frame. Time can now run at a few
        /// hundred times real speed (see <see cref="GameClock"/>), and one frame
        /// at that rate is worth minutes of battle — resolving all of it in a
        /// single frame would stall the game. Falling behind the clock at
        /// extreme speeds is the better failure: the fight is still resolved in
        /// order, just spread over more frames.
        /// </summary>
        const int MaxCatchUpTicks = 8;

        void Update()
        {
            if (!Running) return;

            _tickTimer += Time.deltaTime;
            if (_tickTimer < GameConfig.CombatTickSeconds) return;

            // The remainder is carried rather than thrown away. It used to be
            // zeroed, which capped the battle at one tick per *frame* — so above
            // about x1 the clock raced ahead while the fighting quietly ran at
            // frame rate, and the same scenario resolved differently on a fast
            // machine than on a slow one.
            int ticks = 0;
            while (_tickTimer >= GameConfig.CombatTickSeconds && ticks < MaxCatchUpTicks)
            {
                _tickTimer -= GameConfig.CombatTickSeconds;
                ticks++;
                Tick();
            }

            // Whatever could not be caught up with is dropped, not banked: a
            // backlog would keep the game resolving ticks after the player has
            // slowed time back down.
            if (_tickTimer >= GameConfig.CombatTickSeconds) _tickTimer = 0f;
        }

        /// <summary>
        /// Formations that exchanged fire on the last tick. Read by the HUD and
        /// by <see cref="UnitMover"/>: a formation that has run into the enemy
        /// stops and fights rather than marching on through the engagement.
        /// </summary>
        static readonly HashSet<UnitActor> _engaged = new HashSet<UnitActor>();

        /// <summary>True if this formation is currently in contact with the enemy.</summary>
        public static bool InContact(UnitActor unit) => unit != null && _engaged.Contains(unit);

        /// <summary>How many formations are in contact right now — the HUD's "engagements" readout.</summary>
        public static int ContactCount => _engaged.Count;

        void Tick()
        {
            var blues = new List<UnitActor>(UnitRegistry.OfTeam(Team.User));
            var reds = new List<UnitActor>(UnitRegistry.OfTeam(Team.Enemy));

            _engaged.Clear();

            foreach (var b in blues)
                foreach (var r in reds)
                {
                    double km = GeoUtils.DistanceKm(
                        b.State.latitude, b.State.longitude,
                        r.State.latitude, r.State.longitude);

                    // Range is measured between the formations' near edges, not
                    // between their map pins — the same correction BlastDamage
                    // makes. A brigade whose leading elements are inside a
                    // battalion's weapon range is in range of it, and measuring
                    // centre to centre said otherwise by a kilometre or more.
                    float gap = (float)(km * 1000.0)
                                - EchelonInfo.FootprintRadiusMeters(b.State.EchelonEnum) * ContactFootprintShare
                                - EchelonInfo.FootprintRadiusMeters(r.State.EchelonEnum) * ContactFootprintShare;
                    float gapKm = Mathf.Max(0f, gap) / 1000f;

                    bool bReaches = gapKm <= b.Def.weaponRangeKm;
                    bool rReaches = gapKm <= r.Def.weaponRangeKm;
                    if (!bReaches && !rReaches) continue;

                    // Contact is mutual even when only one of them can shoot.
                    // Being under fire you cannot answer is still being in a
                    // battle, and a formation that marched on through it because
                    // its own range was shorter would be walking away from
                    // rounds that are still landing on it.
                    _engaged.Add(b);
                    _engaged.Add(r);

                    // AUTOMATIC ATTACK off means the formation does not open
                    // fire of its own accord — it is still *in* the battle and
                    // still takes what is coming, which is the whole point of
                    // switching it off on a screen or a recon element. An
                    // explicit attack order is unaffected: that is the player
                    // telling it to shoot, not the sweep deciding for it.
                    if (bReaches && !Ordered(b) && b.State.automaticAttack) ResolveAttack(b, r);
                    if (rReaches && !Ordered(r) && r.State.automaticAttack) ResolveAttack(r, b);
                }

            Ticked?.Invoke();
        }

        /// <summary>
        /// Share of a formation's footprint that counts as "its leading edge"
        /// for engagement range. The same two thirds <see cref="BlastDamage"/>
        /// uses, and for the same reason: the whole radius would make a division
        /// engage from four kilometres further out than it should.
        /// </summary>
        const float ContactFootprintShare = 0.66f;

        bool Ordered(UnitActor unit) => HasAttackOrder != null && HasAttackOrder(unit);

        /// <summary>
        /// One unit firing on another for one tick.
        ///
        /// <paramref name="damageMultiplier"/> and <paramref name="shockMultiplier"/>
        /// are what separate an assault from suppressive fire: the first scales
        /// strength loss, the second scales the morale and organisation damage
        /// that stops a formation functioning without killing anyone. Both are 1
        /// for the ordinary automatic exchange.
        /// </summary>
        public void ResolveAttack(UnitActor attacker, UnitActor defender,
            float damageMultiplier = 1f, float shockMultiplier = 1f)
        {
            if (attacker == null || defender == null || !attacker.IsAlive || !defender.IsAlive) return;

            var a = attacker.Def; var d = defender.Def;
            var s = attacker.State;

            float atkPower = attacker.CurrentPower();
            float defPower = Mathf.Max(1f, defender.CurrentPower());

            // Weapon vs target modifiers
            float mod = 1f;
            if (d.armour > 40f) mod *= Mathf.Lerp(0.25f, 1.6f, a.hardAttack / 100f);
            // Anything in the air is fought with anti-air, whether it is crewed
            // or not: a rifle company is nearly useless against a helicopter for
            // the same reason it is useless against a drone.
            if (d.Category == UnitCategory.Drone || d.Category == UnitCategory.Air)
                mod *= Mathf.Lerp(0.5f, 2.2f, a.antiAir / 100f);
            if (a.isSupport) mod *= 0.4f;
            if (s.ammo <= 0) mod *= 0.25f;

            // Who is commanding this formation, and whether his own chain is
            // intact. Unassigned is exactly 1.0, so a scenario with no order of
            // battle fights precisely as it did before commanders existed.
            // See CommanderRegistry and docs/23-COMMANDERS.md.
            mod *= CommanderRegistry.CommandBonus(attacker);

            // The ordinary exchange is clamped exactly as it always was, and the
            // task multiplier is applied on top of that — folding the multiplier
            // in before the clamp would have quietly changed unordered combat
            // too. The outer ceiling only exists so no single order can delete a
            // formation in one tick.
            float dmg = 0.010f * (atkPower / defPower) * mod;
            dmg = Mathf.Clamp(dmg, 0.001f, 0.08f);
            dmg = Mathf.Min(dmg * Mathf.Max(0f, damageMultiplier), 0.30f);

            // Firing signature at the shooter, impact effects at the target
            // (raised inside ApplyDamage). Both self-throttle — see
            // docs/08-PARTICLE-SYSTEMS.md.
            attacker.NotifyFiring();
            defender.ApplyDamage(dmg);

            // Shock beyond the losses themselves. Applied after the damage so a
            // formation that was just destroyed is not also "suppressed".
            if (shockMultiplier > 1f && defender.IsAlive)
                defender.ApplyShock(dmg * 40f * (shockMultiplier - 1f));

            if (defender.IsAlive) defender.State.status = nameof(UnitStatus.Engaging);

            // Consumption
            s.ammo = Mathf.Max(0, s.ammo - Mathf.CeilToInt(a.ammoStock * 0.004f));
            s.status = nameof(UnitStatus.Engaging);
        }
    }
}
