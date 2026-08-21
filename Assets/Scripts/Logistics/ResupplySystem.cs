using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Logistics
{
    /// <summary>
    /// What a supply point is actually **for**: formations standing on its
    /// ground draw from it, and it runs out.
    ///
    /// Until this existed a logistic installation was a symbol. A designer could
    /// lay out a rear area, an aeroplane could push five bundles onto the
    /// objective, and nothing on the map was any better supplied for it — which
    /// made the whole LOGISTICS panel decoration and made an air supply mission
    /// a firework. A formation that has run dry is the most interesting state a
    /// unit can be in, and it needs somewhere to go.
    ///
    /// **The rule is proximity, and nothing else.** A formation inside the
    /// site's service radius, on the same side, alive, gets topped up. No
    /// convoy, no order, no draw request: this game is played at the operational
    /// level, where "is it in the fuel point's area" is exactly the question a
    /// staff officer asks, and modelling the truck run would be modelling
    /// something the player has no control over anyway.
    ///
    /// **Stock is counted in issues.** One issue is one formation's worth. It is
    /// the number the player can reason about — "this cache is good for four
    /// more battalions" — and the three loads are not comparable in litres and
    /// rounds. What an issue restores is decided here, per
    /// <see cref="SupplyService"/>.
    ///
    /// **A bigger formation costs more.** An issue tops a company up; a division
    /// eats several. Scaled on the echelon's manpower multiplier, so the same
    /// cache serves a great many companies or two brigades — which is the
    /// decision the player is making when they choose where to put it.
    ///
    /// **Battle mode only.** In the editor nothing is being expended, so nothing
    /// needs replacing, and a cache that quietly drained itself while a scenario
    /// was being laid out would be a scenario that started wrong.
    ///
    /// See docs/26-LOGISTICS.md.
    /// </summary>
    public class ResupplySystem : MonoBehaviour
    {
        /// <summary>Short user-facing messages, wired to the HUD's flash line.</summary>
        public System.Action<string> Flash;
        /// <summary>Raised when a site's stock changes, so the panel and the list repaint.</summary>
        public event System.Action Changed;

        /// <summary>
        /// Seconds of scenario time between draws by one formation from one
        /// site.
        ///
        /// Two minutes: long enough that a unit parked on a depot does not
        /// hoover it up in a few seconds of fast-forwarded clock, short enough
        /// that pulling a battalion back to refuel is worth doing rather than a
        /// wait the player watches.
        /// </summary>
        const float DrawIntervalSeconds = 120f;

        /// <summary>How often the sweep runs, real seconds.</summary>
        const float TickSeconds = 0.75f;

        /// <summary>
        /// Fraction of a formation's establishment one issue restores. Not all
        /// of it: a formation that arrives empty and leaves full in one draw
        /// makes the *second* draw meaningless, and the interesting question is
        /// how long a unit has to sit on the ground rather than whether it
        /// touched it.
        /// </summary>
        const float IssueShare = 0.5f;

        /// <summary>
        /// Strength one medical issue returns to duty, and the ceiling it works
        /// up to.
        ///
        /// Capped well short of full: a medical point treats casualties and
        /// returns the lightly wounded, it does not reconstitute a battalion
        /// that has been destroyed. A formation below this is brought up to it
        /// and no further.
        /// </summary>
        const float MedicalRecoveryPerIssue = 0.08f;
        const float MedicalCeiling = 0.75f;

        /// <summary>
        /// Serviceability one repair issue returns to the road.
        ///
        /// Larger than the medical figure and **uncapped**, which is the
        /// difference between the two: a workshop can put a recovered vehicle
        /// back exactly as it was, where a hospital cannot reconstitute a
        /// battalion. Roughly seven draws — a quarter of an hour of scenario
        /// time — takes a formation from fully deadlined to fully serviceable.
        /// </summary>
        const float RepairPerIssue = 0.15f;

        /// <summary>A general site hands out everything, at this share of a specialist's rate.</summary>
        const float GeneralEfficiency = 0.7f;

        LogisticsSystem _sites;
        GameClock _clock;

        float _timer;

        /// <summary>
        /// When each formation last drew from each site, in scenario seconds.
        /// Keyed on both, so a unit sitting between a fuel point and an ammo
        /// point draws from each on its own schedule.
        /// </summary>
        readonly Dictionary<(string unit, string site), float> _lastDraw =
            new Dictionary<(string, string), float>();

        public void Init(LogisticsSystem sites, GameClock clock)
        {
            _sites = sites;
            _clock = clock;
        }

        void Update()
        {
            if (!CombatSystem.BattleRunning || _sites == null) return;

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = TickSeconds;

            Sweep();
        }

        void Sweep()
        {
            var sites = _sites.Sites;
            if (sites == null || sites.Count == 0) return;

            float now = ScenarioSeconds();
            bool anyChange = false;

            // A copy: a spent cache removes itself, which mutates the list.
            var live = new List<LogisticsSite>(sites);

            foreach (var site in live)
            {
                if (site == null) continue;
                var def = LogisticsCatalog.Get(site.Kind);
                if (def.service == SupplyService.None) continue;
                if (site.Data.TracksStock && site.Data.stock <= 0.0) continue;

                foreach (var unit in UnitRegistry.All)
                {
                    if (unit == null || !unit.IsAlive || unit.Def == null) continue;
                    if (unit.State.team != site.Data.team) continue;

                    double km = GeoUtils.DistanceKm(site.Data.latitude, site.Data.longitude,
                        unit.State.latitude, unit.State.longitude);
                    if (km > def.serviceRadiusKm) continue;

                    var key = (unit.State.instanceId, site.Data.id);
                    if (_lastDraw.TryGetValue(key, out float last) &&
                        now - last < DrawIntervalSeconds) continue;

                    if (!Draw(site, def, unit)) continue;

                    _lastDraw[key] = now;
                    anyChange = true;
                    if (site.Data.TracksStock && site.Data.stock <= 0.0) break;
                }
            }

            if (anyChange) Changed?.Invoke();
            RemoveSpent();
        }

        /// <summary>
        /// One formation drawing one issue.
        /// </summary>
        /// <returns>False when the formation needed nothing, so no stock is spent on it.</returns>
        bool Draw(LogisticsSite site, LogisticsDef def, UnitActor unit)
        {
            float efficiency = def.service == SupplyService.General ? GeneralEfficiency : 1f;
            bool took = false;

            if (def.service == SupplyService.Ammunition || def.service == SupplyService.General)
                took |= Rearm(unit, efficiency);

            if (def.service == SupplyService.Fuel || def.service == SupplyService.General)
                took |= Refuel(unit, efficiency);

            if (def.service == SupplyService.Repair || def.service == SupplyService.General)
                took |= Mend(unit, efficiency);

            if (def.service == SupplyService.Medical || def.service == SupplyService.General)
                took |= Treat(unit, efficiency);

            if (!took) return false;

            if (site.Data.TracksStock)
            {
                site.Data.stock = System.Math.Max(0.0, site.Data.stock - IssueCost(unit));
                site.RefreshStock();
            }
            return true;
        }

        /// <summary>
        /// Issues one formation costs. A company is one; a division is a good
        /// deal more. Square-rooted rather than linear, for the same reason the
        /// mine damage is: the difference between a company and a division is
        /// real but a hundredfold cost would make any cache useless to anything
        /// bigger than a battalion.
        /// </summary>
        static double IssueCost(UnitActor unit)
        {
            float bulk = Mathf.Max(1f, EchelonInfo.ManpowerMultiplier(unit.State.EchelonEnum));
            return Mathf.Max(0.25f, Mathf.Sqrt(bulk));
        }

        static bool Rearm(UnitActor unit, float efficiency)
        {
            int establishment = unit.Def.ammoStock;
            if (establishment <= 0 || unit.State.ammo >= establishment) return false;

            int add = Mathf.Max(1, Mathf.RoundToInt(establishment * IssueShare * efficiency));
            unit.State.ammo = Mathf.Min(establishment, unit.State.ammo + add);
            return true;
        }

        static bool Refuel(UnitActor unit, float efficiency)
        {
            float establishment = unit.Def.fuelStock;
            if (establishment <= 0.01f || unit.State.fuel >= establishment) return false;

            float add = establishment * IssueShare * efficiency;
            unit.State.fuel = Mathf.Min(establishment, unit.State.fuel + add);
            return true;
        }

        /// <summary>
        /// One repair issue: deadlined equipment back on the road.
        ///
        /// **A formation that walks takes nothing and is charged nothing.** Six
        /// of the seven infantry types carry no fuel and therefore no equipment
        /// worth recovering (<see cref="UnitActor.HasEquipment"/>), so a rifle
        /// battalion parked on a workshop draws no issue and the workshop's
        /// stock does not move — which is the honest answer, and the one that
        /// stops a repair point being quietly consumed by the wrong customers.
        /// </summary>
        static bool Mend(UnitActor unit, float efficiency)
        {
            if (!unit.HasEquipment) return false;

            float before = Mathf.Clamp01(unit.State.serviceability);
            if (before >= 1f) return false;

            unit.State.serviceability = Mathf.Min(1f, before + RepairPerIssue * efficiency);
            return unit.State.serviceability > before + 0.0001f;
        }

        static bool Treat(UnitActor unit, float efficiency)
        {
            if (unit.State.strength >= MedicalCeiling) return false;

            float before = unit.State.strength;
            unit.State.strength = Mathf.Min(MedicalCeiling,
                before + MedicalRecoveryPerIssue * efficiency);
            // Through the actor rather than by writing the field alone: the
            // strength bar, the burning effect and the routed/steady status all
            // hang off a strength change, and setting the number behind their
            // backs would leave a formation at 60 % still drawn on fire.
            unit.RefreshAfterSupply();
            return unit.State.strength > before + 0.0001f;
        }

        /// <summary>
        /// Takes an emptied **airdropped cache** off the map.
        ///
        /// Only a dropped one. A cache is a pile of boxes and an empty pile of
        /// boxes is not a supply point; a depot that has issued its last
        /// establishment is still a depot, still the place the next convoy comes
        /// to, and still something the designer put there on purpose — removing
        /// it would be the game editing the scenario.
        /// </summary>
        void RemoveSpent()
        {
            List<LogisticsSite> spent = null;
            foreach (var site in _sites.Sites)
            {
                if (site == null || !site.Data.airdropped) continue;
                if (!site.Data.TracksStock || site.Data.stock > 0.0) continue;
                (spent ??= new List<LogisticsSite>()).Add(site);
            }
            if (spent == null) return;

            foreach (var site in spent)
            {
                string what = LogisticsCatalog.Get(site.Kind).name.ToLowerInvariant();
                _sites.Remove(site);
                if (site.Data.team == nameof(Team.User))
                    Flash?.Invoke($"Airdropped {what} exhausted.");
            }
        }

        float ScenarioSeconds() =>
            _clock != null ? (float)(_clock.Now - System.DateTime.MinValue).TotalSeconds
                           : Time.unscaledTime;

        /// <summary>
        /// Every living formation that could draw from this site right now —
        /// what the supply panel lists.
        ///
        /// "Could" rather than "will": a formation already full is in range and
        /// is shown, greyed by the panel, because the answer to "why is this
        /// cache not going down" is usually that everything near it is full.
        /// </summary>
        public List<UnitActor> UnitsInRange(LogisticsSite site)
        {
            var result = new List<UnitActor>();
            if (site == null) return result;

            float radiusKm = LogisticsCatalog.Get(site.Kind).serviceRadiusKm;
            foreach (var unit in UnitRegistry.All)
            {
                if (unit == null || !unit.IsAlive) continue;
                if (unit.State.team != site.Data.team) continue;
                double km = GeoUtils.DistanceKm(site.Data.latitude, site.Data.longitude,
                    unit.State.latitude, unit.State.longitude);
                if (km <= radiusKm) result.Add(unit);
            }
            return result;
        }

        /// <summary>Forgets who has drawn from what — a new scenario, or a new battle.</summary>
        public void Reset() => _lastDraw.Clear();
    }
}
