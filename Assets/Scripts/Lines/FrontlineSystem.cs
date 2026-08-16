using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Lines
{
    /// <summary>
    /// The automatic front line between the two sides — where the fighting
    /// currently is, redrawn as formations move, fight and die.
    ///
    /// **This was rewritten because the old one did not describe the battle.**
    /// It took nine samples across the front; at each one it found the single
    /// nearest blue unit and the single nearest red unit and put a point on the
    /// segment between them. Three consequences, all visible:
    ///
    /// • **Nine points is not a line, it is a zig-zag.** Every sample jumped to
    ///   whichever unit happened to be nearest, so the line kinked hard between
    ///   bands and bore no relation to the shape of either force.
    /// • **Only two units out of a hundred were ever consulted.** A brigade
    ///   massed behind its lead battalion had no effect on the line at all —
    ///   the front went where one unit stood, not where the weight was.
    /// • **"Nearest" was measured in degrees.** A degree of longitude is a third
    ///   shorter than a degree of latitude at Lyon, so the search was stretched
    ///   east-west and picked the wrong unit whenever two were close.
    ///
    /// What replaces it is an **influence field**. Every living formation
    /// contributes, weighted by its combat power, by how close it is to the band
    /// being sampled (a Gaussian across the front), and by how far forward it is
    /// (an exponential toward the enemy, so the lead battalions decide the line
    /// and the field bakery does not). The two sides' weighted front edges are
    /// then interpolated by their local strength, so the stronger side pushes
    /// the line into the weaker one — the one part of the old model worth
    /// keeping. Everything is done in **metres**, in a local east-north-up frame
    /// about the centre of the battle.
    ///
    /// The result is subdivided by Chaikin's corner-cutting into a smooth
    /// polyline of a few hundred vertices, which is what makes it read as a
    /// front rather than as a set of measurements.
    ///
    /// **The line spans every formation on the map.** The solved bands are the
    /// ones where both sides have influence; the rest — out past the flanks,
    /// and any gap between two separate engagements — are filled rather than
    /// dropped (see <see cref="FillUnsolved"/>). A boundary that stopped short
    /// of the units it is drawn between is not a boundary. The band range is
    /// taken from the outermost formation on either side and then carried a
    /// shoulder further (<see cref="FlankPadFraction"/>), so the front runs past
    /// the flanks rather than terminating on the flank unit's own counter.
    ///
    /// **It is a 2D graphic, painted on the ground.** The FLOT states where the
    /// fighting is, which is a fact about a piece of terrain — so it is drawn
    /// flat and clamped to the ground the whole way along, in both the top-down
    /// and the tilted view, and never as a curtain standing in the world. That
    /// is <see cref="LineKind.Boundary"/>'s membership of <c>MapLine.FlatOnly</c>
    /// plus the ground-plane ribbon alignment described there; this class only
    /// has to publish the geometry and say <c>is3D = false</c>.
    ///
    /// All of the shaping constants are settings rather than constants, driven
    /// from <see cref="UI.FrontlinePanelUI"/> — the line is clickable and opens
    /// its own panel.
    /// </summary>
    public class FrontlineSystem : MonoBehaviour
    {
        public const string LineId = "boundary-auto";

        /// <summary>Recompute as the battle moves. Off means the line is a snapshot.</summary>
        public bool AutoUpdate = true;

        // ------------------------------------------------------------ settings

        /// <summary>Bands sampled across the front before smoothing. More = more faithful and more jagged.</summary>
        /// <remarks>41 is the panel's STANDARD setting — see <see cref="UI.FrontlinePanelUI"/>.</remarks>
        public int Resolution { get; private set; } = DefaultResolution;
        /// <summary>Chaikin corner-cutting passes. Each roughly doubles the vertex count.</summary>
        /// <remarks>
        /// Three — the panel's SILK setting — rather than two. The line now runs
        /// the whole width of the deployment (see <see cref="Recompute"/>), and
        /// the extrapolated shoulders out past the flanks meet the solved middle
        /// at a visible corner at two passes. A third pass rounds it, and costs
        /// one more doubling of a vertex list that is a few hundred long.
        /// </remarks>
        public int SmoothingPasses { get; private set; } = DefaultSmoothing;

        /// <summary>Shipped settings, shared by the field initialisers and <see cref="ResetToDefaults"/>.</summary>
        public const int DefaultResolution = 41;
        public const int DefaultSmoothing = 3;
        public const float DefaultInfluenceWidthKm = 6f;
        /// <summary>
        /// Gaussian width across the front, in km. How far along the front a
        /// formation's influence reaches: small values make the line hug each
        /// unit, large values make it a broad sweep through the whole force.
        /// </summary>
        public float InfluenceWidthKm { get; private set; } = DefaultInfluenceWidthKm;

        /// <summary>Whether the line is drawn at all.</summary>
        public bool Visible { get; private set; } = true;

        // ------------------------------------------------------------ readout

        /// <summary>Length of the line as last drawn, in km. For the settings panel.</summary>
        public double LengthKm { get; private set; }
        /// <summary>Formations that contributed to the last solve.</summary>
        public int BlueCount { get; private set; }
        public int RedCount { get; private set; }
        /// <summary>Why the last solve produced nothing, or null when it succeeded.</summary>
        public string LastFailure { get; private set; }

        // ------------------------------------------------- the group holding it

        /// <summary>
        /// The group that has been put on the line, if any — its id and its
        /// name at the time it was assigned.
        ///
        /// **Why the line knows.** Manning the FLOT is an order given to
        /// formations, and the orders themselves live on the units; but "who is
        /// holding the front" is a fact about the *front*, and it has to
        /// survive every recompute of the geometry. Keeping it here is what
        /// lets the line caption itself with the group's name rather than a
        /// bare "FLOT", and what lets the GROUPS panel say which group is on it
        /// without interrogating every formation on the map.
        /// </summary>
        public string HoldingGroupId { get; private set; } = "";
        public string HoldingGroupName { get; private set; } = "";

        /// <summary>
        /// Records which group holds the line, or clears it with an empty id.
        /// Re-captions the line; the deployment itself is the caller's business
        /// — see <c>GameController.ManTheFlot</c>.
        /// </summary>
        public void SetHoldingGroup(string groupId, string groupName)
        {
            HoldingGroupId = groupId ?? "";
            HoldingGroupName = string.IsNullOrEmpty(groupId) ? "" : (groupName ?? "");
            ApplyLabel();
            Recomputed?.Invoke();
        }

        /// <summary>
        /// The line's caption: "FLOT", or "FLOT — 1ST BRIGADE" once a group has
        /// been put on it. Re-applied after every publish, because a line
        /// rebuilt from a save comes back with whatever label the file carried.
        /// </summary>
        void ApplyLabel()
        {
            var line = Line;
            if (line == null) return;

            string label = string.IsNullOrEmpty(HoldingGroupName)
                ? "FLOT"
                : "FLOT — " + HoldingGroupName.ToUpperInvariant();
            if (line.Data.label == label) return;

            line.Data.label = label;
            line.RefreshStyle();       // rebuilds the captions from Data.label
        }
        /// <summary>Raised after every recompute, so the panel can refresh its readout.</summary>
        public event System.Action Recomputed;

        // ------------------------------------------------------------ internals

        /// <summary>
        /// Depth bias, in km. Weight falls off by e for every this-many km a
        /// formation sits behind its side's leading edge, which is what stops
        /// the rear area voting on where the front is.
        /// </summary>
        const float ForwardBiasKm = 4f;
        /// <summary>Metres per degree of latitude. Good to a fraction of a percent anywhere a scenario is fought.</summary>
        const double MetresPerDegLat = 111132.0;
        /// <summary>Share of the deployment's frontage carried out past each flank.</summary>
        const float FlankPadFraction = 0.15f;
        /// <summary>Floor on that shoulder, metres — see <see cref="Recompute"/>.</summary>
        const float MinFlankPadM = 2500f;

        LineManager _lines;
        float _timer;

        /// <summary>A unit reduced to the local frame, so the solve never touches lat/lon again.</summary>
        struct Node
        {
            public float Lateral;    // metres along the front
            public float Depth;      // metres toward the enemy
            public float Power;
        }

        readonly List<Node> _blue = new List<Node>();
        readonly List<Node> _red = new List<Node>();

        public void Init(LineManager lines)
        {
            _lines = lines;
            UnitRegistry.Changed += OnUnitsChanged;
        }

        void OnDestroy() => UnitRegistry.Changed -= OnUnitsChanged;

        void OnUnitsChanged() => _timer = GameConfig.FrontlineUpdateSeconds; // recompute soon

        void Update()
        {
            if (!AutoUpdate) return;
            _timer += Time.deltaTime;
            if (_timer < GameConfig.FrontlineUpdateSeconds) return;
            _timer = 0f;
            Recompute();
        }

        // ------------------------------------------------------------ settings API

        public void SetResolution(int bands)
        {
            Resolution = Mathf.Clamp(bands, 9, 129);
            Recompute();
        }

        public void SetSmoothing(int passes)
        {
            SmoothingPasses = Mathf.Clamp(passes, 0, 4);
            Recompute();
        }

        public void SetInfluenceWidthKm(float km)
        {
            InfluenceWidthKm = Mathf.Clamp(km, 1f, 40f);
            Recompute();
        }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            var line = _lines != null ? _lines.Find(LineId) : null;
            if (line != null) line.gameObject.SetActive(visible);
        }

        /// <summary>Colour and width, from the settings panel. Persisted with the line.</summary>
        public void SetStyle(string colorHex, float widthMeters)
        {
            var line = _lines != null ? _lines.Find(LineId) : null;
            if (line == null) return;
            line.Data.colorHex = colorHex;
            line.Data.widthMeters = widthMeters;
            line.RefreshStyle();
        }

        public MapLine Line => _lines != null ? _lines.Find(LineId) : null;

        /// <summary>
        /// Back to how the line behaves in a fresh scenario. Called by the
        /// editor's RESET, which puts every panel's settings back — a front line
        /// still drawn "Raw / Sweeping / violet" after a reset would be the one
        /// thing on the map still carrying the last session.
        /// </summary>
        public void ResetToDefaults()
        {
            AutoUpdate = true;
            Visible = true;
            HoldingGroupId = "";
            HoldingGroupName = "";
            Resolution = DefaultResolution;
            SmoothingPasses = DefaultSmoothing;
            InfluenceWidthKm = DefaultInfluenceWidthKm;

            var line = Line;
            if (line != null)
            {
                line.Data.colorHex = "";
                line.Data.widthMeters = 0f;
                line.RefreshStyle();
                line.gameObject.SetActive(true);
            }
            Recompute();
        }

        // ------------------------------------------------------------ solve

        public void Recompute()
        {
            if (_lines == null) return;

            if (!Gather(out double lat0, out double lon0,
                        out Vector2 axis, out Vector2 cross))
            {
                Publish(null);
                return;
            }

            // Span across the front, from the outermost formation on either
            // side, padded so the line does not stop dead at the last unit's
            // shoulder. Every unit on the map is inside this span by
            // construction, and — since nothing is trimmed off the ends any
            // more — inside the drawn line too.
            float minLat = float.MaxValue, maxLat = float.MinValue;
            foreach (var n in _blue) { minLat = Mathf.Min(minLat, n.Lateral); maxLat = Mathf.Max(maxLat, n.Lateral); }
            foreach (var n in _red) { minLat = Mathf.Min(minLat, n.Lateral); maxLat = Mathf.Max(maxLat, n.Lateral); }

            float sigma = InfluenceWidthKm * 1000f;

            // The shoulder past the outermost formation. The influence width is
            // a *setting* — at its 1 km floor it would leave the line finishing
            // on top of the flank battalion's counter, which reads as a front
            // that runs out rather than one that continues beyond contact. So
            // the pad is the largest of the influence width, a share of the
            // deployment's own frontage, and a fixed minimum: whichever the
            // scenario is, the line covers every unit on the map with ground to
            // spare on both flanks.
            float pad = Mathf.Max(sigma,
                Mathf.Max((maxLat - minLat) * FlankPadFraction, MinFlankPadM));
            minLat -= pad; maxLat += pad;
            if (maxLat - minLat < 1f)
            {
                LastFailure = "The force has no width across the front.";
                Publish(null);
                return;
            }

            // Leading edges: the most-forward blue and the most-forward red, in
            // the axis's own terms. These anchor the exponential below so it
            // cannot overflow whatever scale the battle is at.
            float blueLead = float.MinValue, redLead = float.MaxValue;
            foreach (var n in _blue) blueLead = Mathf.Max(blueLead, n.Depth);
            foreach (var n in _red) redLead = Mathf.Min(redLead, n.Depth);

            int bands = Mathf.Max(3, Resolution);
            var lateral = new float[bands];
            var depth = new float[bands];
            var solved = new bool[bands];
            int firstSolved = -1, lastSolved = -1;

            for (int i = 0; i < bands; i++)
            {
                float t = Mathf.Lerp(minLat, maxLat, i / (float)(bands - 1));
                lateral[i] = t;

                float bDepth = Edge(_blue, t, sigma, blueLead, forward: true, out float bWeight);
                float rDepth = Edge(_red, t, sigma, redLead, forward: false, out float rWeight);
                if (bWeight <= 0f || rWeight <= 0f) continue;

                // Power-weighted midpoint between the two front edges: the
                // stronger side pushes the line towards the weaker one, which
                // is the whole reason the front moves when a battle is won.
                float share = bWeight / (bWeight + rWeight);
                depth[i] = Mathf.Lerp(bDepth, rDepth, share);
                solved[i] = true;

                if (firstSolved < 0) firstSolved = i;
                lastSolved = i;
            }

            if (firstSolved < 0)
            {
                LastFailure = "The two sides are not in contact anywhere along the front.";
                Publish(null);
                return;
            }

            FillUnsolved(depth, solved, firstSolved, lastSolved);

            var points = new List<Vector2>(bands);
            for (int i = 0; i < bands; i++)
                points.Add(new Vector2(lateral[i], depth[i]));

            for (int pass = 0; pass < SmoothingPasses; pass++) points = Chaikin(points);

            // Back to geodetic. The frame is east-north-up about the battle's
            // centre, so this is the exact inverse of the projection in Gather.
            double mPerDegLon = MetresPerDegLat * System.Math.Cos(lat0 * System.Math.PI / 180.0);
            var geo = new List<GeoPoint>(points.Count);
            foreach (var p in points)
            {
                float east = cross.x * p.x + axis.x * p.y;
                float north = cross.y * p.x + axis.y * p.y;
                geo.Add(new GeoPoint
                {
                    latitude = lat0 + north / MetresPerDegLat,
                    longitude = lon0 + east / System.Math.Max(1.0, mPerDegLon)
                });
            }

            Publish(geo);
        }

        /// <summary>
        /// Projects every living formation into a local east-north-up frame and
        /// works out which way the front runs. Returns false when there is no
        /// front to speak of — one side absent, or both stacked on one point.
        /// </summary>
        bool Gather(out double lat0, out double lon0, out Vector2 axis, out Vector2 cross)
        {
            lat0 = lon0 = 0; axis = Vector2.up; cross = Vector2.right;

            _blue.Clear(); _red.Clear();
            var blues = new List<UnitActor>(UnitRegistry.OfTeam(Team.User));
            var reds = new List<UnitActor>(UnitRegistry.OfTeam(Team.Enemy));
            BlueCount = blues.Count; RedCount = reds.Count;

            if (blues.Count == 0 || reds.Count == 0)
            {
                LastFailure = "Both sides need at least one formation on the map.";
                return false;
            }

            // Origin at the midpoint of the two forces, so the local frame's
            // flat-earth approximation is centred on the battle rather than on
            // whichever unit happened to be first in the registry.
            foreach (var u in blues) { lat0 += u.State.latitude; lon0 += u.State.longitude; }
            foreach (var u in reds) { lat0 += u.State.latitude; lon0 += u.State.longitude; }
            lat0 /= blues.Count + reds.Count;
            lon0 /= blues.Count + reds.Count;

            double mPerDegLon = MetresPerDegLat * System.Math.Cos(lat0 * System.Math.PI / 180.0);

            // Power-weighted centroids define the axis: a battalion should move
            // the front more than a supply point does.
            Vector2 bC = Vector2.zero, rC = Vector2.zero;
            float bP = 0f, rP = 0f;

            var blueXY = new List<(Vector2 pos, float power)>(blues.Count);
            var redXY = new List<(Vector2 pos, float power)>(reds.Count);

            foreach (var u in blues)
            {
                var p = Project(u, lat0, lon0, mPerDegLon, out float power);
                blueXY.Add((p, power)); bC += p * power; bP += power;
            }
            foreach (var u in reds)
            {
                var p = Project(u, lat0, lon0, mPerDegLon, out float power);
                redXY.Add((p, power)); rC += p * power; rP += power;
            }

            if (bP <= 0f || rP <= 0f)
            {
                LastFailure = "No formation on one side has any combat power left.";
                return false;
            }
            bC /= bP; rC /= rP;

            Vector2 d = rC - bC;
            if (d.sqrMagnitude < 1f)
            {
                LastFailure = "The two sides are on top of each other — no front to draw.";
                return false;
            }
            axis = d.normalized;                              // blue → red
            cross = new Vector2(-axis.y, axis.x);             // along the front

            foreach (var (pos, power) in blueXY)
                _blue.Add(new Node { Lateral = Vector2.Dot(pos, cross), Depth = Vector2.Dot(pos, axis), Power = power });
            foreach (var (pos, power) in redXY)
                _red.Add(new Node { Lateral = Vector2.Dot(pos, cross), Depth = Vector2.Dot(pos, axis), Power = power });

            LastFailure = null;
            return true;
        }

        static Vector2 Project(UnitActor u, double lat0, double lon0, double mPerDegLon,
            out float power)
        {
            power = Mathf.Max(0.01f, u.CurrentPower());
            return new Vector2(
                (float)((u.State.longitude - lon0) * mPerDegLon),
                (float)((u.State.latitude - lat0) * MetresPerDegLat));
        }

        /// <summary>
        /// One side's front edge at a point along the front: a weighted mean of
        /// its formations' depths.
        ///
        /// Three factors multiply into each formation's weight — its combat
        /// power, a Gaussian in how far along the front it sits from the band
        /// being sampled, and an exponential in how far forward it is. The last
        /// is what makes this a *front* rather than a centre of mass: the
        /// battalions in contact dominate, and everything behind them fades out
        /// over <see cref="ForwardBiasKm"/>.
        /// </summary>
        static float Edge(List<Node> side, float lateral, float sigma, float lead,
            bool forward, out float weight)
        {
            float sum = 0f;
            weight = 0f;
            float bias = ForwardBiasKm * 1000f;

            foreach (var n in side)
            {
                float dl = (n.Lateral - lateral) / sigma;
                float lateralW = Mathf.Exp(-dl * dl);
                if (lateralW < 1e-4f) continue;

                // Measured from the side's own leading edge, so the exponent is
                // always <= 0 and cannot blow up on a large map.
                float behind = forward ? lead - n.Depth : n.Depth - lead;
                float depthW = Mathf.Exp(-Mathf.Max(0f, behind) / bias);

                float w = n.Power * lateralW * depthW;
                sum += n.Depth * w;
                weight += w;
            }

            return weight > 0f ? sum / weight : 0f;
        }

        /// <summary>
        /// Gives every band a depth, including the ones no solve could reach.
        ///
        /// **This is what makes the line cover the whole map rather than only
        /// the part of it that is fighting.** A band is only solved where both
        /// sides have some influence; out past either flank, and in any gap
        /// between two separate engagements, one side's weight is zero and the
        /// midpoint is undefined. The old code dropped those bands and then
        /// trimmed the ends harder still, so the front was a short segment
        /// hanging in the middle of a deployment that ran well past both of its
        /// ends — a formation on the flank was outside the line that was
        /// supposed to describe where it stood.
        ///
        /// So nothing is dropped. Past the outermost solved band the last
        /// solved depth is **carried out** to the flank — the front runs
        /// straight on out of contact, which is what a front does. Gaps in the
        /// middle are **bridged** by interpolating between the solved bands on
        /// either side, so a two-battle map reads as one continuous front
        /// rather than as two disconnected pieces.
        /// </summary>
        static void FillUnsolved(float[] depth, bool[] solved, int first, int last)
        {
            for (int i = 0; i < first; i++) depth[i] = depth[first];
            for (int i = last + 1; i < depth.Length; i++) depth[i] = depth[last];

            for (int i = first + 1; i < last; i++)
            {
                if (solved[i]) continue;

                // depth[i - 1] is already final: either it was solved, or an
                // earlier pass of this loop filled it.
                int a = i - 1, b = i + 1;
                while (b < last && !solved[b]) b++;

                for (int k = i; k < b; k++)
                    depth[k] = Mathf.Lerp(depth[a], depth[b], (k - a) / (float)(b - a));

                i = b - 1;
            }
        }

        /// <summary>
        /// Chaikin corner-cutting: replaces each segment with its quarter and
        /// three-quarter points, keeping the two ends. One pass roughly doubles
        /// the vertex count and rounds every corner; two is enough to turn a
        /// band-by-band solve into something that reads as a drawn front.
        /// </summary>
        static List<Vector2> Chaikin(List<Vector2> pts)
        {
            if (pts.Count < 3) return pts;

            var result = new List<Vector2>(pts.Count * 2) { pts[0] };
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                result.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.25f));
                result.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.75f));
            }
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        // ------------------------------------------------------------ output

        /// <summary>
        /// Pushes the solved line onto the map, or takes it down when there is
        /// nothing to draw. A failed solve removes the line rather than leaving
        /// the last good one standing: a stale front that no longer matches the
        /// units on the map is worse than no front at all.
        /// </summary>
        void Publish(List<GeoPoint> points)
        {
            var line = _lines.Find(LineId);

            if (points == null || points.Count < 2)
            {
                LengthKm = 0;
                if (line != null) _lines.Remove(line);
                Recomputed?.Invoke();
                return;
            }

            if (line == null)
            {
                line = _lines.Add(new MapLineData
                {
                    id = LineId,
                    kind = LineKind.Boundary.ToString(),
                    team = "",
                    // The front line is a map graphic, never a fence standing in
                    // the world: it is drawn flat on the terrain in both view
                    // modes. MapLine normalises this for every FlatOnly kind, so
                    // this is the record of intent rather than the enforcement.
                    is3D = false,
                    autoGenerated = true,
                    label = "FLOT",
                    points = points
                });
                // The one line on the map with settings worth opening.
                line.SetPickable(true);
            }
            else line.SetPoints(points);

            // Also applied to a line restored from a save, which was rebuilt by
            // LineManager rather than created here and would otherwise come
            // back un-clickable.
            line.SetPickable(true);
            ApplyLabel();
            line.gameObject.SetActive(Visible);
            LengthKm = line.LengthKm;
            LastFailure = null;
            Recomputed?.Invoke();
        }
    }
}
