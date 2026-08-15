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
    /// **It is a page in the strike dock**, alongside the other four fire
    /// menus — see <see cref="StrikeDockUI"/>. It has been a left-hand board
    /// and a right-hand one in turn; what settled it was that the five fire
    /// menus are one family and belong in one place, reached the same way. This
    /// class therefore owns its *contents* and nothing about where they are
    /// drawn: no panel of its own, no show/hide, no docking width.
    ///
    /// It gave up 46 px of width in the move (it used to be
    /// <see cref="UiTheme.MissilePanelWidth"/> against a section's
    /// <see cref="UiTheme.SectionPanelWidth"/>). That width was for comparing
    /// systems on one row — a designation, a description and a radius — and it
    /// is a real loss. One panel in a set of five being wider than the rest is
    /// a worse tell than any row is helped by the space, and every label on the
    /// row is best-fitted, so what it costs is a point of type size.
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
        const float Pad = UiTheme.PanelPadding;
        const float RowHeight = 52f;
        /// <summary>The dock's page width — this board no longer sets its own.</summary>
        const float PanelWidth = StrikeDockUI.PanelWidth;

        MissileStrikeSystem _missiles;
        RectTransform _panel;
        MissileOrigin _origin = MissileOrigin.Nato;

        readonly Dictionary<MissileOrigin, RectTransform> _pages =
            new Dictionary<MissileOrigin, RectTransform>();
        readonly List<(MissileOrigin origin, Image fill, Text label)> _tabs =
            new List<(MissileOrigin, Image, Text)>();
        readonly List<(MissileSystemId id, Image fill, Image glyph, Text label, Text allowance)> _buttons =
            new List<(MissileSystemId, Image, Image, Text, Text)>();

        /// <summary>
        /// Builds the board into a page the strike dock owns. The dock supplies
        /// the panel, the header and the show/hide; this fills the body.
        /// </summary>
        public static MissilePanelUI Create(RectTransform page, MissileStrikeSystem missiles)
        {
            if (page == null) return null;

            var go = new GameObject("MissilePanel");
            go.transform.SetParent(page, false);

            var panel = go.AddComponent<MissilePanelUI>();
            panel._missiles = missiles;
            panel.Build(page);
            missiles.ArmedChanged += panel.Refresh;
            StrikeBudget.Changed += panel.Refresh;
            return panel;
        }

        void OnDestroy()
        {
            if (_missiles != null) _missiles.ArmedChanged -= Refresh;
            StrikeBudget.Changed -= Refresh;
        }

        // ------------------------------------------------------------- build

        void Build(RectTransform page)
        {
            _panel = page;

            float inner = PanelWidth - Pad * 2f;

            // No title and no close button: the dock's header carries both, and
            // a second heading inside the page would repeat it.
            StrikeBudgetRow(-8f, inner);
            BuildTabs(-44f, inner);

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
        /// Names the right-hand column of the buttons below it. The allowance
        /// itself is per system now — see <see cref="StrikeBudget"/> — and this
        /// is what tells the player what the second figure on each row is.
        /// </summary>
        void StrikeBudgetRow(float y, float inner)
        {
            var frame = UIFactory.CreateBorderedPanel(_panel, "AllowanceLegend",
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 28));

            var name = UIFactory.CreateText(frame, "MISSIONS AVAILABLE", UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            name.raycastTarget = false;
            UIFactory.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 0),
                new Vector2(inner - 110f, 14));

            var note = UIFactory.CreateText(frame, "PER SYSTEM", UiTheme.FontLabel,
                UiTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            note.raycastTarget = false;
            UIFactory.Place(note.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 0),
                new Vector2(94, 16));
        }

        void BuildPage(RectTransform page, MissileOrigin origin, float inner)
        {
            // Clear of the allowance legend and the inventory tabs. The dock's
            // header carries the title, so the page starts higher than it did
            // when the board had one of its own.
            float y = -84f;
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

            // The radius on the right, with the system's own allowance under it.
            // Both are numbers that decide which system to task, so they belong
            // on the button rather than only in the hint text.
            var radius = UIFactory.CreateText(frame, MissileCatalog.RadiusText(def),
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleRight);
            radius.raycastTarget = false;
            UIFactory.Place(radius.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, 8),
                new Vector2(56, 14));

            var allowance = UIFactory.CreateText(frame, "", UiTheme.FontLabel,
                UiTheme.Accent, TextAnchor.MiddleRight, FontStyle.Bold);
            allowance.raycastTarget = false;
            UIFactory.Place(allowance.rectTransform, new Vector2(1f, 0.5f), new Vector2(-10, -9),
                new Vector2(56, 14));

            _buttons.Add((def.id, frame.Find("Fill").GetComponent<Image>(), icon, name, allowance));
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

        /// <summary>Repaints which system is armed. Driven by the system's own event.</summary>
        void Refresh()
        {
            var armed = _missiles != null ? _missiles.Armed : null;

            foreach (var (id, fill, glyph, label, allowance) in _buttons)
            {
                bool on = armed.HasValue && armed.Value == id;
                var def = MissileCatalog.Get(id);
                fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                glyph.color = on ? UiTheme.Accent : def.markerColor;
                if (label != null) label.color = on ? UiTheme.Accent : UiTheme.Text;

                if (allowance == null) continue;
                string key = MissileCatalog.BudgetKey(id);
                allowance.text = StrikeBudget.RemainingText(key, def.missions);
                allowance.color = StrikeBudget.RemainingColour(key, def.missions,
                    UiTheme.Accent, UiTheme.Warning, UiTheme.Hostile);
            }
        }
    }
}
