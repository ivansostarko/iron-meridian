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
    /// <see cref="UiTooltip"/> is the equivalent for icon-only UI controls; this
    /// is its counterpart for things on the map, and is separate because the
    /// content is structured rather than a line of text.
    /// </summary>
    public class UnitHoverTooltip : MonoBehaviour
    {
        const float Width = 268f;
        const float Height = 132f;
        /// <summary>Gap between the cursor and the card's corner, in canvas units.</summary>
        const float CursorGap = 20f;
        /// <summary>Seconds the card takes to fade in. Short: a tooltip that lags feels broken.</summary>
        const float FadeSeconds = 0.10f;

        RectTransform _panel;
        CanvasGroup _group;
        Canvas _canvas;

        Image _sideStripe, _strengthBar;
        Text _name, _type, _status, _strengthText, _stats;

        UnitActor _unit;
        float _shown;

        public static UnitHoverTooltip Create(Canvas canvas)
        {
            var go = new GameObject("UnitHoverTooltip");
            go.transform.SetParent(canvas.transform, false);
            var tip = go.AddComponent<UnitHoverTooltip>();
            tip._canvas = canvas;
            tip.Build();
            return tip;
        }

        void Build()
        {
            _panel = UIFactory.CreateBorderedPanel(transform, "Card", UiTheme.Chrome, UiTheme.BorderStrong);
            _panel.sizeDelta = new Vector2(Width, Height);
            // Pivot at the top-left so the card hangs down and right of the
            // cursor, which is where the eye already is.
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

            _name = Label(UiTheme.FontBody, UiTheme.Text, FontStyle.Bold, -8f, 18f);
            _type = Label(UiTheme.FontLabel, UiTheme.TextDim, FontStyle.Normal, -28f, 14f);

            // Strength: bar and number on the same row.
            var track = UIFactory.CreatePanel(_panel, "Track", UiTheme.Surface);
            UIFactory.Place(track, new Vector2(0f, 1f), new Vector2(12, -50), new Vector2(Width - 74f, 8f));
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
                new Vector2(-10, -46), new Vector2(52, 16));

            _status = Label(UiTheme.FontLabel, UiTheme.Accent, FontStyle.Bold, -66f, 14f);
            _stats = Label(UiTheme.FontLabel, UiTheme.TextFaint, FontStyle.Normal, -86f, 32f);
            _stats.alignment = TextAnchor.UpperLeft;

            Hide();
        }

        Text Label(int size, Color colour, FontStyle style, float y, float height)
        {
            var t = UIFactory.CreateText(_panel, "", size, colour, TextAnchor.MiddleLeft, style);
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(12, y),
                new Vector2(Width - 24f, height));
            return t;
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

            _sideStripe.color = friendly ? GameConfig.BlueTeam : GameConfig.RedTeam;

            _name.text = string.IsNullOrEmpty(s.customName) ? def.name : s.customName;
            // Branch rather than category: on the map, what arm a formation
            // belongs to is what a commander reads off a counter. Whether it is
            // modelled as ground or air is an implementation detail.
            _type.text = $"{s.EchelonEnum}  ·  {UnitBranchInfo.DisplayName(def.Branch)}  ·  " +
                         (friendly ? "FRIENDLY" : "HOSTILE");

            float strength = Mathf.Clamp01(s.strength);
            ((RectTransform)_strengthBar.transform).sizeDelta =
                new Vector2((Width - 74f) * strength, 0f);
            // Green through amber to red, the same reading as the icon's own bar.
            _strengthBar.color = strength > 0.6f ? UiTheme.Success
                               : strength > 0.3f ? UiTheme.Warning
                               : UiTheme.Danger;
            _strengthText.text = $"{strength * 100f:0}%";

            _status.text = s.status?.ToUpperInvariant() ?? "";
            _status.color = s.status == UnitStatus.Routed.ToString() ? UiTheme.Danger
                          : s.status == UnitStatus.Suppressed.ToString() ? UiTheme.Warning
                          : s.status == UnitStatus.Moving.ToString() ? UiTheme.Accent
                          : UiTheme.TextDim;

            // The numbers that decide whether it can still do anything.
            _stats.text =
                $"MOR {s.morale:0}   ORG {s.organisation:0}   AMMO {s.ammo:0}   FUEL {s.fuel:0}\n" +
                $"SEE {def.viewRangeKm:0.#} km   RANGE {def.weaponRangeKm:0.#} km";
        }

        void LateUpdate()
        {
            if (_unit == null) return;

            _shown = Mathf.Min(_shown + Time.unscaledDeltaTime, FadeSeconds);
            _group.alpha = _shown / FadeSeconds;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)_canvas.transform, Input.mousePosition, _canvas.worldCamera,
                out Vector2 local);

            // Flip the card back over the cursor near the right or bottom edge,
            // so it is never half off screen.
            var canvasRect = ((RectTransform)_canvas.transform).rect;
            float x = local.x + CursorGap;
            float y = local.y - CursorGap;
            if (x + Width > canvasRect.xMax) x = local.x - CursorGap - Width;
            if (y - Height < canvasRect.yMin) y = local.y + CursorGap + Height;

            _panel.anchoredPosition = new Vector2(x - canvasRect.xMin, y - canvasRect.yMin);
        }
    }
}
