using System;
using System.Collections.Generic;

namespace IronMeridian.Data
{
    /// <summary>
    /// The ground a mission is fought over: a closed polygon of geodetic
    /// vertices, drawn in the map editor's MISSIONS panel.
    ///
    /// **What it is for.** Three things at once, and they are the same thing:
    ///
    ///  • **It bounds the battle.** The camera cannot be walked out of it, so a
    ///    scenario laid out around one city is not played by scrolling to the
    ///    next country and back.
    ///  • **It bounds what is shown.** Ground outside the area is blacked out in
    ///    battle mode whatever the fog of war is doing — see
    ///    <see cref="Units.FogBlanket"/>. A mission is a piece of ground, and
    ///    the map either says which piece or it does not.
    ///  • **It bounds intelligence.** A formation outside the area is off the
    ///    battlefield, and is hidden rather than tracked as a contact — see
    ///    <see cref="Units.FogOfWarSystem"/>.
    ///
    /// **Empty means unbounded.** A mission with fewer than three vertices has
    /// no area, and everything above is switched off. That is the shipped state
    /// for every mission written before this existed, and it has to keep working.
    ///
    /// Point-in-polygon runs in plate carrée — the even-odd crossing test is
    /// exact in any consistent planar frame, and lon/lat is one over ground a
    /// battle is fought on. It would be wrong for an area straddling the
    /// antimeridian or a pole, neither of which a scenario can usefully cover.
    ///
    /// See docs/22-MISSIONS.md and docs/16-FOG-OF-WAR.md.
    /// </summary>
    [Serializable]
    public class MissionArea
    {
        /// <summary>Boundary vertices in order. Implicitly closed — the last joins the first.</summary>
        public List<GeoPoint> points = new List<GeoPoint>();

        /// <summary>
        /// Ground outside the area is drawn this dark in battle. Not fully
        /// black: the outline has to stay findable so a player who has walked
        /// the camera into the edge can see why it stopped.
        /// </summary>
        public const float OutsideOpacity = 0.97f;

        /// <summary>Margin the camera and the fog blanket keep past the boundary, km.</summary>
        public const float EdgeMarginKm = 1.5f;

        /// <summary>A polygon needs three corners before it is an area at all.</summary>
        public bool HasArea => points != null && points.Count >= 3;

        public int VertexCount => points?.Count ?? 0;

        public MissionArea Clone()
        {
            var copy = new MissionArea();
            if (points == null) return copy;
            foreach (var p in points)
                copy.points.Add(new GeoPoint
                {
                    latitude = p.latitude,
                    longitude = p.longitude,
                    heightMeters = p.heightMeters
                });
            return copy;
        }

        public void Clear() => points = new List<GeoPoint>();

        // ------------------------------------------------------------ tests

        /// <summary>
        /// Whether a point is inside. **True when there is no area** — an
        /// unbounded mission must not have every unit on it read as out of
        /// bounds, and every caller here is asking "is this allowed?".
        /// </summary>
        public bool Contains(double lat, double lon)
        {
            if (!HasArea) return true;

            bool inside = false;
            int n = points.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double yi = points[i].latitude, xi = points[i].longitude;
                double yj = points[j].latitude, xj = points[j].longitude;

                // Even-odd crossing: count the edges a ray due east from the
                // point passes through.
                if (yi > lat == yj > lat) continue;
                double x = (xj - xi) * (lat - yi) / (yj - yi) + xi;
                if (lon < x) inside = !inside;
            }
            return inside;
        }

        // ----------------------------------------------------------- extent

        public void Bounds(out double minLat, out double maxLat, out double minLon, out double maxLon)
        {
            minLat = minLon = double.MaxValue;
            maxLat = maxLon = double.MinValue;
            if (!HasArea) { minLat = maxLat = minLon = maxLon = 0.0; return; }

            foreach (var p in points)
            {
                if (p.latitude < minLat) minLat = p.latitude;
                if (p.latitude > maxLat) maxLat = p.latitude;
                if (p.longitude < minLon) minLon = p.longitude;
                if (p.longitude > maxLon) maxLon = p.longitude;
            }
        }

        /// <summary>Centre of the bounding box — where the camera and the fog blanket are laid.</summary>
        public void Centre(out double lat, out double lon)
        {
            Bounds(out double minLat, out double maxLat, out double minLon, out double maxLon);
            lat = (minLat + maxLat) * 0.5;
            lon = (minLon + maxLon) * 0.5;
        }

        /// <summary>Distance from the centre to the furthest vertex, km.</summary>
        public float RadiusKm()
        {
            if (!HasArea) return 0f;
            Centre(out double cLat, out double cLon);

            double max = 0.0;
            foreach (var p in points)
            {
                double d = Map.GeoUtils.DistanceKm(cLat, cLon, p.latitude, p.longitude);
                if (d > max) max = d;
            }
            return (float)max;
        }

        /// <summary>
        /// Enclosed area in km². The shoelace formula on the local east/north
        /// plane rather than on degrees — a degree of longitude is 111 km at the
        /// equator and 71 km at Lyon, and the figure is shown to a designer who
        /// is sizing a battlefield.
        /// </summary>
        public double AreaKm2()
        {
            if (!HasArea) return 0.0;
            Centre(out double cLat, out double cLon);

            double sum = 0.0;
            int n = points.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Map.GeoUtils.ToLocalKm(cLat, cLon, points[i].latitude, points[i].longitude,
                    out double xi, out double yi);
                Map.GeoUtils.ToLocalKm(cLat, cLon, points[j].latitude, points[j].longitude,
                    out double xj, out double yj);
                sum += xj * yi - xi * yj;
            }
            return Math.Abs(sum) * 0.5;
        }

        // ---------------------------------------------------------- clamping

        /// <summary>
        /// Pulls a point back inside, to the nearest place on the boundary when
        /// it has strayed out. Used to stop the camera from being walked off the
        /// mission's ground.
        ///
        /// Nearest-point-on-boundary rather than a snap to the centre: a camera
        /// that jumped to the middle of the map when it touched the edge would be
        /// unusable, where one that slides along the edge feels like a wall.
        /// </summary>
        public void Clamp(ref double lat, ref double lon)
        {
            if (!HasArea || Contains(lat, lon)) return;

            Centre(out double cLat, out double cLon);
            Map.GeoUtils.ToLocalKm(cLat, cLon, lat, lon, out double px, out double py);

            double bestX = px, bestY = py, best = double.MaxValue;
            int n = points.Count;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Map.GeoUtils.ToLocalKm(cLat, cLon, points[i].latitude, points[i].longitude,
                    out double ax, out double ay);
                Map.GeoUtils.ToLocalKm(cLat, cLon, points[j].latitude, points[j].longitude,
                    out double bx, out double by);

                double dx = bx - ax, dy = by - ay;
                double lenSq = dx * dx + dy * dy;
                double t = lenSq <= 1e-9 ? 0.0 : ((px - ax) * dx + (py - ay) * dy) / lenSq;
                t = Math.Max(0.0, Math.Min(1.0, t));

                double qx = ax + dx * t, qy = ay + dy * t;
                double d = (px - qx) * (px - qx) + (py - qy) * (py - qy);
                if (d >= best) continue;
                best = d; bestX = qx; bestY = qy;
            }

            Map.GeoUtils.FromLocalKm(cLat, cLon, bestX, bestY, out lat, out lon);
        }

        // -------------------------------------------------------- construction

        /// <summary>
        /// A rectangle centred on a point, in kilometres. The one-click option in
        /// the editor: most missions want "this much ground around here" and
        /// clicking four corners to say so is four chances to make a wonky box.
        /// </summary>
        public static MissionArea Rectangle(double centreLat, double centreLon,
            double halfWidthKm, double halfHeightKm)
        {
            var area = new MissionArea();
            (double e, double n)[] corners =
            {
                (-halfWidthKm, -halfHeightKm),
                ( halfWidthKm, -halfHeightKm),
                ( halfWidthKm,  halfHeightKm),
                (-halfWidthKm,  halfHeightKm)
            };

            foreach (var (e, n) in corners)
            {
                Map.GeoUtils.FromLocalKm(centreLat, centreLon, e, n, out double lat, out double lon);
                area.points.Add(new GeoPoint { latitude = lat, longitude = lon });
            }
            return area;
        }

        /// <summary>The boundary as a closed ring — the first vertex repeated at the end, for drawing.</summary>
        public List<GeoPoint> ClosedRing()
        {
            var ring = new List<GeoPoint>();
            if (!HasArea) return ring;

            foreach (var p in points)
                ring.Add(new GeoPoint { latitude = p.latitude, longitude = p.longitude });
            ring.Add(new GeoPoint { latitude = points[0].latitude, longitude = points[0].longitude });
            return ring;
        }
    }
}
