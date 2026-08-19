using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;
using IronMeridian.Vfx;

namespace IronMeridian.Lines
{
    /// <summary>
    /// The three defensive tasks a selected unit can be given from the battle
    /// order bar (FM 3-90 ch.8 — defensive tasks vs security tasks). Each one
    /// is now **placed**: the player picks the ground, and the task is laid out
    /// around that point rather than around wherever the formation happened to
    /// be standing.
    ///
    ///  • **Defend** — prepare a position and fight from it. Lays a defence
    ///    line across the threat axis through the chosen ground, a battle
    ///    position enclosing the depth behind it, and pushes the commander's
    ///    subordinates out along the frontage so the line is manned instead of
    ///    merely drawn.
    ///  • **Hold** — retain the chosen ground. A ring about the point, and the
    ///    formation moves onto it and faces the threat.
    ///  • **Guard** — screen forward. Four sectors about the chosen ground, each
    ///    labelled, because a screen is responsibility divided up rather than a
    ///    place somebody stands.
    ///
    /// **The area graphics come from <see cref="TaskAreaSystem"/>**, which draws
    /// the ring, the line and the quadrants for every task in the game — so a
    /// defence, a recon and a rally point are read the same way. What stays here
    /// is what is specific to defending: the frontage arithmetic, the threat
    /// axis, and distributing subordinates along a line.
    ///
    /// Orientation always comes from the enemy: the threat axis is the bearing
    /// to the centre of the opposing force. With no enemy on the map the unit's
    /// own facing stands in, so the tasks still work while a scenario is being
    /// built up.
    /// </summary>
    public class DefenceOrderSystem : MonoBehaviour
    {
        /// <summary>Vertices along a defence line — enough to bow it without reading as a polygon.</summary>
        const int LinePoints = 9;
        /// <summary>Forward bow at the centre of the line, as a fraction of its frontage.</summary>
        const double BowFraction = 0.10;
        /// <summary>Depth of the battle position behind the line, as a fraction of frontage.</summary>
        const double DepthFraction = 0.45;
        /// <summary>Frontage floor and ceiling, km.</summary>
        const double MinFrontageKm = 1.5, MaxFrontageKm = 45.0;
        /// <summary>Radius searched for subordinates when a unit is not in a named group, km.</summary>
        const double SubordinateSearchKm = 12.0;
        /// <summary>Most subordinates one defence will try to place.</summary>
        const int MaxSubordinates = 12;
        /// <summary>Guard standoff floor and ceiling, km.</summary>
        const double MinGuardKm = 0.6, MaxGuardKm = 8.0;

        // Defence runs green-through-teal, deliberately nowhere near the attack
        // orange or the movement blue: a glance at a map full of orders should
        // separate "holding this" from "going there" before a label is read.
        static readonly Color DefendTint = new Color(0.45f, 0.85f, 0.62f);
        static readonly Color HoldTint = new Color(0.40f, 0.80f, 0.75f);
        static readonly Color GuardTint = new Color(0.55f, 0.88f, 0.55f);

        public System.Action<string> Flash;

        LineManager _lines;
        MarkerManager _markers;
        TaskAreaSystem _areas;

        public void Init(LineManager lines, MarkerManager markers, TaskAreaSystem areas)
        {
            _lines = lines; _markers = markers; _areas = areas;
        }

        // ------------------------------------------------------- Defend

        /// <summary>
        /// Prepares a defence on the chosen ground: the line across the threat
        /// axis, the battle position behind it, and the subordinates that man
        /// it. Returns false when there is nothing to defend with.
        /// </summary>
        public bool Defend(UnitActor commander, double lat, double lon)
        {
            if (!Ready(commander, "defend")) return false;

            var subordinates = Subordinates(commander);
            float threat = ThreatBearing(commander);
            var team = commander.State.TeamEnum;
            double frontageKm = FrontageKm(commander, subordinates.Count);
            double depthKm = frontageKm * DepthFraction;

            ClearFor(commander);

            // The line goes through the ground the player picked. It used to be
            // laid a fixed standoff ahead of wherever the commander happened to
            // be standing, which meant the order could not be *aimed* — the only
            // way to move a defence was to move the formation and give it again.
            var forwardEdge = ArcPoints(lat, lon, threat, frontageKm,
                frontageKm * BowFraction, LinePoints);

            string designation = Designation(commander);

            // The shared task area carries the label, the motes and the select
            // pulse; the two lines below are the doctrinal graphic underneath it.
            _areas?.Show(commander, TaskAreaShape.Line, MarkerKind.Defend, "DEFEND",
                lat, lon, frontageKm * 0.5, threat, DefendTint, VfxId.TaskAreaDefend);

            _lines.Upsert(new MapLineData
            {
                id = LineId(commander),
                kind = nameof(LineKind.DefensiveLine),
                team = team.ToString(),
                is3D = true,
                autoGenerated = true,
                points = forwardEdge,
                label = "DEFENCE LINE — " + designation.ToUpperInvariant(),
                echelon = EchelonInfo.Indicator(commander.State.EchelonEnum)
            });

            _lines.Upsert(new MapLineData
            {
                id = AreaId(commander),
                kind = nameof(LineKind.BattlePosition),
                team = team.ToString(),
                is3D = true,
                autoGenerated = true,
                points = AreaPoints(lat, lon, threat, frontageKm, depthKm, forwardEdge)
            });

            int placed = DistributeAlongLine(subordinates, lat, lon, threat, frontageKm);

            // The commander sits back inside the position rather than on the
            // line — it is directing the defence, not holding a slice of it.
            // With nobody to direct it takes the centre of its own line.
            if (subordinates.Count > 0)
            {
                GeoUtils.Destination(lat, lon, Reciprocal(threat), depthKm * 0.6,
                    out double cpLat, out double cpLon);
                Reposition(commander, cpLat, cpLon, threat);
            }
            else
            {
                Reposition(commander, lat, lon, threat);
            }

            Flash?.Invoke(placed > 0
                ? $"{designation} defends — {frontageKm:0.#} km frontage, {placed} subordinate unit(s) distributed."
                : $"{designation} defends — {frontageKm:0.#} km frontage (no subordinates to distribute).");
            return true;
        }

        // ------------------------------------------------------- Hold / Guard

        /// <summary>
        /// Puts the formation on the chosen ground and pins it there, inside a
        /// ring sized to what it can actually hold.
        /// </summary>
        public bool Hold(UnitActor unit, double lat, double lon)
        {
            if (!Ready(unit, "hold")) return false;

            float threat = ThreatBearing(unit);
            double radiusKm = HoldRadiusKm(unit);

            ClearFor(unit);
            _areas?.Show(unit, TaskAreaShape.Ring, MarkerKind.Hold, "HOLD",
                lat, lon, radiusKm, threat, HoldTint, VfxId.TaskAreaDefend);

            Reposition(unit, lat, lon, threat);
            unit.State.status = nameof(UnitStatus.Idle);

            Flash?.Invoke($"{Designation(unit)} holds a {radiusKm:0.#} km position, oriented {threat:000}°.");
            return true;
        }

        /// <summary>
        /// Screens the chosen ground: four sectors about the point, and the
        /// formation moves onto it facing the threat.
        /// </summary>
        public bool Guard(UnitActor unit, double lat, double lon)
        {
            if (!Ready(unit, "guard")) return false;

            float threat = ThreatBearing(unit);
            double radiusKm = GuardRadiusKm(unit);

            ClearFor(unit);
            _areas?.Show(unit, TaskAreaShape.Quadrants, MarkerKind.Guard, "GUARD",
                lat, lon, radiusKm, threat, GuardTint, VfxId.TaskAreaDefend);

            Reposition(unit, lat, lon, threat);

            Flash?.Invoke($"{Designation(unit)} guards a {radiusKm:0.#} km sector in four, oriented {threat:000}°.");
            return true;
        }

        bool Ready(UnitActor unit, string task)
        {
            if (unit != null && unit.IsAlive) return true;
            Flash?.Invoke($"Select a unit before ordering it to {task}.");
            return false;
        }

        /// <summary>Drops every graphic a previous defensive task left for this unit.</summary>
        public void ClearFor(UnitActor unit)
        {
            if (unit == null) return;
            _lines.RemoveAutoGenerated($"defence-{unit.State.instanceId}-");
            _markers.RemoveForUnit(unit.State.instanceId);
            _areas?.ClearFor(unit);
        }

        // ------------------------------------------------------- geometry

        /// <summary>
        /// Points along an arc perpendicular to <paramref name="axisDeg"/>,
        /// bowed forward at the centre. A straight line across a frontage reads
        /// as a boundary; a defence line curves toward what it faces.
        /// </summary>
        static List<GeoPoint> ArcPoints(double centreLat, double centreLon, float axisDeg,
            double frontageKm, double bowKm, int count)
        {
            double lateral = axisDeg + 90.0;
            var pts = new List<GeoPoint>(count);
            for (int i = 0; i < count; i++)
            {
                double t = count == 1 ? 0.0 : i / (double)(count - 1) - 0.5;   // -0.5 .. +0.5
                GeoUtils.Destination(centreLat, centreLon, lateral, t * frontageKm,
                    out double lat, out double lon);

                // Parabolic bow: full at the centre, nothing at the flanks.
                double bow = bowKm * (1.0 - 4.0 * t * t);
                if (System.Math.Abs(bow) > 1e-6)
                    GeoUtils.Destination(lat, lon, axisDeg, bow, out lat, out lon);

                pts.Add(new GeoPoint { latitude = lat, longitude = lon });
            }
            return pts;
        }

        /// <summary>
        /// Closed battle position: the defence line's own trace forward, a rear
        /// edge <paramref name="depthKm"/> behind it, and back to the start so
        /// the polygon closes.
        /// </summary>
        static List<GeoPoint> AreaPoints(double centreLat, double centreLon, float axisDeg,
            double frontageKm, double depthKm, List<GeoPoint> line)
        {
            var pts = new List<GeoPoint>(line.Count * 2 + 1);
            pts.AddRange(line);

            GeoUtils.Destination(centreLat, centreLon, axisDeg + 180.0, depthKm,
                out double rearLat, out double rearLon);
            var rear = ArcPoints(rearLat, rearLon, axisDeg, frontageKm * 0.92, -depthKm * 0.18, LinePoints);
            for (int i = rear.Count - 1; i >= 0; i--) pts.Add(rear[i]);

            pts.Add(new GeoPoint { latitude = pts[0].latitude, longitude = pts[0].longitude });
            return pts;
        }

        /// <summary>
        /// Spreads subordinates evenly across the frontage and marches each to
        /// its slot facing the threat. Assignment is by current lateral position,
        /// so units take the slot nearest them and columns do not cross.
        /// </summary>
        int DistributeAlongLine(List<UnitActor> units, double centreLat, double centreLon,
            float axisDeg, double frontageKm)
        {
            if (units.Count == 0) return 0;

            double lateralBearing = axisDeg + 90.0;
            units.Sort((a, b) => LateralOffsetKm(a, centreLat, centreLon, lateralBearing)
                .CompareTo(LateralOffsetKm(b, centreLat, centreLon, lateralBearing)));

            int placed = 0;
            for (int i = 0; i < units.Count; i++)
            {
                // Slot centres, so the flanks keep half a sub-sector of room.
                double t = (i + 0.5) / units.Count - 0.5;
                GeoUtils.Destination(centreLat, centreLon, lateralBearing, t * frontageKm,
                    out double lat, out double lon);

                if (Reposition(units[i], lat, lon, axisDeg)) placed++;
            }
            return placed;
        }

        /// <summary>
        /// Sends a unit to a point. In battle that is a routed march that leaves
        /// a trail; the same call outside battle would be a silent no-op, so the
        /// unit is placed directly instead — a defence laid out while planning
        /// should still lay itself out.
        /// </summary>
        static bool Reposition(UnitActor unit, double lat, double lon, float faceDeg)
        {
            if (unit == null || !unit.IsAlive) return false;
            if (unit.Mover.MoveTo(lat, lon, faceDeg)) return true;

            unit.SetPosition(lat, lon);
            unit.SetHeading(faceDeg);
            return true;
        }

        static double LateralOffsetKm(UnitActor u, double centreLat, double centreLon, double lateralBearing)
        {
            GeoUtils.ToLocalKm(centreLat, centreLon, u.State.latitude, u.State.longitude,
                out double east, out double north);
            double rad = lateralBearing * System.Math.PI / 180.0;
            // Bearings run clockwise from north, so the unit vector is (sin, cos).
            return east * System.Math.Sin(rad) + north * System.Math.Cos(rad);
        }

        // ------------------------------------------------------- inputs

        /// <summary>
        /// Bearing to the centre of the opposing force — what the defence is
        /// oriented on. Falls back to the unit's own facing when the other side
        /// has nothing on the map yet.
        /// </summary>
        public static float ThreatBearing(UnitActor unit)
        {
            var enemy = unit.State.TeamEnum == Team.User ? Team.Enemy : Team.User;
            double lat = 0, lon = 0;
            int n = 0;
            foreach (var e in UnitRegistry.OfTeam(enemy))
            {
                if (e == null || !e.IsAlive) continue;
                lat += e.State.latitude; lon += e.State.longitude; n++;
            }
            if (n == 0) return unit.State.headingDeg;
            return GeoUtils.BearingDeg(unit.State.latitude, unit.State.longitude, lat / n, lon / n);
        }

        /// <summary>
        /// The units this one commands: its group if it has one, otherwise the
        /// smaller friendly formations standing near it. Grouping is explicit in
        /// the editor, so it wins; proximity is the fallback that makes the order
        /// useful on a map nobody has grouped.
        /// </summary>
        static List<UnitActor> Subordinates(UnitActor commander)
        {
            var result = new List<UnitActor>();
            string group = commander.State.groupId;

            if (!string.IsNullOrEmpty(group))
            {
                foreach (var u in UnitRegistry.OfTeam(commander.State.TeamEnum))
                    if (u != null && u != commander && u.IsAlive && u.State.groupId == group)
                        result.Add(u);
                if (result.Count > 0) return Trim(result);
            }

            foreach (var u in UnitRegistry.OfTeam(commander.State.TeamEnum))
            {
                if (u == null || u == commander || !u.IsAlive) continue;
                if (u.State.EchelonEnum >= commander.State.EchelonEnum) continue;
                if (!string.IsNullOrEmpty(u.State.groupId) && u.State.groupId != group) continue;
                if (GeoUtils.DistanceKm(commander.State.latitude, commander.State.longitude,
                        u.State.latitude, u.State.longitude) > SubordinateSearchKm) continue;
                result.Add(u);
            }
            return Trim(result);
        }

        static List<UnitActor> Trim(List<UnitActor> units)
        {
            if (units.Count > MaxSubordinates) units.RemoveRange(MaxSubordinates, units.Count - MaxSubordinates);
            return units;
        }

        /// <summary>
        /// Frontage a formation defends. Scaled off echelon bulk rather than a
        /// per-echelon table so it stays sensible for any unit type, and widened
        /// a little for each subordinate that has to fit on the line.
        /// </summary>
        static double FrontageKm(UnitActor commander, int subordinates)
        {
            double bulk = Mathf.Max(1f, EchelonInfo.ManpowerMultiplier(commander.State.EchelonEnum));
            double km = 1.1 * System.Math.Sqrt(bulk) * System.Math.Sqrt(System.Math.Max(2, subordinates + 1));
            return System.Math.Min(MaxFrontageKm, System.Math.Max(MinFrontageKm, km));
        }

        /// <summary>Radius of the ground a formation can actually hold, km.</summary>
        static double HoldRadiusKm(UnitActor unit)
        {
            double bulk = Mathf.Max(1f, EchelonInfo.ManpowerMultiplier(unit.State.EchelonEnum));
            return System.Math.Min(MaxGuardKm, System.Math.Max(MinGuardKm, 0.5 * System.Math.Sqrt(bulk)));
        }

        /// <summary>
        /// Radius of a guard sector. Wider than a hold: a screen covers ground
        /// rather than occupying it, so the same formation is responsible for
        /// more of it and correspondingly thinner on all of it.
        /// </summary>
        static double GuardRadiusKm(UnitActor unit)
        {
            double bulk = Mathf.Max(1f, EchelonInfo.ManpowerMultiplier(unit.State.EchelonEnum));
            double km = 1.1 * System.Math.Sqrt(bulk);
            return System.Math.Min(MaxGuardKm * 2.0, System.Math.Max(MinGuardKm * 2.0, km));
        }

        static float Reciprocal(float bearing) => (bearing + 180f) % 360f;

        // The DEFEND marker itself is placed by TaskAreaSystem now, under the
        // task- prefix; these two are the doctrinal graphic that sits under it.
        static string LineId(UnitActor u) => $"defence-{u.State.instanceId}-line";
        static string AreaId(UnitActor u) => $"defence-{u.State.instanceId}-area";

        static string Designation(UnitActor u) =>
            string.IsNullOrEmpty(u.State.customName) ? u.Def.name : u.State.customName;
    }
}
