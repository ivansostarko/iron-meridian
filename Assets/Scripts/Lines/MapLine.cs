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
    /// Vertices are clamped to the terrain in **both** view modes. A constant
    /// height band looks tidy on a flat map right up until the ground rises
    /// through it, at which point the line disappears inside a ridge; following
    /// the terrain and standing off it by a fixed clearance is what keeps a
    /// control measure readable everywhere. The 2D/3D flag now chooses how much
    /// clearance rather than whether to clamp at all — 2D lifts further clear so
    /// the graphics read as a drawn overlay from straight above.
    ///
    /// Because Cesium streams tiles, the first clamp routinely finds no ground.
    /// The line keeps re-clamping until every vertex has real terrain under it,
    /// then stops sampling.
    /// </summary>
    public class MapLine : MonoBehaviour
    {
        public MapLineData Data { get; private set; }

        /// <summary>Metres above the terrain in the tilted 3D view.</summary>
        const double Clearance3DM = 25.0;
        /// <summary>Metres above the terrain in the top-down 2D view.</summary>
        const double Clearance2DM = 140.0;
        /// <summary>Seconds between re-clamps while some vertex still has no terrain under it.</summary>
        const float ReclampSeconds = 1.5f;

        CesiumGeoreference _geo;
        LineRenderer _lr;
        readonly List<MapLabel> _labels = new List<MapLabel>();
        int _unresolved;
        float _reclampTimer;

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
            _lr = gameObject.AddComponent<LineRenderer>();
            _lr.useWorldSpace = true;
            _lr.textureMode = LineTextureMode.Tile;
            _lr.alignment = LineAlignment.View;
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
            System.Enum.TryParse(Data.kind, out LineKind kind);

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

        /// <summary>Recompute world positions from geodetic points.</summary>
        public void Rebuild()
        {
            var pts = Data.points;
            double clearance = Data.is3D ? Clearance3DM : Clearance2DM;

            _unresolved = 0;
            _lr.positionCount = pts.Count;
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
                _lr.SetPosition(i, GeoUtils.GeoToUnity(_geo, pts[i].latitude, pts[i].longitude,
                    pts[i].heightMeters));
            }

            _reclampTimer = ReclampSeconds;
            RefreshLabels();
            if (Pickable) BuildPicker();
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
            var pts = Data.points;
            if (pts.Count < 2)
            {
                if (_picker != null) _picker.enabled = false;
                return;
            }

            if (_picker == null)
            {
                var go = new GameObject("Picker");
                go.transform.SetParent(transform, false);
                _picker = go.AddComponent<MeshCollider>();
                _pickMesh = new Mesh { name = "MapLinePicker" };
                _picker.sharedMesh = _pickMesh;
            }
            _picker.enabled = true;

            float half = Mathf.Max(_lastWidth * PickWidthFactor, MinPickWidthM) * 0.5f;

            var verts = new List<Vector3>(pts.Count * 2);
            var tris = new List<int>((pts.Count - 1) * 6);

            // World positions come back through the line's own transform: the
            // LineRenderer draws in world space but a collider mesh is local, and
            // Cesium re-origins the georeference as the camera roams.
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 here = transform.InverseTransformPoint(_lr.GetPosition(i));

                // Direction of travel at this vertex, averaged across the joint
                // so the ribbon does not pinch on a corner.
                Vector3 prev = i > 0 ? transform.InverseTransformPoint(_lr.GetPosition(i - 1)) : here;
                Vector3 next = i + 1 < pts.Count
                    ? transform.InverseTransformPoint(_lr.GetPosition(i + 1)) : here;
                Vector3 dir = next - prev;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
                dir.Normalize();

                Vector3 side = Vector3.Cross(dir, Vector3.up) * half;
                verts.Add(here - side);
                verts.Add(here + side);
            }

            for (int i = 0; i + 1 < pts.Count; i++)
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
            if (_unresolved <= 0) return;
            _reclampTimer -= Time.deltaTime;
            if (_reclampTimer > 0f) return;
            Rebuild();
        }

        public void SetPoints(List<GeoPoint> points)
        {
            Data.points = points;
            Rebuild();
        }

        public void Set3D(bool is3D)
        {
            Data.is3D = is3D;
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
