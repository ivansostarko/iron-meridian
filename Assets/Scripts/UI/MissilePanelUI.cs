using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Vfx;

namespace IronMeridian.UI
{
    /// <summary>
    /// The missile systems board: ten launchers, split NATO / Enemy, each
    /// arming a strike whose destruction radius is drawn on the map before
    /// anything is committed to.
    ///
    /// **This board is on the left, with the other fire menus.** It used to
    /// dock on the right, which gave it the width it needed and cost it the
    /// thing that mattered more: MISSILE SYSTEMS is a row in the left rail, and
    /// clicking a row on the left to open a board on the right reads as a
    /// mis-click. Worse, the right edge is where the unit info panel and the
    /// front-line panel live, so opening a fire menu had to drop the player's
    /// selection to make room. It now stands exactly where the rail's section
    /// panel stands, and simply takes the section panel's place — only one of
    /// the two is ever up, and both slide out from behind the same rail.
    ///
    /// It is wider than a section (<see cref="UiTheme.MissilePanelWidth"/>
    /// against <see cref="UiTheme.SectionPanelWidth"/>) because it is doing a
    /// different job. The sections hold controls you set and forget — a weather
    /// condition, a tile style. A missile system is chosen by *comparing* it
    /// against nine others on numbers that matter: what it covers, whether that
    /// number is a warhead or an umbrella, and which side fields it. That
    /// comparison needs a designation, a description and a radius on one row.
    ///
    /// **Air defence and surface strike are separated and labelled**, because
    /// their radius figures mean opposite things. A 3 km circle on SAMP/T is
    /// sky it can clear; a 620 m circle on Iskander is ground it destroys.
    /// Sorting them into one list by radius would put the two side by side and
    /// invite exactly the wrong reading.
    ///
    /// See docs/20-MISSILE-SYSTEMS.md.
    /// </summary>
    public class MissilePanelUI : MonoBehaviour
    {
        /// <summary>True while the panel is showing.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Raised when the panel opens, so competing panels can stand down.</summary>
        public System.Action Opened;

        /// <summary>
        /// Raised whenever the board appears or disappears, with the width it
        /// occupies on the left (0 when hidden). The on-map zoom cluster rides
        /// this so it is never buried underneath the board.
        /// </summary>
        public System.Action<float> LeftInsetChanged;

        const float Pad = UiTheme.PanelPadding;
        const float RowHeight = 52f;
        /// <summary>Where the board's left edge sits: hard against the always-present rail.</summary>
        const float DockX = UiTheme.LeftPanelWidth;
        const float PanelWidth = UiTheme.MissilePanelWidth;

        MissileStrikeSystem _missiles;
        RectTransform _panel;
        MissileOrigin _origin = MissileOrigin.Nato;

        readonly Dictionary<MissileOrigin, RectTransform> _pages =
            new Dictionary<MissileOrigin, RectTransform>();
        readonly List<(MissileOrigin origin, Image fill, Text label)> _tabs =
            new List<(MissileOrigin, Image, Text)>();
        readonly List<(MissileSystemId id, Image fill, Image glyph, Text label)> _buttons =
            new List<(MissileSystemId, Image, Image, Text)>();
        Text _budgetLabel;

        public static MissilePanelUI Create(Canvas canvas, MissileStrikeSystem missiles)
        {
            var go = new GameObject("MissilePanel");
            go.transform.SetParent(canvas.transform, false);

            var panel = go.AddComponent<MissilePanelUI>();
            panel._missiles = missiles;
            panel.Build(canvas);
            missiles.ArmedChanged += panel.Refresh;
            StrikeBudget.Changed += panel.Refresh;
            return panel;
        }

        void OnDestroy()
        {
            if (_missiles != null) _missiles.ArmedChanged -= Refresh;
            StrikeBudget.Changed -= Refresh;
            if (IsOpen) IsOpen = false;
        }

        // ------------------------------------------------------------- build

        void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "MissileSystems", UiTheme.Panel);
            _panel.anchorMin = new Vector2(0, 0);
            _panel.anchorMax = new Vector2(0, 1);
            _panel.pivot = new Vector2(0, 0.5f);
            _panel.offsetMin = new Vector2(DockX, 0);
            _panel.offsetMax = new Vector2(DockX + PanelWidth, -UiTheme.TopBarHeight);

            // Hairline down the board's outboard edge, where it meets the map —
            // the inboard edge butts against the rail and needs no rule.
            var edge = UIFactory.CreatePanel(_panel, "Edge", UiTheme.Border);
            edge.anchorMin = new Vector2(1, 0); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(1, 0.5f);
            edge.sizeDelta = new Vector2(1, 0);
            edge.GetComponent<Image>().raycastTarget = false;

            float inner = PanelWidth - Pad * 2f;

            var title = UIFactory.CreateText(_panel, "MISSILE SYSTEMS", UiTheme.FontHeading,
                UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -14),
                new Vector2(inner - 30f, 22));

            var close = UIFactory.CreateIconButton(_panel, UiIcons.Close, Hide,
                new Color(0, 0, 0, 0), UiTheme.TextDim, 7f);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-Pad, -12), new Vector2(26, 26));

            var rule = UIFactory.CreateDivider(_panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -38);

            StrikeBudgetRow(-46f, inner);
            BuildTabs(-82f, inner);

            // One page per inventory, both laid out at the same origin; only the
            // selected one is active. Same device as the artillery menu, for the
            // same reason: the first decision halves the list.
            foreach (MissileOrigin origin in System.Enum.GetValues(typeof(MissileOrigin)))
            {
                var page = UIFactory.CreateGroup(_panel, "MissilePage_" + origin);
                page.anchorMin = new Vector2(0, 0); page.anchorMax = new Vector2(1, 1);
                page.offsetMin = Vector2.zero; page.offsetMax = Vector2.zero;
                _pages[origin] = page;
                BuildPage(page, origin, inner);
            }

            ShowOrigin(_origin);
            _panel.gameObject.SetActive(false);
        }

        void BuildTabs(float y, float inner)
        {
            var origins = new[] { MissileOrigin.Nato, MissileOrigin.Enemy };
            var names = new[] { "NATO", "ENEMY" };
            float w = (inner - 6f) / 2f;

            for (int i = 0; i < origins.Length; i++)
            {
                var origin = origins[i];
                var frame = UIFactory.CreateBorderedPanel(_panel, "MissileOrigin_" + names[i],
                    UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad + i * (w + 6f), y),
                    new Vector2(w, 30));

                var btn = UIFactory.CreateButton(frame, names[i], () => ShowOrigin(origin),
                    new Color(0, 0, 0, 0), UiTheme.Text, UiTheme.FontSmall);
                UIFactory.Stretch((RectTransform)btn.transform);

                _tabs.Add((origin, frame.Find("Fill").GetComponent<Image>(),
                    btn.GetComponentInChildren<Text>()));
            }
        }

        /// <summary>
        /// The shared strike allowance. The same readout the three rail fire
        /// menus carry, because it is the same ninety-nine — see
        /// <see cref="StrikeBudget"/>.
        /// </summary>
        void StrikeBudgetRow(float y, float inner)
        {
            var frame = UIFactory.CreateBorderedPanel(_panel, "StrikeBudget",
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 28));

            var name = UIFactory.CreateText(frame, "STRIKES REMAINING", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(inner - 96f, 14));

            _budgetLabel = UIFactory.CreateText(frame, "", UiTheme.FontSmall, UiTheme.Accent,
                TextAnchor.MiddleRight, FontStyle.Bold);
            _budgetLabel.raycastTarget = false;
            UIFactory.Place(_budgetLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                new Vector2(80, 16));
        }

        void BuildPage(RectTransform page, MissileOrigin origin, float inner)
        {
            // Clear of the title, the allowance readout and the inventory tabs.
            float y = -122f;
            MissileRole? lastRole = null;

            // Air defence first: it is the larger group and the one whose radius
            // needs the caveat, so the heading it sits under is read first.
            foreach (var role in new[] { MissileRole.AirDefence, MissileRole.SurfaceStrike })
            {
                foreach (var def in MissileCatalog.OfOrigin(origin))
                {
                    if (def.role != role) continue;

                    if (lastRole != role)
                    {
                        lastRole = role;
                        SectionLabel(page, role == MissileRole.AirDefence
                            ? "AIR AND MISSILE DEFENCE" : "SURFACE-TO-SURFACE", y, inner);
                        y -= 16f;

                        var note = UIFactory.CreateText(page, role == MissileRole.AirDefence
                                ? "Radius = engagement footprint"
                                : "Radius = destruction area",
                            UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleLeft);
                        UIFactory.Place(note.rectTransform, new Vector2(0f, 1f),
                            new Vector2(Pad, y), new Vector2(inner, 14));
                        y -= 20f;
                    }

                    SystemButton(page, def, y, inner);
                    y -= RowHeight + 4f;
                }
            }

            var stop = UIFactory.CreateBorderedPanel(page, "StandDown", UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(stop, new Vector2(0f, 1f), new Vector2(Pad, y - 4f), new Vector2(inner, 30));
            var stopBtn = UIFactory.CreateButton(stop, "STAND DOWN",
                () => { if (_missiles != null) _missiles.Cancel(); },
                new Color(0, 0, 0, 0), UiTheme.TextDim, UiTheme.FontSmall);
            UIFactory.Stretch((RectTransform)stopBtn.transform);

            var hint = UIFactory.CreateText(page,
                "Pick a system, then click the map. The circle following the cursor is that system's " +
                "radius — see it before you commit to the ground. A ten second countdown runs in the HUD, " +
                "then the missile comes over the horizon and the warhead lands inside the ring. A mission " +
                "cannot be recalled once away; STAND DOWN only clears the launcher.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y - 42f),
                new Vector2(inner, 130));
        }

        void SectionLabel(RectTransform parent, string text, float y, float inner)
        {
            var t = UIFactory.CreateSectionHeader(parent, text, UiTheme.TextDim);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 14));
        }

        void SystemButton(RectTransform page, MissileSystemDef def, float y, float inner)
        {
            var frame = UIFactory.CreateBorderedPanel(page, "Missile_" + def.id,
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, RowHeight));

            var btn = UIFactory.CreateButton(frame, "",
                () => { if (_missiles != null) _missiles.Toggle(def.id); },
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var caption = btn.GetComponentInChildren<Text>(true);
            if (caption != null) caption.gameObject.SetActive(false);

            var icon = UIFactory.CreateImage(frame, Glyph(def), "Glyph");
            icon.color = def.markerColor;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(10, 0), new Vector2(22, 22));

            var (name, _) = UIFactory.CreateStackedLabels(frame, def.label, def.detail,
                40f, inner - 96f, topInset: 8f);

            // The radius on the right. It is the number that decides which
            // system to task, so it belongs on the button rather than only in
            // the hint text.
            var radius = UIFactory.CreateText(frame, MissileCatalog.RadiusText(def),
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                new Vector2(56, 16));

            _buttons.Add((def.id, frame.Find("Fill").GetComponent<Image>(), icon, name));
        }

        /// <summary>
        /// Role first, then weight. A player scanning the list is asking "does
        /// this defend or destroy" before "how big is it", so the glyph answers
        /// that question and the radius answers the other.
        /// </summary>
        static Sprite Glyph(MissileSystemDef def)
        {
            if (def.role == MissileRole.AirDefence) return UiIcons.Interceptor;
            return def.weight == MissileWeight.Heavy ? UiIcons.HeavyMissile : UiIcons.BallisticArc;
        }

        // ------------------------------------------------------------- state

        void ShowOrigin(MissileOrigin origin)
        {
            _origin = origin;
            foreach (var kv in _pages) kv.Value.gameObject.SetActive(kv.Key == origin);

            foreach (var (o, fill, label) in _tabs)
            {
                bool on = o == origin;
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.TextDim;
            }
            Refresh();
        }

        public void Show()
        {
            _panel.gameObject.SetActive(true);
            IsOpen = true;
            Opened?.Invoke();
            LeftInsetChanged?.Invoke(DockX + PanelWidth);
            Refresh();
        }

        public void Hide()
        {
            bool was = IsOpen;
            if (_panel != null) _panel.gameObject.SetActive(false);
            IsOpen = false;
            // Closing the board stands the launcher down: leaving a system armed
            // behind a panel that is no longer on screen would turn the next
            // click on the map into a missile strike nobody asked for.
            if (_missiles != null) _missiles.Cancel();
            if (was) LeftInsetChanged?.Invoke(0f);
        }

        public void Toggle()
        {
            if (IsOpen) Hide(); else Show();
        }

        /// <summary>Repaints which system is armed. Driven by the system's own event.</summary>
        void Refresh()
        {
            if (_budgetLabel != null)
            {
                _budgetLabel.text = StrikeBudget.RemainingText;
                _budgetLabel.color = StrikeBudget.RemainingColour(
                    UiTheme.Accent, UiTheme.Warning, UiTheme.Hostile);
            }

            var armed = _missiles != null ? _missiles.Armed : null;

            foreach (var (id, fill, glyph, label) in _buttons)
            {
                bool on = armed.HasValue && armed.Value == id;
                var def = MissileCatalog.Get(id);
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                glyph.color = on ? UiTheme.Accent : def.markerColor;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.Text;
            }
        }
    }
}
