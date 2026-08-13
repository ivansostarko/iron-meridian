using System.Collections.Generic;
using CesiumForUnity;
using IronMeridian.Data;

namespace IronMeridian.Map
{
    /// <summary>
    /// Turns a "get from A to C" order into a road-like route: a short list of
    /// waypoints that keeps to gentle ground instead of driving straight over
    /// whatever is in the way. A unit then marches A → B → C, taking the bends
    /// at speed the way a vehicle column would, rather than sliding along a
    /// great circle through a hillside.
    ///
    /// **There is no road network to follow.** The map is Cesium terrain plus
    /// raster imagery — OpenStreetMap is drawn as pictures, not as vector ways,
    /// so no actual road centrelines exist to snap to. What is available is the
    /// ground itself, so the planner does what a road surveyor does: it prefers
    /// the flattest corridor and refuses grades no wheeled vehicle would climb.
    /// On real terrain that lands routes in valleys and along contours, which
    /// reads as a road even though none was consulted.
    ///
    /// Method: a corridor is laid between start and goal, divided into layers
    /// across its length and lanes across its width. Each step may move one
    /// layer forward and at most one lane sideways, which makes the search a
    /// layered DAG — a single sweep of dynamic programming, always terminating,
    /// with no open list to blow up. The winning lane sequence is then smoothed
    /// and stripped of redundant vertices so the result is a handful of legs.
    ///
    /// The trade-off of a forward-only corridor is that the planner can steer
    /// around an obstacle but cannot double back around one; a ridge that walls
    /// off the entire corridor falls back to the direct line rather than failing
    /// the order. Terrain sampling is physics raycasts, so planning is done once
    /// per move order, never per frame.
    ///
    /// Identical in 2D and 3D: the view mode is a camera choice, the terrain
    /// underneath is the same, so both views show the same route.
    /// </summary>
    public static class RoutePlanner
    {
        /// <summary>Below this a route is a straight line — planning would be noise.</summary>
        public const double MinPlanningKm = 0.45;
        /// <summary>Above this the corridor grid would be too coarse to mean anything.</summary>
        public const double MaxPlanningKm = 120.0;

        /// <summary>Steps along the corridor. Cost is one terrain sample per lane per layer.</summary>
        const int Layers = 18;
        /// <summary>Lanes across the corridor; odd so start and goal sit in the middle lane.</summary>
        const int Lanes = 9;
        /// <summary>Corridor half-width as a fraction of the direct distance.</summary>
        const double CorridorFraction = 0.30;
        /// <summary>Half-width floor, so short moves still have room to dodge, km.</summary>
        const double MinCorridorHalfKm = 0.35;

        /// <summary>Gradient no wheeled formation will take; steeper steps are impassable.</summary>
        const double MaxGrade = 0.25;
        /// <summary>How hard climbing is punished relative to distance (quadratic in grade).</summary>
        const double SlopePenalty = 14.0;
        /// <summary>Extra cost per lane change, in km — discourages a zig-zag through flat ground.</summary>
        const double LaneChangeKm = 0.05;
        /// <summary>Below this share of successful terrain samples the ground isn't streamed in yet.</summary>
        const double MinSampledFraction = 0.55;

        /// <summary>Vertices closer than this to the line between their neighbours are dropped, km.</summary>
        const double SimplifyToleranceKm = 0.09;

        /// <summary>
        /// Plans a route from one geodetic point to another. Always returns at
        /// least the two endpoints — a move order is never refused because the
        /// terrain would not co-operate.
        /// </summary>
        public static List<GeoPoint> Plan(CesiumGeoreference geo,
            double fromLat, double fromLon, double toLat, double toLon)
        {
            var direct = Direct(fromLat, fromLon, toLat, toLon);

            double totalKm = GeoUtils.DistanceKm(fromLat, fromLon, toLat, toLon);
            if (geo == null || totalKm < MinPlanningKm || totalKm > MaxPlanningKm) return direct;

            // Planar frame centred on the start: `along` points at the goal,
            // `across` is 90° left of it. Every grid coordinate below is in km
            // in this frame, which is why the maths stays free of latitude skew.
            GeoUtils.ToLocalKm(fromLat, fromLon, toLat, toLon, out double ge, out double gn);
            double len = System.Math.Sqrt(ggSq(ge, gn));
            if (len < 1e-6) return direct;

            double ax = ge / len, ay = gn / len;      // along
            double cx = -ay, cy = ax;                 // across

            double halfWidth = System.Math.Max(MinCorridorHalfKm, len * CorridorFraction);
            double laneStep = halfWidth * 2.0 / (Lanes - 1);
            double layerStep = len / Layers;

            // --- sample the corridor -----------------------------------------
            // One raycast per node. Nodes where the terrain has not streamed in
            // are marked unknown and cost nothing extra, so a partially loaded
            // map still yields a usable route instead of a refusal.
            var height = new double[Layers + 1, Lanes];
            var known = new bool[Layers + 1, Lanes];
            int sampled = 0;
            for (int i = 0; i <= Layers; i++)
                for (int j = 0; j < Lanes; j++)
                {
                    NodeGeo(fromLat, fromLon, ax, ay, cx, cy, i * layerStep, Offset(j, laneStep, halfWidth),
                        out double lat, out double lon);
                    if (GeoUtils.TrySampleTerrainHeight(geo, lat, lon, out double h))
                    {
                        height[i, j] = h; known[i, j] = true; sampled++;
                    }
                }

            if (sampled < (Layers + 1) * Lanes * MinSampledFraction) return direct;

            // --- forward sweep -------------------------------------------------
            int mid = Lanes / 2;
            var cost = new double[Layers + 1, Lanes];
            var from = new int[Layers + 1, Lanes];
            for (int j = 0; j < Lanes; j++) { cost[0, j] = double.PositiveInfinity; from[0, j] = -1; }
            cost[0, mid] = 0.0;

            for (int i = 0; i < Layers; i++)
                for (int k = 0; k < Lanes; k++)
                {
                    cost[i + 1, k] = double.PositiveInfinity;
                    from[i + 1, k] = -1;
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        int j = k - dj;
                        if (j < 0 || j >= Lanes) continue;
                        if (double.IsPositiveInfinity(cost[i, j])) continue;

                        double step = StepCost(height, known, i, j, k, layerStep, laneStep);
                        if (double.IsPositiveInfinity(step)) continue;

                        double c = cost[i, j] + step;
                        if (c < cost[i + 1, k]) { cost[i + 1, k] = c; from[i + 1, k] = j; }
                    }
                }

            // The goal is fixed in the middle lane; if the corridor is walled
            // off before reaching it there is nothing better than the direct line.
            if (double.IsPositiveInfinity(cost[Layers, mid])) return direct;

            // --- walk the choice back ------------------------------------------
            var lanes = new int[Layers + 1];
            lanes[Layers] = mid;
            for (int i = Layers; i > 0; i--)
            {
                lanes[i - 1] = from[i, lanes[i]];
                if (lanes[i - 1] < 0) return direct;    // defensive: broken chain
            }

            var raw = new List<GeoPoint>(Layers + 1);
            for (int i = 0; i <= Layers; i++)
            {
                NodeGeo(fromLat, fromLon, ax, ay, cx, cy, i * layerStep,
                    Offset(lanes[i], laneStep, halfWidth), out double lat, out double lon);
                raw.Add(new GeoPoint { latitude = lat, longitude = lon });
            }

            // Endpoints are the order, not an approximation of it.
            raw[0] = new GeoPoint { latitude = fromLat, longitude = fromLon };
            raw[raw.Count - 1] = new GeoPoint { latitude = toLat, longitude = toLon };

            var route = Simplify(Smooth(raw), SimplifyToleranceKm);
            return route.Count >= 2 ? route : direct;
        }

        static double ggSq(double a, double b) => a * a + b * b;

        static List<GeoPoint> Direct(double fromLat, double fromLon, double toLat, double toLon) =>
            new List<GeoPoint>
            {
                new GeoPoint { latitude = fromLat, longitude = fromLon },
                new GeoPoint { latitude = toLat,   longitude = toLon }
            };

        static double Offset(int lane, double laneStep, double halfWidth) => lane * laneStep - halfWidth;

        static void NodeGeo(double originLat, double originLon,
            double ax, double ay, double cx, double cy,
            double along, double across, out double lat, out double lon)
        {
            double east = ax * along + cx * across;
            double north = ay * along + cy * across;
            GeoUtils.FromLocalKm(originLat, originLon, east, north, out lat, out lon);
        }

        /// <summary>
        /// Cost of one corridor step: ground covered, punished by the square of
        /// the gradient, plus a nudge against needless lane changes. Anything
        /// steeper than <see cref="MaxGrade"/> is impassable, which is what
        /// makes routes bend around high ground rather than over it.
        /// </summary>
        static double StepCost(double[,] height, bool[,] known, int i, int j, int k,
            double layerStep, double laneStep)
        {
            double lateral = (k - j) * laneStep;
            double flatKm = System.Math.Sqrt(layerStep * layerStep + lateral * lateral);
            if (flatKm < 1e-9) return double.PositiveInfinity;

            double penalty = 0.0;
            if (known[i, j] && known[i + 1, k])
            {
                double grade = System.Math.Abs(height[i + 1, k] - height[i, j]) / (flatKm * 1000.0);
                if (grade > MaxGrade) return double.PositiveInfinity;
                penalty = SlopePenalty * grade * grade;
            }

            return flatKm * (1.0 + penalty) + (k == j ? 0.0 : LaneChangeKm);
        }

        /// <summary>Chaikin corner-cutting: a lane sequence is a staircase until it is smoothed.</summary>
        static List<GeoPoint> Smooth(List<GeoPoint> pts)
        {
            var current = pts;
            for (int it = 0; it < 2 && current.Count >= 3; it++)
            {
                var next = new List<GeoPoint> { current[0] };
                for (int i = 0; i < current.Count - 1; i++)
                {
                    next.Add(Lerp(current[i], current[i + 1], 0.25));
                    next.Add(Lerp(current[i], current[i + 1], 0.75));
                }
                next.Add(current[current.Count - 1]);
                current = next;
            }
            return current;
        }

        static GeoPoint Lerp(GeoPoint a, GeoPoint b, double t) => new GeoPoint
        {
            latitude = a.latitude + (b.latitude - a.latitude) * t,
            longitude = a.longitude + (b.longitude - a.longitude) * t
        };

        /// <summary>
        /// Ramer–Douglas–Peucker. A smoothed corridor path has dozens of nearly
        /// collinear vertices; the mover wants legs, and the trail wants a route
        /// a player can read, so anything that does not change the shape goes.
        /// </summary>
        static List<GeoPoint> Simplify(List<GeoPoint> pts, double toleranceKm)
        {
            if (pts.Count < 3) return pts;
            var keep = new bool[pts.Count];
            keep[0] = keep[pts.Count - 1] = true;
            SimplifyRange(pts, 0, pts.Count - 1, toleranceKm, keep);

            var result = new List<GeoPoint>();
            for (int i = 0; i < pts.Count; i++) if (keep[i]) result.Add(pts[i]);
            return result;
        }

        static void SimplifyRange(List<GeoPoint> pts, int first, int last, double toleranceKm, bool[] keep)
        {
            if (last <= first + 1) return;

            double origin0 = pts[first].latitude, origin1 = pts[first].longitude;
            GeoUtils.ToLocalKm(origin0, origin1, pts[last].latitude, pts[last].longitude,
                out double ex, out double ny);
            double segLen = System.Math.Sqrt(ggSq(ex, ny));

            int worst = -1;
            double worstDist = 0.0;
            for (int i = first + 1; i < last; i++)
            {
                GeoUtils.ToLocalKm(origin0, origin1, pts[i].latitude, pts[i].longitude,
                    out double px, out double py);
                double dist = segLen < 1e-9
                    ? System.Math.Sqrt(ggSq(px, py))
                    : System.Math.Abs(px * ny - py * ex) / segLen;
                if (dist > worstDist) { worstDist = dist; worst = i; }
            }

            if (worst < 0 || worstDist < toleranceKm) return;
            keep[worst] = true;
            SimplifyRange(pts, first, worst, toleranceKm, keep);
            SimplifyRange(pts, worst, last, toleranceKm, keep);
        }

        /// <summary>Total ground covered by a route, km — what fuel is charged against.</summary>
        public static double LengthKm(IReadOnlyList<GeoPoint> route)
        {
            double km = 0.0;
            for (int i = 0; i < route.Count - 1; i++)
                km += GeoUtils.DistanceKm(route[i].latitude, route[i].longitude,
                                          route[i + 1].latitude, route[i + 1].longitude);
            return km;
        }
    }
}
