using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Lines
{
    /// <summary>
    /// Click-to-draw tool for sector boundaries and defensive lines.
    ///   Left click  — add a point on the terrain
    ///   Right click / Enter — finish the line (min 2 points)
    ///   Esc         — cancel
    /// Lines can be drawn as 2D (flat) or 3D (terrain-following).
    /// </summary>
    public class LineDrawTool : MonoBehaviour
    {
        public enum Mode { None, Boundary, DefensiveLine }

        public Mode Current { get; private set; } = Mode.None;
        /// <summary>
        /// Whether the next line stands clear of the ground in the 3D view.
        /// Ignored for the kinds <see cref="IsFlatKind"/> names — those are map
        /// graphics and are always draped.
        /// </summary>
        public bool Draw3D = true;
        public Team DefensiveTeam = Team.User;

        /// <summary>
        /// Kinds that exist only as a drawn overlay: sector and rear
        /// boundaries, phase lines and the legacy boundary. Mirrors
        /// <see cref="MapLine.FlatOnly"/>, which is the enforcement.
        /// </summary>
        public static bool IsFlatKind(LineKind kind) =>
            kind == LineKind.LateralBoundary || kind == LineKind.RearBoundary ||
            kind == LineKind.PhaseLine || kind == LineKind.Boundary;

        /// <summary>
        /// Style the next drawn line will take. Set from the boundary options
        /// dialog before drawing starts; kept on the tool rather than passed to
        /// StartDrawing so the dialog can be re-opened and adjusted without
        /// interrupting an armed tool.
        /// </summary>
        public LineKind PendingKind = LineKind.LateralBoundary;
        /// <summary>Owning side, or null for a line that belongs to neither.</summary>
        public Team? PendingTeam;
        /// <summary>"#RRGGBB" override, or empty for the doctrinal colour.</summary>
        public string PendingColorHex = "";
        /// <summary>Metres; 0 keeps the width implied by the kind.</summary>
        public float PendingWidth;
        /// <summary>Planned/on-order measures are drawn broken.</summary>
        public bool PendingPlanned;
        /// <summary>Caption drawn on the line.</summary>
        public string PendingLabel = "";

        public event System.Action<Mode> ModeChanged;

        MapManager _map;
        Camera _cam;
        LineManager _lineManager;
        readonly List<GeoPoint> _points = new List<GeoPoint>();
        MapLine _preview;
        int _counter;
        /// <summary>True when drawing was started by a tool-strip button rather than the options dialog.</summary>
        bool _useLegacyKind = true;

        public void Init(MapManager map, Camera cam, LineManager lines)
        {
            _map = map; _cam = cam; _lineManager = lines;
        }

        /// <summary>Legacy entry point: the tool strip's boundary / defensive-line buttons.</summary>
        public void StartDrawing(Mode mode)
        {
            CancelDrawing();
            _useLegacyKind = true;
            Current = mode;
            ModeChanged?.Invoke(Current);
        }

        /// <summary>
        /// Starts drawing with the full pending style. Boundaries and phase
        /// lines route through <see cref="Mode.Boundary"/>, everything else
        /// through <see cref="Mode.DefensiveLine"/> — Mode only exists to tell
        /// the HUD something is being drawn.
        /// </summary>
        public void StartDrawingStyled()
        {
            CancelDrawing();
            _useLegacyKind = false;
            Current = PendingKind == LineKind.DefensiveLine || PendingKind == LineKind.Feba
                ? Mode.DefensiveLine
                : Mode.Boundary;
            ModeChanged?.Invoke(Current);
        }

        public void CancelDrawing()
        {
            if (_preview != null) _lineManager.Remove(_preview);
            _preview = null;
            _points.Clear();
            Current = Mode.None;
            ModeChanged?.Invoke(Current);
        }

        void Update()
        {
            if (Current == Mode.None || _cam == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (Input.GetMouseButtonDown(0) &&
                _map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out double h);
                _points.Add(new GeoPoint { latitude = lat, longitude = lon, heightMeters = h });
                UpdatePreview();
            }

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Return))
                FinishLine();

            if (Input.GetKeyDown(KeyCode.Escape))
                CancelDrawing();
        }

        void UpdatePreview()
        {
            if (_preview == null && _points.Count >= 1)
            {
                _preview = _lineManager.Add(NewData());
            }
            if (_preview != null)
                _preview.SetPoints(new List<GeoPoint>(_points));
        }

        void FinishLine()
        {
            if (_points.Count < 2)
            {
                CancelDrawing();
                return;
            }
            if (_preview == null) _preview = _lineManager.Add(NewData());
            _preview.SetPoints(new List<GeoPoint>(_points));
            _preview = null;
            _points.Clear();
            Current = Mode.None;
            ModeChanged?.Invoke(Current);
        }

        /// <summary>
        /// Builds the line about to be drawn from the pending style. The Mode
        /// only decides the fallback kind for the two legacy tool-strip buttons;
        /// anything opened through the boundary options dialog sets
        /// <see cref="PendingKind"/> and gets exactly what was chosen.
        /// </summary>
        MapLineData NewData()
        {
            LineKind kind = _useLegacyKind
                ? (Current == Mode.Boundary ? LineKind.Boundary : LineKind.DefensiveLine)
                : PendingKind;

            string team = _useLegacyKind
                ? (Current == Mode.DefensiveLine ? DefensiveTeam.ToString() : "")
                : (PendingTeam.HasValue ? PendingTeam.Value.ToString() : "");

            return new MapLineData
            {
                id = $"{kind.ToString().ToLowerInvariant()}-{++_counter}-{Random.Range(1000, 9999)}",
                kind = kind.ToString(),
                team = team,
                // Boundaries and phase lines are map graphics and are always
                // draped on the terrain — MapLine ignores the flag for them, and
                // writing a value it will not honour into the save would be a
                // lie the next reader has to know about. See MapLine.FlatOnly.
                is3D = Draw3D && !IsFlatKind(kind),
                autoGenerated = false,
                planned = !_useLegacyKind && PendingPlanned,
                label = _useLegacyKind ? "" : PendingLabel,
                colorHex = _useLegacyKind ? "" : PendingColorHex,
                widthMeters = _useLegacyKind ? 0f : PendingWidth,
                points = new List<GeoPoint>()
            };
        }
    }
}
