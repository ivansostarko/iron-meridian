using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.Lines
{
    /// <summary>
    /// Draws and shows a mission's <see cref="MissionArea"/> on the map.
    ///
    ///   Left click          — add a corner on the terrain
    ///   Right click / Enter — close the area (min 3 corners)
    ///   Backspace           — undo the last corner
    ///   Esc                 — cancel, leaving the area as it was
    ///
    /// **Why this is not a <see cref="LineManager"/> line.** Everything in the
    /// line manager is part of the *map file* — saved, loaded and fought over.
    /// A mission area belongs to the *mission record* instead: the same ground
    /// can carry two missions with different boundaries, and a boundary has to
    /// survive the map being re-saved. So the overlay here is built directly,
    /// which is what keeps it out of <c>MapSaveData.lines</c>.
    ///
    /// It is now the only click-to-draw tool in the editor — the hand-drawn
    /// control measures it used to sit beside are gone; see docs/03-GAMEPLAY.md.
    ///
    /// The overlay is shown whenever a mission is open, drawing or not: an area
    /// you cannot see is an area you cannot check.
    ///
    /// See docs/22-MISSIONS.md.
    /// </summary>
    public class MissionAreaTool : MonoBehaviour
    {
        /// <summary>
        /// Amber, and its own colour rather than the doctrinal boundary yellow.
        /// This is not a control measure somebody drew for the troops; it is the
        /// edge of the scenario, and the two must never read as the same object.
        /// </summary>
        const string AreaColour = "#FFB020";
        /// <summary>Width in metres. Wider than a boundary — it is the frame, not a line inside it.</summary>
        const float AreaWidthM = 260f;

        public bool Drawing { get; private set; }

        /// <summary>Raised when the area changes — closed, cleared or replaced.</summary>
        public System.Action<MissionArea> AreaChanged;
        /// <summary>Raised when drawing starts or stops, so the HUD can say what the mouse is doing.</summary>
        public System.Action<bool> DrawingChanged;
        /// <summary>Short user-facing messages; wired to the HUD's flash line.</summary>
        public System.Action<string> Flash;

        MapManager _map;
        Camera _cam;
        MissionArea _area;
        MapLine _overlay;
        readonly List<GeoPoint> _draft = new List<GeoPoint>();

        public void Init(MapManager map, Camera cam)
        {
            _map = map;
            _cam = cam;
        }

        /// <summary>The area being shown. Never null once <see cref="Show"/> has run.</summary>
        public MissionArea Area => _area;

        /// <summary>Points the tool at a mission's area and draws it.</summary>
        public void Show(MissionArea area)
        {
            CancelDrawing();
            _area = area;
            Redraw();
        }

        /// <summary>Takes the overlay off the map — the editor is no longer on a mission.</summary>
        public void Hide()
        {
            CancelDrawing();
            _area = null;
            DestroyOverlay();
        }

        // ---------------------------------------------------------- drawing

        public void StartDrawing()
        {
            if (_area == null)
            {
                Flash?.Invoke("Select or create a mission before drawing its area.");
                return;
            }
            _draft.Clear();
            Drawing = true;
            DrawingChanged?.Invoke(true);
            Flash?.Invoke("Click the corners of the mission area. Right-click or Enter to close, Esc to cancel.");
            Redraw();
        }

        public void CancelDrawing()
        {
            if (!Drawing) return;
            _draft.Clear();
            Drawing = false;
            DrawingChanged?.Invoke(false);
            Redraw();
        }

        /// <summary>Replaces the area outright — the RECTANGLE FROM VIEW route.</summary>
        public void SetArea(MissionArea area)
        {
            CancelDrawing();
            if (_area == null || area == null) return;

            _area.points = area.points;
            Redraw();
            AreaChanged?.Invoke(_area);
        }

        public void ClearArea()
        {
            CancelDrawing();
            if (_area == null) return;

            _area.Clear();
            Redraw();
            AreaChanged?.Invoke(_area);
            Flash?.Invoke("Mission area cleared — the mission is unbounded again.");
        }

        void Update()
        {
            if (!Drawing || _cam == null || _map == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            if (Input.GetMouseButtonDown(0) &&
                _map.RaycastGround(_cam, Input.mousePosition, out Vector3 world))
            {
                GeoUtils.UnityToGeo(_map.Georeference, world, out double lat, out double lon, out double h);
                _draft.Add(new GeoPoint { latitude = lat, longitude = lon, heightMeters = h });
                Redraw();
            }

            if (Input.GetKeyDown(KeyCode.Backspace) && _draft.Count > 0)
            {
                _draft.RemoveAt(_draft.Count - 1);
                Redraw();
            }

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Return)) Finish();
            if (Input.GetKeyDown(KeyCode.Escape)) CancelDrawing();
        }

        void Finish()
        {
            if (_draft.Count < 3)
            {
                Flash?.Invoke("A mission area needs at least three corners.");
                CancelDrawing();
                return;
            }

            _area.points = new List<GeoPoint>(_draft);
            _draft.Clear();
            Drawing = false;
            DrawingChanged?.Invoke(false);

            Redraw();
            AreaChanged?.Invoke(_area);
            Flash?.Invoke($"Mission area set — {_area.VertexCount} corners, {_area.AreaKm2():n0} km². " +
                          "SAVE MISSION + MAP to keep it.");
        }

        // ----------------------------------------------------------- drawing

        void Redraw()
        {
            // While drawing, show the draft as an open chain: closing it before
            // the last corner is placed would draw an edge the designer has not
            // asked for and cannot see the shape of.
            List<GeoPoint> ring =
                Drawing ? new List<GeoPoint>(_draft)
                        : (_area != null ? _area.ClosedRing() : new List<GeoPoint>());

            if (ring.Count < 2) { DestroyOverlay(); return; }

            if (_overlay == null) _overlay = MapLine.Create(_map.Georeference, NewData());
            _overlay.SetPoints(ring);
        }

        MapLineData NewData() => new MapLineData
        {
            id = "mission-area",
            // Boundary is the one kind MapLine always drapes on the terrain,
            // which is what an area outline has to do to stay readable across a
            // ridge. Its colour and width are overridden above so it cannot be
            // mistaken for a lateral boundary.
            kind = LineKind.Boundary.ToString(),
            team = "",
            is3D = false,
            autoGenerated = true,
            label = "MISSION AREA",
            colorHex = AreaColour,
            widthMeters = AreaWidthM,
            points = new List<GeoPoint>()
        };

        void DestroyOverlay()
        {
            if (_overlay != null) Destroy(_overlay.gameObject);
            _overlay = null;
        }

        void OnDestroy() => DestroyOverlay();
    }
}
