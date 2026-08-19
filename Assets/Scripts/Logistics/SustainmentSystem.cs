using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.Logistics
{
    /// <summary>
    /// What each side has in stock, what its deployed force burns through in a
    /// day, and therefore how long it can go on fighting.
    ///
    /// **Two halves, and only one of them is editable.** The *stocks* are a
    /// designer's decision and are typed in and saved with the map. The
    /// *consumption* is arithmetic over the units actually on the map — nobody
    /// types it, and it changes the moment a battalion is deployed, reinforced
    /// or destroyed. Keeping the second derived is what stops a scenario whose
    /// stated burn rate and whose order of battle describe two different armies.
    ///
    /// **Manpower is counted, not stocked.** A side's manpower stock is its pool
    /// of *replacements*; the number that matters day to day is how many people
    /// are standing on the map, which is <see cref="ManpowerOnField"/> — summed
    /// from each formation's establishment, scaled by echelon and by how much of
    /// it is left.
    ///
    /// The rates come from <see cref="ResourceCatalog"/>; this class only walks
    /// the registry and adds them up. See docs/27-SUSTAINMENT.md.
    /// </summary>
    public class SustainmentSystem : MonoBehaviour
    {
        /// <summary>Raised when a stock is edited, so the panel can repaint.</summary>
        public event System.Action Changed;

        /// <summary>Stocks, keyed by side and kind. Absent means zero.</summary>
        readonly Dictionary<Team, Dictionary<ResourceKind, double>> _stocks =
            new Dictionary<Team, Dictionary<ResourceKind, double>>();

        // ------------------------------------------------------------ stocks

        public double Stock(Team team, ResourceKind kind) =>
            _stocks.TryGetValue(team, out var side) && side.TryGetValue(kind, out double v) ? v : 0.0;

        public void SetStock(Team team, ResourceKind kind, double quantity)
        {
            if (!_stocks.TryGetValue(team, out var side))
            {
                side = new Dictionary<ResourceKind, double>();
                _stocks[team] = side;
            }
            side[kind] = System.Math.Max(0.0, quantity);
            Changed?.Invoke();
        }

        /// <summary>
        /// Fills a side's stocks with a starting establishment derived from what
        /// it has on the map: what the force carries, times a number of days.
        ///
        /// A scenario with every stock at zero reads as a force that is out of
        /// everything, which is almost never what a designer means and is
        /// tedious to correct one field at a time. This is the "sensible
        /// default" button, not a rule the game enforces.
        /// </summary>
        public void StockFromForce(Team team, float days)
        {
            foreach (var def in ResourceCatalog.All)
                SetStock(team, def.kind, DailyUse(team, def.kind) * days);
        }

        // ------------------------------------------------------- consumption

        /// <summary>
        /// What the side's deployed force consumes of one resource in a day.
        ///
        /// Walked per call rather than cached: it is a few dozen units and a
        /// handful of multiplications, the panel asks for it a few times a
        /// second at most, and a cache would have to be invalidated on every
        /// spawn, casualty and strength change — three chances to report a burn
        /// rate for a force that is no longer there.
        /// </summary>
        public double DailyUse(Team team, ResourceKind kind)
        {
            double total = 0.0;

            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive) continue;
                if (u.State.TeamEnum != team) continue;

                var def = u.Def;
                if (def == null) continue;

                float ech = EchelonInfo.ManpowerMultiplier(u.State.EchelonEnum);
                float strength = Mathf.Clamp01(u.State.strength);
                double people = def.manpower * ech * strength;

                switch (kind)
                {
                    case ResourceKind.Fuel:
                        // Vehicles by the kilometre they are assumed to cover;
                        // everything else by the generator, the cooker and the
                        // command post, which is a flat rate per head.
                        total += def.fuelUsePerKm > 0f
                            ? def.fuelUsePerKm * def.speedKmh * ResourceCatalog.MoveHoursPerDay * ech * strength
                            : people * ResourceCatalog.FuelPerPersonPerDay;
                        break;

                    case ResourceKind.LightAmmo:
                    case ResourceKind.TankAmmo:
                    case ResourceKind.ArtilleryAmmo:
                    case ResourceKind.AirDefenceMissiles:
                        if (AmmoKindOf(ResourceCatalog.AmmoClassOf(def)) != kind) break;
                        total += def.ammoStock * ResourceCatalog.AmmoLoadsPerDay * ech * strength;
                        break;

                    case ResourceKind.Manpower:
                        total += people / 1000.0 * ResourceCatalog.ReplacementsPerThousandPerDay;
                        break;

                    case ResourceKind.Rations:
                        total += people;      // one man-day each, by definition
                        break;

                    case ResourceKind.MedicalSupplies:
                        total += people / 1000.0 * ResourceCatalog.MedicalPerThousandPerDay;
                        break;

                    case ResourceKind.SpareParts:
                        // Only things with engines break down in a way spares fix.
                        if (def.fuelUsePerKm <= 0f) break;
                        total += ResourceCatalog.PartsPerCompanyPerDay * ech * strength;
                        break;
                }
            }

            return total;
        }

        static ResourceKind AmmoKindOf(AmmoClass ammo) => ammo switch
        {
            AmmoClass.Artillery => ResourceKind.ArtilleryAmmo,
            AmmoClass.Tank => ResourceKind.TankAmmo,
            AmmoClass.AirDefence => ResourceKind.AirDefenceMissiles,
            _ => ResourceKind.LightAmmo
        };

        /// <summary>
        /// How long the stock lasts at the current burn, in days.
        /// <see cref="float.PositiveInfinity"/> when nothing is consuming it —
        /// which is the honest answer, not an error.
        /// </summary>
        public double DaysOfSupply(Team team, ResourceKind kind)
        {
            double perDay = DailyUse(team, kind);
            if (perDay <= 0.0001) return double.PositiveInfinity;
            return Stock(team, kind) / perDay;
        }

        /// <summary>The resource that runs out first, and when. Kind is null when nothing is being spent.</summary>
        public (ResourceKind? kind, double days) BindingConstraint(Team team)
        {
            ResourceKind? worst = null;
            double least = double.PositiveInfinity;

            foreach (var def in ResourceCatalog.All)
            {
                double days = DaysOfSupply(team, def.kind);
                if (days >= least) continue;
                least = days;
                worst = def.kind;
            }

            return (worst, least);
        }

        // -------------------------------------------------------- head count

        /// <summary>
        /// People standing on the map for a side, right now: each formation's
        /// establishment at its echelon, scaled by how much of it is left.
        /// </summary>
        public int ManpowerOnField(Team team)
        {
            double total = 0.0;
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || u.State.TeamEnum != team) continue;
                if (u.Def == null) continue;
                total += u.Def.manpower
                       * EchelonInfo.ManpowerMultiplier(u.State.EchelonEnum)
                       * Mathf.Clamp01(u.State.strength);
            }
            return Mathf.RoundToInt((float)total);
        }

        /// <summary>Formations a side has on the map — the denominator for the head count.</summary>
        public int FormationsOnField(Team team)
        {
            int n = 0;
            foreach (var u in UnitRegistry.All)
                if (u != null && u.IsAlive && u.State.TeamEnum == team) n++;
            return n;
        }

        /// <summary>
        /// Establishment strength: what the side would have with every formation
        /// at full strength. The head count read against this is how much of the
        /// force is actually left.
        /// </summary>
        public int EstablishmentOnField(Team team)
        {
            double total = 0.0;
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || u.State.TeamEnum != team || u.Def == null) continue;
                total += u.Def.manpower * EchelonInfo.ManpowerMultiplier(u.State.EchelonEnum);
            }
            return Mathf.RoundToInt((float)total);
        }

        // ------------------------------------------------------------ saving

        public List<ResourceStockData> Serialize()
        {
            var result = new List<ResourceStockData>();
            foreach (var side in _stocks)
                foreach (var entry in side.Value)
                {
                    if (entry.Value <= 0.0) continue;      // zero is the default; no need to write it
                    result.Add(new ResourceStockData
                    {
                        team = side.Key.ToString(),
                        kind = entry.Key.ToString(),
                        quantity = entry.Value
                    });
                }
            return result;
        }

        public void LoadFrom(List<ResourceStockData> data)
        {
            _stocks.Clear();
            if (data != null)
                foreach (var d in data)
                {
                    if (d == null) continue;
                    var team = d.team == nameof(Team.Enemy) ? Team.Enemy : Team.User;
                    if (!_stocks.TryGetValue(team, out var side))
                    {
                        side = new Dictionary<ResourceKind, double>();
                        _stocks[team] = side;
                    }
                    side[ResourceCatalog.Parse(d.kind)] = d.quantity;
                }
            Changed?.Invoke();
        }

        public void Clear()
        {
            _stocks.Clear();
            Changed?.Invoke();
        }
    }
}
