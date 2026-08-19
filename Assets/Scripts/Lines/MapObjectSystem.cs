using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Lines
{
    /// <summary>
    /// The infrastructure a scenario is fought over: bridges, airfields, ports,
    /// built-up areas — drawn on the terrain as polygons rather than dropped as
    /// markers, because what matters about each is the ground it covers.
    ///
    ///   Pick a kind      — the next click starts an outline
    ///   Left click       — add a corner
    ///   Backspace        — undo the last corner
    ///   Right click / ⏎  — close it (at least four corners)
    ///   Esc              — abandon it
    ///
    /// **Four corners, not three.** Everything here is a built thing with an
    /// extent — a span, a runway, a yard, a quarter of a town — and a triangle
    /// is a shape none of them are. It also stops a stray double-click leaving a
    /// sliver on the map that has to be hunted down to delete. See
    /// <see cref="MapObjectCatalog.MinCorners"/>.
    ///
    /// **Each belongs to a side.** A bridge in friendly hands and the same
    /// bridge in the enemy's are different problems, so the outline is drawn in
    /// the owner's colour over the kind's own and the panel lists them apart.
    /// Neutral ownership is deliberately not modelled: the editor works one side
    /// at a time, and a third state nobody can select would be a state nobody
    /// can edit.
    ///
    /// Objects live in the **map file** (`MapSaveData.mapObjects`) — they are
    /// the ground, and the ground is what a map is.
    ///
    /// See docs/33-MAP-OBJECTS.md.
    /// </summary>
    public class MapObjectSystem : MonoBehaviour
    {
        /// <summary>Short user-facing messages, wired to the HUD's flash line.</summary>
        public System.Action<string> Flash;
        /// <summary>Raised when the list or the armed kind changes, so the panel repaints.</summary>
        public event System.Action Changed;

        /// <summary>Which side a newly drawn object belongs to. The panel's side tabs set it.</summary>
        public Team Team = Team.User;

        /// <summary>The kind being drawn, or null when nothing is armed.</summary>
        public MapObjectKind? Armed { get; private set; }

        /// <summary>True while an outline is being laid down.</summary>
        public bool Drawing => _draft.Count > 0;

        MapManager _map;
        Camera _cam;

        readonly List<MapObjectData> _objects = new List<MapObjectData>();
        readonly Dictionary<string, MapLine> _lines = new Dictionary<string, MapLine>();
        readonly List<GeoPoint> _draft = new List<GeoPoint>();
        MapLine _draftLine;

        public IReadOnlyList<MapObjectData> Objects => _objects;

        public void Init(MapManager map, Camera cam)
        {
            _map = map;
            _cam = cam;
        }

        // ------------------------------------------------------------- arming

        public void Arm(MapObjectKind kind)
        {
            Armed = kind;
            ClearDraft();
            var def = MapObjectCatalog.Get(kind);
            Flash?.Invoke($"{def.name} — click {MapObjectCatalog.MinCorners} or more corners on the map, " +
                          "right-click or Enter to close, Esc to abandon.");
            Changed?.Invoke();
        }

        public void Cancel()
        {
            bool wasDrawing = Drawing;
            Armed = null;
            ClearDraft();
            if (wasDrawing) Flash?.Invoke("Outline abandoned.");
            Changed?.Invoke();
        }

        public int CountFor(Team team)
        {
            int n = 0;
            foreach (var o in _objects) if (o.team == team.ToString()) n++;
            return n;
        }

        // ------------------------------------------------------------ drawing

        void Update()
        {
            if (!Armed.HasValue || _cam == null || _map == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (Input.GetMouseButtonDown(0) &&
                _map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out double h);
                _draft.Add(new GeoPoint { latitude = lat, longitude = lon, heightMeters = h });
                RedrawDraft();
            }

            if (Input.GetKeyDown(KeyCode.Backspace) && _draft.Count > 0)
            {
                _draft.RemoveAt(_draft.Count - 1);
                RedrawDraft();
            }

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Return)) Close();
            if (Input.GetKeyDown(KeyCode.Escape)) Cancel();
        }

        /// <summary>
        /// Closes the outline into an object, or says why it cannot be closed.
        ///
        /// A short outline is **kept, not thrown away**: the player is told what
        /// is missing and goes on clicking. Discarding three corners because the
        /// fourth had not been placed yet would punish the one mistake the
        /// minimum is there to prevent.
        /// </summary>
        void Close()
        {
            if (!Armed.HasValue) return;

            if (_draft.Count < MapObjectCatalog.MinCorners)
            {
                Flash?.Invoke($"{MapObjectCatalog.Get(Armed.Value).name} needs at least " +
                              $"{MapObjectCatalog.MinCorners} corners — {_draft.Count} so far.");
                return;
            }

            var def = MapObjectCatalog.Get(Armed.Value);
            var data = new MapObjectData
            {
                id = System.Guid.NewGuid().ToString("N").Substring(0, 10),
                kind = Armed.Value.ToString(),
                team = Team.ToString(),
                label = def.name,
                points = new List<GeoPoint>(_draft)
            };

            _objects.Add(data);
            ClearDraft();
            Draw(data);

            Flash?.Invoke($"{def.name} drawn — {data.points.Count} corners. " +
                          "The kind stays armed; Esc to stop.");
            Changed?.Invoke();
        }

        public bool Remove(MapObjectData data)
        {
            if (data == null || !_objects.Remove(data)) return false;
            if (_lines.TryGetValue(data.id, out var line))
            {
                if (line != null) Destroy(line.gameObject);
                _lines.Remove(data.id);
            }
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            foreach (var line in _lines.Values) if (line != null) Destroy(line.gameObject);
            _lines.Clear();
            _objects.Clear();
            ClearDraft();
            Changed?.Invoke();
        }

        // --------------------------------------------------------------- save

        public List<MapObjectData> Serialize()
        {
            var copy = new List<MapObjectData>(_objects.Count);
            foreach (var o in _objects) copy.Add(o.Clone());
            return copy;
        }

        public void LoadFrom(List<MapObjectData> saved)
        {
            Clear();
            if (saved == null) return;

            foreach (var data in saved)
            {
                if (data == null || data.points == null ||
                    data.points.Count < MapObjectCatalog.MinCorners) continue;
                if (string.IsNullOrEmpty(data.id))
                    data.id = System.Guid.NewGuid().ToString("N").Substring(0, 10);
                _objects.Add(data);
                Draw(data);
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------ drawing

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
                _draftLine = MapLine.Create(_map.Georeference, LineData("draft", Armed ?? MapObjectKind.Bridge, Team, true));
            _draftLine.SetPoints(new List<GeoPoint>(_draft));
        }

        void ClearDraft()
        {
            _draft.Clear();
            if (_draftLine != null) { Destroy(_draftLine.gameObject); _draftLine = null; }
        }

        void Draw(MapObjectData data)
        {
            var kind = data.KindEnum;
            var ring = new List<GeoPoint>(data.points) { data.points[0] };   // closed

            var line = MapLine.Create(_map.Georeference,
                LineData(data.id, kind, data.TeamEnum, false, data.label));
            line.SetPoints(ring);
            _lines[data.id] = line;
        }

        MapLineData LineData(string id, MapObjectKind kind, Team team, bool draft, string label = null)
        {
            var def = MapObjectCatalog.Get(kind);
            return new MapLineData
            {
                id = "object-" + id,
                // Boundary is the one kind MapLine always drapes on the terrain,
                // which an outline has to do to stay readable across a river
                // bank or a ridge.
                kind = nameof(LineKind.Boundary),
                team = team.ToString(),
                is3D = false,
                autoGenerated = true,
                label = draft ? "" : (label ?? def.name),
                colorHex = def.colorHex,
                widthMeters = def.widthMeters,
                points = new List<GeoPoint>()
            };
        }

        void OnDestroy()
        {
            foreach (var line in _lines.Values) if (line != null) Destroy(line.gameObject);
            if (_draftLine != null) Destroy(_draftLine.gameObject);
        }
    }
}
