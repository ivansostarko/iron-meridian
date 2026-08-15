using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Right-side panel shown instead of <see cref="UnitInfoPanel"/> whenever
    /// 2+ units are selected. It does four things:
    ///
    ///  • **Names the current selection as a group**, so it can be recalled later.
    ///  • **Lists what is selected**, with each formation's current group beside
    ///    it and a way to move it to another one.
    ///  • **Lists the existing groups** — click one to select its members,
    ///    double-click to fly the camera to them.
    ///  • **Gives the group orders** — MOVE / ATTACK / DEFEND. Mocked up for now;
    ///    see <see cref="OrderNotImplemented"/>.
    ///
    /// **Why regrouping lives here.** A group is a property of the units in it,
    /// not an object of its own, so "move these units to that group" has to be
    /// expressed as a selection and a destination. Both are already on this
    /// panel: the selection at the top, the destinations at the bottom. Two
    /// paths, because they answer two different questions —
    /// <see cref="MoveSelectionToGroup"/> takes everything selected to one
    /// group (splitting a force between two axes), and the ⇄ on a single row
    /// opens a picker for that one formation (moving a battalion between
    /// brigades).
    /// </summary>
    public class GroupPanelUI : MonoBehaviour
    {
        public System.Action<List<UnitActor>> SelectGroupRequested;
        /// <summary>Double-click on a group row: select its members *and* fly the camera to them.</summary>
        public System.Action<List<UnitActor>> FlyToGroupRequested;
        public System.Action<string> Flash;
        /// <summary>Delete a unit from the map (raised by the ✕ on a selected-unit row).</summary>
        public System.Action<UnitActor> RemoveUnitRequested;

        // ------------------------------------------------------------ layout

        const float PanelWidth = 300f;
        /// <summary>Content width — the panel less an equal margin either side.</summary>
        const float Inner = 264f;

        /// <summary>
        /// Left inset of a selected-unit row's contents — the icon and the
        /// caption beside it. The rows used to start hard against the scroll
        /// viewport's edge, which put an APP-6 frame's left stroke on the
        /// clipping boundary and made the list read as pressed against the side
        /// of the screen rather than as a table inside it.
        /// </summary>
        const float UnitRowInset = 30f;

        /// <summary>Vertical gap above the SELECTED UNITS heading.</summary>
        const float UnitsLabelMargin = 10f;

        // Distances from the panel's top edge to the top of each block. The
        // whole lower half slides down by OrdersBlockHeight when the orders bar
        // is up — see Layout().
        const float TitleTop = 24f, TitleHeight = 32f;
        const float OrdersCaptionTop = 62f;
        const float OrdersButtonsTop = 82f, OrdersButtonsHeight = 46f;
        /// <summary>What the orders block costs everything below it.</summary>
        const float OrdersBlockHeight = 76f;

        const float HintTop = 56f;
        const float NameInputTop = 84f;
        const float CreateTop = 128f, CreateHeight = 40f;
        /// <summary>Heading sits a clear <see cref="UnitsLabelMargin"/> below the CREATE row.</summary>
        const float UnitsLabelTop = CreateTop + CreateHeight + UnitsLabelMargin;
        const float UnitsLabelHeight = 24f;
        const float UnitsScrollTop = UnitsLabelTop + UnitsLabelHeight + 4f;

        /// <summary>
        /// Where the selected-units list stops and the saved-groups list begins,
        /// as a fraction of panel height. Lower than it was: the orders bar took
        /// 76 px off the top half, and at 720p that left the upper list two rows
        /// tall.
        /// </summary>
        const float SplitFraction = 0.38f;

        const float UnitRowHeight = 34f;
        const float GroupRowHeight = 44f;

        // ------------------------------------------------------------- state

        RectTransform _panel;
        Text _title;
        InputField _nameInput;
        RectTransform _listContent;      // existing groups
        RectTransform _unitsContent;     // units in the current selection
        RectTransform _unitsScroll, _unitsLabelRect, _hintRect, _createRect, _ungroupRect;
        RectTransform _ordersBlock;
        Text _ordersCaption;
        RectTransform _picker;           // the per-unit group chooser, when open

        IReadOnlyList<UnitActor> _currentSelection = new List<UnitActor>();

        /// <summary>One existing group, as the lists read it.</summary>
        readonly struct GroupInfo
        {
            public readonly string Id, Name;
            public readonly int Count;
            public GroupInfo(string id, string name, int count) { Id = id; Name = name; Count = count; }
        }

        public void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "GroupPanel", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-PanelWidth, 0);
            // Below the strike dock's icon strip — see StrikeDockUI.
            _panel.offsetMax = new Vector2(0, -(UiTheme.TopBarHeight + UiTheme.StrikeDockHeight));

            _title = UIFactory.CreateText(_panel, "0 UNITS SELECTED", 18,
                UiTheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0, -TitleTop), new Vector2(Inner + 16f, TitleHeight));

            BuildOrdersBar();

            var hint = UIFactory.CreateText(_panel, "Name this selection as a group:", 13,
                UiTheme.TextDim, TextAnchor.MiddleLeft);
            _hintRect = hint.rectTransform;
            UIFactory.Place(_hintRect, new Vector2(0.5f, 1f), new Vector2(0, -HintTop), new Vector2(Inner, 22));

            _nameInput = UIFactory.CreateInputField(_panel, "Group name…", 16);
            UIFactory.Place((RectTransform)_nameInput.transform, new Vector2(0.5f, 1f),
                new Vector2(0, -NameInputTop), new Vector2(Inner, 36));

            // CREATE and UNGROUP share a row. They are the two halves of one
            // question — is this selection a group or not — and stacking them
            // would have cost a row of the list below for no gain.
            var create = UIFactory.CreateButton(_panel, "CREATE GROUP", CreateGroup,
                UiTheme.Accent, new Color(0.1f, 0.1f, 0.1f), 15);
            _createRect = (RectTransform)create.transform;
            UIFactory.Place(_createRect, new Vector2(0.5f, 1f),
                new Vector2(-52f, -CreateTop), new Vector2(160, CreateHeight));
            UIFactory.Fit(create.GetComponentInChildren<Text>(), 10);

            var ungroup = UIFactory.CreateButton(_panel, "UNGROUP", UngroupSelection,
                UiTheme.Surface, UiTheme.TextDim, 13);
            _ungroupRect = (RectTransform)ungroup.transform;
            UIFactory.Place(_ungroupRect, new Vector2(0.5f, 1f),
                new Vector2(82f, -CreateTop), new Vector2(100, CreateHeight));
            UIFactory.Fit(ungroup.GetComponentInChildren<Text>(), 10);

            // ---- units in this selection ----
            var unitsLabel = UIFactory.CreateText(_panel, "SELECTED UNITS", 14, UiTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            _unitsLabelRect = unitsLabel.rectTransform;
            UIFactory.Place(_unitsLabelRect, new Vector2(0.5f, 1f),
                new Vector2(0, -UnitsLabelTop), new Vector2(Inner, UnitsLabelHeight));

            var unitsScroll = UIFactory.CreateScrollView(_panel, out _unitsContent);
            _unitsScroll = (RectTransform)unitsScroll.transform;
            _unitsScroll.anchorMin = new Vector2(0, SplitFraction);
            _unitsScroll.anchorMax = new Vector2(1, 1);
            _unitsScroll.offsetMin = new Vector2(10, 4);
            _unitsScroll.offsetMax = new Vector2(-10, -UnitsScrollTop);
            TightenRows(_unitsContent);

            var removeAll = UIFactory.CreateButton(_panel, "REMOVE ALL SELECTED", RemoveAllSelected,
                new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, 14);
            var rart = (RectTransform)removeAll.transform;
            rart.anchorMin = new Vector2(0, SplitFraction); rart.anchorMax = new Vector2(1, SplitFraction);
            rart.pivot = new Vector2(0.5f, 1f);
            rart.sizeDelta = new Vector2(-20, 32);
            rart.anchoredPosition = new Vector2(0, -6);

            // ---- saved groups ----
            var groupsLabel = UIFactory.CreateText(_panel, "EXISTING GROUPS", 14, UiTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            var glrt = groupsLabel.rectTransform;
            glrt.anchorMin = new Vector2(0, SplitFraction); glrt.anchorMax = new Vector2(1, SplitFraction);
            glrt.pivot = new Vector2(0.5f, 1f);
            glrt.sizeDelta = new Vector2(-36, 24);
            glrt.anchoredPosition = new Vector2(0, -42);

            // Deliberately terse: this has one line to work in before it runs
            // into the list below it, and legacy Text has no ellipsis — a hint
            // that wrapped would overflow onto the first group row.
            var groupsHint = UIFactory.CreateText(_panel,
                "Click selects  ·  double-click flies  ·  ＋ adds selection",
                10, UiTheme.TextFaint, TextAnchor.MiddleLeft);
            var ghrt = groupsHint.rectTransform;
            ghrt.anchorMin = new Vector2(0, SplitFraction); ghrt.anchorMax = new Vector2(1, SplitFraction);
            ghrt.pivot = new Vector2(0.5f, 1f);
            ghrt.sizeDelta = new Vector2(-36, 16);
            ghrt.anchoredPosition = new Vector2(0, -68);
            UIFactory.Fit(groupsHint, 8);

            var scroll = UIFactory.CreateScrollView(_panel, out _listContent);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, SplitFraction);
            srt.offsetMin = new Vector2(10, 10);
            srt.offsetMax = new Vector2(-10, -88);
            TightenRows(_listContent);

            Hide();
        }

        // ------------------------------------------------------- orders bar

        /// <summary>
        /// MOVE / ATTACK / DEFEND for the group as a whole.
        ///
        /// **A mockup, deliberately and visibly.** The buttons exist, are
        /// captioned, carry the same glyphs the single-unit order bar uses
        /// (<see cref="ProceduralTextures"/>), and say plainly that the order is
        /// not wired yet. Giving them a *plausible* behaviour — issuing the
        /// order to each member in turn — would be worse than doing nothing: a
        /// group move is not several unit moves, it is a formation moving with
        /// an axis, a frontage and an order of march, and shipping the naive
        /// version would make the real one a bug report rather than a feature.
        ///
        /// The glyphs are shared with <see cref="UnitActionBarUI"/> on purpose:
        /// when these are wired up they must read as the same verbs, and a
        /// second visual vocabulary for the same three orders would be a
        /// second thing to learn.
        /// </summary>
        void BuildOrdersBar()
        {
            _ordersBlock = UIFactory.CreateGroup(_panel, "GroupOrders");
            UIFactory.Place(_ordersBlock, new Vector2(0.5f, 1f),
                new Vector2(0, -OrdersCaptionTop),
                new Vector2(Inner, OrdersButtonsTop - OrdersCaptionTop + OrdersButtonsHeight));

            _ordersCaption = UIFactory.CreateSectionHeader(_ordersBlock, "GROUP ORDERS", UiTheme.TextFaint);
            UIFactory.PlaceTopLeft(_ordersCaption.rectTransform, 0f, 0f, Inner, 16f);

            const float gap = 6f;
            float w = (Inner - gap * 2f) / 3f;

            OrderButton("MOVE", 0, w, gap, ProceduralTextures.MoveIcon(UiTheme.Text));
            OrderButton("ATTACK", 1, w, gap, ProceduralTextures.AttackIcon(UiTheme.Text));
            OrderButton("DEFEND", 2, w, gap, ProceduralTextures.ShieldIcon(UiTheme.Text));

            _ordersBlock.gameObject.SetActive(false);
        }

        void OrderButton(string label, int index, float width, float gap, Texture2D icon)
        {
            var btn = UIFactory.CreateButton(_ordersBlock, "", () => OrderNotImplemented(label),
                UiTheme.Surface, UiTheme.Text, 11);
            var rt = (RectTransform)btn.transform;
            UIFactory.Place(rt, new Vector2(0f, 1f),
                new Vector2(index * (width + gap), -(OrdersButtonsTop - OrdersCaptionTop)),
                new Vector2(width, OrdersButtonsHeight));

            // CreateButton centres its own caption; this layout wants the glyph
            // above it, so the caption is retargeted to the lower strip — the
            // same arrangement UnitActionBarUI uses.
            var caption = btn.GetComponentInChildren<Text>(true);
            caption.text = label;
            caption.alignment = TextAnchor.LowerCenter;
            var crt = caption.rectTransform;
            crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(1, 0);
            crt.pivot = new Vector2(0.5f, 0f);
            crt.sizeDelta = new Vector2(0, 16);
            crt.anchoredPosition = new Vector2(0, 4);
            UIFactory.Fit(caption, 8);

            var sprite = Sprite.Create(icon, new Rect(0, 0, icon.width, icon.height),
                new Vector2(0.5f, 0.5f), 100f);
            var image = UIFactory.CreateImage(rt, sprite, "Icon");
            image.raycastTarget = false;
            UIFactory.Place((RectTransform)image.transform, new Vector2(0.5f, 1f),
                new Vector2(0, -5), new Vector2(22, 22));
        }

        void OrderNotImplemented(string order) =>
            Flash?.Invoke($"{order} as a group is not wired up yet — order the formations individually for now.");

        /// <summary>
        /// Slides the block below the orders bar up or down by exactly what the
        /// bar costs. Hand-placed rects rather than a layout group, so the shift
        /// has to be applied to each of them — but only these four move, and
        /// everything under the split fraction is anchored to the bottom half
        /// and never moves at all.
        /// </summary>
        void Layout(bool ordersVisible)
        {
            _ordersBlock.gameObject.SetActive(ordersVisible);
            float shift = ordersVisible ? OrdersBlockHeight : 0f;

            _hintRect.anchoredPosition = new Vector2(0, -(HintTop + shift));
            ((RectTransform)_nameInput.transform).anchoredPosition = new Vector2(0, -(NameInputTop + shift));
            _createRect.anchoredPosition = new Vector2(-52f, -(CreateTop + shift));
            _ungroupRect.anchoredPosition = new Vector2(82f, -(CreateTop + shift));
            _unitsLabelRect.anchoredPosition = new Vector2(0, -(UnitsLabelTop + shift));
            _unitsScroll.offsetMax = new Vector2(-10, -(UnitsScrollTop + shift));
        }

        // --------------------------------------------------------- selection

        public void SetSelection(IReadOnlyList<UnitActor> selection)
        {
            _currentSelection = selection;
            if (selection.Count < 2) { Hide(); return; }

            ClosePicker();
            _panel.gameObject.SetActive(true);
            _title.text = $"{selection.Count} UNITS SELECTED";

            // The orders bar belongs to a *group*, not to any two units that
            // happen to be selected together — otherwise "group orders" would
            // be the wrong name for what the buttons act on.
            string groupName = SharedGroupName();
            Layout(groupName != null);
            if (groupName != null) _ordersCaption.text = UiTheme.Spaced("ORDERS · " + groupName);

            RefreshUnitList();
            RefreshGroupList();
        }

        /// <summary>
        /// The group every selected unit belongs to, or null when they do not
        /// all share one. Requiring *all* of them rather than any: a bar
        /// captioned "1st Brigade" acting on a selection that is half 1st
        /// Brigade and half something else would be lying about its scope.
        /// </summary>
        string SharedGroupName()
        {
            string id = null, name = null;
            bool first = true;

            foreach (var u in _currentSelection)
            {
                if (u == null || !u.IsAlive) continue;
                if (string.IsNullOrEmpty(u.State.groupId)) return null;
                if (first) { id = u.State.groupId; name = u.State.groupName; first = false; }
                else if (u.State.groupId != id) return null;
            }
            return first ? null : (string.IsNullOrEmpty(name) ? "Unnamed group" : name);
        }

        /// <summary>Row per selected unit: icon, name, its group, ⇄ to change it, ✕ to delete it.</summary>
        void RefreshUnitList()
        {
            ClearChildren(_unitsContent);

            foreach (var u in _currentSelection)
            {
                if (u == null || !u.IsAlive) continue;

                var row = UIFactory.CreatePanel(_unitsContent, "Unit_" + u.State.instanceId, UiTheme.Surface);
                row.sizeDelta = new Vector2(0, UnitRowHeight);

                string folder = u.State.TeamEnum == Team.User ? "Friendly" : "Enemy";
                var sprite = UIFactory.LoadIconSprite(folder, u.Def.id);
                if (sprite != null)
                {
                    var icon = UIFactory.CreateImage(row, sprite, "Icon");
                    icon.raycastTarget = false;
                    UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                        new Vector2(UnitRowInset, 0), new Vector2(28, 28));
                }

                string name = string.IsNullOrEmpty(u.State.customName) ? u.Def.name : u.State.customName;
                string group = string.IsNullOrEmpty(u.State.groupName) ? "" : $"   ·   {u.State.groupName}";

                var lbl = UIFactory.CreateText(row, $"{name}  ({u.State.echelon}){group}", 12,
                    UiTheme.Text, TextAnchor.MiddleLeft);
                var lr = lbl.rectTransform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(UnitRowInset + 32f, 0);
                lr.offsetMax = new Vector2(-64, 0);
                UIFactory.Fit(lbl, 8);

                // Captured per row — the list is rebuilt whenever the selection
                // changes, so the reference can't go stale behind the button.
                var target = u;

                var move = UIFactory.CreateButton(row, "⇄", () => OpenPicker(target),
                    UiTheme.SurfaceHover, UiTheme.Text, 13);
                UIFactory.Place((RectTransform)move.transform, new Vector2(1f, 0.5f),
                    new Vector2(-34, 0), new Vector2(26, 26));

                var del = UIFactory.CreateButton(row, "✕", () => RemoveUnit(target),
                    new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, 13);
                UIFactory.Place((RectTransform)del.transform, new Vector2(1f, 0.5f),
                    new Vector2(-4, 0), new Vector2(26, 26));
            }
        }

        void RemoveUnit(UnitActor unit)
        {
            if (unit == null) return;
            string name = string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName;
            RemoveUnitRequested?.Invoke(unit);
            Flash?.Invoke($"Removed {name}.");
        }

        void RemoveAllSelected()
        {
            // Copy first: removing units mutates the selection this list is
            // built from, and the callback re-enters SetSelection.
            var doomed = new List<UnitActor>(_currentSelection);
            int count = 0;
            foreach (var u in doomed)
            {
                if (u == null || !u.IsAlive) continue;
                RemoveUnitRequested?.Invoke(u);
                count++;
            }
            Flash?.Invoke($"Removed {count} units.");
        }

        static void TightenRows(RectTransform content)
        {
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3;
            layout.padding = new RectOffset(6, 6, 6, 6);
        }

        static void ClearChildren(RectTransform content)
        {
            // Unparent before Destroy: destruction is deferred to end of frame,
            // so old rows would otherwise sit in the layout beside the new ones.
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
        }

        public void Hide()
        {
            ClosePicker();
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ groups

        void CreateGroup()
        {
            string name = _nameInput.text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                Flash?.Invoke("Type a name for the group first.");
                return;
            }
            string id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            int count = 0;
            foreach (var u in _currentSelection)
            {
                if (u == null) continue;
                u.State.groupId = id;
                u.State.groupName = name;
                count++;
            }
            Flash?.Invoke($"Group '{name}' created ({count} units).");
            _nameInput.text = "";
            SetSelection(_currentSelection);       // the orders bar applies from now on
        }

        /// <summary>Takes every selected formation out of whatever group it is in.</summary>
        void UngroupSelection()
        {
            int count = 0;
            foreach (var u in _currentSelection)
            {
                if (u == null || string.IsNullOrEmpty(u.State.groupId)) continue;
                u.State.groupId = "";
                u.State.groupName = "";
                count++;
            }

            Flash?.Invoke(count == 0
                ? "None of the selected formations is in a group."
                : $"{count} formation(s) removed from their group.");
            SetSelection(_currentSelection);
        }

        /// <summary>Every group with at least one living member, in registry order.</summary>
        List<GroupInfo> CollectGroups()
        {
            var order = new List<string>();
            var names = new Dictionary<string, string>();
            var counts = new Dictionary<string, int>();

            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || string.IsNullOrEmpty(u.State.groupId)) continue;
                string id = u.State.groupId;
                if (!counts.ContainsKey(id))
                {
                    order.Add(id);
                    names[id] = string.IsNullOrEmpty(u.State.groupName) ? "Unnamed group" : u.State.groupName;
                    counts[id] = 0;
                }
                counts[id]++;
            }

            var list = new List<GroupInfo>(order.Count);
            foreach (var id in order) list.Add(new GroupInfo(id, names[id], counts[id]));
            return list;
        }

        void RefreshGroupList()
        {
            ClearChildren(_listContent);

            var groups = CollectGroups();
            if (groups.Count == 0)
            {
                var empty = UIFactory.CreateText(_listContent,
                    "No groups yet — name this selection above to make one.",
                    11, UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 40);
                return;
            }

            string current = SharedGroupIdOfSelection();
            foreach (var g in groups) CreateGroupRow(g, g.Id == current);
        }

        string SharedGroupIdOfSelection()
        {
            string id = null;
            bool first = true;
            foreach (var u in _currentSelection)
            {
                if (u == null || !u.IsAlive) continue;
                if (string.IsNullOrEmpty(u.State.groupId)) return null;
                if (first) { id = u.State.groupId; first = false; }
                else if (u.State.groupId != id) return null;
            }
            return id;
        }

        /// <summary>
        /// One saved group. Click selects its members, double-click flies to
        /// them, and ＋ moves the current selection into it.
        ///
        /// A <see cref="PointerEventData.clickCount"/> trigger rather than a
        /// Button: uGUI's Button has no notion of a second click, and timing
        /// clicks by hand would be a worse copy of what the event system has
        /// already counted. The single-click path still runs on the first click
        /// of a pair, which is right — flying to a group you have not selected
        /// would leave the map somewhere new with nothing to show for it.
        /// </summary>
        void CreateGroupRow(GroupInfo group, bool isCurrent)
        {
            var row = UIFactory.CreateBorderedPanel(_listContent, "Group_" + group.Id,
                isCurrent ? UiTheme.AccentWash : UiTheme.Surface, UiTheme.Border);
            row.sizeDelta = new Vector2(0, GroupRowHeight);

            var trigger = row.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(e =>
            {
                var pointer = (PointerEventData)e;
                if (pointer.clickCount >= 2) FlyToGroup(group.Id);
                else SelectGroup(group.Id);
            });
            trigger.triggers.Add(entry);

            var name = UIFactory.CreateText(row, group.Name, 14,
                isCurrent ? UiTheme.Accent : UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(name.rectTransform, 10f, 6f, Inner - 90f, 17f);
            UIFactory.Fit(name, 9);

            var detail = UIFactory.CreateText(row, $"{group.Count} formation(s)", 11,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(detail.rectTransform, 10f, 23f, Inner - 90f, 15f);

            var add = UIFactory.CreateButton(row, "＋", () => MoveSelectionToGroup(group),
                UiTheme.SurfaceHover, UiTheme.Text, 15);
            UIFactory.Place((RectTransform)add.transform, new Vector2(1f, 0.5f),
                new Vector2(-8, 0), new Vector2(30, 30));
        }

        List<UnitActor> MembersOf(string groupId)
        {
            var members = new List<UnitActor>();
            foreach (var u in UnitRegistry.All)
                if (u != null && u.IsAlive && u.State.groupId == groupId) members.Add(u);
            return members;
        }

        void SelectGroup(string groupId)
        {
            var members = MembersOf(groupId);
            if (members.Count == 0)
            {
                Flash?.Invoke("That group has no units left.");
                RefreshGroupList();
                return;
            }
            SelectGroupRequested?.Invoke(members);
        }

        void FlyToGroup(string groupId)
        {
            var members = MembersOf(groupId);
            if (members.Count == 0)
            {
                Flash?.Invoke("That group has no units left.");
                RefreshGroupList();
                return;
            }
            FlyToGroupRequested?.Invoke(members);
        }

        /// <summary>
        /// Moves everything currently selected into <paramref name="group"/>.
        /// The bulk path — splitting a force between two axes is a statement
        /// about a set of formations, not about each of them in turn.
        /// </summary>
        void MoveSelectionToGroup(GroupInfo group)
        {
            int moved = 0, already = 0;
            foreach (var u in _currentSelection)
            {
                if (u == null || !u.IsAlive) continue;
                if (u.State.groupId == group.Id) { already++; continue; }
                u.State.groupId = group.Id;
                u.State.groupName = group.Name;
                moved++;
            }

            Flash?.Invoke(moved == 0
                ? $"Everything selected is already in '{group.Name}'."
                : $"{moved} formation(s) moved into '{group.Name}'" +
                  (already > 0 ? $" ({already} already there)." : "."));

            SetSelection(_currentSelection);
        }

        // ------------------------------------------------------- unit picker

        /// <summary>
        /// The per-formation destination chooser, opened by the ⇄ on a row.
        ///
        /// A mini-modal inside the panel rather than a popup beside the row: the
        /// row lives inside a scroll viewport, so anchoring to it would mean
        /// converting between the viewport's space and the panel's and then
        /// keeping the popup on screen as the list scrolled underneath it. At
        /// 300 px wide the panel is small enough that covering it *is* the
        /// popup — and it keeps the answer to "which group?" in one place
        /// instead of two.
        /// </summary>
        void OpenPicker(UnitActor unit)
        {
            ClosePicker();
            if (unit == null || !unit.IsAlive) return;

            // Backdrop first: it dims the panel and, more importantly, is a
            // raycast target, so a click anywhere outside the chooser closes it
            // rather than reaching the rows underneath.
            var backdrop = UIFactory.CreatePanel(_panel, "PickerBackdrop", new Color(0.02f, 0.03f, 0.05f, 0.82f));
            UIFactory.Stretch(backdrop);
            var dismiss = backdrop.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = backdrop.GetComponent<Image>();
            dismiss.onClick.AddListener(ClosePicker);
            _picker = backdrop;

            var groups = CollectGroups();
            const float rowH = 30f, rowGap = 4f;
            float bodyHeight = (rowH + rowGap) * (groups.Count + 1);
            float height = Mathf.Min(46f + bodyHeight + 12f, _panel.rect.height - 40f);

            var box = UIFactory.CreateBorderedPanel(backdrop, "Picker", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Inner, height));

            // A do-nothing click handler on the box itself. uGUI walks *up* the
            // hierarchy looking for something that handles a click, so without
            // this a click on the chooser's own background would find the
            // backdrop's dismiss button behind it and close the thing being
            // clicked on.
            box.gameObject.AddComponent<Button>().targetGraphic = box.GetComponent<Image>();

            string name = string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName;
            var caption = UIFactory.CreateText(box, $"MOVE {name.ToUpperInvariant()} TO", 12,
                UiTheme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(caption.rectTransform, 12f, 12f, Inner - 50f, 18f);
            UIFactory.Fit(caption, 8);

            var close = UIFactory.CreateIconButton(box, UiIcons.Close, ClosePicker,
                new Color(0, 0, 0, 0), UiTheme.TextDim, 7f);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f),
                new Vector2(-8, -8), new Vector2(26, 26));

            var scroll = UIFactory.CreateScrollView(box, out var content);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = Vector2.zero; srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(6, 6);
            srt.offsetMax = new Vector2(-6, -38);
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = rowGap;
            layout.padding = new RectOffset(6, 6, 4, 4);

            // "No group" first: taking a formation out is as legitimate a
            // destination as any other, and burying it under the list would make
            // leaving a group feel like a different kind of operation.
            PickerRow(content, "(no group)", string.IsNullOrEmpty(unit.State.groupId),
                () => AssignUnit(unit, "", ""));

            foreach (var g in groups)
            {
                var captured = g;
                PickerRow(content, $"{g.Name}   ·   {g.Count}", unit.State.groupId == g.Id,
                    () => AssignUnit(unit, captured.Id, captured.Name));
            }
        }

        void PickerRow(RectTransform parent, string label, bool current, UnityEngine.Events.UnityAction action)
        {
            var btn = UIFactory.CreateButton(parent, label, action,
                current ? UiTheme.AccentWash : UiTheme.Surface,
                current ? UiTheme.Accent : UiTheme.Text, 12);
            ((RectTransform)btn.transform).sizeDelta = new Vector2(0, 30);
            var text = btn.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(10, 0);
            UIFactory.Fit(text, 8);
        }

        void AssignUnit(UnitActor unit, string groupId, string groupName)
        {
            if (unit == null) return;

            unit.State.groupId = groupId;
            unit.State.groupName = groupName;

            string name = string.IsNullOrEmpty(unit.State.customName) ? unit.Def.name : unit.State.customName;
            Flash?.Invoke(string.IsNullOrEmpty(groupId)
                ? $"{name} removed from its group."
                : $"{name} moved into '{groupName}'.");

            ClosePicker();
            SetSelection(_currentSelection);
        }

        void ClosePicker()
        {
            if (_picker == null) return;
            _picker.SetParent(null, false);
            Destroy(_picker.gameObject);
            _picker = null;
        }
    }
}
