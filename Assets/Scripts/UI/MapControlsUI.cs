using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Map;

namespace IronMeridian.UI
{
    /// <summary>
    /// On-map controls: a zoom/orientation/projection cluster at the bottom-left
    /// of the map, and an optional compass rose at the bottom-right.
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
        RectTransform _fps;
        RectTransform _compass;
        RectTransform _compassDial;
        Text _compassHeading, _zoomReadout;

        // Both readouts run every frame, and assigning Text.text allocates a
        // string and dirties the canvas even when the glyphs are identical.
        // Cache the value each one is showing and only rebuild when it moves.
        int _shownHeading = int.MinValue;
        int _shownZoom = int.MinValue;
        bool _shownZoomInKm;

        Text _fpsLabel;
        int _shownFps = int.MinValue;
        int _fpsFrames;
        float _fpsSince;

        /// <summary>
        /// How often the reading is recomputed. Per-frame would be a number too
        /// jittery to read; a quarter-second still reacts fast enough to see a
        /// stutter, and costs one string a quarter-second instead of sixty.
        /// </summary>
        const float FpsInterval = 0.25f;
        (Image fill, Text label) _mode2D, _mode3D;

        public bool ControlsVisible { get; private set; } = true;
        public bool CompassVisible { get; private set; }

        public void Build(Canvas canvas, MapManager map, CameraRig rig)
        {
            _map = map;
            _rig = rig;

            BuildCluster(canvas);
            BuildCompass(canvas);

            // The projection is also set from MAP CONFIG and by loading a map,
            // so the pair follows the map rather than only its own clicks —
            // buttons that disagreed with the terrain would be worse than the
            // toggle they replaced.
            _map.ViewModeChanged += OnViewModeChanged;

            SetControlsVisible(ControlsVisible);
            SetCompassVisible(CompassVisible);
        }

        void OnViewModeChanged(ViewMode mode) => PaintProjection();

        void OnDestroy()
        {
            if (_map != null) _map.ViewModeChanged -= OnViewModeChanged;
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
            float x = Mathf.Max(LeftInset, chromeRight + 16f);
            _cluster.anchoredPosition = new Vector2(x, BottomInset);
            // The FPS readout is a row of the cluster, so it travels with it.
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

        // --------------------------------------------------------------- fps

        /// <summary>
        /// The frame-rate readout, sitting directly **above the altitude
        /// readout** at the foot of the zoom cluster.
        ///
        /// It used to live on its own at the top-left, which put the one number
        /// that is never about the map in the corner the map's own chrome
        /// claims first. Both readouts answer "how is this going right now", so
        /// they belong in the same stack: it is built as the cluster's
        /// second-to-last row, which also means <see cref="SetLeftInset"/>
        /// carries it aside with everything else instead of moving it
        /// separately.
        /// </summary>
        void BuildFps(ref float y)
        {
            _fps = UIFactory.CreateBorderedPanel(_cluster, "FpsReadout",
                UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(_fps, new Vector2(0f, 1f), new Vector2(0, -y),
                new Vector2(ButtonSize + 44f, 24));

            _fpsLabel = UIFactory.CreateText(_fps, "", UiTheme.FontLabel, UiTheme.TextDim);
            UIFactory.Stretch(_fpsLabel.rectTransform);

            UiTooltip.Attach(_fps.gameObject, "Frames per second");
            _fpsSince = Time.realtimeSinceStartup;

            y += 24f + Gap;
        }

        /// <summary>
        /// Counts frames against the **wall clock**, not <c>deltaTime</c>.
        ///
        /// Both <c>deltaTime</c> and <c>unscaledDeltaTime</c> are fabricated
        /// while <see cref="Time.captureFramerate"/> is set, which is exactly
        /// what <see cref="Core.CaptureSystem"/> does while recording — so
        /// either would report a serene 30 fps no matter how hard the machine
        /// was actually working. <c>realtimeSinceStartup</c> is the only source
        /// that still tells the truth during a take.
        /// </summary>
        void UpdateFps()
        {
            if (_fpsLabel == null) return;

            _fpsFrames++;
            float now = Time.realtimeSinceStartup;
            float elapsed = now - _fpsSince;
            if (elapsed < FpsInterval) return;

            int fps = Mathf.RoundToInt(_fpsFrames / elapsed);
            _fpsFrames = 0;
            _fpsSince = now;

            if (fps == _shownFps) return;
            _shownFps = fps;

            _fpsLabel.text = $"{fps} FPS";
            // Colour is the reading at a glance: the point of a counter you
            // never look directly at is that it catches your eye when it drops.
            _fpsLabel.color = fps >= 50 ? UiTheme.TextDim
                            : fps >= 25 ? UiTheme.Warning
                                        : UiTheme.Danger;
        }

        // ----------------------------------------------------------- cluster

        void BuildCluster(Canvas canvas)
        {
            _cluster = UIFactory.CreateGroup(canvas.transform, "MapControls");
            UIFactory.Place(_cluster, new Vector2(0f, 0f),
                new Vector2(LeftInset, BottomInset), new Vector2(ButtonSize, 250));

            float y = 0f;
            ClusterButton(UiIcons.Plus, "Zoom in", "Wheel up, or R", () => _rig.ZoomIn(), ref y);
            ClusterButton(UiIcons.Minus, "Zoom out", "Wheel down, or F", () => _rig.ZoomOut(), ref y);
            ClusterButton(UiIcons.CompassNeedle, "Face north", "Clears any Q/E rotation", () => _rig.ResetNorth(), ref y);
            BuildProjectionPair(ref y);
            ClusterButton(UiIcons.Person, "Frame the order of battle", "Centres on every deployed unit",
                FrameAllUnits, ref y);

            // Frame rate, then altitude: the two readouts close the stack, the
            // machine's health above the camera's.
            BuildFps(ref y);

            // Altitude readout, so the zoom buttons have a scale reference.
            var readoutFrame = UIFactory.CreateBorderedPanel(_cluster, "ZoomReadout", UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(readoutFrame, new Vector2(0f, 1f), new Vector2(0, -y), new Vector2(ButtonSize + 44f, 24));
            _zoomReadout = UIFactory.CreateText(readoutFrame, "", UiTheme.FontLabel, UiTheme.TextDim);
            UIFactory.Stretch(_zoomReadout.rectTransform);
            UiTooltip.Attach(readoutFrame.gameObject, "Camera altitude above the ground");
        }

        /// <summary>
        /// The projection pair: **2D** and **3D**, side by side in one band.
        ///
        /// Two buttons rather than the single toggle that used to be here. A
        /// toggle carrying a layers glyph says neither which projection the map
        /// is in nor which one pressing it will give you — it is only readable
        /// after you have pressed it and looked at the terrain. A pair states
        /// both: the lit one is where you are, the dark one is where you can go,
        /// and pressing the lit one is harmlessly idempotent rather than a
        /// silent flip back.
        /// </summary>
        void BuildProjectionPair(ref float y)
        {
            _mode2D = ProjectionButton("2D", 0f, ViewMode.Mode2D, y,
                "Flat, north-up — the map as a map");
            _mode3D = ProjectionButton("3D", ButtonSize + Gap, ViewMode.Mode3D, y,
                "Tilted, free heading — the map as ground");

            PaintProjection();
            y += ButtonSize + Gap;
        }

        (Image fill, Text label) ProjectionButton(string caption, float x, ViewMode mode, float y, string hint)
        {
            var frame = UIFactory.CreateBorderedPanel(_cluster, "View_" + caption, UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(ButtonSize, ButtonSize));

            var btn = UIFactory.CreateButton(frame, caption, () => SetProjection(mode),
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)btn.transform);
            UiTooltip.Attach(btn.gameObject, $"{caption} view   ·   {hint}");

            return (frame.Find("Fill").GetComponent<Image>(), btn.GetComponentInChildren<Text>(true));
        }

        /// <summary>
        /// Puts the map into one projection rather than flipping it. Asking for
        /// the projection you are already in has to be a no-op, or the pair
        /// would behave like the toggle it replaced.
        /// </summary>
        void SetProjection(ViewMode mode)
        {
            if (_map.ViewMode != mode) _map.SetViewMode(mode);
            _rig.SetMode(mode);
            PaintProjection();
        }

        /// <summary>Lights whichever projection the map is actually in.</summary>
        void PaintProjection()
        {
            if (_mode2D.fill == null || _map == null) return;
            bool flat = _map.ViewMode == ViewMode.Mode2D;

            _mode2D.fill.color = flat ? UiTheme.AccentWash : UiTheme.Chrome;
            _mode2D.label.color = flat ? UiTheme.Accent : UiTheme.TextDim;
            _mode3D.fill.color = flat ? UiTheme.Chrome : UiTheme.AccentWash;
            _mode3D.label.color = flat ? UiTheme.TextDim : UiTheme.Accent;
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
            if (_fps != null) _fps.gameObject.SetActive(visible);
        }

        public void SetCompassVisible(bool visible)
        {
            CompassVisible = visible;
            if (_compass != null) _compass.gameObject.SetActive(visible);
        }

        void Update()
        {
            // Before the rig check: the readout is about the machine, not the
            // camera, and it should keep counting while a map is still loading.
            if (ControlsVisible) UpdateFps();

            if (_rig == null) return;

            if (CompassVisible && _compassDial != null)
            {
                // The rose counter-rotates against the camera's heading, so its
                // N tick ends up where north actually is on screen: face east
                // (yaw 90) and north swings to the left, which in uGUI's
                // counter-clockwise-positive Z is +90.
                _compassDial.localRotation = Quaternion.Euler(0f, 0f, _rig.Yaw);

                // Yaw is already normalised to [0,360); rounding 359.7 gives
                // 360, which the modulo folds back to 0.
                int heading = Mathf.RoundToInt(_rig.Yaw) % 360;
                if (heading != _shownHeading)
                {
                    _shownHeading = heading;
                    _compassHeading.text = $"{heading:000}°";
                }
            }

            if (ControlsVisible && _zoomReadout != null)
            {
                float m = _rig.DistanceMeters;
                bool km = m >= 1000f;
                // Bucket at the resolution the format strings actually print:
                // a tenth of a kilometre, or a whole metre.
                int bucket = km ? Mathf.RoundToInt(m / 100f) : Mathf.RoundToInt(m);
                if (bucket != _shownZoom || km != _shownZoomInKm)
                {
                    _shownZoom = bucket;
                    _shownZoomInKm = km;
                    _zoomReadout.text = km ? $"{m / 1000f:0.#} km" : $"{m:0} m";
                }
            }
        }
    }
}
