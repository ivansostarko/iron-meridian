using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// The reconnaissance task from the battle order bar: pick it,
    /// click a point on the map, and the unit goes and finds out what is there.
    ///
    /// Recon is what makes <see cref="FogOfWarSystem"/> a game rather than a
    /// blindfold. Every task registers a **sensor** — a detection footprint the
    /// fog reads alongside each unit's own eyes — and the tasks differ in where
    /// that footprint sits, how wide it is and how it gets there:
    ///
    ///  • **Recon Area** — the unit drives to the objective; the sensor switches
    ///    on when it arrives, and is the second-widest on offer.
    ///  • **Recon Route** — the sensor rides the unit the whole way, narrower,
    ///    because it is covering a line rather than a box.
    ///  • **Observe** — the unit does not move at all and sees furthest. Sitting
    ///    still on chosen ground is the best observation there is.
    ///  • **UAV Recon** — the unit stays put and the sensor flies: straight over
    ///    the terrain at its own speed, out to the objective and back, for a
    ///    fixed endurance.
    ///  • **Combat Patrol** — the unit shuttles between where it started and the
    ///    objective, scanning, and fights normally while it does.
    ///
    /// **Battle mode only**, like every other order. A task ends when the battle
    /// stops, when the unit dies, or when the player gives that unit another
    /// order; the sensor is removed with it, and whatever it was holding in view
    /// goes back under fog.
    /// </summary>
    public class ReconOrderSystem : MonoBehaviour
    {
        /// <summary>How close counts as "arrived", km.</summary>
        const double ArrivalKm = 0.25;
        /// <summary>
        /// Scenario seconds an arrived unit dwells on the objective before a
        /// patrol turns around. Three minutes: long enough to be a look at the
        /// ground rather than a touch of the waypoint, short enough that a
        /// patrol keeps patrolling. It was four seconds when the clock ran at
        /// sixty times real speed and four seconds meant four minutes.
        /// </summary>
        const float DwellSeconds = 180f;
        /// <summary>Sensor radius floor, so a short-sighted unit still reports something.</summary>
        const float MinSensorKm = 1.5f;

        public System.Action<string> Flash;

        CombatSystem _combat;
        FogOfWarSystem _fog;
        CesiumGeoreference _geo;
        readonly List<ReconOrder> _orders = new List<ReconOrder>();

        enum Leg { Outbound, OnStation, Inbound }

        class ReconOrder
        {
            public UnitActor unit;
            public ReconTaskDef def;
            public Leg leg;
            public double homeLat, homeLon;         // where the unit started
            public double objLat, objLon;           // what it was sent to look at
            public float sensorKm;
            public FogOfWarSystem.Sensor sensor;
            public AxisArrow arrow;
            public float dwellUntil;

            // Airborne sensors fly themselves; these track the flight.
            public double droneLat, droneLon;
            public float droneExpiry;
        }

        public void Init(CombatSystem combat, FogOfWarSystem fog, CesiumGeoreference geo)
        {
            _combat = combat;
            _fog = fog;
            _geo = geo;
            _combat.RunningChanged += running => { if (!running) CancelAll("Battle stopped — recon tasks cleared."); };
        }

        public bool HasOrder(UnitActor unit)
        {
            if (unit == null) return false;
            foreach (var o in _orders) if (o.unit == unit) return true;
            return false;
        }

        // ------------------------------------------------------- ordering

        /// <summary>Sends <paramref name="unit"/> to reconnoitre a point.</summary>
        public bool Order(UnitActor unit, double lat, double lon, ReconTask task)
        {
            if (!CombatSystem.BattleRunning)
            {
                Flash?.Invoke("Recon tasks need a running battle — press START BATTLE first.");
                return false;
            }
            if (unit == null || !unit.IsAlive)
            {
                Flash?.Invoke("Select a unit before ordering reconnaissance.");
                return false;
            }

            Cancel(unit);

            var def = ReconTaskCatalog.Get(task);
            var order = new ReconOrder
            {
                unit = unit,
                def = def,
                leg = Leg.Outbound,
                homeLat = unit.State.latitude,
                homeLon = unit.State.longitude,
                objLat = lat,
                objLon = lon,
                sensorKm = Mathf.Max(MinSensorKm, unit.Def.viewRangeKm * def.sensorRangeFactor)
            };

            // Observe has no objective to travel to: it watches from where it
            // stands, so its sensor is live immediately and centred on the unit.
            if (!def.moves && !def.airborne)
            {
                order.objLat = unit.State.latitude;
                order.objLon = unit.State.longitude;
                order.leg = Leg.OnStation;
                unit.SetHeading(GeoUtils.BearingDeg(unit.State.latitude, unit.State.longitude, lat, lon));
            }

            order.sensor = _fog.AddSensor(order.objLat, order.objLon, order.sensorKm, def.name);
            _orders.Add(order);

            if (def.airborne)
            {
                order.droneLat = unit.State.latitude;
                order.droneLon = unit.State.longitude;
                order.droneExpiry = Time.time + def.airborneEnduranceSeconds;
                order.sensor.latitude = order.droneLat;
                order.sensor.longitude = order.droneLon;
            }
            else if (def.moves && !unit.Mover.MoveTo(lat, lon))
            {
                Flash?.Invoke($"{Name(unit)} cannot reach that point — recon cancelled.");
                Cancel(unit);
                return false;
            }

            // Recon points at ground, so the axis arrow's far end is a fixed
            // objective rather than a formation.
            order.arrow = AxisArrow.CreateToPoint(_geo, unit, lat, lon, def.arrowTint);

            double km = GeoUtils.DistanceKm(order.homeLat, order.homeLon, lat, lon);
            Flash?.Invoke($"{Name(unit)} — {def.name}, {km:0.#} km out, {order.sensorKm:0.#} km sensor.");
            return true;
        }

        public void Cancel(UnitActor unit, string message = null)
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                if (_orders[i].unit != unit) continue;
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

        void Retire(ReconOrder order)
        {
            _fog.RemoveSensor(order.sensor);
            if (order.arrow != null) order.arrow.Finish();
            if (order.def.moves && order.unit != null && order.unit.Mover != null && order.unit.Mover.IsMoving)
                order.unit.Mover.Cancel();
        }

        // ------------------------------------------------------- running

        void Update()
        {
            for (int i = _orders.Count - 1; i >= 0; i--)
            {
                var order = _orders[i];
                if (order.unit == null || !order.unit.IsAlive || !CombatSystem.BattleRunning)
                {
                    Retire(order); _orders.RemoveAt(i); continue;
                }

                if (order.def.airborne) StepDrone(order, i);
                else StepGround(order);
            }
        }

        /// <summary>
        /// Ground tasks: the sensor either rides the unit or waits on the
        /// objective, and a patrol turns around at each end.
        /// </summary>
        void StepGround(ReconOrder order)
        {
            var s = order.unit.State;

            // Where the sensor is looking. A route recon or a patrol carries it;
            // an area recon leaves it on the objective until the unit gets there,
            // so the reported footprint is never somewhere nothing is looking.
            bool onStation = order.leg == Leg.OnStation;
            if (order.def.scansWhileMoving || onStation)
            {
                order.sensor.latitude = s.latitude;
                order.sensor.longitude = s.longitude;
            }

            if (!order.def.moves) return;

            if (order.unit.Mover.IsMoving) return;

            // Arrived at whichever end it was heading for.
            if (order.leg == Leg.Outbound || order.leg == Leg.Inbound)
            {
                order.leg = Leg.OnStation;
                order.dwellUntil = Time.time + DwellSeconds;
                order.sensor.latitude = s.latitude;
                order.sensor.longitude = s.longitude;
                if (order.arrow != null) { order.arrow.Finish(); order.arrow = null; }
                return;
            }

            if (!order.def.patrols || Time.time < order.dwellUntil) return;

            // A patrol shuttles: whichever end it is not at becomes the next one.
            bool atObjective = GeoUtils.DistanceKm(s.latitude, s.longitude, order.objLat, order.objLon) < ArrivalKm * 4;
            double nextLat = atObjective ? order.homeLat : order.objLat;
            double nextLon = atObjective ? order.homeLon : order.objLon;
            if (order.unit.Mover.MoveTo(nextLat, nextLon))
                order.leg = atObjective ? Leg.Inbound : Leg.Outbound;
        }

        /// <summary>
        /// The UAV. The sensor flies itself, straight and level, out to the
        /// objective and back — it is not routed over the terrain because it is
        /// not on the terrain.
        /// </summary>
        void StepDrone(ReconOrder order, int index)
        {
            if (Time.time >= order.droneExpiry)
            {
                Retire(order); _orders.RemoveAt(index);
                Flash?.Invoke($"{Name(order.unit)} — UAV recovered, sensor off station.");
                return;
            }

            double targetLat = order.leg == Leg.Inbound ? order.homeLat : order.objLat;
            double targetLon = order.leg == Leg.Inbound ? order.homeLon : order.objLon;

            double remainingKm = GeoUtils.DistanceKm(order.droneLat, order.droneLon, targetLat, targetLon);
            // Same rule as a ground march: the airframe's own km/h against the
            // scenario clock, so a UAV sortie takes as long as the airframe says
            // it does and speeding time up speeds the sortie up with everything
            // else. See UnitMover.
            double stepKm = order.def.airborneSpeedKmh * Core.GameClock.GameSecondsPerRealSecond
                            / 3600.0 * Time.deltaTime;

            if (remainingKm <= stepKm)
            {
                order.droneLat = targetLat;
                order.droneLon = targetLon;
                if (order.leg != Leg.Inbound)
                {
                    order.leg = Leg.Inbound;
                    if (order.arrow != null) { order.arrow.Finish(); order.arrow = null; }
                }
            }
            else
            {
                float bearing = GeoUtils.BearingDeg(order.droneLat, order.droneLon, targetLat, targetLon);
                GeoUtils.Destination(order.droneLat, order.droneLon, bearing, stepKm,
                    out order.droneLat, out order.droneLon);
            }

            order.sensor.latitude = order.droneLat;
            order.sensor.longitude = order.droneLon;
        }

        static string Name(UnitActor u) =>
            u == null ? "unit"
            : string.IsNullOrEmpty(u.State.customName) ? u.Def.name : u.State.customName;
    }
}
