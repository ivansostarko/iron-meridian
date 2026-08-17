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
    ///  • **Makes the current selection a group**, so it can be recalled later.
    ///    The group names itself — see <see cref="NextDesignation"/>.
    ///  • **Lists what is selected**, with each formation's current group beside
    ///    it and a way to move it to another one.
    ///  • **Lists the existing groups** — click one to select its members,
    ///    double-click to fly the camera to them.
    ///
    /// **It does not give orders.** It used to carry a MOVE / ATTACK / DEFEND
    /// bar of its own, which was a mockup that did nothing, in a different
    /// place and a different shape from the order bar every other order is
    /// given on. Those orders now live where a formation's orders live — the
    /// bar at the foot of the map (<see cref="UnitActionBarUI"/>) — captioned
    /// with the group's name and carried out by every formation in it. One
    /// place, one vocabulary, and a group can be given all six orders instead
    /// of three.
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
        /// <summary>
        /// Raised whenever this panel changes what the groups are or what they
        /// are called. The order bar is captioned with the group's name, and it
        /// is not on this panel, so it has to be told — a rename that left the
        /// bar reading the old name until the next click would be two places
        /// disagreeing about one group.
        /// </summary>
        public System.Action GroupsChanged;

        // ------------------------------------------------------------ layout

        /// <summary>Shared with the other right-hand docks — see <see cref="UiTheme.RightPanelWidth"/>.</summary>
        const float PanelWidth = UiTheme.RightPanelWidth;
        /// <summary>Content width — the panel less an equal margin either side.</summary>
        const float Inner = PanelWidth - 36f;

        /// <summary>
        /// Left and right inset of every row in both of this panel's lists,
        /// applied as the scroll content's own layout padding so the rows are
        /// genuinely narrower rather than merely drawn inboard.
        ///
        /// The lists used to run edge to edge inside their viewports, which put
        /// an APP-6 frame's left stroke on the clipping boundary and made both
        /// of them read as pressed against the side of the screen rather than as
        /// tables inside a panel.
        /// </summary>
        const float RowPad = 25f;

        /// <summary>
        /// Width a row actually gets: the panel, less the scroll views' shared
        /// 10 px margins, less <see cref="RowPad"/> either side. Everything
        /// placed against a row's right-hand end is measured from this — a row
        /// that shrank while its contents did not would put the buttons through
        /// the captions.
        /// </summary>
        const float RowWidth = PanelWidth - 20f - RowPad * 2f;

        /// <summary>
        /// Left inset of a selected-unit row's contents — the icon and the
        /// caption beside it. Small, because <see cref="RowPad"/> is what now
        /// holds the list off the viewport edge; the two stacking would have
        /// spent 55 px of a 330 px panel on empty gutter.
        /// </summary>
        const float UnitRowInset = 6f;

        /// <summary>Vertical gap above the SELECTED UNITS heading.</summary>
        const float UnitsLabelMargin = 10f;

        // Distances from the panel's top edge to the top of each block. Fixed
        // now that the orders bar has gone to the foot of the map — nothing in
        // the upper half appears or disappears any more, so nothing shifts.
        const float TitleTop = 24f, TitleHeight = 32f;
        /// <summary>Which group this is, and where its orders are given.</summary>
        const float ScopeTop = 56f, ScopeHeight = 16f;
        const float ScopeNoteTop = 72f, ScopeNoteHeight = 14f;

        const float ButtonsTop = 96f, ButtonsHeight = 40f;
        /// <summary>Heading sits a clear <see cref="UnitsLabelMargin"/> below the button row.</summary>
        const float UnitsLabelTop = ButtonsTop + ButtonsHeight + UnitsLabelMargin;
        const float UnitsLabelHeight = 24f;
        const float UnitsScrollTop = UnitsLabelTop + UnitsLabelHeight + 4f;

        /// <summary>
        /// Button row: GROUP SELECTION · UNGROUP, meeting exactly at
        /// <see cref="Inner"/>. GROUP SELECTION takes whatever the panel's width
        /// leaves, so the two always fill the row rather than ending short of it.
        /// </summary>
        const float UngroupWidth = 94f, ButtonGap = 4f;
        const float CreateWidth = Inner - ButtonGap - UngroupWidth;

        /// <summary>
        /// Where the selected-units list stops and the saved-groups list begins,
        /// as a fraction of panel height.
        /// </summary>
        const float SplitFraction = 0.38f;

        const float UnitRowHeight = 34f;
        const float GroupRowHeight = 44f;

        // ------------------------------------------------------------- state

        RectTransform _panel;
        Text _title;
        RectTransform _listContent;      // existing groups
        RectTransform _unitsContent;     // units in the current selection
        RectTransform _unitsScroll;
        Text _scope, _scopeNote;
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

            // Which group this selection is, and — because the buttons that
            // used to be here have moved — where its orders are now given.
            _scope = UIFactory.CreateText(_panel, "", 12, UiTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(_scope.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0, -ScopeTop), new Vector2(Inner, ScopeHeight));
            UIFactory.Fit(_scope, 9);

            _scopeNote = UIFactory.CreateText(_panel,
                "Orders are on the bar below the map.", 10,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.Place(_scopeNote.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0, -ScopeNoteTop), new Vector2(Inner, ScopeNoteHeight));
            UIFactory.Fit(_scopeNote, 8);

            // GROUP SELECTION · UNGROUP. Two buttons, no text field: a group is
            // made and unmade here, and it names itself — see NextDesignation.
            float x = -Inner * 0.5f;
            var create = UIFactory.CreateButton(_panel, "GROUP SELECTION", CreateGroup,
                UiTheme.Accent, new Color(0.1f, 0.1f, 0.1f), 14);
            UIFactory.Place((RectTransform)create.transform, new Vector2(0.5f, 1f),
                new Vector2(x + CreateWidth * 0.5f, -ButtonsTop), new Vector2(CreateWidth, ButtonsHeight));
            UIFactory.Fit(create.GetComponentInChildren<Text>(), 9);
            x += CreateWidth + ButtonGap;

            var ungroup = UIFactory.CreateButton(_panel, "UNGROUP", UngroupSelection,
                UiTheme.Surface, UiTheme.TextDim, 13);
            UIFactory.Place((RectTransform)ungroup.transform, new Vector2(0.5f, 1f),
                new Vector2(x + UngroupWidth * 0.5f, -ButtonsTop), new Vector2(UngroupWidth, ButtonsHeight));
            UIFactory.Fit(ungroup.GetComponentInChildren<Text>(), 9);

            // ---- units in this selection ----
            var unitsLabel = UIFactory.CreateText(_panel, "SELECTED UNITS", 14, UiTheme.Accent,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(unitsLabel.rectTransform, new Vector2(0.5f, 1f),
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

        // ------------------------------------------------------------ naming

        /// <summary>
        /// The next free group designation — GROUP 1, GROUP 2, and so on.
        ///
        /// **A group names itself.** The panel used to carry a text field and a
        /// RENAME button beside CREATE, which is three controls and a decision
        /// for something that exists to be *pointed at*: what a player needs of
        /// a group is a handle short enough to read on a row and on the order
        /// bar, and any handle will do so long as it is unique and stable. So
        /// the panel assigns one and gives the space back to the lists, which
        /// are what the player is actually reading.
        ///
        /// The lowest unused number rather than a running count, so deleting
        /// GROUP 2 and making another gets GROUP 2 back instead of leaving a
        /// hole and climbing forever.
        /// </summary>
        static string NextDesignation()
        {
            var taken = new HashSet<string>();
            foreach (var u in UnitRegistry.All)
                if (u != null && !string.IsNullOrEmpty(u.State.groupName))
                    taken.Add(u.State.groupName);

            for (int n = 1; n < 1000; n++)
            {
                string candidate = "GROUP " + n;
                if (!taken.Contains(candidate)) return candidate;
            }
            return "GROUP";
        }

        /// <summary>
        /// Repaints the header block: which group this selection is, or that it
        /// is not one yet.
        /// </summary>
        void RefreshNaming()
        {
            string groupName = SharedGroupName();
            bool grouped = groupName != null;

            _scope.text = grouped
                ? UiTheme.Spaced("GROUP · " + groupName)
                : UiTheme.Spaced("NOT A GROUP YET");
            _scope.color = grouped ? UiTheme.Accent : UiTheme.TextDim;

            _scopeNote.text = grouped
                ? "Orders for this group are on the bar below the map."
                : "GROUP SELECTION makes these one, so they can be recalled together.";
        }

        // --------------------------------------------------------- selection

        public void SetSelection(IReadOnlyList<UnitActor> selection)
        {
            _currentSelection = selection;
            if (selection.Count < 2) { Hide(); return; }

            ClosePicker();
            _panel.gameObject.SetActive(true);
            _title.text = $"{selection.Count} UNITS SELECTED";

            RefreshNaming();
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

        /// <summary>
        /// Repaints this panel for a membership or naming change, and tells
        /// whoever else is showing the group's name.
        /// </summary>
        void Regrouped()
        {
            SetSelection(_currentSelection);
            GroupsChanged?.Invoke();
        }

        static void TightenRows(RectTransform content)
        {
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 3;
            layout.padding = new RectOffset((int)RowPad, (int)RowPad, 6, 6);
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
            string name = NextDesignation();
            string id = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            int count = 0;
            foreach (var u in _currentSelection)
            {
                if (u == null) continue;
                u.State.groupId = id;
                u.State.groupName = name;
                count++;
            }

            Flash?.Invoke($"{name} formed — {count} formation(s). Its orders are on the bar below the map.");
            Regrouped();
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
            Regrouped();
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
            UIFactory.PlaceTopLeft(name.rectTransform, 10f, 6f, RowWidth - 60f, 17f);
            UIFactory.Fit(name, 9);

            var detail = UIFactory.CreateText(row, $"{group.Count} formation(s)", 11,
                UiTheme.TextFaint, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(detail.rectTransform, 10f, 23f, RowWidth - 60f, 15f);

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

            Regrouped();
        }

        // ------------------------------------------------------- unit picker

        /// <summary>
        /// The per-formation destination chooser, opened by the ⇄ on a row.
        ///
        /// A mini-modal inside the panel rather than a popup beside the row: the
        /// row lives inside a scroll viewport, so anchoring to it would mean
        /// converting between the viewport's space and the panel's and then
        /// keeping the popup on screen as the list scrolled underneath it. The
        /// panel is narrow enough that covering it *is* the
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
            Regrouped();
        }

        void ClosePicker()
        {
            if (_picker == null) return;
            _picker.SetParent(null, false);
            Destroy(_picker.gameObject);
            _picker = null;
        }

        /// <summary>
        /// Moves the panel's top edge, so it can clear whatever is docked above
        /// it on this edge — the fire-menu cluster always, and the minimap too
        /// once a battle starts. One caller decides for all of them; see
        /// <c>GameController.RefreshRightDockTop</c>.
        /// </summary>
        public void SetTopInset(float pixels)
        {
            if (_panel != null) _panel.offsetMax = new Vector2(0, -pixels);
        }
    }
}
