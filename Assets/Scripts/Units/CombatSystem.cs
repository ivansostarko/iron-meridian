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

        void Update()
        {
            if (!Running) return;
            _tickTimer += Time.deltaTime;
            if (_tickTimer < GameConfig.CombatTickSeconds) return;
            _tickTimer = 0f;
            Tick();
        }

        void Tick()
        {
            var blues = new List<UnitActor>(UnitRegistry.OfTeam(Team.User));
            var reds = new List<UnitActor>(UnitRegistry.OfTeam(Team.Enemy));

            foreach (var b in blues)
                foreach (var r in reds)
                {
                    double km = GeoUtils.DistanceKm(
                        b.State.latitude, b.State.longitude,
                        r.State.latitude, r.State.longitude);

                    bool bReaches = km <= b.Def.weaponRangeKm;
                    bool rReaches = km <= r.Def.weaponRangeKm;
                    if (!bReaches && !rReaches) continue;

                    if (bReaches && !Ordered(b)) ResolveAttack(b, r);
                    if (rReaches && !Ordered(r)) ResolveAttack(r, b);
                }

            Ticked?.Invoke();
        }

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
            if (d.Category == UnitCategory.Drone) mod *= Mathf.Lerp(0.5f, 2.2f, a.antiAir / 100f);
            if (a.isSupport) mod *= 0.4f;
            if (s.ammo <= 0) mod *= 0.25f;

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

            if (defender.IsAlive) defender.State.status = UnitStatus.Engaging.ToString();

            // Consumption
            s.ammo = Mathf.Max(0, s.ammo - Mathf.CeilToInt(a.ammoStock * 0.004f));
            s.status = UnitStatus.Engaging.ToString();
        }
    }
}
