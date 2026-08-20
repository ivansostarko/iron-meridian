using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Save
{
    /// <summary>
    /// Gives a mission the two things it cannot be played properly without and
    /// which nobody remembers to set: **a boundary and two headquarters.**
    ///
    /// **Why seed them rather than leave them empty.** Both are optional in the
    /// record and both read harmlessly as "not set", which is exactly the
    /// problem — nothing breaks, so nothing gets fixed, and the result is a
    /// campaign of missions that are all subtly worse than they should be:
    ///
    /// • **No boundary** means the camera can be walked to the next country, the
    ///   fog blanket has no edge to draw, and a formation that wanders off is
    ///   still on the battlefield. A scenario is a piece of ground; a mission
    ///   with no area does not say which piece.
    /// • **No headquarters** means neither side has a place its command post is,
    ///   so nothing can be ordered against one and the map cannot say what the
    ///   fight is *for*.
    ///
    /// A seeded value is a starting point, not an answer. The designer moves it
    /// afterwards from the MISSIONS and ZONES panels, and the seeder never
    /// overwrites anything already placed unless it is explicitly asked to
    /// (<paramref name="force"/>).
    ///
    /// **Everything is derived from the order of battle when there is one.** The
    /// units on the map already say where the fight is and which way it faces;
    /// asking the designer to restate that in a boundary and two pins is asking
    /// them to type out something the file already knows. Only an empty map
    /// falls back to a default box round the mission's start point.
    ///
    /// See docs/22-MISSIONS.md.
    /// </summary>
    public static class MissionSeeder
    {
        /// <summary>
        /// Half-width of the boundary laid on a mission with nothing on its map,
        /// km. Twenty kilometres across: a brigade sector, which is the scale
        /// this game is played at and the size the panel's own first preset is.
        /// </summary>
        public const float EmptyMapHalfKm = 10f;

        /// <summary>
        /// Clear ground kept between the outermost formation and the boundary,
        /// km. A boundary drawn through the units would put half the order of
        /// battle out of bounds the moment the mission opened.
        /// </summary>
        public const float UnitMarginKm = 6f;

        /// <summary>
        /// Smallest boundary the seeder will lay, half-width in km. A scenario
        /// whose units all sit within a few hundred metres of one another is a
        /// test map, and boxing it that tightly would leave nowhere to fight.
        /// </summary>
        public const float MinHalfKm = 5f;

        /// <summary>
        /// How far behind its own force a seeded headquarters sits, as a
        /// fraction of the distance between the two sides' centres of mass.
        ///
        /// A quarter: far enough back to read as a command post rather than as
        /// another rifle company, near enough that it is inside the boundary and
        /// on ground the player will actually see. Real depth is a decision, and
        /// the designer makes it by dragging the pin.
        /// </summary>
        const double HqSetbackFraction = 0.25;

        /// <summary>Floor and ceiling on that setback, km — a sanity clamp on a derived figure.</summary>
        const double MinSetbackKm = 2.0, MaxSetbackKm = 25.0;

        /// <summary>What one pass of the seeder actually did, for the report line.</summary>
        public struct Result
        {
            public bool areaSeeded;
            public bool friendlyHqSeeded;
            public bool enemyHqSeeded;

            public bool AnythingSeeded => areaSeeded || friendlyHqSeeded || enemyHqSeeded;

            public string Report()
            {
                if (!AnythingSeeded) return "";
                var parts = new List<string>(3);
                if (areaSeeded) parts.Add("boundary");
                if (friendlyHqSeeded && enemyHqSeeded) parts.Add("both HQs");
                else if (friendlyHqSeeded) parts.Add("friendly HQ");
                else if (enemyHqSeeded) parts.Add("enemy HQ");
                return "seeded " + string.Join(" + ", parts);
            }
        }

        /// <summary>
        /// Fills in whatever the mission is missing.
        ///
        /// <paramref name="data"/> is the mission's map save and may be null —
        /// a mission whose map has never been written still gets a boundary and
        /// two headquarters round its start point, which is the whole point of
        /// doing this automatically.
        /// </summary>
        public static Result Seed(MissionDefinition mission, MapSaveData data, bool force = false)
        {
            var result = default(Result);
            if (mission == null) return result;

            if (mission.area == null) mission.area = new MissionArea();
            if (mission.friendlyHq == null) mission.friendlyHq = new MissionZone();
            if (mission.enemyHq == null) mission.enemyHq = new MissionZone();

            Centre(data, Team.User, out bool haveBlue, out double blueLat, out double blueLon);
            Centre(data, Team.Enemy, out bool haveRed, out double redLat, out double redLon);

            if (force || !mission.area.HasArea)
            {
                mission.area.points = BuildBoundary(mission, data);
                result.areaSeeded = mission.area.HasArea;
            }

            if (force || !mission.friendlyHq.placed)
                result.friendlyHqSeeded = SeedHq(mission.friendlyHq, mission,
                    haveBlue, blueLat, blueLon, haveRed, redLat, redLon, friendly: true);

            if (force || !mission.enemyHq.placed)
                result.enemyHqSeeded = SeedHq(mission.enemyHq, mission,
                    haveRed, redLat, redLon, haveBlue, blueLat, blueLon, friendly: false);

            return result;
        }

        // ----------------------------------------------------------- boundary

        /// <summary>
        /// A rectangle round whatever the mission actually consists of: the
        /// order of battle when there is one, the start point when there is not.
        ///
        /// A rectangle rather than a hull of the units. A boundary is a
        /// *statement about the scenario* — the ground the fight is allowed to
        /// happen on — and a tight polygon round the opening laydown would
        /// forbid the manoeuvre the scenario exists to permit. The box plus its
        /// margin leaves both sides room to go somewhere.
        /// </summary>
        static List<GeoPoint> BuildBoundary(MissionDefinition mission, MapSaveData data)
        {
            double centreLat = mission.latitude, centreLon = mission.longitude;
            double halfKm = EmptyMapHalfKm;

            if (data != null && data.units != null && data.units.Count > 0)
            {
                double minLat = double.MaxValue, maxLat = double.MinValue;
                double minLon = double.MaxValue, maxLon = double.MinValue;
                foreach (var u in data.units)
                {
                    if (u == null) continue;
                    if (u.latitude < minLat) minLat = u.latitude;
                    if (u.latitude > maxLat) maxLat = u.latitude;
                    if (u.longitude < minLon) minLon = u.longitude;
                    if (u.longitude > maxLon) maxLon = u.longitude;
                }

                if (minLat <= maxLat)
                {
                    centreLat = (minLat + maxLat) * 0.5;
                    centreLon = (minLon + maxLon) * 0.5;

                    // Half the longer side of the laydown, plus clear ground.
                    // The longer side, so a front line strung out east-west is
                    // not boxed in along its own axis.
                    double spanNsKm = GeoUtils.DistanceKm(minLat, centreLon, maxLat, centreLon);
                    double spanEwKm = GeoUtils.DistanceKm(centreLat, minLon, centreLat, maxLon);
                    halfKm = System.Math.Max(spanNsKm, spanEwKm) * 0.5 + UnitMarginKm;
                }
            }

            halfKm = System.Math.Max(halfKm, MinHalfKm);
            return Box(centreLat, centreLon, halfKm);
        }

        /// <summary>
        /// A four-corner box, in the order a polygon wants them. Corners are
        /// found by travelling along the two cardinal bearings rather than by
        /// adding degrees, so the box is the same width in kilometres at any
        /// latitude — which a naive lat/lon offset is not.
        /// </summary>
        public static List<GeoPoint> Box(double lat, double lon, double halfKm)
        {
            GeoUtils.Destination(lat, lon, 0, halfKm, out double northLat, out _);
            GeoUtils.Destination(lat, lon, 180, halfKm, out double southLat, out _);
            GeoUtils.Destination(lat, lon, 90, halfKm, out _, out double eastLon);
            GeoUtils.Destination(lat, lon, 270, halfKm, out _, out double westLon);

            return new List<GeoPoint>
            {
                new GeoPoint { latitude = northLat, longitude = westLon },
                new GeoPoint { latitude = northLat, longitude = eastLon },
                new GeoPoint { latitude = southLat, longitude = eastLon },
                new GeoPoint { latitude = southLat, longitude = westLon }
            };
        }

        // ---------------------------------------------------------------- HQs

        /// <summary>
        /// Puts one side's headquarters behind that side's own force.
        ///
        /// "Behind" is defined by the enemy: the bearing from the other side's
        /// centre of mass to this one, continued past it. That is the only
        /// direction on the map that means anything without a compass rose —
        /// rear is away from the people shooting at you — and it falls out of
        /// the laydown for free.
        ///
        /// With only one side on the map there is no such bearing, so the two
        /// HQs are put on opposite sides of the mission's start point instead:
        /// friendly south, enemy north. An arbitrary convention, but a *stated*
        /// one, and it puts two pins somewhere sensible on a map that has told
        /// us nothing.
        /// </summary>
        static bool SeedHq(MissionZone zone, MissionDefinition mission,
            bool haveOwn, double ownLat, double ownLon,
            bool haveOther, double otherLat, double otherLon, bool friendly)
        {
            double lat, lon;

            if (haveOwn && haveOther)
            {
                double separationKm = GeoUtils.DistanceKm(otherLat, otherLon, ownLat, ownLon);
                double setback = Mathf.Clamp((float)(separationKm * HqSetbackFraction),
                    (float)MinSetbackKm, (float)MaxSetbackKm);
                float rearward = GeoUtils.BearingDeg(otherLat, otherLon, ownLat, ownLon);
                GeoUtils.Destination(ownLat, ownLon, rearward, setback, out lat, out lon);
            }
            else if (haveOwn)
            {
                // One side only: still put it behind that force, using the
                // stated convention for which way "behind" runs.
                GeoUtils.Destination(ownLat, ownLon, friendly ? 180 : 0, MinSetbackKm * 2.0,
                    out lat, out lon);
            }
            else
            {
                GeoUtils.Destination(mission.latitude, mission.longitude,
                    friendly ? 180 : 0, EmptyMapHalfKm * 0.6, out lat, out lon);
            }

            // Never outside the mission's own boundary: an HQ off the map is a
            // headquarters the player can neither see nor reach, which is worse
            // than one that has not been placed.
            if (mission.area != null && mission.area.HasArea && !mission.area.Contains(lat, lon))
            {
                mission.area.Centre(out double areaLat, out double areaLon);
                GeoUtils.Destination(areaLat, areaLon, friendly ? 180 : 0,
                    MinSetbackKm, out lat, out lon);
            }

            zone.placed = true;
            zone.latitude = lat;
            zone.longitude = lon;
            return true;
        }

        /// <summary>Centre of mass of one side's formations on a map save.</summary>
        static void Centre(MapSaveData data, Team team, out bool found, out double lat, out double lon)
        {
            found = false; lat = 0; lon = 0;
            if (data == null || data.units == null) return;

            int n = 0;
            string want = team.ToString();
            foreach (var u in data.units)
            {
                if (u == null || u.team != want) continue;
                lat += u.latitude; lon += u.longitude; n++;
            }
            if (n == 0) return;

            lat /= n; lon /= n;
            found = true;
        }
    }
}
