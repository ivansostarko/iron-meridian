using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;

namespace IronMeridian.UI
{
    /// <summary>
    /// What a unit *type* is, docked on the right — opened by clicking a card in
    /// the palette's AVAILABLE list.
    ///
    /// **Why this is not <see cref="UnitInfoPanel"/>.** That panel describes a
    /// formation on the map: its strength, its heading, its orders, the ground
    /// it is standing on. None of that exists yet for a type in the catalogue,
    /// and half of it is editable, so showing a definition through it would mean
    /// a panel of controls that act on nothing. A type has one honest question —
    /// *what is this, and is it the thing I want to deploy?* — and this answers
    /// it and nothing else.
    ///
    /// **It closes the moment a formation is selected.** Both panels dock on the
    /// same strip of screen, and a selection is a clear statement about which of
    /// them the player now wants.
    ///
    /// Read-only: the place to *change* a unit's figures is
    /// DEVELOPMENT → UNITS LIST, which writes the tuning patch (docs/04-UNITS.md).
    /// </summary>
    public class UnitTypePanel : MonoBehaviour
    {
        const float PanelWidth = UiTheme.RightPanelWidth;

        /// <summary>
        /// The gutter every piece of the panel is measured from — the icon, the
        /// section headings, both columns of every row, the hairlines and the
        /// footer hint. 45 px either side rather than the 30 it started with:
        /// the panel is docked against the screen edge on one side and against
        /// the map on the other, and at 30 the values read as text pinned to
        /// the edge rather than as a page. Every value is best-fitted
        /// (<see cref="UIFactory.Fit"/>), so the width this costs is paid in
        /// type size rather than in truncation.
        /// </summary>
        const float Pad = 45f;

        /// <summary>Content width — the panel less the same margin either side.</summary>
        const float Inner = PanelWidth - Pad * 2f;

        /// <summary>Icon width plus the gap to the title beside it.</summary>
        const float TitleIndent = 64f;
        /// <summary>Clear air the close button needs at the top right.</summary>
        const float CloseClearance = 46f;

        RectTransform _panel, _rows;
        Image _icon;
        Text _title, _subtitle;

        public void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "UnitTypePanel", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-PanelWidth, 0);
            _panel.offsetMax = new Vector2(0, -(UiTheme.TopBarHeight + UiTheme.StrikeDockHeight));

            var close = UIFactory.CreateButton(_panel, "✕", Hide,
                new Color(0, 0, 0, 0), UiTheme.TextDim, 16);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-8, -8), new Vector2(30, 30));

            var frame = UIFactory.CreateBorderedPanel(_panel, "IconFrame",
                UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Pad, -16), new Vector2(52, 52));

            _icon = UIFactory.CreateImage(frame, null, "Icon");
            _icon.preserveAspect = true;
            _icon.raycastTarget = false;
            var irt = (RectTransform)_icon.transform;
            UIFactory.Stretch(irt);
            irt.offsetMin = new Vector2(6, 6);
            irt.offsetMax = new Vector2(-6, -6);

            _title = UIFactory.CreateText(_panel, "", UiTheme.FontTitle, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(_title.rectTransform, Pad + TitleIndent, 18f,
                PanelWidth - (Pad + TitleIndent) - CloseClearance, 26f);
            UIFactory.Fit(_title, 12);

            _subtitle = UIFactory.CreateText(_panel, "", UiTheme.FontSmall, UiTheme.Accent,
                TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(_subtitle.rectTransform, Pad + TitleIndent, 46f,
                PanelWidth - (Pad + TitleIndent) - CloseClearance, 18f);
            UIFactory.Fit(_subtitle, 9);

            var rule = UIFactory.CreateDivider(_panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -84f);

            var scroll = UIFactory.CreateScrollView(_panel, out _rows,
                withScrollbar: true, autoHideScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(0, 56);
            srt.offsetMax = new Vector2(0, -90);

            var layout = _rows.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 0;
            layout.padding = new RectOffset(0, 0, 6, 8);

            var hint = UIFactory.CreateText(_panel,
                "Drag the card onto the map to deploy one. Figures are the type's — a deployed " +
                "formation carries its own strength and orders.",
                UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.UpperLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 8),
                new Vector2(Inner, 44));

            Hide();
        }

        /// <summary>Shows one catalogue entry, in the colours of the side it would be deployed for.</summary>
        public void Show(UnitDefinition def, Team team)
        {
            if (_panel == null) return;
            if (def == null) { Hide(); return; }

            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();

            bool friendly = team == Team.User;
            _icon.sprite = UIFactory.LoadIconSprite(friendly ? "Friendly" : "Enemy", def.id);
            _icon.color = _icon.sprite != null ? Color.white : new Color(1, 1, 1, 0);

            _title.text = def.name;
            _subtitle.text = $"{UnitBranchInfo.DisplayName(def.Branch)}  ·  " +
                             (friendly ? "FRIENDLY" : "HOSTILE");
            _subtitle.color = friendly ? UiTheme.Accent : UiTheme.Hostile;

            Rebuild(def);
        }

        void Rebuild(UnitDefinition def)
        {
            for (int i = _rows.childCount - 1; i >= 0; i--)
            {
                var c = _rows.GetChild(i);
                c.SetParent(null, false);
                Destroy(c.gameObject);
            }

            if (!string.IsNullOrEmpty(def.description)) Note(def.description);

            Section("COMBAT POWER");
            Row("Attack", $"{def.attack:0}");
            Row("Attack vs armour", $"{def.hardAttack:0}");
            Row("Defence", $"{def.defence:0}");
            Row("Armour", $"{def.armour:0}");
            Row("Anti-air", $"{def.antiAir:0}");

            Section("REACH");
            Row("Weapon range", $"{def.weaponRangeKm:0.#} km");
            Row("View range", $"{def.viewRangeKm:0.#} km");
            Row("Speed", $"{def.speedKmh:0} km/h");

            Section("THE MEN");
            Row("Manpower", $"{def.manpower:n0}");
            Row("Training", $"{def.training:0}");
            Row("Morale", $"{def.morale:0}");
            Row("Organisation", $"{def.organisation:0}");

            Section("WHAT IT CARRIES");
            Row("Ammunition", string.IsNullOrEmpty(def.ammoType) ? "—" : def.ammoType);
            Row("Rounds", $"{def.ammoStock:n0}");
            Row("Fuel", def.fuelStock > 0f ? $"{def.fuelStock:n0} l" : "—");
            Row("Fuel use", def.fuelUsePerKm > 0f ? $"{def.fuelUsePerKm:0.#} l/km" : "—");
            Row("Rations", $"{def.foodDays} day(s)");

            Section("ROLE");
            Row("Holds ground", def.HoldsGround ? "Yes" : "No");
            Row("Indirect fire", def.canIndirectFire ? "Yes" : "No");
            Row("Counter-UAS", def.canCounterUas ? "Yes" : "No");
            Row("Support", def.isSupport ? "Fights poorly alone" : "No");
        }

        // ------------------------------------------------------------ pieces

        void Section(string label)
        {
            var holder = UIFactory.CreateGroup(_rows, "Section_" + label);
            holder.sizeDelta = new Vector2(0, 30);

            var h = UIFactory.CreateSectionHeader(holder, label);
            UIFactory.Place(h.rectTransform, new Vector2(0f, 0f), new Vector2(Pad, 4),
                new Vector2(Inner, 18));
        }

        void Row(string label, string value)
        {
            var row = UIFactory.CreatePanel(_rows, "Row_" + label, new Color(0, 0, 0, 0));
            row.sizeDelta = new Vector2(0, UiTheme.RowHeight);

            var rule = UIFactory.CreateDivider(row, new Color(1f, 1f, 1f, 0.045f));
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.offsetMin = new Vector2(Pad, 0);
            rule.offsetMax = new Vector2(-Pad, 1);

            var lbl = UIFactory.CreateText(row, label, UiTheme.FontLabel, UiTheme.TextDim,
                TextAnchor.MiddleLeft);
            var lr = lbl.rectTransform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0.55f, 1f);
            lr.offsetMin = new Vector2(Pad, 0); lr.offsetMax = new Vector2(-6, 0);
            UIFactory.Fit(lbl, 8);

            var val = UIFactory.CreateText(row, value, UiTheme.FontLabel, UiTheme.Text,
                TextAnchor.MiddleRight, FontStyle.Bold);
            var vr = val.rectTransform;
            vr.anchorMin = new Vector2(0.55f, 0f); vr.anchorMax = new Vector2(1f, 1f);
            vr.offsetMin = new Vector2(6, 0); vr.offsetMax = new Vector2(-Pad, 0);
            UIFactory.Fit(val, 8);
        }

        void Note(string text)
        {
            var holder = UIFactory.CreateGroup(_rows, "Note");
            holder.sizeDelta = new Vector2(0, 58);

            var t = UIFactory.CreateText(holder, text, UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.UpperLeft);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, -6),
                new Vector2(Inner, 52));
        }

        public bool Visible => _panel != null && _panel.gameObject.activeSelf;

        public void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        /// <summary>Keeps the panel clear of whatever is docked above it, as the others do.</summary>
        public void SetTopInset(float top)
        {
            if (_panel != null) _panel.offsetMax = new Vector2(0, -top);
        }
    }
}
