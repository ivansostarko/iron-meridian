using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.Lines
{
    /// <summary>
    /// A rendered polyline on the globe: sector boundary, defensive line or
    /// battle position.
    ///
    /// **The line is draped, not strung.** Only the vertices the player clicked
    /// used to be clamped to the ground, so a boundary drawn with two clicks
    /// across ten kilometres was a single straight chord between two terrain
    /// heights — it flew over the valleys and disappeared inside every ridge in
    /// between. Each segment is now subdivided at
    /// <see cref="DrapeSpacingM"/> and every sample is clamped, so the line lies
    /// on the ground the whole way along it. The subdivision is capped
    /// (<see cref="MaxRenderPoints"/>) so a hundred-kilometre phase line costs a
    /// bounded number of terrain samples rather than one per two hundred metres.
    ///
    /// **Boundaries are a 2D graphic.** A control measure is a line drawn on a
    /// map, not a fence standing in the world: it exists to say where one
    /// formation's ground ends and the next one's begins, and a wall of colour
    /// reaching into the sky hides the very terrain the boundary is being judged
    /// against. So every kind in <see cref="FlatOnly"/> ignores the 2D/3D flag
    /// entirely and is always drawn draped, with height used for nothing except
    /// putting the line on the terrain (<see cref="ClearanceDrapedM"/>). The
    /// remaining kinds — defensive lines and battle positions, which mark ground
    /// that is being physically held — keep the old behaviour, where the flag
    /// chooses how far clear of the ground the graphic floats.
    ///
    /// **Draped is not the same as flat, and both are needed.** Clamping the
    /// vertices to the ground only fixes where the ribbon *is*; a
    /// <c>LineRenderer</c> at <see cref="LineAlignment.View"/> then rotates that
    /// ribbon to face the camera, so a 70 m-wide front line tilts up out of the
    /// terrain as soon as the view is off vertical and reads as a wall of colour
    /// standing in the landscape — exactly the thing a control measure must not
    /// be. Flat kinds are therefore aligned to their own transform's Z
    /// (<see cref="OrientFlat"/>), which is pointed along the local geodetic up,
    /// so the ribbon lies in the ground plane and is painted on the map from
    /// every camera angle. They are also draped at
    /// <see cref="FlatDrapeSpacingM"/> rather than the standard spacing: a line
    /// lying *on* the ground shows every fold it crosses, so it has to be
    /// sampled finely enough to follow them.
    ///
    /// Because Cesium streams tiles, the first clamp routinely finds no ground.
    /// The line keeps re-clamping until every sample has real terrain under it,
    /// then stops — with an attempt cap, because ground the camera never goes
    /// near never streams in and a line waiting for it would sample forever.
    /// </summary>
    public class MapLine : MonoBehaviour
    {
        public MapLineData Data { get; private set; }

        /// <summary>Metres above the terrain in the tilted 3D view.</summary>
        const double Clearance3DM = 25.0;
        /// <summary>Metres above the terrain in the top-down 2D view.</summary>
        const double Clearance2DM = 140.0;
        /// <summary>
        /// Metres above the terrain for a draped control measure, in both view
        /// modes. Small: the line is meant to read as painted on the ground, and
        /// the only reason it is not at zero is that a polyline sitting exactly
        /// on a streamed mesh z-fights with it.
        /// </summary>
        const double ClearanceDrapedM = 30.0;
        /// <summary>Ground spacing between draped samples along a segment, metres.</summary>
        const double DrapeSpacingM = 220.0;
        /// <summary>
        /// Ground spacing for a <see cref="FlatOnly"/> kind. Tighter, because
        /// these lie on the terrain rather than floating over it: at 220 m a
        /// hand-drawn boundary bridges a re-entrant instead of running down
        /// into it, and the part that bridges is buried.
        ///
        /// Not tighter still, deliberately. The front line already arrives with
        /// its own vertices ~125 m apart (41 bands, three Chaikin passes), and
        /// every sample is a terrain raycast on a line that is re-solved every
        /// few seconds — halving this would double that cost for ground
        /// resolution finer than the streamed terrain itself.
        /// </summary>
        const double FlatDrapeSpacingM = 150.0;
        /// <summary>Ceiling on rendered vertices — the drape spacing is widened to stay under it.</summary>
        const int MaxRenderPoints = 512;
        /// <summary>
        /// The same ceiling for a flat kind. Higher, because the front line is
        /// the longest graphic on the map and the one whose whole job is to
        /// follow the ground; it is still bounded, so a hundred-kilometre line
        /// costs a fixed number of terrain samples rather than one per 150 m.
        /// </summary>
        const int MaxFlatRenderPoints = 768;
        /// <summary>Seconds between re-clamps while some sample still has no terrain under it.</summary>
        const float ReclampSeconds = 1.5f;
        /// <summary>
        /// How many times a line will go back for missing ground before giving
        /// up. Terrain the camera never approaches is never streamed, and
        /// without this a line drawn across it re-samples every 1.5 s forever.
        /// </summary>
        const int MaxReclampAttempts = 24;

        CesiumGeoreference _geo;
        LineRenderer _lr;
        readonly List<MapLabel> _labels = new List<MapLabel>();
        /// <summary>World positions actually drawn — the authored points plus the draped samples between them.</summary>
        readonly List<Vector3> _render = new List<Vector3>();
        int _unresolved;
        float _reclampTimer;
        int _reclampAttempts;
        LineKind _kind;

        /// <summary>
        /// True for the kinds that are map graphics rather than things standing
        /// on the ground: sector boundaries, rear boundaries, phase lines and the
        /// legacy hand-drawn boundary (which the automatic front line also uses).
        /// These are always draped and never stand up in 3D — see the class
        /// remarks.
        /// </summary>
        public bool FlatOnly =>
            _kind == LineKind.LateralBoundary || _kind == LineKind.RearBoundary ||
            _kind == LineKind.PhaseLine || _kind == LineKind.Boundary;

        // --- click picking ---
        /// <summary>Multiplier and floor applied to the drawn width to get a click target.</summary>
        const float PickWidthFactor = 6f, MinPickWidthM = 400f;
        MeshCollider _picker;
        Mesh _pickMesh;
        float _lastWidth;

        /// <summary>
        /// Whether this line can be clicked on the map. Off by default: control
        /// measures are drawn *over* the ground you are trying to click, and
        /// making every phase line a click target would mean a boundary
        /// swallowing every order given near it.
        ///
        /// The automatic front line turns it on, because it is the one line with
        /// settings of its own worth opening — see
        /// <see cref="FrontlineSystem"/> and <see cref="UI.FrontlinePanelUI"/>.
        /// </summary>
        public bool Pickable { get; private set; }

        public void SetPickable(bool pickable)
        {
            if (Pickable == pickable) return;
            Pickable = pickable;

            if (pickable) BuildPicker();
            else if (_picker != null) _picker.enabled = false;
        }

        /// <summary>Length of the drawn line along the ground, in kilometres.</summary>
        public double LengthKm
        {
            get
            {
                double km = 0;
                var pts = Data.points;
                for (int i = 0; i + 1 < pts.Count; i++)
                    km += Map.GeoUtils.DistanceKm(pts[i].latitude, pts[i].longitude,
                                                  pts[i + 1].latitude, pts[i + 1].longitude);
                return km;
            }
        }

        /// <summary>Re-reads the style from <see cref="Data"/> — colour, width, planned/actual.</summary>
        public void RefreshStyle()
        {
            ApplyStyle();
            RefreshLabels();
            if (Pickable) BuildPicker();
        }

        public static MapLine Create(CesiumGeoreference geo, MapLineData data)
        {
            var go = new GameObject($"Line_{data.kind}_{data.id}");
            go.transform.SetParent(geo.transform, false);
            var line = go.AddComponent<MapLine>();
            line.Build(geo, data);
            return line;
        }

        void Build(CesiumGeoreference geo, MapLineData data)
        {
            _geo = geo; Data = data;
            System.Enum.TryParse(data.kind, out _kind);
            // A boundary saved from an older map may still carry is3D; the flag
            // is meaningless for these kinds now, so it is normalised on load
            // rather than being quietly ignored in two places.
            if (FlatOnly) data.is3D = false;

            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.useWorldSpace = true;
            _lr.textureMode = LineTextureMode.Tile;
            // Flat kinds lie in the ground plane and are aligned to this
            // object's Z, which OrientFlat points along the local up. Everything
            // else is a graphic standing in the world and keeps billboarding.
            _lr.alignment = FlatOnly ? LineAlignment.TransformZ : LineAlignment.View;
            _lr.numCapVertices = 4;
            _lr.numCornerVertices = 4;
            ApplyStyle();
            Rebuild();

            // LineRenderer positions are absolute world-space, but Cesium
            // periodically re-origins the georeference for floating-point
            // precision as the camera roams — without this, drawn lines
            // drift away from the terrain the moment that happens.
            _geo.changed += Rebuild;
        }

        void OnDestroy()
        {
            if (_geo != null) _geo.changed -= Rebuild;
            if (_pickMesh != null) Destroy(_pickMesh);
        }

        void ApplyStyle()
        {
            LineKind kind = _kind;

            Color color;
            float width;
            switch (kind)
            {
                case LineKind.LateralBoundary:
                case LineKind.RearBoundary:
                    // Boundaries belong to the formation whose AO they bound,
                    // so they take the owning side's colour.
                    color = SideColor(GameConfig.BoundaryYellow);
                    width = 45f;
                    break;

                case LineKind.Feba:
                    color = SideColor(GameConfig.BlueTeam);
                    width = 80f;
                    break;

                case LineKind.PhaseLine:
                    color = GameConfig.BoundaryYellow;
                    width = 45f;
                    break;

                case LineKind.Boundary:
                    // Two very different things share this kind: the tool
                    // strip's legacy DRAW BOUNDARY, which is a hand-drawn sector
                    // boundary and keeps the doctrinal yellow, and the automatic
                    // front line, which is not a control measure anyone drew but
                    // a statement about where the fighting is. Only the second
                    // one is red and heavy — see GameConfig.FrontlineRed.
                    color = Data.autoGenerated ? GameConfig.FrontlineRed : GameConfig.BoundaryYellow;
                    width = Data.autoGenerated ? 70f : 55f;
                    break;

                case LineKind.BattlePosition:
                    // The ground a formation defends from, not the line it holds:
                    // thinner than the defence line it wraps, so the two read as
                    // an area and its forward edge rather than as two lines.
                    color = SideColor(GameConfig.BlueTeam);
                    width = 32f;
                    break;

                default:                                   // DefensiveLine
                    color = SideColor(GameConfig.BlueTeam);
                    width = 85f;
                    break;
            }

            // Scenario overrides win over the doctrinal defaults above.
            if (TryParseHex(Data.colorHex, out Color custom)) color = custom;
            if (Data.widthMeters > 0f) width = Data.widthMeters;

            // FM 101-5-1 / SS0529: actual control measures are solid, planned
            // or on-order ones are broken.
            var mat = Data.planned
                ? RuntimeMaterials.UnlitTexture(ProceduralTextures.Dash(color, 64, 0.5f))
                : RuntimeMaterials.UnlitColor(color);
            if (Data.planned) mat.color = color;

            _lr.startWidth = _lr.endWidth = width;
            _lr.material = mat;
            _lastWidth = width;
        }

        /// <summary>Parses "#RRGGBB" / "RRGGBB". Returns false for empty or malformed values.</summary>
        static bool TryParseHex(string hex, out Color color)
        {
            color = default;
            if (string.IsNullOrEmpty(hex)) return false;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out color);
        }

        Color SideColor(Color fallback) =>
            Data.team == Team.Enemy.ToString() ? GameConfig.RedTeam
            : Data.team == Team.User.ToString() ? GameConfig.BlueTeam
            : fallback;

        /// <summary>
        /// Recompute world positions from the geodetic points, draping the line
        /// over the terrain between them.
        ///
        /// The authored vertices keep their own resolved heights — those are
        /// what the save file carries, and what every other reader of
        /// <see cref="MapLineData"/> sees. The extra samples exist only to draw
        /// with, which is why they live in <see cref="_render"/> and not in the
        /// data.
        /// </summary>
        public void Rebuild()
        {
            var pts = Data.points;
            double clearance = FlatOnly ? ClearanceDrapedM
                : (Data.is3D ? Clearance3DM : Clearance2DM);

            _unresolved = 0;
            _render.Clear();

            if (pts.Count == 0)
            {
                _lr.positionCount = 0;
                _reclampTimer = ReclampSeconds;
                RefreshLabels();
                return;
            }

            // Authored vertices first: they are the ones whose heights persist.
            for (int i = 0; i < pts.Count; i++)
            {
                if (GeoUtils.TrySampleTerrainHeight(_geo, pts[i].latitude, pts[i].longitude, out double ground))
                {
                    pts[i].heightMeters = ground + clearance;
                }
                else
                {
                    // Nothing under this vertex yet. Keep whatever height it had
                    // (a saved value, or the fallback) so the line is still drawn,
                    // and come back for it.
                    if (pts[i].heightMeters <= 0.0) pts[i].heightMeters = 250.0 + clearance;
                    _unresolved++;
                }
            }

            double spacing = DrapeSpacing(pts);

            _render.Add(GeoUtils.GeoToUnity(_geo, pts[0].latitude, pts[0].longitude, pts[0].heightMeters));

            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                double metres = GeoUtils.DistanceKm(a.latitude, a.longitude, b.latitude, b.longitude) * 1000.0;
                int steps = Mathf.Max(1, Mathf.CeilToInt((float)(metres / spacing)));

                for (int s = 1; s <= steps; s++)
                {
                    double t = s / (double)steps;
                    double lat = a.latitude + (b.latitude - a.latitude) * t;
                    double lon = a.longitude + (b.longitude - a.longitude) * t;

                    double height;
                    if (s == steps)
                    {
                        height = b.heightMeters;         // already resolved above
                    }
                    else if (GeoUtils.TrySampleTerrainHeight(_geo, lat, lon, out double ground))
                    {
                        height = ground + clearance;
                    }
                    else
                    {
                        // No ground under this sample: fall back to the straight
                        // chord between the two authored heights, which is what
                        // the line used to be everywhere.
                        height = a.heightMeters + (b.heightMeters - a.heightMeters) * t;
                        _unresolved++;
                    }

                    _render.Add(GeoUtils.GeoToUnity(_geo, lat, lon, height));
                }
            }

            _lr.positionCount = _render.Count;
            _lr.SetPositions(_render.ToArray());

            if (FlatOnly) OrientFlat(pts[pts.Count / 2]);

            _reclampTimer = ReclampSeconds;
            RefreshLabels();
            if (Pickable) BuildPicker();
        }

        /// <summary>
        /// Ground distance between draped samples. <see cref="DrapeSpacingM"/>
        /// normally; widened when that would blow the vertex ceiling, so the
        /// cost of a line is bounded by its point count rather than by its
        /// length.
        /// </summary>
        double DrapeSpacing(List<GeoPoint> pts)
        {
            double metres = 0.0;
            for (int i = 0; i + 1 < pts.Count; i++)
                metres += GeoUtils.DistanceKm(pts[i].latitude, pts[i].longitude,
                    pts[i + 1].latitude, pts[i + 1].longitude) * 1000.0;

            // Every segment costs at least one sample, so the budget for the
            // subdivision is what is left after the authored points.
            int ceiling = FlatOnly ? MaxFlatRenderPoints : MaxRenderPoints;
            double target = FlatOnly ? FlatDrapeSpacingM : DrapeSpacingM;
            int budget = Mathf.Max(1, ceiling - pts.Count);
            return System.Math.Max(target, metres / budget);
        }

        /// <summary>
        /// Points this object's Z along the local geodetic up, which is what
        /// makes a <see cref="LineAlignment.TransformZ"/> ribbon lie flat on the
        /// ground instead of standing up towards the camera.
        ///
        /// Taken at the line's midpoint rather than per vertex — a
        /// <c>LineRenderer</c> has one alignment for the whole line, and over
        /// the tens of kilometres a scenario spans the globe's curvature moves
        /// "up" by a fraction of a degree. Re-derived on every rebuild, so it
        /// also survives Cesium re-origining the georeference.
        ///
        /// The drawn positions are world-space (<c>useWorldSpace</c>), so
        /// rotating this transform moves nothing on screen; the captions place
        /// themselves in world space each frame, and the click ribbon is built
        /// through <c>InverseTransformPoint</c>, so both follow.
        /// </summary>
        void OrientFlat(GeoPoint mid)
        {
            Vector3 ground = GeoUtils.GeoToUnity(_geo, mid.latitude, mid.longitude, mid.heightMeters);
            Vector3 up = GeoUtils.GeoToUnity(_geo, mid.latitude, mid.longitude, mid.heightMeters + 1000.0) - ground;
            if (up.sqrMagnitude < 1e-4f) return;
            up.Normalize();

            // Any axis across the up will do — only Z is read — but taking it
            // from local north keeps the transform readable in the inspector.
            Vector3 north = GeoUtils.GeoToUnity(_geo, mid.latitude + 0.01, mid.longitude, mid.heightMeters) - ground;
            north = Vector3.ProjectOnPlane(north, up);
            if (north.sqrMagnitude < 1e-4f) north = Vector3.Cross(up, Vector3.right);
            if (north.sqrMagnitude < 1e-4f) return;

            transform.rotation = Quaternion.LookRotation(up, north.normalized);
        }

        /// <summary>
        /// Rebuilds the invisible ribbon that makes this line clickable.
        ///
        /// A <c>LineRenderer</c> has no collider of its own and its
        /// <c>BakeMesh</c> depends on the camera it was baked for, which is no
        /// use for a line that has to stay clickable while the view orbits. So
        /// the picker is its own flat ribbon: each segment extruded sideways in
        /// the horizontal plane, several times wider than the line is drawn.
        ///
        /// The extra width is the whole point. The line reads at 70 m across,
        /// which from operational altitude is about two pixels — a click target
        /// nobody could hit. The ribbon is invisible, so it can be as generous
        /// as it needs to be without changing what the map looks like.
        /// </summary>
        void BuildPicker()
        {
            // The ribbon follows what is drawn, not what was clicked: a draped
            // line bends where the ground bends, and a click target cutting the
            // straight chord would sit off the line over every ridge.
            int count = _render.Count;
            if (count < 2)
            {
                if (_picker != null) _picker.enabled = false;
                return;
            }

            if (_picker == null)
            {
                var go = new GameObject("Picker");
                go.transform.SetParent(transform, false);
                _picker = go.AddComponent<MeshCollider>();
                // This ribbon lies along a line that is itself clamped to the
                // ground, so terrain sampling must not mistake it for ground —
                // it would re-clamp the line onto its own collider and the line
                // would climb a clearance every rebuild. See Core.NonTerrain.
                Core.NonTerrain.Mark(go);
                _pickMesh = new Mesh { name = "MapLinePicker" };
                _picker.sharedMesh = _pickMesh;
            }
            _picker.enabled = true;

            float half = Mathf.Max(_lastWidth * PickWidthFactor, MinPickWidthM) * 0.5f;

            var verts = new List<Vector3>(count * 2);
            var tris = new List<int>((count - 1) * 6);

            // World positions come back through the line's own transform: the
            // LineRenderer draws in world space but a collider mesh is local, and
            // Cesium re-origins the georeference as the camera roams.
            for (int i = 0; i < count; i++)
            {
                Vector3 here = transform.InverseTransformPoint(_render[i]);

                // Direction of travel at this vertex, averaged across the joint
                // so the ribbon does not pinch on a corner.
                Vector3 prev = i > 0 ? transform.InverseTransformPoint(_render[i - 1]) : here;
                Vector3 next = i + 1 < count
                    ? transform.InverseTransformPoint(_render[i + 1]) : here;
                Vector3 dir = next - prev;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                dir.Normalize();

                Vector3 side = Vector3.Cross(dir, Vector3.up) * half;
                verts.Add(here - side);
                verts.Add(here + side);
            }

            for (int i = 0; i + 1 < count; i++)
            {
                int b = i * 2;
                // Both windings: the ribbon lies flat and is clicked from above
                // in 2D and from a shallow angle in 3D.
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            _pickMesh.Clear();
            _pickMesh.SetVertices(verts);
            _pickMesh.SetTriangles(tris, 0);
            _pickMesh.RecalculateBounds();
            // Reassigned so the collider picks up the new geometry — a
            // MeshCollider caches its cooked mesh and will not notice otherwise.
            _picker.sharedMesh = null;
            _picker.sharedMesh = _pickMesh;
        }

        void Update()
        {
            if (_unresolved <= 0 || _reclampAttempts >= MaxReclampAttempts) return;
            _reclampTimer -= Time.deltaTime;
            if (_reclampTimer > 0f) return;
            _reclampAttempts++;
            Rebuild();
        }

        public void SetPoints(List<GeoPoint> points)
        {
            Data.points = points;
            // New geometry is new ground: whatever was given up on before says
            // nothing about whether this line's terrain will arrive.
            _reclampAttempts = 0;
            Rebuild();
        }

        /// <summary>
        /// Switches between the tilted and top-down clearances.
        ///
        /// A no-op for <see cref="FlatOnly"/> kinds — a boundary is a graphic on
        /// the map in both projections, and the view mode has nothing to say
        /// about it. Everything else takes the flag.
        /// </summary>
        public void Set3D(bool is3D)
        {
            if (FlatOnly) return;
            if (Data.is3D == is3D) return;
            Data.is3D = is3D;
            _reclampAttempts = 0;
            Rebuild();
        }

        /// <summary>
        /// Draws the line's own caption on the map — "FEBA", "PL BLUE",
        /// "DEFENCE LINE — 2 RIFLES". The <c>label</c> amplifier is part of the
        /// save, so a line that says what it is keeps saying it after a reload,
        /// which a caption owned by whichever system drew the line would not.
        ///
        /// Doctrine captions a long control measure at both ends, so anything
        /// with enough vertices to have ends gets two; a short one gets a single
        /// caption at its midpoint.
        /// </summary>
        void RefreshLabels()
        {
            var pts = Data.points;
            bool wanted = !string.IsNullOrEmpty(Data.label) && pts.Count >= 2;
            int want = !wanted ? 0 : (pts.Count >= 4 ? 2 : 1);

            while (_labels.Count > want)
            {
                var last = _labels[_labels.Count - 1];
                _labels.RemoveAt(_labels.Count - 1);
                if (last != null) Destroy(last.gameObject);
            }
            while (_labels.Count < want)
                _labels.Add(MapLabel.Create(_geo, transform, $"{Data.id}-{_labels.Count}"));

            if (want == 0) return;

            Color color = _lr.material != null ? _lr.material.color : Color.white;
            if (want == 1)
            {
                var mid = pts[pts.Count / 2];
                _labels[0].Set(Data.label, color, mid.latitude, mid.longitude);
                return;
            }
            // One vertex in from each end: on the line, but clear of its caps.
            _labels[0].Set(Data.label, color, pts[1].latitude, pts[1].longitude);
            _labels[1].Set(Data.label, color, pts[pts.Count - 2].latitude, pts[pts.Count - 2].longitude);
        }
    }
}
