using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using CesiumForUnity;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Lines
{
    /// <summary>
    /// Every mine and obstacle graphic on the map, and the tool that puts them
    /// there.
    ///
    /// **Barrier graphics are control measures.** A minefield symbol tells
    /// whoever is reading the map that there are mines there; it is not a thing
    /// that fights, is not in anybody's order of battle, and belongs to the
    /// scenario rather than to a formation — so it is owned here, beside the
    /// other control measures, rather than being a unit with no weapons or a
    /// task marker that would be swept away when its unit died.
    ///
    /// **Most barriers are pick-then-click**, the same gesture the effects and
    /// the logistics sites use, with a ghost of the chosen symbol tracking the
    /// ground so what is seen is what lands. The tool stays armed after a
    /// placement, because a barrier plan is laid several graphics at a time —
    /// a belt is not one symbol.
    ///
    /// **A minefield is outlined instead.** Kinds flagged
    /// <see cref="ObstacleDef.areaDrawn"/> arm a polygon tool rather than a
    /// stamp:
    ///
    ///   Left click       — add a corner
    ///   Backspace        — undo the last corner
    ///   Right click / ⏎  — close the belt (at least three corners)
    ///   Esc              — abandon it
    ///
    /// The same gesture <see cref="MapObjectSystem"/> and
    /// <see cref="MissionAreaTool"/> use, because it is the same act: saying
    /// which ground something covers. A minefield is the one barrier where that
    /// is the whole content of the graphic — a roadblock closes one place, a
    /// belt *is* an area — and it is also what
    /// <see cref="Units.MinefieldSystem"/> needs before it can tell whether a
    /// column has driven into one. A short outline is kept rather than thrown
    /// away: the player is told what is missing and goes on clicking.
    ///
    /// **Each graphic takes the bearing the camera is facing.** An obstacle lies
    /// *across* something, so it needs a direction; taking it from the view is
    /// the one answer that needs no extra control and is almost always right,
    /// since a designer laying a belt is looking along it.
    ///
    /// See docs/31-OBSTACLES.md.
    /// </summary>
    public class ObstacleSystem : MonoBehaviour
    {
        /// <summary>User-facing messages (wired to the HUD's flash line).</summary>
        public System.Action<string> Flash;
        /// <summary>Raised when the armed kind or the list changes, so the panel can repaint.</summary>
        public event System.Action Changed;

        public bool IsArmed => _armed.HasValue;
        public ObstacleKind? Armed => _armed;

        /// <summary>True while a belt outline is being laid down.</summary>
        public bool Drawing => _draft.Count > 0;

        /// <summary>Corners on the outline in progress — what the panel's hint counts.</summary>
        public int DraftCorners => _draft.Count;

        /// <summary>Whether the armed kind is outlined rather than stamped.</summary>
        public bool ArmedIsArea => _armed.HasValue && ObstacleCatalog.IsArea(_armed.Value);

        /// <summary>Which side a newly placed graphic belongs to. The panel's team tab sets it.</summary>
        public Team Team { get; set; } = Team.User;

        public IReadOnlyList<ObstacleMarker> Markers => _markers;

        readonly List<ObstacleMarker> _markers = new List<ObstacleMarker>();

        MapManager _map;
        Camera _cam;
        ObstacleKind? _armed;

        GameObject _ghost;
        CesiumGlobeAnchor _ghostAnchor;
        Transform _ghostQuad;
        Material _ghostMat;
        float _pulse;

        readonly List<GeoPoint> _draft = new List<GeoPoint>();
        MapLine _draftLine;

        bool _validGround;
        double _lat, _lon;

        public void Init(MapManager map, Camera cam)
        {
            _map = map;
            _cam = cam;
            BuildGhost();
        }

        // ------------------------------------------------------------ placing

        /// <summary>Arms a kind, or disarms it if the same one is picked again.</summary>
        public void Toggle(ObstacleKind kind)
        {
            if (_armed.HasValue && _armed.Value == kind) { Cancel(); return; }

            _armed = kind;
            ClearDraft();
            var def = ObstacleCatalog.Get(kind);

            if (def.areaDrawn)
            {
                // No ghost: there is nothing to preview until the first corner
                // is down, and a symbol tracking the cursor would be promising a
                // stamp the click is not going to make.
                if (_ghost != null) _ghost.SetActive(false);
                Flash?.Invoke($"{def.name} — click {ObstacleCatalog.MinAreaCorners} or more corners " +
                              "round the belt, right-click or Enter to close, Esc to abandon.");
                Changed?.Invoke();
                return;
            }

            _ghostMat.mainTexture = UI.UiIcons.GlyphFor(kind).texture;
            _ghostMat.color = def.tint;
            _ghostQuad.localScale = Vector3.one * def.widthMeters;

            Flash?.Invoke($"Click the map to lay {def.name.ToLowerInvariant()}. " +
                          "Right-click or Esc to stop.");
            Changed?.Invoke();
        }

        public void Cancel()
        {
            if (!_armed.HasValue) return;
            bool wasDrawing = Drawing;
            _armed = null;
            ClearDraft();
            if (_ghost != null) _ghost.SetActive(false);
            if (wasDrawing) Flash?.Invoke("Outline abandoned.");
            Changed?.Invoke();
        }

        void Update()
        {
            if (!_armed.HasValue) return;

            if (ObstacleCatalog.IsArea(_armed.Value)) { UpdateOutlining(); return; }

            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Cancel();
                return;
            }

            TrackGround();

            if (Input.GetMouseButtonDown(0)) PlaceHere();
        }

        // ---------------------------------------------------------- outlining

        void UpdateOutlining()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }

            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();

            if (!overUI && Input.GetMouseButtonDown(0) &&
                _map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out double h);
                _draft.Add(new GeoPoint { latitude = lat, longitude = lon, heightMeters = h });
                RedrawDraftAndReport();
            }

            if (Input.GetKeyDown(KeyCode.Backspace) && _draft.Count > 0)
            {
                _draft.RemoveAt(_draft.Count - 1);
                RedrawDraftAndReport();
            }

            if (Input.GetKeyDown(KeyCode.Return) || (!overUI && Input.GetMouseButtonDown(1)))
                CloseOutline();
        }

        /// <summary>
        /// Turns the outline into a barrier, or says why it cannot yet.
        ///
        /// The corners are **kept** when there are too few: discarding two
        /// because the third had not been placed would punish the one mistake
        /// the minimum exists to prevent. The kind stays armed afterwards, like
        /// the stamp tool — a barrier plan is several belts, not one.
        /// </summary>
        void CloseOutline()
        {
            if (!_armed.HasValue) return;
            var def = ObstacleCatalog.Get(_armed.Value);

            if (_draft.Count < ObstacleCatalog.MinAreaCorners)
            {
                Flash?.Invoke($"{def.name} needs at least {ObstacleCatalog.MinAreaCorners} corners — " +
                              $"{_draft.Count} so far.");
                return;
            }

            Centroid(_draft, out double lat, out double lon);
            Add(new ObstacleSiteData
            {
                id = NewId(),
                kind = _armed.Value.ToString(),
                team = Team.ToString(),
                latitude = lat,
                longitude = lon,
                headingDeg = CameraBearing(),
                points = new List<GeoPoint>(_draft)
            });

            // Measured before the draft is cleared, not after — the figure is
            // the one thing the designer cannot read off the map.
            int corners = _draft.Count;
            double km2 = AreaKm2(_draft);
            ClearDraft();
            Flash?.Invoke($"{def.name} recorded — {corners} corners, {km2:0.0} km² of ground. " +
                          "The kind stays armed; Esc to stop.");
            Changed?.Invoke();
        }

        void RedrawDraft()
        {
            if (_draft.Count < 2)
            {
                if (_draftLine != null) { Destroy(_draftLine.gameObject); _draftLine = null; }
                return;
            }

            // Open while it is being drawn: closing the ring before the last
            // corner is placed draws an edge the player has not asked for and
            // cannot see the shape of.
            if (_draftLine == null)
            {
                var def = ObstacleCatalog.Get(_armed ?? ObstacleKind.Minefield);
                _draftLine = MapLine.Create(_map.Georeference, new MapLineData
                {
                    id = "obstacle-draft",
                    kind = nameof(LineKind.Boundary),
                    team = Team.ToString(),
                    is3D = false,
                    autoGenerated = true,
                    label = "",
                    colorHex = "#" + ColorUtility.ToHtmlStringRGB(def.tint),
                    widthMeters = 90f,
                    points = new List<GeoPoint>()
                });
            }
            _draftLine.SetPoints(new List<GeoPoint>(_draft));
        }

        /// <summary>
        /// Redraws the outline and tells the panel, so its corner count moves as
        /// the corners go down. A progress figure that only appeared once the
        /// shape was finished would be telling the designer something they no
        /// longer need.
        /// </summary>
        void RedrawDraftAndReport()
        {
            RedrawDraft();
            Changed?.Invoke();
        }

        void ClearDraft()
        {
            _draft.Clear();
            if (_draftLine != null) { Destroy(_draftLine.gameObject); _draftLine = null; }
        }

        /// <summary>
        /// The polygon's centre, stored on the record so an area answers "where
        /// is it" the same way a stamped symbol does — which is what lets the
        /// list row, the focus button and the screen-space pick stay ignorant of
        /// which sort of graphic they are dealing with.
        /// </summary>
        public static void Centroid(List<GeoPoint> points, out double lat, out double lon)
        {
            lat = lon = 0.0;
            if (points == null || points.Count == 0) return;
            foreach (var p in points) { lat += p.latitude; lon += p.longitude; }
            lat /= points.Count;
            lon /= points.Count;
        }

        /// <summary>
        /// Ground enclosed, km². The shoelace formula in plate carrée with a
        /// cosine correction on the longitude — exact enough for a belt a few
        /// kilometres across, which is the only size one is ever drawn at.
        /// </summary>
        public static double AreaKm2(List<GeoPoint> points)
        {
            if (points == null || points.Count < 3) return 0.0;

            Centroid(points, out double cLat, out _);
            double kmPerDegLat = 111.32;
            double kmPerDegLon = 111.32 * System.Math.Cos(cLat * System.Math.PI / 180.0);

            double twice = 0.0;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                double xi = points[i].longitude * kmPerDegLon, yi = points[i].latitude * kmPerDegLat;
                double xj = points[j].longitude * kmPerDegLon, yj = points[j].latitude * kmPerDegLat;
                twice += xj * yi - xi * yj;
            }
            return System.Math.Abs(twice) * 0.5;
        }

        void TrackGround()
        {
            bool overUI = EventSystem.current != null &&
                          EventSystem.current.IsPointerOverGameObject();

            Vector3 hit = default;
            _validGround = !overUI && _map.RaycastGround(_cam, Input.mousePosition, out hit);

            if (!_validGround)
            {
                if (_ghost != null) _ghost.SetActive(false);
                return;
            }

            GeoUtils.UnityToGeo(_map.Georeference, hit, out _lat, out _lon, out _);

            double h = GeoUtils.SampleTerrainHeight(_map.Georeference, _lat, _lon, 250) + 25.0;
            _ghostAnchor.longitudeLatitudeHeight = new Unity.Mathematics.double3(_lon, _lat, h);
            _ghost.SetActive(true);
        }

        void PlaceHere()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (!_validGround)
            {
                Flash?.Invoke("Terrain not loaded there yet — try again in a moment.");
                return;
            }

            var def = ObstacleCatalog.Get(_armed.Value);
            Add(new ObstacleSiteData
            {
                id = NewId(),
                kind = _armed.Value.ToString(),
                team = Team.ToString(),
                latitude = _lat,
                longitude = _lon,
                headingDeg = CameraBearing()
            });

            Flash?.Invoke($"{def.name} laid — {def.widthMeters:0} m of ground marked.");
        }

        /// <summary>
        /// The bearing the camera is looking along, which is the direction a
        /// placed graphic is laid on. In the top-down view the camera is north-up
        /// and every belt would come out east-west, so 2D falls back to a due-
        /// north lay — which at least is a stated convention rather than an
        /// accident of the projection.
        /// </summary>
        float CameraBearing()
        {
            if (_cam == null) return 0f;
            Vector3 forward = _cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return 0f;
            return Quaternion.LookRotation(forward).eulerAngles.y;
        }

        static string NewId() =>
            "obs-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);

        // ----------------------------------------------------------- markers

        public ObstacleMarker Add(ObstacleSiteData data)
        {
            if (string.IsNullOrEmpty(data.id)) data.id = NewId();
            var marker = ObstacleMarker.Create(_map.Georeference, data);
            _markers.Add(marker);
            Changed?.Invoke();
            return marker;
        }

        public void Remove(ObstacleMarker marker)
        {
            if (marker == null) return;
            _markers.Remove(marker);
            Destroy(marker.gameObject);
            Changed?.Invoke();
        }

        public void Clear()
        {
            foreach (var m in _markers) if (m != null) Destroy(m.gameObject);
            _markers.Clear();
            Changed?.Invoke();
        }

        public int CountFor(Team team)
        {
            int n = 0;
            foreach (var m in _markers)
                if (m != null && m.Data.team == team.ToString()) n++;
            return n;
        }

        /// <summary>
        /// The graphic under a screen position, or null. Screen space rather
        /// than a collider, for the same reason the logistic sites use it: the
        /// pick has to agree with what is on screen, and adding colliders to
        /// map furniture is how terrain sampling ends up clamping to it.
        /// </summary>
        public ObstacleMarker PickAt(Camera cam, Vector2 screenPos, float radiusPx = 26f)
        {
            if (cam == null) return null;

            ObstacleMarker best = null;
            float bestDistance = radiusPx;

            foreach (var marker in _markers)
            {
                if (marker == null) continue;
                Vector3 anchor = marker.Anchor;
                if (anchor.sqrMagnitude < 1e-6f) continue;

                Vector3 view = cam.WorldToViewportPoint(anchor);
                if (view.z <= 0f) continue;

                float distance = Vector2.Distance(screenPos, cam.WorldToScreenPoint(anchor));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = marker;
            }

            return best;
        }

        // ------------------------------------------------------------ saving

        public List<ObstacleSiteData> Serialize()
        {
            var result = new List<ObstacleSiteData>();
            foreach (var m in _markers) if (m != null) result.Add(m.Data);
            return result;
        }

        public void LoadFrom(List<ObstacleSiteData> data)
        {
            Clear();
            if (data == null) return;
            foreach (var d in data)
            {
                if (d == null) continue;
                // JsonUtility leaves a member absent from the file at null
                // rather than at its initialiser, so a map saved before belts
                // were areas arrives with no list at all.
                if (d.points == null) d.points = new List<GeoPoint>();
                _markers.Add(ObstacleMarker.Create(_map.Georeference, d));
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------- ghost

        void BuildGhost()
        {
            _ghost = new GameObject("ObstaclePlacementGhost");
            _ghost.transform.SetParent(_map.Georeference.transform, false);
            _ghostAnchor = _ghost.AddComponent<CesiumGlobeAnchor>();

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(_ghost.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = Vector3.one * 300f;

            _ghostMat = RuntimeMaterials.UnlitTexture(UI.UiIcons.MineGeneral.texture);
            quad.GetComponent<MeshRenderer>().material = _ghostMat;
            _ghostQuad = quad.transform;

            _ghost.SetActive(false);
        }

        void LateUpdate()
        {
            if (_ghost == null || !_ghost.activeSelf) return;

            // The ghost lies flat like the graphic it previews, and turns with
            // the camera so the lay is visible before the click commits it.
            if (_cam != null)
                _ghost.transform.localRotation = Quaternion.Euler(0f, CameraBearing(), 0f);

            _pulse += Time.unscaledDeltaTime;
            var c = _ghostMat.color;
            c.a = Mathf.Lerp(0.35f, 0.9f, (Mathf.Sin(_pulse * 3.2f) + 1f) * 0.5f);
            _ghostMat.color = c;
        }

        void OnDestroy()
        {
            if (_ghostMat != null) Destroy(_ghostMat);
            if (_draftLine != null) Destroy(_draftLine.gameObject);
        }
    }
}
