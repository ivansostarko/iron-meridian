using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Bottom order bar shown while a battle is running and a unit is selected:
    /// Move, Attack, Defence. Only Move is wired to real behaviour — it arms a
    /// pending order and the next click on the map becomes the destination.
    /// Attack and Defence are mockups pending a combat-order system.
    /// </summary>
    public class UnitActionBarUI : MonoBehaviour
    {
        public System.Action MoveRequested;
        public System.Action<string> Flash;

        RectTransform _panel;
        Text _title;
        Button _moveBtn;
        bool _moveArmed;

        static readonly Color Idle = new Color(0.18f, 0.22f, 0.29f);
        static readonly Color Armed = new Color(0.85f, 0.65f, 0.13f);

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
            ActionButton("DEFENCE", 2, ProceduralTextures.ShieldIcon(UiTheme.Text),
                () => Mock("Defence"));

            Hide();
        }

        Button ActionButton(string label, int index, Texture2D icon, UnityEngine.Events.UnityAction onClick)
        {
            const float w = 130f, h = 62f, gap = 10f;
            float total = w * 3 + gap * 2;
            float x = -total / 2f + w / 2f + index * (w + gap);

            var btn = UIFactory.CreateButton(_panel, "", onClick, Idle, UiTheme.Text, 12);
            var rt = (RectTransform)btn.transform;
            UIFactory.Place(rt, new Vector2(0.5f, 0f), new Vector2(x, 10), new Vector2(w, h));

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

        void OnMove()
        {
            _moveArmed = true;
            _moveBtn.image.color = Armed;
            MoveRequested?.Invoke();
            Flash?.Invoke("Move order — click the map to set the destination (Esc cancels).");
        }

        void Mock(string what) => Flash?.Invoke($"{what} orders are not implemented yet.");

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
            string name = string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName;
            _title.text = $"ORDERS — {name.ToUpperInvariant()}";
        }

        public void Hide()
        {
            ClearMoveArmed();
            if (_panel != null) _panel.gameObject.SetActive(false);
        }
    }
}
