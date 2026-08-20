using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Lines;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// The battle minimap: the whole engagement at a glance, docked at the
    /// **top left**, under the command bar and clear of the left rail.
    ///
    /// **Why it exists.** The map is played at a few kilometres across while a
    /// scenario is tens of kilometres wide, so for most of a battle the player
    /// is looking at one part of a fight whose shape they cannot see. Zooming
    /// out to find it costs the detail they were using, and zooming back in
    /// costs the position they had. A minimap is the standard answer because it
    /// is the right one: a second, fixed-scale view that always shows the whole
    /// thing and never moves under you.
    ///
    /// **What it draws, and why only this.** The order of battle as blips, the
    /// front line, the mission boundary, and where the camera is looking. That
    /// is the operational picture — who is where, which way the fight faces,
    /// what ground the battle is on, and what part of it is on screen. Terrain
    /// imagery is deliberately absent: at 244 px a satellite thumbnail is a
    /// brown-green smear that hides the blips, and the map itself is right
    /// there for anyone who wants to look at ground.
    ///
    /// **It obeys the fog.** An enemy formation hidden by
    /// <see cref="FogOfWarSystem"/> is not drawn. A minimap that showed the
    /// whole red laydown would be a way of cheating past the fog, not a
    /// convenience.
    ///
    /// **Battle mode only**, like the fire menus opposite it — see
    /// <see cref="SetVisible"/>. In scenario mode nothing is moving, the left
    /// rail's DEPLOYED list already lists everything on the map, and the corner
    /// is better spent on the editor.
    ///
    /// **It folds away.** The header carries a toggle that collapses the picture
    /// to its caption bar and back — see <see cref="SetCollapsed"/>. A minimap is
    /// ambient, and ambient chrome that cannot be put away is chrome the player
    /// has to play around when the fight moves under it. Collapsed, it stops
    /// redrawing as well as stops drawing.
    ///
    /// The picture is rasterised into a <see cref="Texture2D"/> a few times a
    /// second rather than built from uGUI rects: a hundred formations would be
    /// a hundred <c>Image</c> components rebuilt on every move, and the front
    /// line is a polyline of several hundred vertices that uGUI has no way of
    /// drawing at all.
    /// </summary>
    public class MiniMapUI : MonoBehaviour
    {
        /// <summary>Raised with the ground the player clicked; the controller flies the camera there.</summary>
        public System.Action<double, double> FlyRequested;
        /// <summary>The open mission's boundary, when there is one. Null is normal — the editor has no mission.</summary>
        public System.Func<MissionArea> AreaSource;

        // ------------------------------------------------------------ layout

        /// <summary>Raster size. Square, and near 1:1 with the drawn size.</summary>
        const int Tex = 256;
        /// <summary>
        /// On-screen size of the picture, px. Matches the fire-menu cluster's
        /// width — six buttons at 44 px with 6 px gaps — so the two read as one
        /// block of chrome. Grew with the cluster when AIR SUPPLY joined it.
        /// </summary>
        const float MapSize = 294f;
        const float Pad = UiTheme.PanelPadding;
        const float HeaderHeight = 22f, FooterHeight = 16f;

        /// <summary>Panel width — deliberately the cluster's width, so the two stack as one block.</summary>
        public const float PanelWidth = MapSize + Pad * 2f;
        /// <summary>Overall height of the docked block, so the panels below it know what to clear.</summary>
        public const float BlockHeight = HeaderHeight + MapSize + FooterHeight + Pad;
        /// <summary>Height of the caption bar left behind when the picture is folded away.</summary>
        const float CollapsedHeight = HeaderHeight + 4f;
        /// <summary>Side of the header's fold control.</summary>
        const float ToggleSize = 18f;

        /// <summary>Seconds between redraws. Fast enough to read as live, cheap enough to ignore.</summary>
        const float RefreshSeconds = 0.15f;

        // ------------------------------------------------------------- state

        RectTransform _panel;
        RawImage _image;
        Texture2D _texture;
        Color32[] _pixels;
        Text _scaleLabel, _north, _toggleGlyph;

        MapManager _map;
        CameraRig _rig;
        FrontlineSystem _frontline;

        IReadOnlyList<UnitActor> _selection = new List<UnitActor>();

        /// <summary>Centre of the picture, geodetic. Eased toward the battle's own centre.</summary>
        double _centreLat, _centreLon;
        /// <summary>Half the picture's width, km. Eased, so the view does not jump as units die.</summary>
        float _halfSpanKm = 6f;
        bool _framed;

        float _timer;
        bool _visible;
        /// <summary>
        /// Folded to its caption bar. Survives the battle stopping and starting
        /// again, and **starts folded**.
        ///
        /// A battle opens on the ground the player chose to look at, and 294 px
        /// of chrome unfolding over the top left corner of it the moment the
        /// fight starts is the screen deciding for them. Collapsed, the block is
        /// a caption bar with a control on it: the overview is one click away
        /// and announces that it is there, which is the thing a hidden panel
        /// cannot do. It stays open once opened — the state outlives the battle
        /// stopping and starting.
        /// </summary>
        bool _collapsed = true;

        /// <summary>
        /// Distance from the left edge of the screen. Rides the editor's left
        /// chrome, like the zoom cluster does, so the panel sliding out does not
        /// bury it - see <see cref="SetLeftInset"/>.
        /// </summary>
        float _leftInset = DefaultLeftInset;

        /// <summary>Clear of the rail and a margin — where it sits until told otherwise.</summary>
        const float DefaultLeftInset = UiTheme.LeftPanelWidth + 16f;
        /// <summary>
        /// The screen's own margin. The floor for <see cref="SetLeftInset"/>,
        /// which is what a mission — with no rail at all — gets.
        /// </summary>
        const float MinLeftInset = 16f;
        /// <summary>Clears the command bar across the top of the screen.</summary>
        const float TopInset = UiTheme.TopBarHeight + 12f;

        // ------------------------------------------------------------ colours

        static readonly Color32 Ground = new Color32(9, 15, 22, 235);
        static readonly Color32 Grid = new Color32(22, 34, 47, 255);
        static readonly Color32 GridMajor = new Color32(32, 48, 66, 255);
        static readonly Color32 Halo = new Color32(4, 7, 10, 255);
        static readonly Color32 AreaEdge = new Color32(70, 104, 140, 255);

        // -------------------------------------------------------------- build

        public void Build(Canvas canvas, MapManager map, CameraRig rig, FrontlineSystem frontline,
            double centreLat, double centreLon)
        {
            _map = map;
            _rig = rig;
            _frontline = frontline;
            _centreLat = centreLat;
            _centreLon = centreLon;

            // Top-left, under the command bar. The right-hand edge is three
            // panels deep already - the unit inspector, the group panel, the
            // fire menus - and a minimap there was either covering one of them
            // or being covered by it. The bottom left belongs to the zoom
            // cluster and the order bar; the top left is the one corner of the
            // map this screen leaves empty, and a picture that is read at a
            // glance belongs where the eye already starts.
            _panel = UIFactory.CreateBorderedPanel(canvas.transform, "MiniMap", UiTheme.Chrome, UiTheme.Border);
            UIFactory.Place(_panel, new Vector2(0f, 1f), new Vector2(_leftInset, -TopInset),
                new Vector2(PanelWidth, BlockHeight));

            // Header: caption, the north note, and the fold control at the end
            // of the row. Widths are measured back from the right-hand edge so
            // the three meet rather than overlap.
            float toggleX = PanelWidth - Pad - ToggleSize;
            float northX = toggleX - 6f - 40f;

            var caption = UIFactory.CreateSectionHeader(_panel, "TACTICAL OVERVIEW", UiTheme.TextFaint);
            UIFactory.PlaceTopLeft(caption.rectTransform, Pad, 6f, northX - Pad - 6f, 14f);
            UIFactory.Fit(caption, 8);

            // North is up and stays up: the picture is a map, not a repeat of
            // the camera. Saying so once is cheaper than a rotating rose.
            _north = UIFactory.CreateText(_panel, "N ▲", UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.PlaceTopLeft(_north.rectTransform, northX, 6f, 40f, 14f);

            var toggle = UIFactory.CreateButton(_panel, "", () => SetCollapsed(!_collapsed),
                UiTheme.Surface, UiTheme.TextDim, UiTheme.FontLabel);
            UIFactory.PlaceTopLeft((RectTransform)toggle.transform, toggleX, 4f, ToggleSize, ToggleSize);
            _toggleGlyph = toggle.GetComponentInChildren<Text>(true);
            UiTooltip.Attach(toggle.gameObject, "Show or hide the tactical overview", UiTooltip.Side.Right);

            _texture = new Texture2D(Tex, Tex, TextureFormat.RGBA32, false)
            {
                name = "MiniMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _pixels = new Color32[Tex * Tex];

            _image = UIFactory.CreateRawImage(_panel, "Picture");
            _image.texture = _texture;
            UIFactory.PlaceTopLeft(_image.rectTransform, Pad, HeaderHeight, MapSize, MapSize);

            // Click to go there. An EventTrigger rather than a Button because
            // *where* on the picture was clicked is the whole message, and
            // uGUI's Button throws the pointer data away.
            var trigger = _image.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(e => OnPictureClicked((PointerEventData)e));
            trigger.triggers.Add(entry);
            // To the right: the panel is against the left edge now, so a caption
            // on that side would be clamped back over the picture it describes.
            UiTooltip.Attach(_image.gameObject, "Click to move the camera there", UiTooltip.Side.Right);

            _scaleLabel = UIFactory.CreateText(_panel, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(_scaleLabel.rectTransform, Pad, HeaderHeight + MapSize + 3f,
                MapSize, FooterHeight);

            ApplyCollapsed();
            SetVisible(false);
        }

        /// <summary>
        /// Folds the picture away, leaving the caption bar and its control — so
        /// the way back is in the same place the way out was, which a panel that
        /// vanished entirely could not manage.
        /// </summary>
        public void SetCollapsed(bool collapsed)
        {
            if (_collapsed == collapsed) return;
            _collapsed = collapsed;
            ApplyCollapsed();
            if (!_collapsed && _visible) Redraw();
        }

        void ApplyCollapsed()
        {
            if (_panel == null) return;

            _panel.sizeDelta = new Vector2(PanelWidth, _collapsed ? CollapsedHeight : BlockHeight);
            if (_image != null) _image.gameObject.SetActive(!_collapsed);
            if (_scaleLabel != null) _scaleLabel.gameObject.SetActive(!_collapsed);
            if (_north != null) _north.gameObject.SetActive(!_collapsed);
            // ▼ folds it away, ► brings it back — the arrow points at what
            // pressing it does, not at the state it is in.
            if (_toggleGlyph != null) _toggleGlyph.text = _collapsed ? "►" : "▼";
        }

        /// <summary>
        /// Keeps the block clear of the editor's left chrome as the section
        /// panel slides in and out - the same treatment the zoom cluster gets,
        /// and for the same reason: a control the panel slides over is a control
        /// that is missing for as long as the panel is open.
        /// </summary>
        public void SetLeftInset(float chromeRight)
        {
            _leftInset = Mathf.Max(MinLeftInset, chromeRight + 16f);
            if (_panel != null) _panel.anchoredPosition = new Vector2(_leftInset, -TopInset);
        }

        /// <summary>Whether the block is on the screen, so the panels below it know whether to clear it.</summary>
        public bool Visible => _visible;

        /// <summary>Battle mode on or off. Redraws at once when it comes back.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_panel != null) _panel.gameObject.SetActive(visible);
            if (visible && !_collapsed)
            {
                _framed = false;      // re-frame on the laydown as it stands now
                Redraw();
            }
        }

        /// <summary>The current selection, so it can be picked out of the blips.</summary>
        public void SetSelection(IReadOnlyList<UnitActor> selection)
        {
            _selection = selection ?? new List<UnitActor>();
            if (_visible && !_collapsed) Redraw();
        }

        void OnDestroy()
        {
            if (_texture != null) Destroy(_texture);
        }

        void Update()
        {
            if (!_visible || _collapsed) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < RefreshSeconds) return;
            _timer = 0f;
            Redraw();
        }

        // ------------------------------------------------------------ framing

        /// <summary>
        /// Centres and scales the picture on everything that matters — every
        /// living formation, the mission boundary if there is one, and the
        /// ground the camera is looking at, so the view box can never be off
        /// the edge of the map that is supposed to be showing where it is.
        ///
        /// Eased rather than snapped. The extent changes whenever a formation
        /// moves or dies, and a minimap that re-scaled on every casualty would
        /// be a picture the player has to re-read instead of one they can
        /// glance at.
        /// </summary>
        void Frame()
        {
            double sumLat = 0, sumLon = 0;
            int count = 0;

            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive) continue;
                sumLat += u.State.latitude; sumLon += u.State.longitude; count++;
            }

            var area = AreaSource?.Invoke();
            if (area != null && area.HasArea)
                foreach (var p in area.points)
                {
                    sumLat += p.latitude; sumLon += p.longitude; count++;
                }

            GeoUtils.UnityToGeo(_map.Georeference, _rig.Focus, out double camLat, out double camLon, out _);
            sumLat += camLat; sumLon += camLon; count++;

            double targetLat = sumLat / count, targetLon = sumLon / count;

            // Half-span: the furthest thing from that centre, plus a margin so
            // nothing is drawn on the frame itself.
            double furthest = 0;
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive) continue;
                furthest = System.Math.Max(furthest,
                    GeoUtils.DistanceKm(targetLat, targetLon, u.State.latitude, u.State.longitude));
            }
            if (area != null && area.HasArea)
                foreach (var p in area.points)
                    furthest = System.Math.Max(furthest,
                        GeoUtils.DistanceKm(targetLat, targetLon, p.latitude, p.longitude));
            furthest = System.Math.Max(furthest,
                GeoUtils.DistanceKm(targetLat, targetLon, camLat, camLon));

            float targetHalf = Mathf.Clamp((float)furthest * 1.18f, 2.5f, 400f);

            if (!_framed)
            {
                _centreLat = targetLat; _centreLon = targetLon; _halfSpanKm = targetHalf;
                _framed = true;
                return;
            }

            // Unscaled: the picture must keep settling while the battle is
            // paused, which is exactly when it is being studied.
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2.5f);
            _centreLat += (targetLat - _centreLat) * k;
            _centreLon += (targetLon - _centreLon) * k;
            _halfSpanKm = Mathf.Lerp(_halfSpanKm, targetHalf, k);
        }

        // ------------------------------------------------------------ drawing

        void Redraw()
        {
            if (_texture == null || _map == null || _rig == null) return;

            Frame();
            Clear();
            DrawGrid();
            DrawMissionArea();
            DrawFrontline();
            DrawUnits();
            DrawViewBox();
            DrawFrame();

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);

            _scaleLabel.text = $"{_halfSpanKm * 2f:0.#} km across   ·   click to move the camera";
        }

        void Clear()
        {
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = Ground;
        }

        /// <summary>
        /// A grid at a round number of kilometres, so distances on the picture
        /// can be estimated rather than guessed. The spacing steps through
        /// 1/2/5/10/… so it stays between roughly four and ten lines whatever
        /// the battle's size.
        /// </summary>
        void DrawGrid()
        {
            float spacingKm = NiceStep(_halfSpanKm * 2f / 6f);
            float pxPerKm = Tex * 0.5f / _halfSpanKm;
            int step = Mathf.Max(8, Mathf.RoundToInt(spacingKm * pxPerKm));

            for (int offset = 0; offset <= Tex; offset += step)
            {
                bool major = (offset / step) % 5 == 0;
                var colour = major ? GridMajor : Grid;
                int a = Tex / 2 + offset, b = Tex / 2 - offset;
                VLine(a, colour); VLine(b, colour);
                HLine(a, colour); HLine(b, colour);
            }
        }

        /// <summary>1, 2, 5, 10, 20, 50 … — the grid spacings a map is allowed to have.</summary>
        static float NiceStep(float km)
        {
            float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(0.05f, km))));
            float normalised = km / magnitude;
            float step = normalised < 1.5f ? 1f : normalised < 3.5f ? 2f : normalised < 7.5f ? 5f : 10f;
            return step * magnitude;
        }

        void DrawMissionArea()
        {
            var area = AreaSource?.Invoke();
            if (area == null || !area.HasArea) return;

            var pts = area.points;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];      // implicitly closed
                Project(a.latitude, a.longitude, out int x0, out int y0);
                Project(b.latitude, b.longitude, out int x1, out int y1);
                Line(x0, y0, x1, y1, AreaEdge);
            }
        }

        /// <summary>
        /// Every published stretch of front — both sides' edges, or the manual
        /// trace — read straight off the front-line system's own
        /// segments, so the minimap can never disagree with the map about
        /// where the fighting is. Blue edge blue, red edge red, the manual
        /// line in the front-line red.
        /// </summary>
        void DrawFrontline()
        {
            var segments = _frontline != null ? _frontline.Segments : null;
            if (segments == null) return;

            foreach (var seg in segments)
            {
                var pts = seg.Points;
                if (pts == null || pts.Count < 2) continue;

                var colour = (Color32)(seg.Manual ? GameConfig.FrontlineRed
                    : seg.Team == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam);

                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    Project(pts[i].latitude, pts[i].longitude, out int x0, out int y0);
                    Project(pts[i + 1].latitude, pts[i + 1].longitude, out int x1, out int y1);
                    Line(x0, y0, x1, y1, colour);
                }
            }
        }

        void DrawUnits()
        {
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive) continue;
                // The fog decides what the player knows; the minimap only
                // reports it — see the class remarks.
                if (u.HiddenByFog) continue;

                Project(u.State.latitude, u.State.longitude, out int x, out int y);
                if (x < -4 || y < -4 || x > Tex + 4 || y > Tex + 4) continue;

                bool selected = Contains(_selection, u);
                var colour = (Color32)(u.State.TeamEnum == Team.User
                    ? GameConfig.BlueTeam : GameConfig.RedTeam);

                // Dark halo first: a blip on a grid line is otherwise the same
                // brightness as the line and disappears into it.
                Block(x, y, selected ? 4 : 3, Halo);
                Block(x, y, selected ? 3 : 2, colour);
                if (selected) Block(x, y, 1, new Color32(255, 255, 255, 255));
            }
        }

        static bool Contains(IReadOnlyList<UnitActor> list, UnitActor unit)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == unit) return true;
            return false;
        }

        /// <summary>
        /// What the camera can see, as a box on the ground with a tick out of
        /// its leading edge for the heading.
        ///
        /// The footprint is the ground the camera's field of view covers at its
        /// standoff, which is exact looking straight down and an approximation
        /// when the view is tilted (the true shape is then a trapezoid). A box
        /// is the right answer anyway: this is a "you are about here" marker,
        /// and a shape that changed as the view tilted would read as the battle
        /// moving rather than the camera.
        /// </summary>
        void DrawViewBox()
        {
            var cam = _rig.Cam;
            if (cam == null) return;

            GeoUtils.UnityToGeo(_map.Georeference, _rig.Focus, out double lat, out double lon, out _);

            float halfHeightKm = _rig.DistanceMeters * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / 1000f;
            float halfWidthKm = halfHeightKm * Mathf.Max(0.2f, cam.aspect);

            float yaw = _rig.Yaw * Mathf.Deg2Rad;
            // East-north components of "forward" and "right" at that heading.
            var fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));

            Vector2 Corner(float w, float h) => right * (w * halfWidthKm) + fwd * (h * halfHeightKm);

            var corners = new[]
            {
                Corner(-1f, 1f), Corner(1f, 1f), Corner(1f, -1f), Corner(-1f, -1f)
            };

            var colour = (Color32)UiTheme.Accent;
            for (int i = 0; i < corners.Length; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % corners.Length];
                ProjectOffset(lat, lon, a, out int x0, out int y0);
                ProjectOffset(lat, lon, b, out int x1, out int y1);
                Line(x0, y0, x1, y1, colour);
            }

            // Heading tick off the leading edge, and the focus itself.
            ProjectOffset(lat, lon, fwd * halfHeightKm, out int hx0, out int hy0);
            ProjectOffset(lat, lon, fwd * (halfHeightKm * 1.35f), out int hx1, out int hy1);
            Line(hx0, hy0, hx1, hy1, colour);

            Project(lat, lon, out int fx, out int fy);
            Line(fx - 2, fy, fx + 2, fy, colour);
            Line(fx, fy - 2, fx, fy + 2, colour);
        }

        /// <summary>A hairline inside the raster's own edge, so the picture reads as an instrument.</summary>
        void DrawFrame()
        {
            var colour = (Color32)UiTheme.BorderStrong;
            HLine(0, colour); HLine(Tex - 1, colour);
            VLine(0, colour); VLine(Tex - 1, colour);
        }

        // ------------------------------------------------------- projection

        void Project(double lat, double lon, out int px, out int py)
        {
            GeoUtils.ToLocalKm(_centreLat, _centreLon, lat, lon, out double east, out double north);
            float scale = Tex * 0.5f / Mathf.Max(0.001f, _halfSpanKm);
            px = Mathf.RoundToInt((float)east * scale + Tex * 0.5f);
            py = Mathf.RoundToInt((float)north * scale + Tex * 0.5f);
        }

        /// <summary>A point given as an east/north offset in km from a geodetic origin.</summary>
        void ProjectOffset(double lat, double lon, Vector2 offsetKm, out int px, out int py)
        {
            GeoUtils.FromLocalKm(lat, lon, offsetKm.x, offsetKm.y, out double outLat, out double outLon);
            Project(outLat, outLon, out px, out py);
        }

        void OnPictureClicked(PointerEventData pointer)
        {
            if (_image == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _image.rectTransform, pointer.position, pointer.pressEventCamera, out Vector2 local))
                return;

            // Local space is centred on the picture's own pivot-independent
            // rect, so shift by the rect's centre before scaling.
            var rect = _image.rectTransform.rect;
            float u = (local.x - rect.center.x) / (rect.width * 0.5f);
            float v = (local.y - rect.center.y) / (rect.height * 0.5f);
            if (Mathf.Abs(u) > 1f || Mathf.Abs(v) > 1f) return;

            GeoUtils.FromLocalKm(_centreLat, _centreLon,
                u * _halfSpanKm, v * _halfSpanKm, out double lat, out double lon);
            FlyRequested?.Invoke(lat, lon);
        }

        // ---------------------------------------------------------- raster

        void Plot(int x, int y, Color32 colour)
        {
            if (x < 0 || y < 0 || x >= Tex || y >= Tex) return;
            _pixels[y * Tex + x] = colour;
        }

        void HLine(int y, Color32 colour)
        {
            if (y < 0 || y >= Tex) return;
            int row = y * Tex;
            for (int x = 0; x < Tex; x++) _pixels[row + x] = colour;
        }

        void VLine(int x, Color32 colour)
        {
            if (x < 0 || x >= Tex) return;
            for (int y = 0; y < Tex; y++) _pixels[y * Tex + x] = colour;
        }

        /// <summary>A filled square of side 2·<paramref name="half"/>+1 centred on the point.</summary>
        void Block(int cx, int cy, int half, Color32 colour)
        {
            for (int y = cy - half; y <= cy + half; y++)
                for (int x = cx - half; x <= cx + half; x++)
                    Plot(x, y, colour);
        }

        /// <summary>Bresenham. Endpoints off the raster are fine — <see cref="Plot"/> clips.</summary>
        void Line(int x0, int y0, int x1, int y1, Color32 colour)
        {
            // A line between two points that are both far outside would still
            // step through every pixel of its length; the front line can run
            // well past the frame when the view is zoomed into one flank.
            if ((x0 < 0 && x1 < 0) || (y0 < 0 && y1 < 0) ||
                (x0 >= Tex && x1 >= Tex) || (y0 >= Tex && y1 >= Tex)) return;

            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            // Bounded, so a pathological pair of coordinates cannot spin here.
            for (int guard = 0; guard < Tex * 4; guard++)
            {
                Plot(x0, y0, colour);
                if (x0 == x1 && y0 == y1) return;
                int e2 = err * 2;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
