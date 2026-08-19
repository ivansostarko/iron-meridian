using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Lines
{
    /// <summary>How the FLOT is produced — see <see cref="FrontlineSystem"/>.</summary>
    public enum FlotMode
    {
        /// <summary>The game calculates the line from unit positions and battlefield state.</summary>
        Automatic,
        /// <summary>The scenario designer draws and controls the line.</summary>
        Manual,
        /// <summary>Designer draws the initial line; the game takes over when the battle starts.</summary>
        Hybrid
    }

    /// <summary>What a stretch of the front is currently doing.</summary>
    public enum FlotState { Stable, Advancing, Retreating, Contested, Breached, Collapsing, Isolated }

    /// <summary>Who holds a piece of ground, as the front lines read it.</summary>
    public enum TerritoryOwner { Blue, Red, Contested }

    /// <summary>
    /// One published stretch of front: a side's forward edge, or the manual
    /// line. There is exactly one per side — see <see cref="FrontlineSystem"/>.
    /// </summary>
    public class FlotSegment
    {
        public string Id;
        public Team Team;
        public FlotState State;
        public List<GeoPoint> Points = new List<GeoPoint>();
        public double LengthKm;
        /// <summary>Drawn from what the player can see rather than from the truth — fog is on.</summary>
        public bool Estimated;
        /// <summary>The designer's hand-drawn line, in Manual/Hybrid mode.</summary>
        public bool Manual;
        /// <summary>Formations contributing to this stretch.</summary>
        public int Contributors;
        /// <summary>Km the stretch moved toward the enemy since the last solve. Negative = giving ground.</summary>
        public float AdvanceKm;
    }

    /// <summary>
    /// The FLOT — the forward line of own troops, per side, as a gameplay
    /// object rather than a drawing.
    ///
    /// **The key rule: the line represents effective control by combat
    /// formations, not the physical position of the most advanced unit.**
    /// Everything below serves that sentence.
    ///
    /// The pipeline, each stage a method in this file:
    ///
    ///     units → eligibility (FlotEligibility: only frontline-capable,
    ///             combat-effective formations vote)
    ///           → clustering (mutually supporting groups, per side)
    ///           → outlier filtering (a lone recon car deep in enemy ground
    ///             does not drag the front with it; a cluster cut off from its
    ///             side's main body stops voting on where the front runs)
    ///           → one engagement: each side's remaining clusters merged into a
    ///             single body, and the operational direction taken from the two
    ///             bodies' centres
    ///           → per-band forward edges, one per side (influence field,
    ///             Gaussian across the front, exponential toward the enemy)
    ///           → smoothing, stability damping, terrain projection (MapLine
    ///             drapes and grounds every flat kind)
    ///           → segments with states, breach detection, territory answers,
    ///             history snapshots
    ///
    /// **Exactly two lines: ours and theirs.** Each side has one forward edge;
    /// the ground between them is contested. That is what makes territory a
    /// query the game can answer (<see cref="TerritoryAt"/>) instead of a
    /// colour, and a breach a definable event (<see cref="Breach"/>) instead of
    /// a feeling.
    ///
    /// **And never more than two.** The solver used to pair every friendly
    /// cluster with the enemy cluster it faced and publish an edge per battle,
    /// plus a ring around each pocket — so a dispersed order of battle put four
    /// or five identically-coloured traces on the map and left the player
    /// working out which of them was the front. The FLOT answers one question,
    /// so it is one line per side: the clusters are merged into a single body
    /// before the solve, and everything downstream (territory, breach, manning,
    /// history) reads the engagement's nodes rather than the published geometry,
    /// so none of it changes shape with the merge.
    ///
    /// **Three modes.** Automatic solves from the force; Manual publishes the
    /// designer's drawn line and uses it for the same queries; Hybrid is
    /// manual until the battle starts and automatic after — the designer sets
    /// the opening trace, the fight redraws it.
    ///
    /// **Fog.** The enemy's *published* edge is computed only from enemy
    /// formations the player can currently see, and drawn broken, because an
    /// estimate should look like one. The internal line — breach detection,
    /// territory, states — always uses the truth, because the simulation is
    /// not the player.
    ///
    /// The shaping settings (resolution, smoothing, influence width) are
    /// driven from <see cref="UI.FrontlinePanelUI"/> — any FLOT line is
    /// clickable and opens the panel.
    /// </summary>
    public class FrontlineSystem : MonoBehaviour
    {
        public const string LineIdPrefix = "flot-";
        /// <summary>The friendly forward edge. One line, always this id.</summary>
        public const string UserLineId = "flot-user";
        /// <summary>The hostile forward edge — the truth, or the estimate under fog.</summary>
        public const string EnemyLineId = "flot-enemy";
        public const string ManualLineId = "flot-manual";
        /// <summary>The single midline the old solver published; removed from old saves on the first solve.</summary>
        public const string LegacyLineId = "boundary-auto";

        /// <summary>True for any line this system owns — the panel opens from a click on any of them.</summary>
        public static bool IsFlotLine(string id) =>
            !string.IsNullOrEmpty(id) && (id.StartsWith(LineIdPrefix) || id == LegacyLineId);

        /// <summary>Recompute as the battle moves. Off means the line is a snapshot.</summary>
        public bool AutoUpdate = true;

        /// <summary>User-facing messages (drawing instructions, mode changes).</summary>
        public System.Action<string> Flash;

        // ------------------------------------------------------------ settings

        public FlotMode Mode { get; private set; } = FlotMode.Automatic;

        /// <summary>Bands sampled across each engagement before smoothing.</summary>
        public int Resolution { get; private set; } = DefaultResolution;
        /// <summary>Chaikin corner-cutting passes.</summary>
        public int SmoothingPasses { get; private set; } = DefaultSmoothing;
        /// <summary>Gaussian width across the front, km — how far along it one formation speaks for.</summary>
        public float InfluenceWidthKm { get; private set; } = DefaultInfluenceWidthKm;
        /// <summary>Whether the lines are drawn at all.</summary>
        public bool Visible { get; private set; } = true;

        public const int DefaultResolution = 41;
        public const int DefaultSmoothing = 3;
        public const float DefaultInfluenceWidthKm = 6f;

        // ------------------------------------------------------------- tuning

        /// <summary>Depth bias, km: weight falls by e per this many km behind the side's leading edge.</summary>
        const float ForwardBiasKm = 4f;
        /// <summary>Units this close support each other — the clustering link distance.</summary>
        const float ClusterLinkKm = 12f;
        /// <summary>A cluster this far from its side's main body is on its own.</summary>
        const float IsolationKm = 30f;
        /// <summary>Share of the side's power below which an isolated cluster is an outlier, not a pocket.</summary>
        const float OutlierPowerFraction = 0.05f;
        /// <summary>Share of the *victim's* power an intruding cluster needs before it registers as a breach.</summary>
        const float BreachPowerFraction = 0.08f;
        /// <summary>Km past the victim's edge an intruder must be to count as a breach.</summary>
        const float BreachDepthKm = 2f;
        /// <summary>Mean gap between the two edges below which a stretch reads as contested, km.</summary>
        const float ContestGapKm = 1.0f;
        /// <summary>Mean movement below this is jitter and is not republished, metres.</summary>
        const float MinMoveM = 50f;
        /// <summary>Advance/retreat below this is stable, km per solve.</summary>
        const float StateMoveKm = 0.1f;
        /// <summary>A retreat past this in one solve is a collapse, km.</summary>
        const float CollapseKm = 1.0f;
        /// <summary>Shoulder carried past the flanks: fraction of frontage, with a floor.</summary>
        const float FlankPadFraction = 0.15f;
        const float MinFlankPadM = 2500f;
        /// <summary>Metres per degree of latitude.</summary>
        const double MetresPerDegLat = 111132.0;
        /// <summary>Game-seconds between history snapshots (5 scenario minutes).</summary>
        const double HistoryGameSeconds = 300.0;
        /// <summary>Snapshots kept — 4 hours of scenario time at the cadence above.</summary>
        const int HistoryCap = 48;

        // ------------------------------------------------------------ readout

        public double LengthKm { get; private set; }
        /// <summary>Eligible (not merely living) formations in the last solve.</summary>
        public int BlueCount { get; private set; }
        public int RedCount { get; private set; }
        public string LastFailure { get; private set; }
        public event System.Action Recomputed;

        /// <summary>
        /// FLOT_BREACH: an enemy force with real combat power is established
        /// behind a side's forward edge. (victim, lat, lon, penetration km).
        /// Raised once per intrusion, not once per solve.
        /// </summary>
        public event System.Action<Team, double, double, double> Breach;

        /// <summary>Everything currently published, for the minimap and the panel.</summary>
        public IReadOnlyList<FlotSegment> Segments => _segments;

        /// <summary>One history record: when, and where each side's main edge stood.</summary>
        public struct HistoryEntry
        {
            public string Time;
            public double BlueLat, BlueLon, RedLat, RedLon;
        }
        /// <summary>Periodic snapshots of the front, oldest first. For AAR and movement readouts.</summary>
        public IReadOnlyList<HistoryEntry> History => _history;

        /// <summary>The scenario clock, for history cadence. Set by the controller; null = no snapshots.</summary>
        public GameClock Clock;

        // ---------------------------------------------------------- internals

        LineManager _lines;
        MapManager _map;
        Camera _cam;
        float _timer;

        readonly List<FlotSegment> _segments = new List<FlotSegment>();
        readonly HashSet<string> _publishedIds = new HashSet<string>();
        readonly Dictionary<string, List<GeoPoint>> _prevPoints = new Dictionary<string, List<GeoPoint>>();
        readonly Dictionary<string, float> _prevMeanDepth = new Dictionary<string, float>();
        readonly HashSet<string> _activeBreaches = new HashSet<string>();
        readonly List<HistoryEntry> _history = new List<HistoryEntry>();
        double _lastHistoryGameSecond = double.MinValue;

        struct Node { public float Lateral, Depth, Power; }

        class Cluster
        {
            public readonly List<UnitActor> Units = new List<UnitActor>();
            public float Power;
            public double Lat, Lon;
            public bool Isolated;
        }

        /// <summary>One solved battle: the frame and the influence nodes, kept for queries.</summary>
        class Engagement
        {
            public double Lat0, Lon0;
            public Vector2 Axis, Cross;
            public float MinLat, MaxLat, Sigma;
            public float BlueLead, RedLead;
            public List<Node> Blue, Red;
        }
        readonly List<Engagement> _engagements = new List<Engagement>();

        // --------------------------------------------------------- lifecycle

        public void Init(LineManager lines, MapManager map, Camera cam)
        {
            _lines = lines;
            _map = map;
            _cam = cam;
            UnitRegistry.Changed += OnUnitsChanged;
        }

        void OnDestroy() => UnitRegistry.Changed -= OnUnitsChanged;
        void OnUnitsChanged() => _timer = GameConfig.FrontlineUpdateSeconds;

        void Update()
        {
            HandleDrawing();

            if (!AutoUpdate) return;
            _timer += Time.deltaTime;
            if (_timer < GameConfig.FrontlineUpdateSeconds) return;
            _timer = 0f;
            Recompute();
        }

        // ------------------------------------------------------ settings API

        public void SetMode(FlotMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            CancelDrawing();
            Recompute();
        }

        public void SetResolution(int bands) { Resolution = Mathf.Clamp(bands, 9, 129); Recompute(); }
        public void SetSmoothing(int passes) { SmoothingPasses = Mathf.Clamp(passes, 0, 4); Recompute(); }
        public void SetInfluenceWidthKm(float km) { InfluenceWidthKm = Mathf.Clamp(km, 1f, 40f); Recompute(); }

        public void SetVisible(bool visible)
        {
            Visible = visible;
            foreach (var id in _publishedIds)
            {
                var line = _lines.Find(id);
                if (line != null) line.gameObject.SetActive(visible);
            }
        }

        /// <summary>Custom colour/width from the panel, applied to every published line.</summary>
        public void SetStyle(string colorHex, float widthMeters)
        {
            _customHex = colorHex; _customWidth = widthMeters;
            foreach (var id in _publishedIds)
            {
                var line = _lines.Find(id);
                if (line == null) continue;
                if (!string.IsNullOrEmpty(colorHex)) line.Data.colorHex = colorHex;
                if (widthMeters > 0f) line.Data.widthMeters = widthMeters;
                line.RefreshStyle();
            }
        }
        string _customHex = "";
        float _customWidth;

        /// <summary>The blue main edge, or whatever is published first. Kept for older callers.</summary>
        public MapLine Line
        {
            get
            {
                var seg = MainSegment(Team.User) ?? (_segments.Count > 0 ? _segments[0] : null);
                return seg != null && _lines != null ? _lines.Find(seg.Id) : null;
            }
        }

        /// <summary>
        /// A side's segment. There is only ever one, but the largest still wins
        /// the tie so a manual trace and an automatic edge cannot both answer.
        /// </summary>
        public FlotSegment MainSegment(Team team)
        {
            FlotSegment best = null;
            foreach (var s in _segments)
            {
                if (!s.Manual && s.Team != team) continue;
                if (best == null || s.LengthKm > best.LengthKm) best = s;
            }
            return best;
        }

        /// <summary>The trace a group manning the front should distribute along.</summary>
        public List<GeoPoint> PointsForManning(Team team)
        {
            var seg = MainSegment(team);
            return seg?.Points;
        }

        public void ResetToDefaults()
        {
            AutoUpdate = true;
            Visible = true;
            Mode = FlotMode.Automatic;
            HoldingGroupId = "";
            HoldingGroupName = "";
            Resolution = DefaultResolution;
            SmoothingPasses = DefaultSmoothing;
            InfluenceWidthKm = DefaultInfluenceWidthKm;
            _customHex = ""; _customWidth = 0f;
            _manual.Clear();
            CancelDrawing();
            _history.Clear();
            _activeBreaches.Clear();
            Recompute();
        }

        // ------------------------------------------------- the holding group

        public string HoldingGroupId { get; private set; } = "";
        public string HoldingGroupName { get; private set; } = "";

        public void SetHoldingGroup(string groupId, string groupName)
        {
            HoldingGroupId = groupId ?? "";
            HoldingGroupName = string.IsNullOrEmpty(groupId) ? "" : (groupName ?? "");
            Recompute();
        }

        // ------------------------------------------------------------- solve

        /// <summary>Hybrid is manual until the battle starts — the fight takes over the trace.</summary>
        FlotMode EffectiveMode =>
            Mode == FlotMode.Hybrid ? (CombatSystem.BattleRunning ? FlotMode.Automatic : FlotMode.Manual) : Mode;

        public void Recompute()
        {
            if (_lines == null) return;

            _segments.Clear();
            _engagements.Clear();
            LastFailure = null;

            if (EffectiveMode == FlotMode.Manual) SolveManual();
            else SolveAutomatic();

            Publish();
            DetectBreaches();
            TakeHistorySnapshot();
            Recomputed?.Invoke();
        }

        // -------------------------------------------------------- automatic

        void SolveAutomatic()
        {
            // 1. Eligibility — only frontline-capable, combat-effective units.
            var blue = new List<UnitActor>();
            var red = new List<UnitActor>();
            foreach (var u in UnitRegistry.All)
            {
                if (!FlotEligibility.CanInfluence(u)) continue;
                (u.State.TeamEnum == Team.User ? blue : red).Add(u);
            }
            BlueCount = blue.Count; RedCount = red.Count;

            if (blue.Count == 0 || red.Count == 0)
            {
                LastFailure = "Both sides need at least one combat-effective frontline formation.";
                return;
            }

            // 2. Clustering — mutually supporting groups, per side.
            var blueClusters = BuildClusters(blue);
            var redClusters = BuildClusters(red);

            // 3. Outlier filtering. The strongest cluster is the main body;
            // anything far from it is either dropped from the solve entirely (a
            // lone probing unit must not pull the whole front with it) or marked
            // isolated, which keeps it out of the line without deleting it.
            float bluePower = SidePower(blueClusters), redPower = SidePower(redClusters);
            MarkIsolation(blueClusters, bluePower);
            MarkIsolation(redClusters, redPower);

            // 4. One engagement. Each side's surviving clusters are merged into
            // a single body, so the solve publishes exactly one forward edge per
            // side however many separate battles the ground happens to hold —
            // see the class comment for why the front is one line and not one
            // per fight.
            var blueBody = Merge(blueClusters);
            var redBody = Merge(redClusters);
            if (blueBody == null || redBody == null)
            {
                LastFailure = "No main bodies face each other — only isolated groups.";
                return;
            }

            SolveEngagement(blueBody, redBody);
        }

        /// <summary>
        /// Every cluster a side still has, as one body: the union of their units
        /// and a power-weighted centre.
        ///
        /// Isolated clusters are left out while the side has anything else — a
        /// cut-off battalion must not drag its side's front back across the map
        /// to reach it. A side that is *only* isolated clusters still gets a
        /// line built from them, because being surrounded is a shape of front
        /// rather than the absence of one, and the alternative is a side with no
        /// front at all.
        /// </summary>
        static Cluster Merge(List<Cluster> clusters)
        {
            bool anyMain = false;
            foreach (var c in clusters) if (!c.Isolated) { anyMain = true; break; }

            var merged = new Cluster();
            double lat = 0, lon = 0; float power = 0f;
            foreach (var c in clusters)
            {
                if (anyMain && c.Isolated) continue;
                merged.Units.AddRange(c.Units);
                lat += c.Lat * c.Power; lon += c.Lon * c.Power; power += c.Power;
            }
            if (power <= 0f) return null;

            merged.Lat = lat / power; merged.Lon = lon / power; merged.Power = power;
            return merged;
        }

        List<Cluster> BuildClusters(List<UnitActor> units)
        {
            var clusters = new List<Cluster>();
            var assigned = new bool[units.Count];

            for (int i = 0; i < units.Count; i++)
            {
                if (assigned[i]) continue;
                var cluster = new Cluster();
                var queue = new Queue<int>();
                queue.Enqueue(i); assigned[i] = true;

                while (queue.Count > 0)
                {
                    int k = queue.Dequeue();
                    cluster.Units.Add(units[k]);
                    for (int j = 0; j < units.Count; j++)
                    {
                        if (assigned[j]) continue;
                        if (GeoUtils.DistanceKm(units[k].State.latitude, units[k].State.longitude,
                                units[j].State.latitude, units[j].State.longitude) > ClusterLinkKm) continue;
                        assigned[j] = true; queue.Enqueue(j);
                    }
                }

                double lat = 0, lon = 0; float power = 0f;
                foreach (var u in cluster.Units)
                {
                    float w = FlotEligibility.Weight(u);
                    lat += u.State.latitude * w; lon += u.State.longitude * w; power += w;
                }
                if (power <= 0f) continue;
                cluster.Lat = lat / power; cluster.Lon = lon / power; cluster.Power = power;
                clusters.Add(cluster);
            }
            return clusters;
        }

        static float SidePower(List<Cluster> clusters)
        {
            float p = 0f;
            foreach (var c in clusters) p += c.Power;
            return p;
        }

        void MarkIsolation(List<Cluster> clusters, float sidePower)
        {
            Cluster main = null;
            foreach (var c in clusters) if (main == null || c.Power > main.Power) main = c;
            if (main == null) return;

            for (int i = clusters.Count - 1; i >= 0; i--)
            {
                var c = clusters[i];
                if (c == main) continue;
                double km = GeoUtils.DistanceKm(c.Lat, c.Lon, main.Lat, main.Lon);
                if (km <= IsolationKm) continue;      // detached but supported — solves normally

                if (c.Power < sidePower * OutlierPowerFraction)
                    clusters.RemoveAt(i);              // an outlier: no vote at all
                else
                    c.Isolated = true;                 // a pocket: its own stretch of front
            }
        }

        /// <summary>
        /// Solves the battle: both sides' forward edges across the band span,
        /// published as one segment per side. The frame and the nodes are kept
        /// so territory and breach queries can re-ask it later.
        /// </summary>
        void SolveEngagement(Cluster blue, Cluster red)
        {
            var eng = new Engagement { Blue = new List<Node>(), Red = new List<Node>() };

            // Local ENU frame about the engagement's own centre.
            eng.Lat0 = (blue.Lat * blue.Power + red.Lat * red.Power) / (blue.Power + red.Power);
            eng.Lon0 = (blue.Lon * blue.Power + red.Lon * red.Power) / (blue.Power + red.Power);
            double mPerDegLon = MetresPerDegLat * System.Math.Cos(eng.Lat0 * System.Math.PI / 180.0);

            Vector2 ToLocal(double lat, double lon) => new Vector2(
                (float)((lon - eng.Lon0) * mPerDegLon),
                (float)((lat - eng.Lat0) * MetresPerDegLat));

            // Operational direction: this cluster toward the enemy cluster it
            // faces. Per engagement, so a two-front battle has two forwards.
            Vector2 d = ToLocal(red.Lat, red.Lon) - ToLocal(blue.Lat, blue.Lon);
            if (d.sqrMagnitude < 1f)
            {
                LastFailure = "Two opposing forces are on top of each other — no front to draw.";
                return;
            }
            eng.Axis = d.normalized;
            eng.Cross = new Vector2(-eng.Axis.y, eng.Axis.x);

            float minLat = float.MaxValue, maxLat = float.MinValue;
            void AddNodes(List<UnitActor> units, List<Node> into)
            {
                foreach (var u in units)
                {
                    var p = ToLocal(u.State.latitude, u.State.longitude);
                    var n = new Node
                    {
                        Lateral = Vector2.Dot(p, eng.Cross),
                        Depth = Vector2.Dot(p, eng.Axis),
                        Power = FlotEligibility.Weight(u)
                    };
                    into.Add(n);
                    minLat = Mathf.Min(minLat, n.Lateral); maxLat = Mathf.Max(maxLat, n.Lateral);
                }
            }
            AddNodes(blue.Units, eng.Blue);
            AddNodes(red.Units, eng.Red);

            eng.Sigma = InfluenceWidthKm * 1000f;
            float pad = Mathf.Max(eng.Sigma, Mathf.Max((maxLat - minLat) * FlankPadFraction, MinFlankPadM));
            eng.MinLat = minLat - pad; eng.MaxLat = maxLat + pad;

            eng.BlueLead = float.MinValue; eng.RedLead = float.MaxValue;
            foreach (var n in eng.Blue) eng.BlueLead = Mathf.Max(eng.BlueLead, n.Depth);
            foreach (var n in eng.Red) eng.RedLead = Mathf.Min(eng.RedLead, n.Depth);

            _engagements.Add(eng);

            // Per-band forward edges. The blue edge is where blue's effective
            // control ends toward red; the red edge the reverse; the ground
            // between them is contested.
            int bands = Mathf.Max(3, Resolution);
            var blueDepth = new float[bands]; var blueSolved = new bool[bands];
            var redDepth = new float[bands]; var redSolved = new bool[bands];
            int bFirst = -1, bLast = -1, rFirst = -1, rLast = -1;
            float gapSum = 0f; int gapCount = 0;

            for (int i = 0; i < bands; i++)
            {
                float t = Mathf.Lerp(eng.MinLat, eng.MaxLat, i / (float)(bands - 1));
                float b = Edge(eng.Blue, t, eng.Sigma, eng.BlueLead, forward: true, out float bW);
                float r = Edge(eng.Red, t, eng.Sigma, eng.RedLead, forward: false, out float rW);

                if (bW > 0f)
                {
                    blueDepth[i] = b; blueSolved[i] = true;
                    if (bFirst < 0) bFirst = i;
                    bLast = i;
                }
                if (rW > 0f)
                {
                    redDepth[i] = r; redSolved[i] = true;
                    if (rFirst < 0) rFirst = i;
                    rLast = i;
                }
                if (bW > 0f && rW > 0f) { gapSum += (r - b) / 1000f; gapCount++; }
            }

            float meanGap = gapCount > 0 ? gapSum / gapCount : float.MaxValue;

            if (bFirst >= 0)
                AddEdgeSegment(eng, blueDepth, blueSolved, bFirst, bLast, bands,
                    Team.User, blue.Units.Count, meanGap, estimated: false);

            if (rFirst >= 0)
            {
                // The published enemy edge respects the fog: computed from the
                // formations the player can actually see, drawn broken because
                // an estimate should look like one. Breach detection and
                // territory keep using the true nodes stored on the engagement.
                bool fogged = FogOfWarSystem.Active != null && FogOfWarSystem.Active.InEffect;
                if (fogged)
                {
                    var visible = new List<Node>();
                    int k = 0;
                    foreach (var u in red.Units)
                    {
                        if (!u.HiddenByFog) visible.Add(eng.Red[k]);
                        k++;
                    }
                    if (visible.Count == 0) return;      // nothing seen — no estimate to draw

                    for (int i = 0; i < bands; i++)
                    {
                        float t = Mathf.Lerp(eng.MinLat, eng.MaxLat, i / (float)(bands - 1));
                        float r = Edge(visible, t, eng.Sigma, eng.RedLead, forward: false, out float rW);
                        redSolved[i] = rW > 0f;
                        if (redSolved[i]) redDepth[i] = r;
                    }
                    rFirst = -1; rLast = -1;
                    for (int i = 0; i < bands; i++)
                        if (redSolved[i]) { if (rFirst < 0) rFirst = i; rLast = i; }
                    if (rFirst < 0) return;

                    AddEdgeSegment(eng, redDepth, redSolved, rFirst, rLast, bands,
                        Team.Enemy, visible.Count, meanGap, estimated: true);
                }
                else
                {
                    AddEdgeSegment(eng, redDepth, redSolved, rFirst, rLast, bands,
                        Team.Enemy, red.Units.Count, meanGap, estimated: false);
                }
            }
        }

        void AddEdgeSegment(Engagement eng, float[] depth, bool[] solved, int first, int last,
            int bands, Team team, int contributors, float meanGap, bool estimated)
        {
            FillUnsolved(depth, solved, first, last);

            var pts = new List<Vector2>(bands);
            float meanDepth = 0f;
            for (int i = 0; i < bands; i++)
            {
                float t = Mathf.Lerp(eng.MinLat, eng.MaxLat, i / (float)(bands - 1));
                pts.Add(new Vector2(t, depth[i]));
                meanDepth += depth[i];
            }
            meanDepth /= bands;

            for (int pass = 0; pass < SmoothingPasses; pass++) pts = Chaikin(pts);

            var seg = new FlotSegment
            {
                Id = team == Team.User ? UserLineId : EnemyLineId,
                Team = team,
                Points = ToGeo(eng, pts),
                Contributors = contributors,
                Estimated = estimated
            };

            // §7 stability, and §11 state. Advance is measured against this
            // segment's own previous mean depth: toward the enemy is positive
            // for both sides, so the sign reads the same on both lines.
            float prev = _prevMeanDepth.TryGetValue(seg.Id, out float p) ? p : meanDepth;
            float advance = (team == Team.User ? meanDepth - prev : prev - meanDepth) / 1000f;
            _prevMeanDepth[seg.Id] = meanDepth;
            seg.AdvanceKm = advance;

            seg.State =
                advance <= -CollapseKm ? FlotState.Collapsing
                : meanGap < ContestGapKm ? FlotState.Contested
                : advance >= StateMoveKm ? FlotState.Advancing
                : advance <= -StateMoveKm ? FlotState.Retreating
                : FlotState.Stable;

            _segments.Add(seg);
        }

        List<GeoPoint> ToGeo(Engagement eng, List<Vector2> pts)
        {
            double mPerDegLon = MetresPerDegLat * System.Math.Cos(eng.Lat0 * System.Math.PI / 180.0);
            var geo = new List<GeoPoint>(pts.Count);
            foreach (var p in pts)
            {
                float east = eng.Cross.x * p.x + eng.Axis.x * p.y;
                float north = eng.Cross.y * p.x + eng.Axis.y * p.y;
                geo.Add(new GeoPoint
                {
                    latitude = eng.Lat0 + north / MetresPerDegLat,
                    longitude = eng.Lon0 + east / System.Math.Max(1.0, mPerDegLon)
                });
            }
            return geo;
        }

        /// <summary>
        /// One side's forward edge at a point along the front: a weighted mean
        /// of its formations' depths — power × Gaussian across the front ×
        /// exponential toward the enemy, so the battalions in contact decide
        /// the line and the rear does not.
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

                float behind = forward ? lead - n.Depth : n.Depth - lead;
                float depthW = Mathf.Exp(-Mathf.Max(0f, behind) / bias);

                float w = n.Power * lateralW * depthW;
                sum += n.Depth * w;
                weight += w;
            }
            return weight > 0f ? sum / weight : 0f;
        }

        /// <summary>Carries the edge out past the flanks and bridges gaps, so nothing is dropped.</summary>
        static void FillUnsolved(float[] depth, bool[] solved, int first, int last)
        {
            for (int i = 0; i < first; i++) depth[i] = depth[first];
            for (int i = last + 1; i < depth.Length; i++) depth[i] = depth[last];

            for (int i = first + 1; i < last; i++)
            {
                if (solved[i]) continue;
                int a = i - 1, b = i + 1;
                while (b < last && !solved[b]) b++;
                for (int k = i; k < b; k++)
                    depth[k] = Mathf.Lerp(depth[a], depth[b], (k - a) / (float)(b - a));
                i = b - 1;
            }
        }

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

        // ------------------------------------------------------------ manual

        readonly List<GeoPoint> _manual = new List<GeoPoint>();

        /// <summary>True while the designer is clicking the line onto the map.</summary>
        public bool Drawing { get; private set; }

        public void StartDrawing()
        {
            if (EffectiveMode != FlotMode.Manual)
            {
                Flash?.Invoke("Switch the FLOT to MANUAL or HYBRID (before battle) to draw it.");
                return;
            }
            Drawing = true;
            _manual.Clear();
            Flash?.Invoke("Drawing the FLOT — click along the line. Enter/RMB finishes (min 2), " +
                          "Backspace undoes, Esc cancels.");
        }

        public void CancelDrawing() => Drawing = false;

        void HandleDrawing()
        {
            if (!Drawing) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Drawing = false;
                _manual.Clear();
                Recompute();
                Flash?.Invoke("FLOT drawing cancelled.");
                return;
            }
            if (Input.GetKeyDown(KeyCode.Backspace) && _manual.Count > 0)
            {
                _manual.RemoveAt(_manual.Count - 1);
                Recompute();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(1))
            {
                if (_manual.Count < 2) { Flash?.Invoke("A line needs at least two points."); return; }
                Drawing = false;
                Recompute();
                Flash?.Invoke($"FLOT drawn — {_manual.Count} points.");
                return;
            }

            if (!Input.GetMouseButtonDown(0)) return;
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
            if (_map == null || !_map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                Flash?.Invoke("Terrain not loaded there yet — try again in a moment.");
                return;
            }

            GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out _);
            _manual.Add(new GeoPoint { latitude = lat, longitude = lon });
            Recompute();      // live preview: the line grows as it is clicked
        }

        void SolveManual()
        {
            BlueCount = RedCount = 0;

            // A map saved in manual mode carries the drawn line as an ordinary
            // MapLine; adopt its points so the trace survives a reload.
            if (_manual.Count < 2)
            {
                var saved = _lines.Find(ManualLineId);
                if (saved != null && saved.Data.points.Count >= 2)
                    foreach (var p in saved.Data.points)
                        _manual.Add(new GeoPoint { latitude = p.latitude, longitude = p.longitude });
            }

            if (_manual.Count < 2)
            {
                if (!Drawing) LastFailure = "No FLOT drawn yet — DRAW FLOT in the panel puts one down.";
                return;
            }

            var pts = new List<GeoPoint>(_manual.Count);
            foreach (var p in _manual) pts.Add(new GeoPoint { latitude = p.latitude, longitude = p.longitude });

            _segments.Add(new FlotSegment
            {
                Id = ManualLineId,
                Team = Team.User,
                Manual = true,
                Points = pts,
                State = FlotState.Stable,
                Contributors = 0
            });

            // Queries against a manual line share the automatic machinery: one
            // synthetic engagement whose two edges are both the drawn trace.
            BuildManualEngagement(pts);
        }

        void BuildManualEngagement(List<GeoPoint> pts)
        {
            // Forward still has to mean something: from the blue force's centre
            // toward the red force's. Without both sides on the map there is
            // nothing to breach and no territory to assign, and skipping the
            // engagement is the honest answer.
            double bLat = 0, bLon = 0, rLat = 0, rLon = 0; float bP = 0, rP = 0;
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || u.Def == null) continue;
                float w = Mathf.Max(0.01f, u.CurrentPower());
                if (u.State.TeamEnum == Team.User) { bLat += u.State.latitude * w; bLon += u.State.longitude * w; bP += w; }
                else { rLat += u.State.latitude * w; rLon += u.State.longitude * w; rP += w; }
            }
            if (bP <= 0f || rP <= 0f) return;
            bLat /= bP; bLon /= bP; rLat /= rP; rLon /= rP;

            var eng = new Engagement { Blue = new List<Node>(), Red = new List<Node>() };
            var mid = pts[pts.Count / 2];
            eng.Lat0 = mid.latitude; eng.Lon0 = mid.longitude;
            double mPerDegLon = MetresPerDegLat * System.Math.Cos(eng.Lat0 * System.Math.PI / 180.0);

            Vector2 ToLocal(double lat, double lon) => new Vector2(
                (float)((lon - eng.Lon0) * mPerDegLon),
                (float)((lat - eng.Lat0) * MetresPerDegLat));

            Vector2 d = ToLocal(rLat, rLon) - ToLocal(bLat, bLon);
            if (d.sqrMagnitude < 1f) return;
            eng.Axis = d.normalized;
            eng.Cross = new Vector2(-eng.Axis.y, eng.Axis.x);
            eng.Sigma = InfluenceWidthKm * 1000f;
            eng.BlueLead = 0f; eng.RedLead = 0f;

            // The drawn trace becomes both edges: heavy synthetic nodes along
            // it, so Edge() reproduces the line and the same breach/territory
            // code runs unchanged.
            float minLat = float.MaxValue, maxLat = float.MinValue;
            foreach (var p in pts)
            {
                var local = ToLocal(p.latitude, p.longitude);
                var n = new Node
                {
                    Lateral = Vector2.Dot(local, eng.Cross),
                    Depth = Vector2.Dot(local, eng.Axis),
                    Power = 1000f
                };
                eng.Blue.Add(n); eng.Red.Add(n);
                minLat = Mathf.Min(minLat, n.Lateral); maxLat = Mathf.Max(maxLat, n.Lateral);
            }
            eng.MinLat = minLat; eng.MaxLat = maxLat;
            foreach (var n in eng.Blue) { eng.BlueLead = Mathf.Max(eng.BlueLead, n.Depth); }
            eng.RedLead = float.MaxValue;
            foreach (var n in eng.Red) { eng.RedLead = Mathf.Min(eng.RedLead, n.Depth); }

            _engagements.Add(eng);
        }

        // ----------------------------------------------------------- publish

        void Publish()
        {
            // The old solver's single midline, if this map was saved before
            // per-side edges existed.
            var legacy = _lines.Find(LegacyLineId);
            if (legacy != null) _lines.Remove(legacy);

            var newIds = new HashSet<string>();
            double userLength = 0;

            foreach (var seg in _segments)
            {
                newIds.Add(seg.Id);

                // §7 stability: below the movement threshold the previous
                // geometry is kept (no shaking); above it but nearby, the line
                // blends halfway per solve rather than snapping.
                if (_prevPoints.TryGetValue(seg.Id, out var prev) && prev.Count == seg.Points.Count)
                {
                    double meanM = 0;
                    for (int i = 0; i < prev.Count; i++)
                        meanM += GeoUtils.DistanceKm(prev[i].latitude, prev[i].longitude,
                            seg.Points[i].latitude, seg.Points[i].longitude) * 1000.0;
                    meanM /= prev.Count;

                    if (meanM < MinMoveM) seg.Points = prev;
                    else if (meanM < 3000.0)
                        for (int i = 0; i < prev.Count; i++)
                        {
                            seg.Points[i].latitude = (prev[i].latitude + seg.Points[i].latitude) * 0.5;
                            seg.Points[i].longitude = (prev[i].longitude + seg.Points[i].longitude) * 0.5;
                        }
                }
                _prevPoints[seg.Id] = ClonePoints(seg.Points);

                var line = _lines.Find(seg.Id);
                if (line == null)
                {
                    line = _lines.Add(new MapLineData
                    {
                        id = seg.Id,
                        kind = nameof(LineKind.Boundary),
                        team = seg.Team.ToString(),
                        is3D = false,
                        autoGenerated = true,
                        points = seg.Points
                    });
                }
                else line.SetPoints(seg.Points);

                line.Data.label = LabelFor(seg);
                line.Data.planned = seg.Estimated;      // an estimate draws broken
                line.Data.colorHex = !string.IsNullOrEmpty(_customHex) ? _customHex
                    : seg.Team == Team.User && !seg.Manual
                        ? "#" + ColorUtility.ToHtmlStringRGB(GameConfig.BlueTeam)
                        : "";                            // red/manual: the system default
                if (_customWidth > 0f) line.Data.widthMeters = _customWidth;
                line.RefreshStyle();
                line.SetPickable(true);
                line.gameObject.SetActive(Visible);

                seg.LengthKm = line.LengthKm;
                if (seg.Team == Team.User) userLength += seg.LengthKm;
            }

            // Anything published last solve and absent now comes down — the
            // enemy edge when fog closes over it, or the manual trace when the
            // mode changes.
            foreach (var id in _publishedIds)
            {
                if (newIds.Contains(id)) continue;
                var stale = _lines.Find(id);
                if (stale != null) _lines.Remove(stale);
                _prevPoints.Remove(id);
                _prevMeanDepth.Remove(id);
            }

            // And so does anything left on the map that this system owns but no
            // longer publishes: a scenario saved before the front became one
            // line per side carries flot-user-0, flot-pocket-… and friends in
            // its line list, and they would otherwise sit there for ever as
            // traces nothing updates.
            //
            // The drawn line is the exception — it is the designer's own work,
            // read back by SolveManual when the mode returns to MANUAL, and
            // deleting it because the map happens to be solving automatically
            // right now would throw it away.
            for (int i = _lines.Lines.Count - 1; i >= 0; i--)
            {
                var line = _lines.Lines[i];
                if (line == null || !IsFlotLine(line.Data.id)) continue;
                if (line.Data.id == ManualLineId || newIds.Contains(line.Data.id)) continue;
                _lines.Remove(line);
            }

            _publishedIds.Clear();
            foreach (var id in newIds) _publishedIds.Add(id);

            LengthKm = userLength;
        }

        string LabelFor(FlotSegment seg)
        {
            if (seg.Manual || seg.Team == Team.User)
                return string.IsNullOrEmpty(HoldingGroupName) || seg.Id != MainSegment(Team.User)?.Id
                    ? "FLOT"
                    : "FLOT — " + HoldingGroupName.ToUpperInvariant();
            return seg.Estimated ? "ENEMY FLOT (EST)" : "ENEMY FLOT";
        }

        static List<GeoPoint> ClonePoints(List<GeoPoint> pts)
        {
            var copy = new List<GeoPoint>(pts.Count);
            foreach (var p in pts)
                copy.Add(new GeoPoint { latitude = p.latitude, longitude = p.longitude, heightMeters = p.heightMeters });
            return copy;
        }

        // ------------------------------------------------------------ breach

        /// <summary>
        /// FLOT_BREACH: an enemy *cluster* — not a lone probing unit — standing
        /// more than <see cref="BreachDepthKm"/> behind a side's forward edge
        /// with at least <see cref="BreachPowerFraction"/> of the victim's own
        /// power. A real breakthrough moves the alarm; a scout does not.
        /// </summary>
        void DetectBreaches()
        {
            var current = new HashSet<string>();

            void Check(Team victim)
            {
                // Intruders: eligible enemy formations clustered, so power is
                // judged per force rather than per counter.
                var intruders = new List<UnitActor>();
                float victimPower = 0f;
                foreach (var u in UnitRegistry.All)
                {
                    float w = FlotEligibility.Weight(u);
                    if (w <= 0f) continue;
                    if (u.State.TeamEnum == victim) victimPower += w;
                    else intruders.Add(u);
                }
                if (victimPower <= 0f || intruders.Count == 0) return;

                foreach (var cluster in BuildClusters(intruders))
                {
                    if (cluster.Power < victimPower * BreachPowerFraction) continue;

                    double penetration = PenetrationKm(victim, cluster.Lat, cluster.Lon);
                    if (penetration < BreachDepthKm) continue;

                    string key = $"{victim}:{cluster.Lat:0.00}:{cluster.Lon:0.00}";
                    current.Add(key);
                    if (_activeBreaches.Contains(key)) continue;

                    _activeBreaches.Add(key);
                    MarkBreached(victim, cluster.Lat, cluster.Lon);
                    Breach?.Invoke(victim, cluster.Lat, cluster.Lon, penetration);
                }
            }

            Check(Team.User);
            Check(Team.Enemy);

            // A breach that no longer exists may fire again if it re-happens.
            _activeBreaches.RemoveWhere(k => !current.Contains(k));
        }

        /// <summary>Km a point stands past a side's forward edge, 0 if in front of it or off every span.</summary>
        double PenetrationKm(Team victim, double lat, double lon)
        {
            double worst = 0;
            foreach (var eng in _engagements)
            {
                double mPerDegLon = MetresPerDegLat * System.Math.Cos(eng.Lat0 * System.Math.PI / 180.0);
                var p = new Vector2(
                    (float)((lon - eng.Lon0) * mPerDegLon),
                    (float)((lat - eng.Lat0) * MetresPerDegLat));
                float lateral = Vector2.Dot(p, eng.Cross);
                float depth = Vector2.Dot(p, eng.Axis);
                if (lateral < eng.MinLat || lateral > eng.MaxLat) continue;

                if (victim == Team.User)
                {
                    float edge = Edge(eng.Blue, lateral, eng.Sigma, eng.BlueLead, forward: true, out float w);
                    if (w > 0f && depth < edge) worst = System.Math.Max(worst, (edge - depth) / 1000.0);
                }
                else
                {
                    float edge = Edge(eng.Red, lateral, eng.Sigma, eng.RedLead, forward: false, out float w);
                    if (w > 0f && depth > edge) worst = System.Math.Max(worst, (depth - edge) / 1000.0);
                }
            }
            return worst;
        }

        void MarkBreached(Team victim, double lat, double lon)
        {
            // The breached stretch is the victim's segment nearest the
            // intrusion — its state is overridden until the intruder is gone.
            FlotSegment nearest = null; double best = double.MaxValue;
            foreach (var seg in _segments)
            {
                if (seg.Team != victim || seg.Points.Count == 0) continue;
                var mid = seg.Points[seg.Points.Count / 2];
                double km = GeoUtils.DistanceKm(mid.latitude, mid.longitude, lat, lon);
                if (km < best) { best = km; nearest = seg; }
            }
            if (nearest != null) nearest.State = FlotState.Breached;
        }

        // --------------------------------------------------------- territory

        /// <summary>
        /// Who holds a point on the map, as the front lines read it: behind the
        /// blue edge is blue, behind the red edge red, between them contested.
        /// Off every engagement's span, the nearer force's side wins — depth
        /// without a front is just distance.
        /// </summary>
        public TerritoryOwner TerritoryAt(double lat, double lon)
        {
            foreach (var eng in _engagements)
            {
                double mPerDegLon = MetresPerDegLat * System.Math.Cos(eng.Lat0 * System.Math.PI / 180.0);
                var p = new Vector2(
                    (float)((lon - eng.Lon0) * mPerDegLon),
                    (float)((lat - eng.Lat0) * MetresPerDegLat));
                float lateral = Vector2.Dot(p, eng.Cross);
                float depth = Vector2.Dot(p, eng.Axis);
                if (lateral < eng.MinLat || lateral > eng.MaxLat) continue;

                float blue = Edge(eng.Blue, lateral, eng.Sigma, eng.BlueLead, forward: true, out float bW);
                float red = Edge(eng.Red, lateral, eng.Sigma, eng.RedLead, forward: false, out float rW);
                if (bW <= 0f && rW <= 0f) continue;
                if (bW <= 0f) return depth > red ? TerritoryOwner.Red : TerritoryOwner.Contested;
                if (rW <= 0f) return depth < blue ? TerritoryOwner.Blue : TerritoryOwner.Contested;

                if (blue > red) return TerritoryOwner.Contested;    // interpenetration
                if (depth <= blue) return TerritoryOwner.Blue;
                if (depth >= red) return TerritoryOwner.Red;
                return TerritoryOwner.Contested;
            }

            // No front covers this ground: nearest force decides.
            double bestBlue = double.MaxValue, bestRed = double.MaxValue;
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive) continue;
                double km = GeoUtils.DistanceKm(lat, lon, u.State.latitude, u.State.longitude);
                if (u.State.TeamEnum == Team.User) bestBlue = System.Math.Min(bestBlue, km);
                else bestRed = System.Math.Min(bestRed, km);
            }
            if (bestBlue == double.MaxValue && bestRed == double.MaxValue) return TerritoryOwner.Contested;
            return bestBlue <= bestRed ? TerritoryOwner.Blue : TerritoryOwner.Red;
        }

        // ----------------------------------------------------------- history

        void TakeHistorySnapshot()
        {
            if (Clock == null) return;
            double now = Clock.Now.Ticks / (double)System.TimeSpan.TicksPerSecond;
            if (now - _lastHistoryGameSecond < HistoryGameSeconds) return;
            _lastHistoryGameSecond = now;

            var blue = MainSegment(Team.User);
            var red = MainSegment(Team.Enemy);
            if (blue == null && red == null) return;

            var entry = new HistoryEntry { Time = Clock.TimeText };
            if (blue != null) MeanOf(blue.Points, out entry.BlueLat, out entry.BlueLon);
            if (red != null) MeanOf(red.Points, out entry.RedLat, out entry.RedLon);

            _history.Add(entry);
            if (_history.Count > HistoryCap) _history.RemoveAt(0);
        }

        static void MeanOf(List<GeoPoint> pts, out double lat, out double lon)
        {
            lat = lon = 0;
            if (pts.Count == 0) return;
            foreach (var p in pts) { lat += p.latitude; lon += p.longitude; }
            lat /= pts.Count; lon /= pts.Count;
        }

        /// <summary>Km the blue main edge has moved since the oldest kept snapshot. For the panel.</summary>
        public double MovementSinceKm()
        {
            var blue = MainSegment(Team.User);
            if (blue == null || _history.Count == 0) return 0;
            MeanOf(blue.Points, out double lat, out double lon);
            var oldest = _history[0];
            if (oldest.BlueLat == 0 && oldest.BlueLon == 0) return 0;
            return GeoUtils.DistanceKm(oldest.BlueLat, oldest.BlueLon, lat, lon);
        }
    }
}
