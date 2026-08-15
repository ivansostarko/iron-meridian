using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Data;
using IronMeridian.Lines;
using IronMeridian.Map;
using IronMeridian.Vfx;

namespace IronMeridian.Units
{
    /// <summary>
    /// The ground a task has been given, drawn on the map.
    ///
    /// **Why one system for all of them.** Defend, hold, guard, recon, attack,
    /// withdraw and retreat all end the same way: the player picks a place, and
    /// the map has to show what was asked for, where, by whom, and how big.
    /// Before this, three of those drew something and four drew nothing, and the
    /// three that did each built their own graphic. One system means a new task
    /// gets a proper area by naming a shape, and every area on the map is read
    /// the same way.
    ///
    /// **Three shapes, and they answer three different questions** (see
    /// <see cref="TaskAreaShape"/>):
    ///
    ///  • **Ring** — *how far from here.* A circle about the point, its radius
    ///    called out on the rim. Hold, attack, retreat and the three plain moves.
    ///  • **Line** — *which line do I hold.* A bowed trace across the threat
    ///    axis with the task's name along it. Defend and withdraw.
    ///  • **Quadrants** — *which ground do I cover.* Four sectors about the
    ///    point, each labelled on its own border, which is what a screening or
    ///    searching task actually is: responsibility divided up and allocated.
    ///    Guard and recon.
    ///
    /// **Everything drawn here is ordinary map data.** Lines go through
    /// <see cref="LineManager"/> and markers through <see cref="MarkerManager"/>,
    /// so a task area survives a save/load like anything else. Ids are prefixed
    /// <c>task-&lt;unit&gt;-</c>, which keeps them clear of the <c>sector-</c>
    /// set that "clear tactical graphics" regenerates and gives
    /// <see cref="ClearFor"/> one prefix to sweep.
    ///
    /// **The particles are attached to the area, not played at it.** A one-shot
    /// puff says something happened; a task area is a standing state, so the
    /// motes loop for as long as the order does and stop when it is cancelled.
    ///
    /// See docs/15-COMBAT-ORDERS.md.
    /// </summary>
    public class TaskAreaSystem : MonoBehaviour
    {
        /// <summary>Vertices around a ring. 40 is smooth at every zoom the map is read at.</summary>
        const int RingPoints = 40;
        /// <summary>Vertices along one quadrant's outer arc.</summary>
        const int QuadrantArcPoints = 10;
        /// <summary>Vertices along a task line, enough to bow it without reading as a polygon.</summary>
        const int LinePoints = 9;
        /// <summary>Forward bow at the centre of a task line, as a fraction of its frontage.</summary>
        const double BowFraction = 0.10;

        /// <summary>Line width, in metres, at rest and while the owning unit is selected.</summary>
        const float RestWidthM = 45f, SelectedWidthM = 110f;

        /// <summary>
        /// Seconds the reveal takes when an area is placed. Short — this is a
        /// confirmation that the order landed, not a cutscene.
        /// </summary>
        const float RevealSeconds = 0.45f;

        public System.Action<string> Flash;

        LineManager _lines;
        MarkerManager _markers;
        CesiumGeoreference _geo;

        /// <summary>One live task area: its lines, its motes and its 3D volume.</summary>
        class Area
        {
            public string unitId;
            public string prefix;
            public TaskAreaShape shape;
            public Color tint;
            public double lat, lon, radiusKm;
            public readonly List<string> lineIds = new List<string>();
            public VfxInstance motes;
            public TargetAreaMarker volume;
            public bool selected;
            /// <summary>Seconds into the placement reveal; ≥ RevealSeconds once finished.</summary>
            public float reveal;
        }

        readonly Dictionary<string, Area> _areas = new Dictionary<string, Area>();

        public void Init(LineManager lines, MarkerManager markers, CesiumGeoreference geo)
        {
            _lines = lines; _markers = markers; _geo = geo;
        }

        // --------------------------------------------------------- placement

        /// <summary>
        /// Draws (or replaces) the area for one task.
        ///
        /// <paramref name="axisDeg"/> orients the shapes that have a front — a
        /// line is laid across it and the quadrants are indexed from it — and is
        /// ignored by a ring, which has no front by definition.
        /// </summary>
        public void Show(UnitActor owner, TaskAreaShape shape, MarkerKind marker,
            string caption, double lat, double lon, double radiusKm, float axisDeg,
            Color tint, VfxId motes)
        {
            if (owner == null || _lines == null) return;

            ClearFor(owner);

            var area = new Area
            {
                unitId = owner.State.instanceId,
                prefix = Prefix(owner),
                shape = shape,
                tint = tint,
                lat = lat,
                lon = lon,
                radiusKm = System.Math.Max(0.1, radiusKm)
            };

            string team = owner.State.TeamEnum.ToString();
            string designation = Designation(owner);
            string hex = "#" + ColorUtility.ToHtmlStringRGB(tint);

            switch (shape)
            {
                case TaskAreaShape.Ring: BuildRing(area, team, hex, caption, designation); break;
                case TaskAreaShape.Line: BuildLine(area, team, hex, caption, designation, axisDeg); break;
                default: BuildQuadrants(area, team, hex, caption, designation, axisDeg); break;
            }

            _markers.Set(new MapMarkerData
            {
                id = area.prefix + "marker",
                kind = marker.ToString(),
                team = team,
                unitId = owner.State.instanceId,
                label = $"{caption}\n{designation}",
                latitude = lat,
                longitude = lon,
                headingDeg = axisDeg
            });

            // The volume is what makes the area read in 3D rather than as a
            // decal on the terrain, and it is what carries the reveal — see
            // Update. Ring-shaped tasks get one sized to the ring; the other two
            // get a small one at the centre, because a cylinder over a defence
            // line would be claiming ground the line does not.
            float volumeM = shape == TaskAreaShape.Ring
                ? (float)(area.radiusKm * 1000.0)
                : (float)(area.radiusKm * 250.0);
            area.volume = TargetAreaMarker.Create(_geo, volumeM, tint);
            area.volume.MoveTo(lat, lon);
            area.volume.SetAlarm(0f);

            area.motes = VfxSystem.Play(motes, lat, lon,
                Mathf.Clamp((float)area.radiusKm * 0.35f, 0.5f, 3f));

            _areas[area.unitId] = area;
            ApplyWidth(area);
        }

        /// <summary>Drops every graphic a task left for this unit.</summary>
        public void ClearFor(UnitActor owner)
        {
            if (owner == null) return;
            ClearFor(owner.State.instanceId);
        }

        public void ClearFor(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return;

            if (_areas.TryGetValue(unitId, out var area))
            {
                if (area.motes != null) area.motes.Stop();
                if (area.volume != null) Destroy(area.volume.gameObject);
                _areas.Remove(unitId);
            }

            _lines?.RemoveAutoGenerated($"task-{unitId}-");
            _markers?.RemoveForUnit(unitId);
        }

        /// <summary>Drops every task area on the map. Used when a scenario is reloaded.</summary>
        public void ClearAll()
        {
            foreach (var id in new List<string>(_areas.Keys)) ClearFor(id);
        }

        /// <summary>
        /// Marks which units are selected, so their areas stand out from the
        /// rest. The whole map can be carrying orders at once; without this a
        /// screen of overlapping areas says nothing about which one belongs to
        /// the formation being commanded.
        /// </summary>
        public void SetSelection(IReadOnlyList<UnitActor> selection)
        {
            var ids = new HashSet<string>();
            if (selection != null)
                foreach (var u in selection)
                    if (u != null) ids.Add(u.State.instanceId);

            foreach (var area in _areas.Values)
            {
                bool on = ids.Contains(area.unitId);
                if (on == area.selected) continue;
                area.selected = on;
                ApplyWidth(area);
                // Replaying the reveal on selection is what makes the area
                // *answer* the click: the same animation that said "this is
                // where I sent you" says "this is the one you are looking at".
                if (on) area.reveal = 0f;
            }
        }

        // ------------------------------------------------------------ shapes

        void BuildRing(Area area, string team, string hex, string caption, string designation)
        {
            AddLine(area, "ring", LineKind.BattlePosition, team, hex,
                $"{caption} — {designation}  ·  {area.radiusKm:0.#} km",
                CirclePoints(area.lat, area.lon, area.radiusKm, RingPoints, close: true));
        }

        void BuildLine(Area area, string team, string hex, string caption, string designation, float axisDeg)
        {
            // The line runs *across* the axis: a defence line faces the threat,
            // it does not point at it.
            var trace = Arc(area.lat, area.lon, axisDeg, area.radiusKm * 2.0,
                area.radiusKm * 2.0 * BowFraction, LinePoints);

            AddLine(area, "line", LineKind.Feba, team, hex,
                $"{caption} — {designation}", trace);
        }

        /// <summary>
        /// Four sectors about the point, each a wedge of the circle with its own
        /// label on its outer border. The quadrants are indexed from the axis,
        /// so sector 1 is always the one facing the threat — which is what makes
        /// "the north-east quadrant" a thing two people can agree on.
        /// </summary>
        void BuildQuadrants(Area area, string team, string hex, string caption,
            string designation, float axisDeg)
        {
            for (int q = 0; q < 4; q++)
            {
                double from = axisDeg - 45.0 + q * 90.0;
                var pts = new List<GeoPoint>(QuadrantArcPoints + 3)
                {
                    new GeoPoint { latitude = area.lat, longitude = area.lon }
                };

                for (int i = 0; i < QuadrantArcPoints; i++)
                {
                    double bearing = from + 90.0 * i / (QuadrantArcPoints - 1);
                    GeoUtils.Destination(area.lat, area.lon, bearing, area.radiusKm,
                        out double lat, out double lon);
                    pts.Add(new GeoPoint { latitude = lat, longitude = lon });
                }
                pts.Add(new GeoPoint { latitude = area.lat, longitude = area.lon });

                AddLine(area, "q" + q, LineKind.BattlePosition, team, hex,
                    $"{caption} {q + 1}  ·  {designation}", pts);
            }
        }

        void AddLine(Area area, string suffix, LineKind kind, string team, string hex,
            string label, List<GeoPoint> points)
        {
            string id = area.prefix + suffix;
            _lines.Upsert(new MapLineData
            {
                id = id,
                kind = kind.ToString(),
                team = team,
                is3D = true,
                autoGenerated = true,
                points = points,
                label = label,
                colorHex = hex,
                widthMeters = RestWidthM
            });
            area.lineIds.Add(id);
        }

        // ---------------------------------------------------------- geometry

        static List<GeoPoint> CirclePoints(double lat, double lon, double radiusKm, int count, bool close)
        {
            var pts = new List<GeoPoint>(count + 1);
            for (int i = 0; i < count; i++)
            {
                GeoUtils.Destination(lat, lon, 360.0 * i / count, radiusKm,
                    out double pLat, out double pLon);
                pts.Add(new GeoPoint { latitude = pLat, longitude = pLon });
            }
            if (close && pts.Count > 0)
                pts.Add(new GeoPoint { latitude = pts[0].latitude, longitude = pts[0].longitude });
            return pts;
        }

        /// <summary>
        /// Points along an arc perpendicular to <paramref name="axisDeg"/>,
        /// bowed forward at the centre. A straight line across a frontage reads
        /// as a boundary; a task line curves toward what it faces.
        /// </summary>
        static List<GeoPoint> Arc(double centreLat, double centreLon, float axisDeg,
            double frontageKm, double bowKm, int count)
        {
            double lateral = axisDeg + 90.0;
            var pts = new List<GeoPoint>(count);
            for (int i = 0; i < count; i++)
            {
                double t = count == 1 ? 0.0 : i / (double)(count - 1) - 0.5;
                GeoUtils.Destination(centreLat, centreLon, lateral, t * frontageKm,
                    out double lat, out double lon);

                double bow = bowKm * (1.0 - 4.0 * t * t);
                if (System.Math.Abs(bow) > 1e-6)
                    GeoUtils.Destination(lat, lon, axisDeg, bow, out lat, out lon);

                pts.Add(new GeoPoint { latitude = lat, longitude = lon });
            }
            return pts;
        }

        // --------------------------------------------------------- animation

        /// <summary>
        /// Runs the reveal on every area that is still playing one.
        ///
        /// The animation is carried by the 3D volume's alarm channel — the same
        /// escalation a called strike's target area uses — rather than by
        /// rewriting the lines. Line width and colour go through
        /// <see cref="MapLine.RefreshStyle"/>, which rebuilds the polyline's
        /// geometry; doing that sixty times a second for every order on the map
        /// would be a rebuild storm for an effect nobody would thank us for.
        /// </summary>
        void Update()
        {
            foreach (var area in _areas.Values)
            {
                if (area.reveal >= RevealSeconds) continue;

                area.reveal += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(area.reveal / RevealSeconds);

                // Peaks in the middle and settles: a swell, not a flash.
                if (area.volume != null)
                    area.volume.SetAlarm(Mathf.Sin(t * Mathf.PI) * (area.selected ? 0.85f : 0.55f));
            }
        }

        /// <summary>
        /// Thickens the area's lines while its unit is selected. One
        /// <see cref="MapLine.RefreshStyle"/> per line per selection change —
        /// not per frame; see <see cref="Update"/>.
        /// </summary>
        void ApplyWidth(Area area)
        {
            float width = area.selected ? SelectedWidthM : RestWidthM;
            foreach (var id in area.lineIds)
            {
                var line = _lines.Find(id);
                if (line == null) continue;
                line.Data.widthMeters = width;
                line.RefreshStyle();
            }
        }

        // ------------------------------------------------------------ naming

        static string Prefix(UnitActor u) => $"task-{u.State.instanceId}-";

        static string Designation(UnitActor u) =>
            string.IsNullOrEmpty(u.State.customName) ? u.Def.name : u.State.customName;
    }
}
