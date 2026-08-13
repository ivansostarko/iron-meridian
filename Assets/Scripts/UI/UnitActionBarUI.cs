using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Bottom order bar shown while a battle is running and a unit is selected:
    /// Move, Attack, Defence.
    ///
    /// Move arms a pending order and the next click on the map becomes the
    /// destination. Attack and Defence each open a submenu of tasks, because
    /// neither is one order: closing to destroy, assaulting an objective,
    /// pinning a formation, lying in wait and striking back are five different
    /// jobs, and so are preparing a position, retaining ground and screening
    /// forward. The submenus are built from
    /// <see cref="AttackTaskCatalog"/> and a matching list so the captions and
    /// the behaviour cannot drift apart.
    ///
    /// Only one submenu is open at a time, and any of them closes when the
    /// selection changes — a menu that silently re-targeted whichever unit
    /// happened to be selected when you finally clicked would be a trap.
    /// </summary>
    public class UnitActionBarUI : MonoBehaviour
    {
        public System.Action MoveRequested;
        /// <summary>Raised with the offensive task the player picked; the map click comes next.</summary>
        public System.Action<AttackTask> AttackRequested;
        public System.Action DefendRequested;
        public System.Action HoldRequested;
        public System.Action GuardRequested;
        public System.Action<string> Flash;

        RectTransform _panel;
        RectTransform _attackMenu, _defenceMenu;
        Text _title;
        Button _moveBtn, _attackBtn, _defenceBtn;
        bool _moveArmed;
        bool _attackArmed;

        static readonly Color Idle = new Color(0.18f, 0.22f, 0.29f);
        static readonly Color Armed = new Color(0.85f, 0.65f, 0.13f);

        /// <summary>Order-button geometry; the submenus line themselves up from the same numbers.</summary>
        const float ButtonWidth = 130f, ButtonHeight = 62f, ButtonGap = 10f;
        /// <summary>Submenu metrics. Wider than a button so five task captions fit.</summary>
        const float MenuWidth = ButtonWidth + 46f;
        const float MenuRowHeight = 34f, MenuRowGap = 4f, MenuHeaderHeight = 22f, MenuPad = 6f;

        /// <summary>One submenu row: caption, the line under it, and what it does.</summary>
        readonly struct TaskRow
        {
            public readonly string Label, Detail;
            public readonly UnityEngine.Events.UnityAction Action;
            public TaskRow(string label, string detail, UnityEngine.Events.UnityAction action)
            {
                Label = label; Detail = detail; Action = action;
            }
        }

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
            _attackBtn = ActionButton("ATTACK", 1, ProceduralTextures.AttackIcon(UiTheme.Text),
                () => ToggleMenu(_attackMenu));
            _defenceBtn = ActionButton("DEFENCE", 2, ProceduralTextures.ShieldIcon(UiTheme.Text),
                () => ToggleMenu(_defenceMenu));

            BuildAttackMenu();
            BuildDefenceMenu();
            Hide();
        }

        /// <summary>Centre-relative x of order button <paramref name="index"/>.</summary>
        static float ButtonX(int index) =>
            -(ButtonWidth * 3f + ButtonGap * 2f) / 2f + ButtonWidth / 2f + index * (ButtonWidth + ButtonGap);

        Button ActionButton(string label, int index, Texture2D icon, UnityEngine.Events.UnityAction onClick)
        {
            var btn = UIFactory.CreateButton(_panel, "", onClick, Idle, UiTheme.Text, 12);
            var rt = (RectTransform)btn.transform;
            UIFactory.Place(rt, new Vector2(0.5f, 0f), new Vector2(ButtonX(index), 10),
                new Vector2(ButtonWidth, ButtonHeight));

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

        // ------------------------------------------------------- submenus

        /// <summary>
        /// The five offensive tasks, read straight off the catalogue so a task
        /// added there shows up here with the same name and one-liner.
        /// </summary>
        void BuildAttackMenu()
        {
            var rows = new List<TaskRow>();
            foreach (var def in AttackTaskCatalog.All)
            {
                var task = def.task;      // captured per row, not per loop variable
                rows.Add(new TaskRow(def.name, def.detail, () => Choose(() => OnAttack(task))));
            }
            _attackMenu = BuildTaskMenu("AttackMenu", "OFFENSIVE TASK", 1, rows);
        }

        void BuildDefenceMenu()
        {
            var rows = new List<TaskRow>
            {
                new TaskRow("DEFEND", "Prepare a position", () => Choose(DefendRequested)),
                new TaskRow("HOLD",   "Retain this ground", () => Choose(HoldRequested)),
                new TaskRow("GUARD",  "Screen forward",     () => Choose(GuardRequested))
            };
            _defenceMenu = BuildTaskMenu("DefenceMenu", "DEFENSIVE TASK", 2, rows);
        }

        /// <summary>
        /// Builds a submenu stacked above order button <paramref name="buttonIndex"/>.
        /// Built once and toggled rather than created on demand, so repeat use
        /// does not churn uGUI objects mid-battle.
        /// </summary>
        RectTransform BuildTaskMenu(string name, string header, int buttonIndex, List<TaskRow> rows)
        {
            float height = MenuHeaderHeight + MenuPad
                         + rows.Count * MenuRowHeight + (rows.Count - 1) * MenuRowGap
                         + MenuPad;
            float inner = MenuWidth - MenuPad * 2f;

            var menu = UIFactory.CreateBorderedPanel(_panel, name, UiTheme.Panel, UiTheme.BorderStrong);
            // Anchored to the bar's top edge, above its own button.
            UIFactory.Place(menu, new Vector2(0.5f, 1f), new Vector2(ButtonX(buttonIndex), 4),
                new Vector2(MenuWidth, height));
            menu.pivot = new Vector2(0.5f, 0f);

            var caption = UIFactory.CreateSectionHeader(menu, header);
            caption.alignment = TextAnchor.MiddleCenter;
            UIFactory.PlaceTopLeft(caption.rectTransform, MenuPad, MenuPad, inner, MenuHeaderHeight - MenuPad);
            UIFactory.Fit(caption);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float top = MenuHeaderHeight + MenuPad + i * (MenuRowHeight + MenuRowGap);

                var btn = UIFactory.CreateButton(menu, "", row.Action, UiTheme.Surface, UiTheme.Text, 12);
                UIFactory.PlaceTopLeft((RectTransform)btn.transform, MenuPad, top, inner, MenuRowHeight);

                // Two lines inside a 34 px row: the task, and what it does. The
                // factory's centred caption is retargeted to the top line so the
                // two rects meet rather than overlap.
                var label = btn.GetComponentInChildren<Text>(true);
                label.text = row.Label;
                label.alignment = TextAnchor.MiddleLeft;
                label.fontStyle = FontStyle.Bold;
                UIFactory.PlaceTopLeft(label.rectTransform, 8f, 3f, inner - 16f, 16f);
                UIFactory.Fit(label);

                var detail = UIFactory.CreateText((RectTransform)btn.transform, row.Detail,
                    UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleLeft);
                UIFactory.PlaceTopLeft(detail.rectTransform, 8f, 19f, inner - 16f, 13f);
                UIFactory.Fit(detail);
            }

            menu.gameObject.SetActive(false);
            return menu;
        }

        /// <summary>Opens one submenu and shuts the other; clicking an open one shuts it.</summary>
        void ToggleMenu(RectTransform menu)
        {
            bool open = menu != null && !menu.gameObject.activeSelf;
            CloseMenus();
            if (!open || menu == null) return;

            menu.gameObject.SetActive(true);
            menu.SetAsLastSibling();
            MenuButton(menu).image.color = Armed;
        }

        void CloseMenus()
        {
            SetMenu(_attackMenu, false);
            SetMenu(_defenceMenu, false);
        }

        void SetMenu(RectTransform menu, bool open)
        {
            if (menu == null) return;
            menu.gameObject.SetActive(open);
            var btn = MenuButton(menu);
            // The attack button also latches while a target is being picked, so
            // closing its menu must not clear that.
            if (btn != null && !(btn == _attackBtn && _attackArmed))
                btn.image.color = open ? Armed : Idle;
        }

        Button MenuButton(RectTransform menu) => menu == _attackMenu ? _attackBtn : _defenceBtn;

        void Choose(System.Action order)
        {
            CloseMenus();
            order?.Invoke();
        }

        // ------------------------------------------------------- orders

        void OnMove()
        {
            CloseMenus();
            ClearAttackArmed();
            _moveArmed = true;
            _moveBtn.image.color = Armed;
            MoveRequested?.Invoke();
            Flash?.Invoke("Move order — click the map to set the destination (Esc cancels).");
        }

        void OnAttack(AttackTask task)
        {
            ClearMoveArmed();
            _attackArmed = true;
            if (_attackBtn != null) _attackBtn.image.color = Armed;
            AttackRequested?.Invoke(task);

            var def = AttackTaskCatalog.Get(task);
            Flash?.Invoke($"{def.name} — click the enemy formation to target (Esc or RMB cancels).");
        }

        /// <summary>Called once the pending move has been placed or cancelled.</summary>
        public void ClearMoveArmed()
        {
            if (!_moveArmed) return;
            _moveArmed = false;
            if (_moveBtn != null) _moveBtn.image.color = Idle;
        }

        /// <summary>Called once the pending attack target has been picked or cancelled.</summary>
        public void ClearAttackArmed()
        {
            if (!_attackArmed) return;
            _attackArmed = false;
            if (_attackBtn != null) _attackBtn.image.color = Idle;
        }

        public void Show(UnitActor unit)
        {
            if (_panel == null || unit == null) { Hide(); return; }
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            // The submenus act on the unit that was selected when they opened, so
            // a selection change closes them rather than silently re-targeting.
            CloseMenus();
            string name = string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName;
            _title.text = $"ORDERS — {name.ToUpperInvariant()}";
        }

        public void Hide()
        {
            ClearMoveArmed();
            ClearAttackArmed();
            CloseMenus();
            if (_panel != null) _panel.gameObject.SetActive(false);
        }
    }
}
