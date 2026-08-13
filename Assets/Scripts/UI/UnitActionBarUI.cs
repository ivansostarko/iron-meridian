using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Bottom order bar shown while a battle is running and a unit is selected:
    /// Move, Attack, Defence.
    ///
    /// Move arms a pending order and the next click on the map becomes the
    /// destination. Defence opens a submenu of the three defensive tasks —
    /// Defend, Hold, Guard — because "defence" is not one order: preparing a
    /// position, retaining ground and screening forward are different jobs with
    /// different graphics on the map. Attack is still a mockup pending an
    /// offensive-order system.
    /// </summary>
    public class UnitActionBarUI : MonoBehaviour
    {
        public System.Action MoveRequested;
        public System.Action DefendRequested;
        public System.Action HoldRequested;
        public System.Action GuardRequested;
        public System.Action<string> Flash;

        RectTransform _panel;
        RectTransform _defenceMenu;
        Text _title;
        Button _moveBtn, _defenceBtn;
        bool _moveArmed;

        static readonly Color Idle = new Color(0.18f, 0.22f, 0.29f);
        static readonly Color Armed = new Color(0.85f, 0.65f, 0.13f);

        /// <summary>Order-button geometry; the submenu lines itself up from the same numbers.</summary>
        const float ButtonWidth = 130f, ButtonHeight = 62f, ButtonGap = 10f;

        public void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "UnitActionBar", UiTheme.Panel);
            _panel.anchorMin = new Vector2(0.5f, 0f);
            _panel.anchorMax = new Vector2(0.5f, 0f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.sizeDelta = new Vector2(430, 104);
            _panel.anchoredPosition = new Vector2(0, 44);   // clear of the help line

            _title = UIFactory.CreateText(_panel, "", 13, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(400, 20));

            _moveBtn = ActionButton("MOVE", 0, ProceduralTextures.MoveIcon(UiTheme.Text), OnMove);
            ActionButton("ATTACK", 1, ProceduralTextures.AttackIcon(UiTheme.Text),
                () => Mock("Attack"));
            _defenceBtn = ActionButton("DEFENCE", 2, ProceduralTextures.ShieldIcon(UiTheme.Text),
                ToggleDefenceMenu);

            BuildDefenceMenu();
            Hide();
        }

        Button ActionButton(string label, int index, Texture2D icon, UnityEngine.Events.UnityAction onClick)
        {
            float total = ButtonWidth * 3 + ButtonGap * 2;
            float x = -total / 2f + ButtonWidth / 2f + index * (ButtonWidth + ButtonGap);

            var btn = UIFactory.CreateButton(_panel, "", onClick, Idle, UiTheme.Text, 12);
            var rt = (RectTransform)btn.transform;
            UIFactory.Place(rt, new Vector2(0.5f, 0f), new Vector2(x, 10), new Vector2(ButtonWidth, ButtonHeight));

            // CreateButton's own label is centred; this layout wants the glyph
            // above the caption, so retarget it to the lower strip.
            var caption = btn.GetComponentInChildren<Text>(true);
            caption.text = label;
            caption.alignment = TextAnchor.LowerCenter;
            var crt = caption.rectTransform;
            crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(1, 0);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(0, 18);
            crt.anchoredPosition = new Vector2(0, 6);

            var sprite = Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height),
                new Vector2(0.5f, 0.5f), 100f);
            var image = UIFactory.CreateImage(rt, sprite, "Icon");
            image.raycastTarget = false;
            UIFactory.Place((RectTransform)image.transform, new Vector2(0.5f, 1f), new Vector2(0, -6), new Vector2(30, 30));

            return btn;
        }

        // ------------------------------------------------------- defence submenu

        /// <summary>
        /// The three defensive tasks, stacked above the DEFENCE button. Built
        /// once and toggled rather than created on demand, so repeat use does
        /// not churn uGUI objects mid-battle.
        /// </summary>
        void BuildDefenceMenu()
        {
            const float rowHeight = 34f, rowGap = 4f, headerHeight = 22f, pad = 6f;
            float height = headerHeight + pad + 3 * rowHeight + 2 * rowGap + pad;

            _defenceMenu = UIFactory.CreateBorderedPanel(_panel, "DefenceMenu", UiTheme.Panel, UiTheme.BorderStrong);
            // Anchored to the bar's top edge, above the third button.
            float x = ButtonWidth + ButtonGap;
            UIFactory.Place(_defenceMenu, new Vector2(0.5f, 1f), new Vector2(x, 4), new Vector2(ButtonWidth + 34, height));
            _defenceMenu.pivot = new Vector2(0.5f, 0f);

            var header = UIFactory.CreateSectionHeader(_defenceMenu, "DEFENSIVE TASK");
            header.alignment = TextAnchor.MiddleCenter;
            UIFactory.PlaceTopLeft(header.rectTransform, pad, pad, ButtonWidth + 34 - pad * 2, headerHeight - pad);
            UIFactory.Fit(header);

            TaskRow("DEFEND", "Prepare a position", 0, () => Choose(DefendRequested));
            TaskRow("HOLD", "Retain this ground", 1, () => Choose(HoldRequested));
            TaskRow("GUARD", "Screen forward", 2, () => Choose(GuardRequested));

            _defenceMenu.gameObject.SetActive(false);

            void TaskRow(string label, string detail, int index, UnityEngine.Events.UnityAction onClick)
            {
                float top = headerHeight + pad + index * (rowHeight + rowGap);
                var btn = UIFactory.CreateButton(_defenceMenu, "", onClick, UiTheme.Surface, UiTheme.Text, 12);
                var rt = (RectTransform)btn.transform;
                UIFactory.PlaceTopLeft(rt, pad, top, ButtonWidth + 34 - pad * 2, rowHeight);

                // Two lines inside a 34 px row: the task, and what it does. The
                // factory's centred caption is retargeted to the top line so the
                // two rects meet rather than overlap.
                var caption = btn.GetComponentInChildren<Text>(true);
                caption.text = label;
                caption.alignment = TextAnchor.MiddleLeft;
                caption.fontStyle = FontStyle.Bold;
                UIFactory.PlaceTopLeft(caption.rectTransform, 8f, 3f, ButtonWidth + 34 - pad * 2 - 16f, 16f);
                UIFactory.Fit(caption);

                var sub = UIFactory.CreateText(rt, detail, UiTheme.FontLabel, UiTheme.TextFaint,
                    TextAnchor.MiddleLeft);
                UIFactory.PlaceTopLeft(sub.rectTransform, 8f, 19f, ButtonWidth + 34 - pad * 2 - 16f, 13f);
                UIFactory.Fit(sub);
            }
        }

        void ToggleDefenceMenu() => SetDefenceMenu(!_defenceMenu.gameObject.activeSelf);

        void SetDefenceMenu(bool open)
        {
            if (_defenceMenu == null) return;
            _defenceMenu.gameObject.SetActive(open);
            _defenceMenu.SetAsLastSibling();
            if (_defenceBtn != null) _defenceBtn.image.color = open ? Armed : Idle;
        }

        void Choose(System.Action order)
        {
            SetDefenceMenu(false);
            order?.Invoke();
        }

        // ------------------------------------------------------- move

        void OnMove()
        {
            SetDefenceMenu(false);
            _moveArmed = true;
            _moveBtn.image.color = Armed;
            MoveRequested?.Invoke();
            Flash?.Invoke("Move order — click the map to set the destination (Esc cancels).");
        }

        void Mock(string what)
        {
            SetDefenceMenu(false);
            Flash?.Invoke($"{what} orders are not implemented yet.");
        }

        /// <summary>Called once the pending move has been placed or cancelled.</summary>
        public void ClearMoveArmed()
        {
            if (!_moveArmed) return;
            _moveArmed = false;
            if (_moveBtn != null) _moveBtn.image.color = Idle;
        }

        public void Show(UnitActor unit)
        {
            if (_panel == null || unit == null) { Hide(); return; }
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            // The submenu acts on the unit that was selected when it opened, so
            // a selection change closes it rather than silently re-targeting.
            SetDefenceMenu(false);
            string name = string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName;
            _title.text = $"ORDERS — {name.ToUpperInvariant()}";
        }

        public void Hide()
        {
            ClearMoveArmed();
            SetDefenceMenu(false);
            if (_panel != null) _panel.gameObject.SetActive(false);
        }
    }
}
