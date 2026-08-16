using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.UI
{
    /// <summary>
    /// On-map controls: a zoom/orientation cluster at the bottom-left of the
    /// map, and an optional compass rose at the bottom-right.
    ///
    /// Both are opt-in from the MAP panel. Keyboard and mouse already cover
    /// everything here (wheel, WASD, Q/E), so permanent on-screen buttons would
    /// be clutter for a player who knows the shortcuts — but they are the only
    /// discoverable route for one who does not, and the compass is genuinely
    /// useful the moment the view is rotated off north. Every button carries a
    /// hover caption (<see cref="UiTooltip"/>), because a row of bare glyphs is
    /// only discoverable if you can find out what they do without pressing them.
    ///
    /// **The compass rotates, the index does not.** The rose carries the
    /// cardinal ticks and turns so its N tick sits where real north is on
    /// screen; a fixed marker at the top of the bezel reads against it, which is
    /// the bearing printed underneath. Rotating around the rose's own centre is
    /// the part that has to be right — anchoring the dial by its top edge, as
    /// this did, swings it round that edge instead of spinning it in place.
    /// </summary>
    public class MapControlsUI : MonoBehaviour
    {
        const float ButtonSize = 36f;
        const float Gap = 6f;
        /// <summary>Clears the left rail plus a margin. The floor for <see cref="SetLeftInset"/>.</summary>
        const float LeftInset = UiTheme.LeftPanelWidth + 16f;
        /// <summary>Clears the unit action bar and the shortcut hint line.</summary>
        const float BottomInset = 74f;
        /// <summary>Margin from the right edge of the screen when nothing is in the way.</summary>
        const float RightInset = 24f;
        const float CompassSize = 104f;

        MapManager _map;
        CameraRig _rig;

        RectTransform _cluster;
        RectTransform _compass;
        RectTransform _compassDial;
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

        /// <summary>
        /// Keeps the compass clear of the right-hand chrome. The unit info panel
        /// only exists while something is selected, so the compass sits in the
        /// true bottom-right corner most of the time and steps aside when the
        /// panel opens — rather than being permanently parked a panel's width inboard for
        /// a panel that is usually not there.
        /// </summary>
        public void SetRightInset(float chromeWidth)
        {
            if (_compass == null) return;
            _compass.anchoredPosition = new Vector2(-(RightInset + Mathf.Max(0f, chromeWidth)), BottomInset);
        }

        // ----------------------------------------------------------- cluster

        void BuildCluster(Canvas canvas)
        {
            _cluster = UIFactory.CreateGroup(canvas.transform, "MapControls");
            UIFactory.Place(_cluster, new Vector2(0f, 0f),
                new Vector2(LeftInset, BottomInset), new Vector2(ButtonSize, 220));

            float y = 0f;
            ClusterButton(UiIcons.Plus, "Zoom in", "Wheel up, or R", () => _rig.ZoomIn(), ref y);
            ClusterButton(UiIcons.Minus, "Zoom out", "Wheel down, or F", () => _rig.ZoomOut(), ref y);
            ClusterButton(UiIcons.CompassNeedle, "Face north", "Clears any Q/E rotation", () => _rig.ResetNorth(), ref y);
            ClusterButton(UiIcons.Layers, "Toggle 2D / 3D", "Same world, different camera", () =>
            {
                _map.ToggleViewMode();
                _rig.SetMode(_map.ViewMode);
            }, ref y);
            ClusterButton(UiIcons.Person, "Frame the order of battle", "Centres on every deployed unit",
                FrameAllUnits, ref y);

            // Altitude readout, so the zoom buttons have a scale reference.
            var readoutFrame = UIFactory.CreateBorderedPanel(_cluster, "ZoomReadout", UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(readoutFrame, new Vector2(0f, 1f), new Vector2(0, -y), new Vector2(ButtonSize + 44f, 24));
            _zoomReadout = UIFactory.CreateText(readoutFrame, "", UiTheme.FontLabel, UiTheme.TextDim);
            UIFactory.Stretch(_zoomReadout.rectTransform);
            UiTooltip.Attach(readoutFrame.gameObject, "Camera altitude above the ground");
        }

        /// <summary>
        /// One glyph button plus its hover caption. The caption is two lines:
        /// what it does, and the keyboard route to the same thing — so the
        /// buttons teach the shortcuts rather than competing with them.
        /// </summary>
        void ClusterButton(Sprite glyph, string label, string hint,
            UnityEngine.Events.UnityAction action, ref float y)
        {
            var frame = UIFactory.CreateBorderedPanel(_cluster, "Ctl_" + label, UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(0, -y), new Vector2(ButtonSize, ButtonSize));

            var btn = UIFactory.CreateIconButton(frame, glyph, action, new Color(0, 0, 0, 0), UiTheme.TextDim, 9f);
            UIFactory.Stretch((RectTransform)btn.transform);
            btn.name = label;
            UiTooltip.Attach(btn.gameObject,
                string.IsNullOrEmpty(hint) ? label : $"{label}   ·   {hint}");

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
            _compass = UIFactory.CreateGroup(canvas.transform, "Compass");
            UIFactory.Place(_compass, new Vector2(1f, 0f),
                new Vector2(-RightInset, BottomInset),
                new Vector2(CompassSize, CompassSize + 24f));

            // Backing plate, so the dial reads against satellite imagery instead
            // of disappearing into whatever happens to be under it.
            var plate = UIFactory.CreateBorderedPanel(_compass, "Plate", UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(plate, new Vector2(0.5f, 1f), new Vector2(0, 0), new Vector2(CompassSize, CompassSize));
            plate.pivot = new Vector2(0.5f, 1f);
            plate.GetComponent<Image>().raycastTarget = false;
            plate.Find("Fill").GetComponent<Image>().raycastTarget = false;

            // Everything that turns lives in this group, centred on the plate:
            // rotation happens about the pivot, so the pivot has to be the
            // rose's own centre or the dial orbits instead of spinning.
            _compassDial = UIFactory.CreateGroup(plate, "Dial");
            _compassDial.anchorMin = _compassDial.anchorMax = _compassDial.pivot = new Vector2(0.5f, 0.5f);
            _compassDial.anchoredPosition = Vector2.zero;
            _compassDial.sizeDelta = new Vector2(CompassSize, CompassSize);

            var rose = UIFactory.CreateImage(_compassDial, UiIcons.CompassRose, "Rose");
            rose.color = new Color(1f, 1f, 1f, 0.6f);
            rose.raycastTarget = false;
            UIFactory.Stretch((RectTransform)rose.transform);

            var needle = UIFactory.CreateImage(_compassDial, UiIcons.CompassNeedle, "Needle");
            needle.color = UiTheme.Hostile;
            needle.raycastTarget = false;
            UIFactory.Stretch((RectTransform)needle.transform);

            // Fixed index at the top of the bezel: the rose turns under it, and
            // what it points at is the bearing printed below.
            var index = UIFactory.CreatePanel(plate, "Index", UiTheme.Accent);
            UIFactory.Place(index, new Vector2(0.5f, 1f), new Vector2(0, -2), new Vector2(3, 12));
            index.GetComponent<Image>().raycastTarget = false;

            _compassHeading = UIFactory.CreateText(_compass, "000°", UiTheme.FontSmall, UiTheme.Text,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_compassHeading.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 0),
                new Vector2(CompassSize, 20));

            UiTooltip.Attach(plate.gameObject, "View heading — click to face north", UiTooltip.Side.Left);
            var reset = plate.gameObject.AddComponent<Button>();
            reset.targetGraphic = plate.GetComponent<Image>();
            reset.onClick.AddListener(() => _rig.ResetNorth());
            plate.GetComponent<Image>().raycastTarget = true;
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

            if (CompassVisible && _compassDial != null)
            {
                // The rose counter-rotates against the camera's heading, so its
                // N tick ends up where north actually is on screen: face east
                // (yaw 90) and north swings to the left, which in uGUI's
                // counter-clockwise-positive Z is +90.
                _compassDial.localRotation = Quaternion.Euler(0f, 0f, _rig.Yaw);
                _compassHeading.text = $"{Mathf.RoundToInt(_rig.Yaw) % 360:000}°";
            }

            if (ControlsVisible && _zoomReadout != null)
            {
                float m = _rig.DistanceMeters;
                _zoomReadout.text = m >= 1000f ? $"{m / 1000f:0.#} km" : $"{m:0} m";
            }
        }
    }
}
