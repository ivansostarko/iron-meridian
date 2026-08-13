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
    /// </summary>
    public class CombatSystem : MonoBehaviour
    {
        public bool Running { get; private set; }
        public event System.Action<bool> RunningChanged;

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

                    if (bReaches) Exchange(b, r);
                    if (rReaches) Exchange(r, b);
                }
        }

        void Exchange(UnitActor attacker, UnitActor defender)
        {
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

            float dmg = 0.010f * (atkPower / defPower) * mod;
            dmg = Mathf.Clamp(dmg, 0.001f, 0.08f);

            // Firing signature at the shooter, impact effects at the target
            // (raised inside ApplyDamage). Both self-throttle — see
            // docs/08-PARTICLE-SYSTEMS.md.
            attacker.NotifyFiring();
            defender.ApplyDamage(dmg);
            defender.State.status = defender.IsAlive ? UnitStatus.Engaging.ToString()
                                                     : UnitStatus.Destroyed.ToString();

            // Consumption
            s.ammo = Mathf.Max(0, s.ammo - Mathf.CeilToInt(a.ammoStock * 0.004f));
            s.status = UnitStatus.Engaging.ToString();
        }
    }
}
