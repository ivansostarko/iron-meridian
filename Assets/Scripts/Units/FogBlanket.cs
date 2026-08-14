using System.Collections.Generic;
using UnityEngine;
using CesiumForUnity;
using Unity.Mathematics;
using IronMeridian.Core;
using IronMeridian.Map;

namespace IronMeridian.Units
{
    /// <summary>
    /// The dark over the ground nobody is looking at.
    ///
    /// <see cref="FogOfWarSystem"/> hides enemy *formations* the player cannot
    /// see. This hides the *map* — the terrain itself goes dark outside what the
    /// player's own units and sensors are covering, so an unscouted valley is a
    /// blank on the map rather than a photograph with the enemy politely removed
    /// from it. Without this, fog of war withholds the counters while leaving
    /// every road, ridge and town in plain view, which is not intelligence.
    ///
    /// **How.** A grid is laid over the operational area and clamped to the
    /// terrain, sitting a few tens of metres above it. Each vertex carries an
    /// alpha, recomputed on the fog's own sweep: clear where something of the
    /// player's can see, dim where they have been but are not looking now, and
    /// near-opaque where they have never been. Vertex colours rather than a
    /// projected texture, because the project builds every material from code
    /// and this needs no shader of its own.
    ///
    /// **Two tiers, not one.** Ground once explored stays faintly readable rather
    /// than going fully black again. Terrain does not move: a commander who has
    /// been somewhere still knows the shape of it, and blacking it out again the
    /// moment the patrol leaves makes the map unnavigable without adding any
    /// uncertainty that the enemy-hiding does not already provide.
    ///
    /// Heights are sampled a few hundred vertices per frame rather than all at
    /// once — the grid is thousands of physics raycasts, and Cesium streams the
    /// terrain in anyway, so a blanket that settles onto the ground over the
    /// first second is both cheaper and more correct than one that samples
    /// everything the instant the battle starts.
    ///
    /// See docs/16-FOG-OF-WAR.md.
    /// </summary>
    public class FogBlanket : MonoBehaviour
    {
        /// <summary>
        /// Vertices per side, floor and ceiling. 80² is 6 400 — fine enough that
        /// a view-range edge reads as a curve at 20 km, and small enough that
        /// re-uploading the colours every frame is not a per-frame megabyte.
        /// A large mission area buys more, up to 128² (16 384), because the
        /// alternative is a boundary that steps in four-kilometre blocks.
        /// </summary>
        const int MinResolution = 80, MaxResolution = 128;
        /// <summary>Cell size the resolution aims for, km. Only the ceiling above stops it being met.</summary>
        const float TargetCellKm = 1.4f;
        /// <summary>Metres the blanket floats above the terrain.</summary>
        const float LiftM = 45f;
        /// <summary>Half-extent floor and ceiling for the covered area, km.</summary>
        const float MinHalfExtentKm = 6f, MaxHalfExtentKm = 45f;
        /// <summary>
        /// Ceiling when the blanket is covering a **mission area** rather than
        /// following the units. It has to be larger than the ordinary one, or a
        /// 120 km scenario would have its boundary drawn well inside itself —
        /// the mask would then be lying about where the battlefield ends, which
        /// is worse than not drawing one.
        /// </summary>
        const float MaxAreaHalfExtentKm = 200f;
        /// <summary>Margin added around the units' bounding box, km.</summary>
        const float MarginKm = 4f;
        /// <summary>Rebuild the grid once a friendly unit is within this fraction of the edge.</summary>
        const float RefitEdgeFraction = 0.9f;
        /// <summary>Terrain samples taken per frame while the blanket settles.</summary>
        const int SamplesPerFrame = 420;

        /// <summary>Opacity over ground never observed, and over ground explored but not currently watched.</summary>
        const float UnexploredAlpha = 0.94f;
        const float ExploredAlpha = 0.52f;
        /// <summary>Fraction of a view radius over which the edge softens.</summary>
        const float EdgeSoftness = 0.18f;
        /// <summary>Seconds the blanket takes to fade in or out when fog is switched.</summary>
        const float FadeSeconds = 0.8f;

        static readonly Color FogColour = new Color(0.020f, 0.031f, 0.055f);

        CesiumGeoreference _geo;
        CesiumGlobeAnchor _anchor;
        Material _mat;
        UnityEngine.Mesh _mesh;

        double _centreLat, _centreLon, _centreHeight;
        float _halfExtentKm;

        Vector3[] _verts;
        Color[] _colours;
        /// <summary>Vertex local east/north in km, cached for the visibility sweep.</summary>
        float[] _east, _north;
        /// <summary>Ground the player has observed at some point this battle.</summary>
        bool[] _explored;
        /// <summary>Target alpha per vertex; the mesh eases toward it so the edge does not pop.</summary>
        float[] _target;
        /// <summary>
        /// False for vertices outside the mission area. Computed once when the
        /// grid is laid, because a point-in-polygon test per vertex per sweep is
        /// thousands of tests a second for an answer that cannot change — the
        /// area is fixed for the whole battle.
        /// </summary>
        bool[] _inArea;

        /// <summary>
        /// The mission's ground, or null for an unbounded scenario. Outside it
        /// the blanket is opaque and stays that way whatever anybody can see —
        /// see <see cref="Data.MissionArea"/>.
        /// </summary>
        Data.MissionArea _area;

        /// <summary>
        /// When true, everything inside the area is simply clear and only the
        /// outside is masked. This is the shape the blanket takes for a mission
        /// that bounds its ground but leaves fog of war off: the player is meant
        /// to see the whole battlefield, and only the battlefield.
        /// </summary>
        bool _areaMaskOnly;

        int _sampleCursor;
        bool _settled;
        float _fade;
        bool _wanted;

        public static FogBlanket Create(CesiumGeoreference geo)
        {
            var go = new GameObject("FogBlanket");
            go.transform.SetParent(geo.transform, false);

            var blanket = go.AddComponent<FogBlanket>();
            blanket._geo = geo;
            blanket._anchor = go.AddComponent<CesiumGlobeAnchor>();
            blanket._mat = RuntimeMaterials.UnlitColor(FogColour);
            blanket.BuildRenderer();
            go.SetActive(false);
            return blanket;
        }

        void BuildRenderer()
        {
            _mesh = new UnityEngine.Mesh { name = "FogBlanket" };
            // 96² is under 65 535, but a later resolution bump should not
            // silently corrupt the mesh.
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = _mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>
        /// Turns the blanket on or off. Off fades out and then deactivates, so
        /// lifting fog does not snap the whole map to daylight in one frame.
        /// </summary>
        public void SetWanted(bool wanted)
        {
            _wanted = wanted;
            if (wanted && !gameObject.activeSelf) gameObject.SetActive(true);
        }

        /// <summary>Forgets everything explored — a new battle starts blind again.</summary>
        public void ResetExploration()
        {
            if (_explored == null) return;
            for (int i = 0; i < _explored.Length; i++) _explored[i] = false;
        }

        /// <summary>
        /// The mission's ground. Pass null for an unbounded scenario. Changing
        /// it invalidates the grid, because which vertices are inside is baked
        /// in when the grid is laid.
        /// </summary>
        /// <remarks>
        /// The caller re-lays the grid after this: which vertices fall inside is
        /// baked in when the grid is built, so an area change that did not
        /// trigger a rebuild would leave the old mask in place.
        /// </remarks>
        public void SetArea(Data.MissionArea area, bool maskOnly)
        {
            _area = area != null && area.HasArea ? area : null;
            _areaMaskOnly = maskOnly;
        }

        // ------------------------------------------------------------ layout

        /// <summary>
        /// Lays the grid over the ground worth covering: everything currently on
        /// the map, plus a margin. Called when fog starts and whenever a friendly
        /// unit approaches the edge of what is already covered.
        /// </summary>
        public void Fit(IEnumerable<UnitActor> units)
        {
            double minLat = double.MaxValue, maxLat = double.MinValue;
            double minLon = double.MaxValue, maxLon = double.MinValue;
            bool any = false;

            foreach (var u in units)
            {
                if (u == null || !u.IsAlive) continue;
                any = true;
                minLat = System.Math.Min(minLat, u.State.latitude);
                maxLat = System.Math.Max(maxLat, u.State.latitude);
                minLon = System.Math.Min(minLon, u.State.longitude);
                maxLon = System.Math.Max(maxLon, u.State.longitude);
            }

            if (!any) return;

            _centreLat = (minLat + maxLat) * 0.5;
            _centreLon = (minLon + maxLon) * 0.5;

            // Half the diagonal of the bounding box, plus a margin, so the
            // blanket reaches well past the outermost formation.
            double spanKm = GeoUtils.DistanceKm(minLat, minLon, maxLat, maxLon);
            _halfExtentKm = Mathf.Clamp((float)(spanKm * 0.5) + MarginKm,
                MinHalfExtentKm, MaxHalfExtentKm);

            BuildGrid();
        }

        /// <summary>
        /// Lays the grid over the mission's own ground, plus enough margin to
        /// cover the dark outside it.
        ///
        /// The margin is generous on purpose. The blanket is what *shows* the
        /// boundary, so it has to extend past the area far enough that the dark
        /// reaches the edge of the screen — a mask that stopped at the boundary
        /// would leave a lit ring of out-of-bounds terrain around it, which says
        /// the opposite of what it is for.
        /// </summary>
        public void FitToArea(Data.MissionArea area)
        {
            if (area == null || !area.HasArea) return;

            area.Centre(out _centreLat, out _centreLon);
            _halfExtentKm = Mathf.Clamp(area.RadiusKm() * 1.6f + MarginKm,
                MinHalfExtentKm, MaxAreaHalfExtentKm);

            BuildGrid();
        }

        /// <summary>
        /// True if this unit is far enough out that the grid should be re-laid.
        /// Never for a bounded mission: the grid is laid over the mission's
        /// ground and is not supposed to follow anybody off it.
        /// </summary>
        public bool NeedsRefit(UnitActor unit)
        {
            if (_area != null) return false;
            if (unit == null || !unit.IsAlive || _verts == null) return false;
            double km = GeoUtils.DistanceKm(_centreLat, _centreLon,
                unit.State.latitude, unit.State.longitude);
            return km > _halfExtentKm * RefitEdgeFraction;
        }

        void BuildGrid()
        {
            _centreHeight = GeoUtils.SampleTerrainHeight(_geo, _centreLat, _centreLon, 250.0);
            _anchor.longitudeLatitudeHeight = new double3(_centreLon, _centreLat, _centreHeight);

            // Resolution follows the extent so a cell stays about the same size
            // on the ground: 80² over a 12 km editor engagement and 128² over a
            // 260 km theatre are the same picture, where a fixed grid would make
            // the second one step in four-kilometre blocks.
            int n = Mathf.Clamp(Mathf.CeilToInt(_halfExtentKm * 2f / TargetCellKm) + 1,
                MinResolution, MaxResolution);
            int count = n * n;

            if (_verts == null || _verts.Length != count)
            {
                _verts = new Vector3[count];
                _colours = new Color[count];
                _east = new float[count];
                _north = new float[count];
                _explored = new bool[count];
                _target = new float[count];
                _inArea = new bool[count];
            }

            float extentM = _halfExtentKm * 1000f;
            float step = extentM * 2f / (n - 1);

            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int idx = j * n + i;
                    float e = -extentM + i * step;
                    float nt = -extentM + j * step;
                    _east[idx] = e / 1000f;
                    _north[idx] = nt / 1000f;
                    // Height starts flat and is replaced as the samples come in.
                    _verts[idx] = new Vector3(e, LiftM, nt);
                    _explored[idx] = false;

                    // Baked once, here: which side of the mission boundary this
                    // vertex falls on cannot change for the length of a battle.
                    if (_area == null) _inArea[idx] = true;
                    else
                    {
                        GeoUtils.FromLocalKm(_centreLat, _centreLon, _east[idx], _north[idx],
                            out double lat, out double lon);
                        _inArea[idx] = _area.Contains(lat, lon);
                    }

                    float start = _inArea[idx] ? UnexploredAlpha : Data.MissionArea.OutsideOpacity;
                    _colours[idx] = new Color(1f, 1f, 1f, start);
                    _target[idx] = start;
                }

            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }

            _mesh.Clear();
            _mesh.vertices = _verts;
            _mesh.colors = _colours;
            _mesh.triangles = tris;
            _mesh.RecalculateBounds();

            _sampleCursor = 0;
            _settled = false;
        }

        // ------------------------------------------------------- visibility

        /// <summary>
        /// Recomputes what is visible. Called on the fog's sweep, not per frame.
        ///
        /// Watchers are converted to the grid's own local kilometre frame once,
        /// so the inner loop over thousands of vertices is a squared-distance
        /// compare rather than a haversine.
        /// </summary>
        public void Refresh(IEnumerable<UnitActor> watchers, IEnumerable<FogOfWarSystem.Sensor> sensors)
        {
            if (_verts == null) return;

            // (east km, north km, radius km) per watcher.
            var eyes = new List<Vector3>();

            foreach (var w in watchers)
            {
                if (w == null || !w.IsAlive) continue;
                GeoUtils.ToLocalKm(_centreLat, _centreLon, w.State.latitude, w.State.longitude,
                    out double e, out double n);
                eyes.Add(new Vector3((float)e, (float)n, Mathf.Max(0.2f, w.Def.viewRangeKm)));
            }

            foreach (var s in sensors)
            {
                if (s == null) continue;
                GeoUtils.ToLocalKm(_centreLat, _centreLon, s.latitude, s.longitude,
                    out double e, out double n);
                eyes.Add(new Vector3((float)e, (float)n, Mathf.Max(0.2f, s.radiusKm)));
            }

            for (int i = 0; i < _target.Length; i++)
            {
                // Out of bounds: opaque, and no amount of looking at it changes
                // that. Skipping the eye loop for these vertices is also most of
                // the sweep's work saved on a small area inside a large grid.
                if (_inArea != null && !_inArea[i])
                {
                    _target[i] = Data.MissionArea.OutsideOpacity;
                    continue;
                }

                // Bounded but with fog off: the battlefield is simply visible,
                // and the blanket is doing nothing here but framing it.
                if (_areaMaskOnly) { _target[i] = 0f; continue; }

                float clear = 0f;                    // 1 = fully in view

                for (int k = 0; k < eyes.Count && clear < 1f; k++)
                {
                    var eye = eyes[k];
                    float dx = _east[i] - eye.x, dy = _north[i] - eye.y;
                    float r = eye.z;
                    float d2 = dx * dx + dy * dy;
                    if (d2 > r * r) continue;

                    // Soft edge, so a view range does not end on a hard circle.
                    float d = Mathf.Sqrt(d2);
                    float inner = r * (1f - EdgeSoftness);
                    clear = Mathf.Max(clear, d <= inner ? 1f : 1f - (d - inner) / (r - inner));
                }

                if (clear > 0.5f) _explored[i] = true;

                float baseAlpha = _explored[i] ? ExploredAlpha : UnexploredAlpha;
                _target[i] = Mathf.Lerp(baseAlpha, 0f, clear);
            }
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            if (_verts == null) return;

            // Fade the whole blanket in and out with the fog switch.
            float want = _wanted ? 1f : 0f;
            _fade = Mathf.MoveTowards(_fade, want, Time.unscaledDeltaTime / FadeSeconds);
            if (_fade <= 0.001f && !_wanted)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }

            SettleHeights();
            EaseColours();
        }

        /// <summary>
        /// Clamps the blanket onto the terrain a few hundred vertices at a time.
        /// Keeps cycling after the first pass so the blanket follows tiles that
        /// stream in at a different resolution later.
        /// </summary>
        void SettleHeights()
        {
            int budget = _settled ? SamplesPerFrame / 6 : SamplesPerFrame;
            bool moved = false;

            for (int s = 0; s < budget; s++)
            {
                int idx = _sampleCursor;
                _sampleCursor++;
                if (_sampleCursor >= _verts.Length)
                {
                    _sampleCursor = 0;
                    _settled = true;
                }

                GeoUtils.FromLocalKm(_centreLat, _centreLon, _east[idx], _north[idx],
                    out double lat, out double lon);

                if (!GeoUtils.TrySampleTerrainHeight(_geo, lat, lon, out double h)) continue;

                float y = (float)(h - _centreHeight) + LiftM;
                if (Mathf.Approximately(_verts[idx].y, y)) continue;

                _verts[idx] = new Vector3(_verts[idx].x, y, _verts[idx].z);
                moved = true;
            }

            // Once the blanket has settled onto stable terrain, most passes
            // change nothing — and re-uploading six thousand vertices to say so
            // is the kind of cost that only shows up as a frame-time floor.
            if (moved) _mesh.vertices = _verts;
        }

        void EaseColours()
        {
            // Eased rather than assigned, so ground coming into view brightens
            // instead of snapping — the sweep only runs a few times a second and
            // hard steps at that cadence read as flicker.
            float k = 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime);

            for (int i = 0; i < _colours.Length; i++)
            {
                float a = Mathf.Lerp(_colours[i].a, _target[i] * _fade, k);
                _colours[i].a = a;
            }

            _mesh.colors = _colours;
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
