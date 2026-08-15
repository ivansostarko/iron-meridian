using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// The card that appears beside the cursor when the mouse is over a unit
    /// icon on the map.
    ///
    /// It answers the question "what is that?" without costing a click. Before
    /// this, the only way to identify a counter was to select it — which
    /// replaces the current selection, closes whatever was open on the right,
    /// and cancels any order being aimed. Reading the map should not have side
    /// effects.
    ///
    /// **What is on it is the point.** Not every field the unit has — the info
    /// panel exists for that — but the handful that decide what you do next:
    /// who it belongs to, what it is, how badly hurt it is, and whether it can
    /// still fight. Strength gets a bar as well as a number because a bar is
    /// read at a glance and a number is read deliberately.
    ///
    /// **Everything on it is labelled by a picture.** The card used to be four
    /// lines of prose with the readings run together as
    /// <c>MOR 74 ORG 61 AMMO 3200 FUEL 480</c> — six numbers a player had to
    /// parse a caption to identify, on a card that is up for about a second.
    /// It now leads with the formation's own APP-6 symbol, so the card and the
    /// counter under the cursor are visibly the same thing, and every reading
    /// sits behind a glyph in a fixed position. Position and shape are what a
    /// glance can use; a word is what a glance skips.
    ///
    /// <see cref="UiTooltip"/> is the equivalent for icon-only UI controls; this
    /// is its counterpart for things on the map, and is separate because the
    /// content is structured rather than a line of text.
    ///
    /// **The card is placed against the icon, not against the cursor.** It used
    /// to follow the mouse, which was wrong twice over. A counter is a small
    /// target and the pointer is somewhere inside it, so a card hung off the
    /// cursor sits *on top of* the thing it is describing — you cannot see the
    /// symbol you are asking about. And the cursor keeps moving while the same
    /// unit stays hovered, so the card slid around under a hand that was holding
    /// still. Anchoring to <see cref="UnitActor.IconWorldPosition"/> projected to
    /// screen puts the card beside the counter, clear of it, and holds it there.
    /// </summary>
    public class UnitHoverTooltip : MonoBehaviour
    {
        const float Width = 300f;
        const float Height = 196f;

        // --- the card's own grid, top down ---
        /// <summary>Left inset of everything, clear of the side stripe.</summary>
        const float Inset = 14f;
        /// <summary>The APP-6 symbol block: a framed square at the top left.</summary>
        const float SymbolSize = 42f;
        /// <summary>Where the text column starts: inset + symbol + gutter.</summary>
        const float TextX = Inset + SymbolSize + 10f;
        /// <summary>Y of the strength row, the status row and the first stat row.</summary>
        const float StrengthY = 68f, StatusY = 92f, GridY = 122f;
        /// <summary>Vertical pitch of the stat grid.</summary>
        const float GridPitch = 24f;
        /// <summary>Width of one stat cell — two side by side across the card.</summary>
        const float CellWidth = (Width - Inset * 2f - 12f) / 2f;
        /// <summary>Glyph size in a stat cell and on the strength/status rows.</summary>
        const float GlyphSize = 13f;
        /// <summary>
        /// Clear space between the icon and the card's near edge, in canvas
        /// units — on top of the icon's own drawn half-width, which is measured
        /// per frame because counters hold a constant apparent size but the
        /// canvas is scaled.
        /// </summary>
        const float IconGap = 14f;
        /// <summary>Seconds the card takes to fade in. Short: a tooltip that lags feels broken.</summary>
        const float FadeSeconds = 0.10f;
        /// <summary>Keep the card this far inside the canvas edges.</summary>
        const float ScreenMargin = 8f;

        RectTransform _root;
        RectTransform _panel;
        CanvasGroup _group;
        Canvas _canvas;
        Camera _worldCam;

        Image _sideStripe, _strengthBar, _symbol, _statusGlyph;
        Text _name, _type, _status, _strengthText;
        Text _morale, _organisation, _ammo, _fuel, _view, _range;
        /// <summary>The symbol's frame, tinted to the side so an empty slot still reads.</summary>
        Image _symbolFrame;

        UnitActor _unit;
        float _shown;

        /// <summary>
        /// <paramref name="worldCam"/> is what projects the counter to screen.
        /// Without it the card has nothing to sit beside and falls back to the
        /// cursor.
        /// </summary>
        public static UnitHoverTooltip Create(Canvas canvas, Camera worldCam = null)
        {
            // A RectTransform, not a bare Transform. This was the positioning
            // bug: a RectTransform child of a plain Transform has no parent rect
            // to anchor against, so the card's (0,0) anchor resolved to the
            // canvas *centre* instead of its bottom-left corner and every
            // position computed below landed half a screen away from the unit.
            var go = new GameObject("UnitHoverTooltip", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);

            var tip = go.AddComponent<UnitHoverTooltip>();
            tip._canvas = canvas;
            tip._worldCam = worldCam;
            tip._root = (RectTransform)go.transform;
            UIFactory.Stretch(tip._root);
            tip.Build();
            return tip;
        }

        void Build()
        {
            _panel = UIFactory.CreateBorderedPanel(_root, "Card", UiTheme.Chrome, UiTheme.BorderStrong);
            _panel.sizeDelta = new Vector2(Width, Height);
            // Anchored to the root's bottom-left corner, which is the canvas's,
            // so the positions computed in LateUpdate are plain canvas pixels.
            // Pivot at the top-left so the card hangs down and to the right of
            // wherever it is placed.
            _panel.anchorMin = _panel.anchorMax = new Vector2(0, 0);
            _panel.pivot = new Vector2(0, 1);

            _group = _panel.gameObject.AddComponent<CanvasGroup>();
            // Never eat a click. The card sits under the cursor by definition,
            // and a tooltip that blocks the thing it is describing is a trap.
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // Team stripe down the left edge: the fastest possible "whose is it".
            _sideStripe = UIFactory.CreatePanel(_panel, "Side", GameConfig.BlueTeam).GetComponent<Image>();
            var stripe = (RectTransform)_sideStripe.transform;
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.pivot = new Vector2(0, 0.5f);
            stripe.sizeDelta = new Vector2(4, 0);
            stripe.anchoredPosition = Vector2.zero;
            _sideStripe.raycastTarget = false;

            // --- identity: the formation's own APP-6 symbol, then its name ---
            //
            // The symbol first because it is the thing under the cursor. A card
            // that describes a counter without showing it makes the player match
            // words to a shape; showing the same shape makes the connection
            // before a word is read.
            var frame = UIFactory.CreateBorderedPanel(_panel, "SymbolFrame",
                UiTheme.Surface, UiTheme.BorderStrong);
            UIFactory.Place(frame, new Vector2(0f, 1f), new Vector2(Inset, -10),
                new Vector2(SymbolSize, SymbolSize));
            _symbolFrame = frame.GetComponent<Image>();
            _symbolFrame.raycastTarget = false;

            var symbolGo = new GameObject("Symbol", typeof(RectTransform), typeof(Image));
            symbolGo.transform.SetParent(frame, false);
            _symbol = symbolGo.GetComponent<Image>();
            _symbol.preserveAspect = true;
            _symbol.raycastTarget = false;
            var srt = (RectTransform)symbolGo.transform;
            UIFactory.Stretch(srt);
            srt.offsetMin = new Vector2(4, 4);
            srt.offsetMax = new Vector2(-4, -4);

            _name = Label(TextX, -12f, Width - TextX - Inset, 18f,
                UiTheme.FontBody, UiTheme.Text, FontStyle.Bold);
            _type = Label(TextX, -32f, Width - TextX - Inset, 14f,
                UiTheme.FontLabel, UiTheme.TextDim, FontStyle.Normal);

            Divider(-60f);

            // --- strength: glyph, bar, number ---
            Glyph(UiIcons.Shield, Inset, -StrengthY + 2f, UiTheme.TextFaint);

            var track = UIFactory.CreatePanel(_panel, "Track", UiTheme.Surface);
            UIFactory.Place(track, new Vector2(0f, 1f),
                new Vector2(Inset + GlyphSize + 8f, -StrengthY - 3f),
                new Vector2(Width - Inset * 2f - GlyphSize - 8f - 52f, 8f));
            track.GetComponent<Image>().raycastTarget = false;

            _strengthBar = UIFactory.CreatePanel(track, "Bar", UiTheme.Success).GetComponent<Image>();
            var bar = (RectTransform)_strengthBar.transform;
            bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(0, 1);
            bar.pivot = new Vector2(0, 0.5f);
            bar.anchoredPosition = Vector2.zero;
            _strengthBar.raycastTarget = false;

            _strengthText = UIFactory.CreateText(_panel, "", UiTheme.FontLabel, UiTheme.Text,
                TextAnchor.MiddleRight, FontStyle.Bold);
            _strengthText.raycastTarget = false;
            UIFactory.Place(_strengthText.rectTransform, new Vector2(1f, 1f),
                new Vector2(-Inset, -StrengthY + 2f), new Vector2(46, 16));

            // --- status ---
            _statusGlyph = Glyph(UiIcons.Pulse, Inset, -StatusY, UiTheme.Accent);
            _status = Label(Inset + GlyphSize + 8f, -StatusY, Width - Inset * 2f - GlyphSize - 8f, 14f,
                UiTheme.FontLabel, UiTheme.Accent, FontStyle.Bold);

            Divider(-112f);

            // --- the readings, two to a row, each behind its own glyph ---
            _morale = StatCell(UiIcons.Flag, 0, 0, "MOR");
            _organisation = StatCell(UiIcons.Orders, 1, 0, "ORG");
            _ammo = StatCell(UiIcons.ShellMedium, 0, 1, "AMMO");
            _fuel = StatCell(UiIcons.Disc, 1, 1, "FUEL");
            _view = StatCell(UiIcons.ReconEye, 0, 2, "SEE");
            _range = StatCell(UiIcons.Artillery, 1, 2, "REACH");

            Hide();
        }

        Text Label(float x, float y, float width, float height,
            int size, Color colour, FontStyle style)
        {
            var t = UIFactory.CreateText(_panel, "", size, colour, TextAnchor.MiddleLeft, style);
            t.raycastTarget = false;
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(x, y),
                new Vector2(width, height));
            UIFactory.Fit(t, 8);
            return t;
        }

        Image Glyph(Sprite sprite, float x, float y, Color colour)
        {
            var img = UIFactory.CreateImage(_panel, sprite, "Glyph");
            img.color = colour;
            img.raycastTarget = false;
            UIFactory.Place((RectTransform)img.transform, new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(GlyphSize, GlyphSize));
            return img;
        }

        void Divider(float y)
        {
            var rule = UIFactory.CreateDivider(_panel, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.offsetMin = new Vector2(Inset, rule.offsetMin.y);
            rule.offsetMax = new Vector2(-Inset, rule.offsetMax.y);
            rule.anchoredPosition = new Vector2(0, y);
        }

        /// <summary>
        /// One reading: glyph, short caption, value. The caption is three or
        /// four characters — with the glyph carrying the meaning it only has to
        /// disambiguate, and a full word would push the number out of the cell.
        /// </summary>
        Text StatCell(Sprite sprite, int column, int row, string caption)
        {
            float x = Inset + column * (CellWidth + 12f);
            float y = -(GridY + row * GridPitch);

            Glyph(sprite, x, y, UiTheme.TextFaint);

            var label = UIFactory.CreateText(_panel, caption, UiTheme.FontLabel,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            label.raycastTarget = false;
            UIFactory.Place(label.rectTransform, new Vector2(0f, 1f),
                new Vector2(x + GlyphSize + 6f, y), new Vector2(38f, 14f));

            var value = UIFactory.CreateText(_panel, "", UiTheme.FontLabel, UiTheme.Text,
                TextAnchor.MiddleRight, FontStyle.Bold);
            value.raycastTarget = false;
            UIFactory.Place(value.rectTransform, new Vector2(0f, 1f),
                new Vector2(x + GlyphSize + 44f, y), new Vector2(CellWidth - GlyphSize - 44f, 14f));
            UIFactory.Fit(value, 8);
            return value;
        }

        /// <summary>
        /// Shows the card for a unit, or hides it when given null. Safe to call
        /// every frame with the same unit — it only rebuilds on a change.
        /// </summary>
        public void Show(UnitActor unit)
        {
            // A formation the fog has taken off the map must not be described by
            // a tooltip: the icon is gone precisely so its position is unknown.
            if (unit != null && (!unit.IsAlive || unit.HiddenByFog)) unit = null;

            if (unit == _unit)
            {
                // Same unit, but its numbers move while a battle runs.
                if (unit != null) Fill(unit);
                return;
            }

            _unit = unit;
            if (unit == null) { Hide(); return; }

            Fill(unit);
            _panel.gameObject.SetActive(true);
            // Placed now rather than waiting for LateUpdate: the card is
            // switched on this frame, and one frame at wherever the last unit
            // was reads as a flicker across the screen.
            Place();
        }

        public void Hide()
        {
            _unit = null;
            _shown = 0f;
            if (_panel != null)
            {
                _group.alpha = 0f;
                _panel.gameObject.SetActive(false);
            }
        }

        void Fill(UnitActor unit)
        {
            var s = unit.State;
            var def = unit.Def;
            bool friendly = s.TeamEnum == Team.User;

            Color side = friendly ? GameConfig.BlueTeam : GameConfig.RedTeam;
            _sideStripe.color = side;
            // The symbol's frame carries the side too, so the block reads as
            // friendly or hostile even before the artwork inside it resolves.
            _symbolFrame.color = new Color(side.r, side.g, side.b, 0.55f);

            _symbol.sprite = UIFactory.LoadIconSprite(friendly ? "Friendly" : "Enemy", def.id);
            _symbol.enabled = _symbol.sprite != null;

            _name.text = string.IsNullOrEmpty(s.customName) ? def.name : s.customName;
            // Branch rather than category: on the map, what arm a formation
            // belongs to is what a commander reads off a counter. Whether it is
            // modelled as ground or air is an implementation detail.
            _type.text = $"{s.EchelonEnum}  ·  {UnitBranchInfo.DisplayName(def.Branch)}  ·  " +
                         (friendly ? "FRIENDLY" : "HOSTILE");

            float strength = Mathf.Clamp01(s.strength);
            var track = (RectTransform)_strengthBar.transform.parent;
            ((RectTransform)_strengthBar.transform).sizeDelta =
                new Vector2(track.sizeDelta.x * strength, 0f);
            // Green through amber to red, the same reading as the icon's own bar.
            _strengthBar.color = strength > 0.6f ? UiTheme.Success
                               : strength > 0.3f ? UiTheme.Warning
                               : UiTheme.Danger;
            _strengthText.text = $"{strength * 100f:0}%";

            _status.text = s.status?.ToUpperInvariant() ?? "";
            Color statusColour =
                  s.status == UnitStatus.Routed.ToString() ? UiTheme.Danger
                : s.status == UnitStatus.Suppressed.ToString() ? UiTheme.Warning
                : s.status == UnitStatus.Engaging.ToString() ? UiTheme.Warning
                : s.status == UnitStatus.Moving.ToString() ? UiTheme.Accent
                : UiTheme.TextDim;
            _status.color = statusColour;
            _statusGlyph.color = statusColour;

            // The readings that decide whether it can still do anything. Morale
            // and organisation are coloured on the same thresholds the strength
            // bar uses, so "in trouble" looks the same wherever it appears.
            _morale.text = $"{s.morale:0}";
            _morale.color = Level(s.morale / 100f);
            _organisation.text = $"{s.organisation:0}";
            _organisation.color = Level(s.organisation / 100f);

            // Out of ammunition is not a low number, it is a different state —
            // a formation with none fights at a quarter strength (CombatSystem).
            _ammo.text = $"{s.ammo:n0}";
            _ammo.color = s.ammo <= 0 ? UiTheme.Danger : UiTheme.Text;

            _fuel.text = def.fuelStock > 0f ? $"{s.fuel:n0}" : "—";
            _fuel.color = def.fuelStock > 0f && s.fuel <= 0f ? UiTheme.Danger : UiTheme.Text;

            _view.text = $"{def.viewRangeKm:0.#} km";
            _range.text = $"{def.weaponRangeKm:0.#} km";
        }

        /// <summary>Green / amber / red on the same thresholds as the strength bar.</summary>
        static Color Level(float fraction01) =>
            fraction01 > 0.6f ? UiTheme.Text
            : fraction01 > 0.3f ? UiTheme.Warning
            : UiTheme.Danger;

        void LateUpdate()
        {
            if (_unit == null) return;

            // The unit can die or be hidden between the hover event and this
            // frame; keeping the card up over nothing is worse than a flicker.
            if (!_unit.IsAlive || _unit.HiddenByFog) { Hide(); return; }

            _shown = Mathf.Min(_shown + Time.unscaledDeltaTime, FadeSeconds);
            _group.alpha = _shown / FadeSeconds;

            Place();
        }

        /// <summary>
        /// Puts the card beside the hovered counter.
        ///
        /// The anchor is the icon's own screen position, offset clear of its
        /// drawn half-width so the card never covers the symbol being asked
        /// about. It prefers the right of the icon and flips to the left near
        /// the right-hand edge; vertically it is centred on the icon and then
        /// clamped inside the canvas, which keeps it whole on screen without
        /// letting it jump between corners as the camera moves.
        /// </summary>
        void Place()
        {
            var canvasRect = _root.rect;
            float width = canvasRect.width;
            float height = canvasRect.height;

            if (!TryIconCanvasPoint(out Vector2 icon, out float iconHalfWidth))
            {
                // No usable projection — the unit is behind the camera, or there
                // is no world camera. Fall back to the cursor so the card is at
                // least somewhere sensible rather than frozen at the last spot.
                if (!TryCanvasPoint(Input.mousePosition, out icon)) return;
                iconHalfWidth = 0f;
            }

            float gap = iconHalfWidth + IconGap;

            // Right of the icon by default; left when that would run off.
            float x = icon.x + gap;
            if (x + Width > width - ScreenMargin) x = icon.x - gap - Width;
            x = Mathf.Clamp(x, ScreenMargin, Mathf.Max(ScreenMargin, width - Width - ScreenMargin));

            // Vertically centred on the icon (the pivot is the card's top edge),
            // then clamped so neither end leaves the canvas.
            float y = icon.y + Height * 0.5f;
            y = Mathf.Clamp(y, Height + ScreenMargin, Mathf.Max(Height + ScreenMargin, height - ScreenMargin));

            _panel.anchoredPosition = new Vector2(x, y);
        }

        /// <summary>
        /// The hovered icon's position in canvas pixels, measured from the
        /// canvas's bottom-left corner, plus its drawn half-width in the same
        /// units. False when the counter is not in front of the camera.
        /// </summary>
        bool TryIconCanvasPoint(out Vector2 point, out float halfWidth)
        {
            point = default;
            halfWidth = 0f;
            if (_worldCam == null || _unit == null) return false;

            Vector3 world = _unit.IconWorldPosition;
            Vector3 screen = _worldCam.WorldToScreenPoint(world);
            if (screen.z <= 0f) return false;              // behind the camera

            if (!TryCanvasPoint(screen, out point)) return false;

            // Measure the icon's width by projecting a second point one radius
            // to the camera's right: the counter holds a constant *apparent*
            // size, so its canvas width cannot be derived from its world size
            // without going through the camera.
            float radius = _unit.IconWorldRadius;
            if (radius > 0f)
            {
                Vector3 edge = _worldCam.WorldToScreenPoint(world + _worldCam.transform.right * radius);
                if (edge.z > 0f && TryCanvasPoint(edge, out Vector2 edgePoint))
                    halfWidth = Mathf.Abs(edgePoint.x - point.x);
            }
            return true;
        }

        /// <summary>Screen pixels to canvas pixels measured from the bottom-left corner.</summary>
        bool TryCanvasPoint(Vector3 screen, out Vector2 point)
        {
            point = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _root, screen, _canvas.worldCamera, out Vector2 local)) return false;

            var rect = _root.rect;
            point = new Vector2(local.x - rect.xMin, local.y - rect.yMin);
            return true;
        }
    }
}
