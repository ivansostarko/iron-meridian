using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.UI
{
    /// <summary>
    /// On-map controls: a zoom/orientation cluster at the bottom-left of the
    /// map, and an optional compass rose.
    ///
    /// Both are opt-in from the MAP panel. Keyboard and mouse already cover
    /// everything here (wheel, WASD, Q/E), so permanent on-screen buttons would
    /// be clutter for a player who knows the shortcuts — but they are the only
    /// discoverable route for one who does not, and the compass is genuinely
    /// useful the moment the view is rotated off north.
    /// </summary>
    public class MapControlsUI : MonoBehaviour
    {
        const float ButtonSize = 36f;
        const float Gap = 6f;
        /// <summary>Clears the left rail plus a margin. The floor for <see cref="SetLeftInset"/>.</summary>
        const float LeftInset = UiTheme.LeftPanelWidth + 16f;
        /// <summary>Clears the unit action bar and the shortcut hint line.</summary>
        const float BottomInset = 74f;
        const float CompassSize = 96f;

        MapManager _map;
        CameraRig _rig;

        RectTransform _cluster;
        RectTransform _compass;
        RectTransform _compassNeedle;
        Text _compassHeading, _zoomReadout;

        public bool ControlsVisible { get; private set; } = true;
        public bool CompassVisible { get; private set; }

        public void Build(Canvas canvas, MapManager map, CameraRig rig)
        {
            _map = map;
            _rig = rig;

            BuildCluster(canvas);
            BuildCompass(canvas);

            SetControlsVisible(ControlsVisible);
            SetCompassVisible(CompassVisible);
        }

        /// <summary>
        /// Keeps the cluster clear of the editor's left chrome as the section
        /// panel slides in and out. The palette drives this every frame while
        /// the panel is moving, so the buttons travel with it rather than being
        /// covered by it — and the inset never drops below the rail, so they
        /// cannot end up underneath the nav.
        /// </summary>
        public void SetLeftInset(float chromeRight)
        {
            if (_cluster == null) return;
            _cluster.anchoredPosition = new Vector2(Mathf.Max(LeftInset, chromeRight + 16f), BottomInset);
        }

        // ----------------------------------------------------------- cluster

        void BuildCluster(Canvas canvas)
        {
            _cluster = UIFactory.CreateGroup(canvas.transform, "MapControls");
            UIFactory.Place(_cluster, new Vector2(0f, 0f),
                new Vector2(LeftInset, BottomInset), new Vector2(ButtonSize, 220));

            float y = 0f;
            ClusterButton(UiIcons.Plus, "Zoom in", () => _rig.ZoomIn(), ref y);
            ClusterButton(UiIcons.Minus, "Zoom out", () => _rig.ZoomOut(), ref y);
            ClusterButton(UiIcons.CompassNeedle, "Reset to north", () => _rig.ResetNorth(), ref y);
            ClusterButton(UiIcons.Layers, "Toggle 2D / 3D", () =>
            {
                _map.ToggleViewMode();
                _rig.SetMode(_map.ViewMode);
            }, ref y);
            ClusterButton(UiIcons.Person, "Frame the order of battle", FrameAllUnits, ref y);

            // Altitude readout, so the zoom buttons have a scale reference.
            var readoutFrame = UIFactory.CreateBorderedPanel(_cluster, "ZoomReadout", UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(readoutFrame, new Vector2(0f, 1f), new Vector2(0, -y), new Vector2(ButtonSize + 44f, 24));
            _zoomReadout = UIFactory.CreateText(readoutFrame, "", UiTheme.FontLabel, UiTheme.TextDim);
            UIFactory.Stretch(_zoomReadout.rectTransform);
        }

        void ClusterButton(Sprite glyph, string tooltip, UnityEngine.Events.UnityAction action, ref float y)
        {
            var frame = UIFactory.CreateBorderedPanel(_cluster, "Ctl_" + tooltip, UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(0, -y), new Vector2(ButtonSize, ButtonSize));

            var btn = UIFactory.CreateIconButton(frame, glyph, action, new Color(0, 0, 0, 0), UiTheme.TextDim, 9f);
            UIFactory.Stretch((RectTransform)btn.transform);
            btn.name = tooltip;

            y += ButtonSize + Gap;
        }

        /// <summary>Pulls the camera back far enough to see every deployed unit.</summary>
        void FrameAllUnits()
        {
            var all = IronMeridian.Units.UnitRegistry.All;
            int count = 0;
            double lat = 0, lon = 0;
            foreach (var u in all)
            {
                if (u == null || !u.IsAlive) continue;
                lat += u.State.latitude; lon += u.State.longitude; count++;
            }
            if (count == 0) return;

            var focus = GeoUtils.GeoToUnity(_map.Georeference, lat / count, lon / count, 300);
            _rig.JumpTo(focus);
        }

        // ----------------------------------------------------------- compass

        void BuildCompass(Canvas canvas)
        {
            // Bottom-right of the map viewport, inset far enough to clear the
            // unit info panel when it is open.
            _compass = UIFactory.CreateGroup(canvas.transform, "Compass");
            UIFactory.Place(_compass, new Vector2(1f, 0f),
                new Vector2(-(UiTheme.RightPanelWidth + 24f), BottomInset),
                new Vector2(CompassSize, CompassSize + 22f));

            var dial = UIFactory.CreateImage(_compass, UiIcons.CompassRose, "Dial");
            dial.color = new Color(1f, 1f, 1f, 0.55f);
            dial.raycastTarget = false;
            UIFactory.Place((RectTransform)dial.transform, new Vector2(0.5f, 1f),
                new Vector2(0, 0), new Vector2(CompassSize, CompassSize));

            var needle = UIFactory.CreateImage(_compass, UiIcons.CompassNeedle, "Needle");
            needle.color = UiTheme.Hostile;
            needle.raycastTarget = false;
            _compassNeedle = (RectTransform)needle.transform;
            UIFactory.Place(_compassNeedle, new Vector2(0.5f, 1f), new Vector2(0, 0), new Vector2(CompassSize, CompassSize));

            _compassHeading = UIFactory.CreateText(_compass, "000°", UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_compassHeading.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 0), new Vector2(CompassSize, 20));
        }

        // ------------------------------------------------------------ toggles

        public void SetControlsVisible(bool visible)
        {
            ControlsVisible = visible;
            if (_cluster != null) _cluster.gameObject.SetActive(visible);
        }

        public void SetCompassVisible(bool visible)
        {
            CompassVisible = visible;
            if (_compass != null) _compass.gameObject.SetActive(visible);
        }

        void Update()
        {
            if (_rig == null) return;

            if (CompassVisible && _compassNeedle != null)
            {
                // The needle points to north, so it counter-rotates against the
                // camera's heading: turning the view right swings north left.
                _compassNeedle.localRotation = Quaternion.Euler(0f, 0f, _rig.Yaw);
                _compassHeading.text = $"{Mathf.RoundToInt(_rig.Yaw):000}°";
            }

            if (ControlsVisible && _zoomReadout != null)
            {
                float m = _rig.DistanceMeters;
                _zoomReadout.text = m >= 1000f ? $"{m / 1000f:0.#} km" : $"{m:0} m";
            }
        }
    }
}
