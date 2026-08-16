using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// The battle order bar: six things a selected formation can be told, each
    /// opening a submenu of what that means.
    ///
    /// **MOVE · ATTACK · RECON · DEFENCE · COMMANDS · PLANNER.** The first four
    /// are tasks — they take an objective, and the next click on the map is it.
    /// The last two are not:
    ///
    ///  • **COMMANDS** are standing switches with no objective at all. Stop what
    ///    you are doing; roam when idle; shoot at what comes past. They apply
    ///    the moment they are clicked, which is why they carry a lamp showing
    ///    their current state rather than a prompt to click the map.
    ///  • **PLANNER** draws intentions. Nothing it puts on the map executes —
    ///    see <see cref="PlannerSystem"/> for why that is worth having.
    ///
    /// **Every task menu is read off its catalogue** —
    /// <see cref="MoveTaskCatalog"/>, <see cref="AttackTaskCatalog"/>,
    /// <see cref="ReconTaskCatalog"/> — so a task added there shows up here with
    /// the same name and the same one-liner, and the caption cannot drift from
    /// the behaviour. The defence menu is the exception: its three tasks are
    /// distinct enough in what they *do* that they are three methods rather than
    /// three rows of numbers.
    ///
    /// Only one submenu is open at a time, and any of them closes when the
    /// selection changes — a menu that silently re-targeted whichever unit
    /// happened to be selected when you finally clicked would be a trap.
    ///
    /// **One formation or a whole group.** The bar is the only place an order
    /// is given, whichever is selected: with several formations up it is
    /// captioned for the group and every one of them carries the order out,
    /// spread across a frontage rather than stacked on one point (see
    /// <c>GameController.ForSelectionOnGround</c>). The lamps read the lead
    /// formation, because a standing switch is flipped for the group as a whole
    /// from whatever the lead currently is.
    /// </summary>
    public class UnitActionBarUI : MonoBehaviour
    {
        // --- tasks: the player picks one, then clicks the map ---
        /// <summary>Raised with the movement task picked; the map click comes next.</summary>
        public System.Action<MoveTask> MoveRequested;
        /// <summary>Raised with the offensive task picked; the map click comes next.</summary>
        public System.Action<AttackTask> AttackRequested;
        /// <summary>Raised with the reconnaissance task picked; the map click comes next.</summary>
        public System.Action<ReconTask> ReconRequested;
        /// <summary>Raised with the defensive task picked; the map click comes next.</summary>
        public System.Action<DefenceTask> DefenceRequested;
        /// <summary>Raised with the plan being drawn; the map click is its objective.</summary>
        public System.Action<PlanKind> PlanRequested;

        // --- commands: applied at once, no map click ---
        public System.Action StopRequested;
        public System.Action ToggleFreeMovementRequested;
        public System.Action ToggleAutomaticAttackRequested;

        public System.Action<string> Flash;

        /// <summary>
        /// The three defensive tasks. An enum rather than three actions so the
        /// bar, the ground-pick and the order system all name the same thing —
        /// they each used to carry their own trio of callbacks.
        /// </summary>
        public enum DefenceTask { Defend, Hold, Guard }

        RectTransform _panel;
        Text _title;
        readonly Dictionary<Slot, RectTransform> _menus = new Dictionary<Slot, RectTransform>();
        readonly Dictionary<Slot, Button> _buttons = new Dictionary<Slot, Button>();

        /// <summary>Which button, if any, is waiting on a map click.</summary>
        Slot? _armed;
        /// <summary>Which menu is open.</summary>
        Slot? _openMenu;

        /// <summary>Lamps on the two toggle rows, repainted from the selected unit.</summary>
        RectTransform _freeLamp, _autoLamp;
        Text _freeLabel, _autoLabel;
        UnitActor _unit;

        enum Slot { Move, Attack, Recon, Defence, Commands, Planner }

        static readonly Color Idle = new Color(0.18f, 0.22f, 0.29f);
        static readonly Color Armed = new Color(0.85f, 0.65f, 0.13f);

        /// <summary>Order-button geometry; the submenus line themselves up from the same numbers.</summary>
        const float ButtonWidth = 112f, ButtonHeight = 62f, ButtonGap = 8f;
        const int ButtonCount = 6;
        const float BarWidth = ButtonWidth * ButtonCount + ButtonGap * (ButtonCount + 1);
        /// <summary>Submenu metrics. Wider than a button so a caption and its one-liner fit.</summary>
        const float MenuWidth = ButtonWidth + 64f;
        const float MenuRowHeight = 34f, MenuRowGap = 4f, MenuHeaderHeight = 22f, MenuPad = 6f;

        /// <summary>One submenu row: caption, the line under it, and what it does.</summary>
        readonly struct TaskRow
        {
            public readonly string Label, Detail;
            public readonly UnityEngine.Events.UnityAction Action;
            /// <summary>True for the two rows that show a live on/off lamp.</summary>
            public readonly bool Lamp;

            public TaskRow(string label, string detail, UnityEngine.Events.UnityAction action,
                bool lamp = false)
            {
                Label = label; Detail = detail; Action = action; Lamp = lamp;
            }
        }

        public void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "UnitActionBar", UiTheme.Panel);
            _panel.anchorMin = new Vector2(0.5f, 0f);
            _panel.anchorMax = new Vector2(0.5f, 0f);
            _panel.pivot = new Vector2(0.5f, 0f);
            _panel.sizeDelta = new Vector2(BarWidth, 104);
            _panel.anchoredPosition = new Vector2(0, 44);   // clear of the help line

            _title = UIFactory.CreateText(_panel, "", 13, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -6),
                new Vector2(BarWidth - 30f, 20));

            ActionButton(Slot.Move, "MOVE", ProceduralTextures.MoveIcon(UiTheme.Text));
            ActionButton(Slot.Attack, "ATTACK", ProceduralTextures.AttackIcon(UiTheme.Text));
            ActionButton(Slot.Recon, "RECON", ProceduralTextures.ReconIcon(UiTheme.Text));
            ActionButton(Slot.Defence, "DEFENCE", ProceduralTextures.ShieldIcon(UiTheme.Text));
            ActionButton(Slot.Commands, "COMMANDS", ProceduralTextures.CommandIcon(UiTheme.Text));
            ActionButton(Slot.Planner, "PLANNER", ProceduralTextures.PlannerIcon(UiTheme.Text));

            BuildMoveMenu();
            BuildAttackMenu();
            BuildReconMenu();
            BuildDefenceMenu();
            BuildCommandsMenu();
            BuildPlannerMenu();
            Hide();
        }

        /// <summary>Centre-relative x of an order button.</summary>
        static float ButtonX(int index) =>
            -(ButtonWidth * ButtonCount + ButtonGap * (ButtonCount - 1)) / 2f
            + ButtonWidth / 2f + index * (ButtonWidth + ButtonGap);

        void ActionButton(Slot slot, string label, Texture2D icon)
        {
            var captured = slot;
            var btn = UIFactory.CreateButton(_panel, "", () => ToggleMenu(captured),
                Idle, UiTheme.Text, 12);
            var rt = (RectTransform)btn.transform;
            UIFactory.Place(rt, new Vector2(0.5f, 0f), new Vector2(ButtonX((int)slot), 10),
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
            UIFactory.Fit(caption, 8);

            var sprite = Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height),
                new Vector2(0.5f, 0.5f), 100f);
            var image = UIFactory.CreateImage(rt, sprite, "Icon");
            image.raycastTarget = false;
            UIFactory.Place((RectTransform)image.transform, new Vector2(0.5f, 1f),
                new Vector2(0, -6), new Vector2(28, 28));

            _buttons[slot] = btn;
        }

        // ------------------------------------------------------- submenus

        void BuildMoveMenu()
        {
            var rows = new List<TaskRow>();
            foreach (var def in MoveTaskCatalog.All)
            {
                var task = def.task;      // captured per row, not per loop variable
                rows.Add(new TaskRow(def.name, def.detail, () => Arm(Slot.Move, () => OnMove(task))));
            }
            _menus[Slot.Move] = BuildTaskMenu("MoveMenu", "MOVEMENT", Slot.Move, rows);
        }

        void BuildAttackMenu()
        {
            var rows = new List<TaskRow>();
            foreach (var def in AttackTaskCatalog.All)
            {
                var task = def.task;
                rows.Add(new TaskRow(def.name, def.detail, () => Arm(Slot.Attack, () => OnAttack(task))));
            }
            _menus[Slot.Attack] = BuildTaskMenu("AttackMenu", "OFFENSIVE TASK", Slot.Attack, rows);
        }

        void BuildReconMenu()
        {
            var rows = new List<TaskRow>();
            foreach (var def in ReconTaskCatalog.All)
            {
                var task = def.task;
                rows.Add(new TaskRow(def.name, def.detail, () => Arm(Slot.Recon, () => OnRecon(task))));
            }
            _menus[Slot.Recon] = BuildTaskMenu("ReconMenu", "RECONNAISSANCE", Slot.Recon, rows);
        }

        void BuildDefenceMenu()
        {
            var rows = new List<TaskRow>
            {
                new TaskRow("DEFEND", "Hold a line across the threat",
                    () => Arm(Slot.Defence, () => OnDefence(DefenceTask.Defend))),
                new TaskRow("HOLD", "Retain a piece of ground",
                    () => Arm(Slot.Defence, () => OnDefence(DefenceTask.Hold))),
                new TaskRow("GUARD", "Screen a sector, in four",
                    () => Arm(Slot.Defence, () => OnDefence(DefenceTask.Guard)))
            };
            _menus[Slot.Defence] = BuildTaskMenu("DefenceMenu", "DEFENSIVE TASK", Slot.Defence, rows);
        }

        /// <summary>
        /// The standing switches. These act at once rather than arming a map
        /// click, so the menu closes on pick and the two toggles carry a lamp
        /// showing what they currently are — a switch you cannot read the state
        /// of is a switch you press twice to find out.
        /// </summary>
        void BuildCommandsMenu()
        {
            var rows = new List<TaskRow>
            {
                new TaskRow("STOP", "Cancel every order",
                    () => Choose(() => StopRequested?.Invoke())),
                new TaskRow("FREE MOVEMENT", "Roam when idle",
                    () => Choose(() => ToggleFreeMovementRequested?.Invoke()), lamp: true),
                new TaskRow("AUTO ATTACK", "Engage without orders",
                    () => Choose(() => ToggleAutomaticAttackRequested?.Invoke()), lamp: true)
            };
            _menus[Slot.Commands] = BuildTaskMenu("CommandsMenu", "COMMANDS", Slot.Commands, rows);
        }

        void BuildPlannerMenu()
        {
            var rows = new List<TaskRow>
            {
                new TaskRow("MAIN ATTACK", "Draw the decisive axis",
                    () => Arm(Slot.Planner, () => OnPlan(PlanKind.MainAttack))),
                new TaskRow("SUPPORTING", "Draw a shaping axis",
                    () => Arm(Slot.Planner, () => OnPlan(PlanKind.SupportingAttack))),
                // Deliberately the same order the movement menu gives, not a
                // copy of it: two controls that looked like planning and behaved
                // differently would be worse than one control in two places.
                new TaskRow("RETREAT LINE", "Same as MOVE → RETREAT",
                    () => Arm(Slot.Planner, () => OnMove(MoveTask.Retreat)))
            };
            _menus[Slot.Planner] = BuildTaskMenu("PlannerMenu", "PLANNER", Slot.Planner, rows);
        }

        /// <summary>
        /// Builds a submenu stacked above its own order button. Built once and
        /// toggled rather than created on demand, so repeat use does not churn
        /// uGUI objects mid-battle.
        /// </summary>
        RectTransform BuildTaskMenu(string name, string header, Slot slot, List<TaskRow> rows)
        {
            float height = MenuHeaderHeight + MenuPad
                         + rows.Count * MenuRowHeight + (rows.Count - 1) * MenuRowGap
                         + MenuPad;
            float inner = MenuWidth - MenuPad * 2f;

            var menu = UIFactory.CreateBorderedPanel(_panel, name, UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(menu, new Vector2(0.5f, 1f), new Vector2(ButtonX((int)slot), 4),
                new Vector2(MenuWidth, height));
            menu.pivot = new Vector2(0.5f, 0f);

            var caption = UIFactory.CreateSectionHeader(menu, header);
            caption.alignment = TextAnchor.MiddleCenter;
            UIFactory.PlaceTopLeft(caption.rectTransform, MenuPad, MenuPad, inner, MenuHeaderHeight - MenuPad);
            UIFactory.Fit(caption, 8);

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float top = MenuHeaderHeight + MenuPad + i * (MenuRowHeight + MenuRowGap);

                var btn = UIFactory.CreateButton(menu, "", row.Action, UiTheme.Surface, UiTheme.Text, 12);
                var brt = (RectTransform)btn.transform;
                UIFactory.PlaceTopLeft(brt, MenuPad, top, inner, MenuRowHeight);

                // A lamp on the left of the two toggle rows, and the text column
                // steps in to make room for it.
                float textX = 8f;
                if (row.Lamp)
                {
                    textX = 22f;
                    var lamp = UIFactory.CreatePanel(brt, "Lamp", UiTheme.TextFaint);
                    UIFactory.Place(lamp, new Vector2(0f, 0.5f), new Vector2(8, 0), new Vector2(8, 8));
                    lamp.GetComponent<Image>().raycastTarget = false;
                    if (row.Label.StartsWith("FREE")) _freeLamp = lamp; else _autoLamp = lamp;
                }

                // Two lines inside a 34 px row: the task, and what it does. The
                // factory's centred caption is retargeted to the top line so the
                // two rects meet rather than overlap.
                var label = btn.GetComponentInChildren<Text>(true);
                label.text = row.Label;
                label.alignment = TextAnchor.MiddleLeft;
                label.fontStyle = FontStyle.Bold;
                UIFactory.PlaceTopLeft(label.rectTransform, textX, 3f, inner - textX - 8f, 16f);
                UIFactory.Fit(label, 8);

                var detail = UIFactory.CreateText(brt, row.Detail,
                    UiTheme.FontLabel, UiTheme.TextFaint, TextAnchor.MiddleLeft);
                UIFactory.PlaceTopLeft(detail.rectTransform, textX, 19f, inner - textX - 8f, 13f);
                UIFactory.Fit(detail, 8);

                if (row.Lamp)
                {
                    if (row.Label.StartsWith("FREE")) _freeLabel = detail; else _autoLabel = detail;
                }
            }

            menu.gameObject.SetActive(false);
            return menu;
        }

        // --------------------------------------------------------- menu state

        /// <summary>Opens one submenu and shuts the others; clicking an open one shuts it.</summary>
        void ToggleMenu(Slot slot)
        {
            bool open = _openMenu != slot;
            CloseMenus();
            if (!open) return;

            _openMenu = slot;
            var menu = _menus[slot];
            menu.gameObject.SetActive(true);
            menu.SetAsLastSibling();
            RefreshCommandLamps();
            Paint();
        }

        void CloseMenus()
        {
            foreach (var kv in _menus) kv.Value.gameObject.SetActive(false);
            _openMenu = null;
            Paint();
        }

        /// <summary>
        /// A row that acts at once: shut the menu and do it. Nothing latches,
        /// because there is no map click coming.
        /// </summary>
        void Choose(System.Action action)
        {
            CloseMenus();
            action?.Invoke();
            RefreshCommandLamps();
        }

        /// <summary>
        /// A row that arms a map click: shut the menu, latch the button, and
        /// raise the order. <see cref="ClearArmed"/> puts it back.
        /// </summary>
        void Arm(Slot slot, System.Action order)
        {
            CloseMenus();
            _armed = slot;
            Paint();
            order?.Invoke();
        }

        /// <summary>
        /// One button reads as armed and one menu reads as open. Kept in a
        /// single method because the two states share the same highlight, and
        /// two places setting one colour is how a button gets stuck lit.
        /// </summary>
        void Paint()
        {
            foreach (var kv in _buttons)
                kv.Value.image.color = (_armed == kv.Key || _openMenu == kv.Key) ? Armed : Idle;
        }

        // ------------------------------------------------------------ orders

        void OnMove(MoveTask task)
        {
            MoveRequested?.Invoke(task);
            var def = MoveTaskCatalog.Get(task);
            Flash?.Invoke(def.isContingency
                ? $"{def.name} — click the ground to fall back to (Esc or RMB cancels)."
                : $"{def.name} — click the map to set the destination (Esc or RMB cancels).");
        }

        void OnAttack(AttackTask task)
        {
            AttackRequested?.Invoke(task);
            Flash?.Invoke($"{AttackTaskCatalog.Get(task).name} — click an enemy formation, " +
                          "or bare ground to attack everything on it (Esc or RMB cancels).");
        }

        void OnRecon(ReconTask task)
        {
            ReconRequested?.Invoke(task);
            Flash?.Invoke($"{ReconTaskCatalog.Get(task).name} — click the centre of the area to " +
                          "search (Esc or RMB cancels).");
        }

        void OnDefence(DefenceTask task)
        {
            DefenceRequested?.Invoke(task);
            Flash?.Invoke($"{task.ToString().ToUpperInvariant()} — click the ground to " +
                          $"{task.ToString().ToLowerInvariant()} (Esc or RMB cancels).");
        }

        void OnPlan(PlanKind kind)
        {
            PlanRequested?.Invoke(kind);
            Flash?.Invoke(kind == PlanKind.MainAttack
                ? "Main attack — click where the axis goes (Esc or RMB cancels)."
                : "Supporting attack — click where the axis goes (Esc or RMB cancels).");
        }

        /// <summary>Called once a pending order has been placed or cancelled.</summary>
        public void ClearArmed()
        {
            if (!_armed.HasValue) return;
            _armed = null;
            Paint();
        }

        // ------------------------------------------------------------ lamps

        /// <summary>
        /// Repaints the two toggle rows from the selected formation. Called when
        /// the menu opens and after either is flipped, rather than every frame:
        /// the state only changes when something in here changed it.
        /// </summary>
        void RefreshCommandLamps()
        {
            if (_freeLamp == null || _autoLamp == null) return;

            bool free = _unit != null && _unit.State.freeMovement;
            bool auto = _unit == null || _unit.State.automaticAttack;

            _freeLamp.GetComponent<Image>().color = free ? UiTheme.Success : UiTheme.TextFaint;
            _autoLamp.GetComponent<Image>().color = auto ? UiTheme.Success : UiTheme.TextFaint;

            if (_freeLabel != null)
                _freeLabel.text = free
                    ? $"ON — {CommandInfo.FreeMovementRadiusKm:0} km of ground"
                    : "OFF — holds position when idle";
            if (_autoLabel != null)
                _autoLabel.text = auto ? "ON — engages in range" : "OFF — holds fire";
        }

        // --------------------------------------------------------- lifecycle

        /// <summary>
        /// Puts the bar up for a formation — or for a group, in which case
        /// <paramref name="scopeTitle"/> says whose orders these are and
        /// <paramref name="unit"/> is the lead formation the lamps are read
        /// from.
        ///
        /// **The group's orders live here, on the same six buttons.** They used
        /// to be three unwired buttons inside the group panel on the right, and
        /// having two places to give an order — with two vocabularies, one of
        /// which did nothing — was worse than having one. A group is given the
        /// same six orders in the same place as a single formation; the only
        /// difference is how many formations carry them out, and that is what
        /// the caption is for.
        /// </summary>
        public void Show(UnitActor unit, string scopeTitle = null)
        {
            if (_panel == null || unit == null) { Hide(); return; }
            _unit = unit;
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            // The submenus act on the unit that was selected when they opened, so
            // a selection change closes them rather than silently re-targeting.
            CloseMenus();
            ClearArmed();
            RefreshCommandLamps();

            string name = string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName;
            _title.text = string.IsNullOrEmpty(scopeTitle)
                ? $"ORDERS — {name.ToUpperInvariant()}"
                : scopeTitle.ToUpperInvariant();
        }

        public void Hide()
        {
            _unit = null;
            ClearArmed();
            CloseMenus();
            if (_panel != null) _panel.gameObject.SetActive(false);
        }
    }
}
