using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Right-side panel shown instead of <see cref="UnitInfoPanel"/> whenever
    /// 2+ units are selected: name the current selection as a group, and
    /// recall any existing group by clicking its name (re-selects its members).
    /// </summary>
    public class GroupPanelUI : MonoBehaviour
    {
        public System.Action<List<UnitActor>> SelectGroupRequested;
        public System.Action<string> Flash;
        /// <summary>Delete a unit from the map (raised by the ✕ on a selected-unit row).</summary>
        public System.Action<UnitActor> RemoveUnitRequested;

        RectTransform _panel;
        Text _title;
        InputField _nameInput;
        RectTransform _listContent;      // existing groups
        RectTransform _unitsContent;     // units in the current selection
        IReadOnlyList<UnitActor> _currentSelection = new List<UnitActor>();

        public void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "GroupPanel", UiTheme.Panel);
            _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-300, 0);
            _panel.offsetMax = new Vector2(0, -UiTheme.TopBarHeight);

            _title = UIFactory.CreateText(_panel, "0 UNITS SELECTED", 18,
                UiTheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -24), new Vector2(280, 32));

            var hint = UIFactory.CreateText(_panel, "Name this selection as a group:", 13, UiTheme.TextDim, TextAnchor.MiddleLeft);
            UIFactory.Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -56), new Vector2(264, 22));

            _nameInput = UIFactory.CreateInputField(_panel, "Group name…", 16);
            UIFactory.Place((RectTransform)_nameInput.transform, new Vector2(0.5f, 1f), new Vector2(0, -84), new Vector2(264, 36));

            var create = UIFactory.CreateButton(_panel, "CREATE GROUP", CreateGroup,
                UiTheme.Accent, new Color(0.1f, 0.1f, 0.1f), 16);
            UIFactory.Place((RectTransform)create.transform, new Vector2(0.5f, 1f), new Vector2(0, -128), new Vector2(264, 40));

            // ---- units in this selection ----
            var unitsLabel = UIFactory.CreateText(_panel, "SELECTED UNITS", 14, UiTheme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Place(unitsLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -160), new Vector2(264, 24));

            var unitsScroll = UIFactory.CreateScrollView(_panel, out _unitsContent);
            var urt = (RectTransform)unitsScroll.transform;
            // Splits the panel's remaining height with the groups list below.
            urt.anchorMin = new Vector2(0, 0.42f); urt.anchorMax = new Vector2(1, 1);
            urt.offsetMin = new Vector2(10, 4);
            urt.offsetMax = new Vector2(-10, -184);
            TightenRows(_unitsContent);

            var removeAll = UIFactory.CreateButton(_panel, "REMOVE ALL SELECTED", RemoveAllSelected,
                new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, 14);
            var rart = (RectTransform)removeAll.transform;
            rart.anchorMin = new Vector2(0, 0.42f); rart.anchorMax = new Vector2(1, 0.42f);
            rart.pivot = new Vector2(0.5f, 1f);
            rart.sizeDelta = new Vector2(-20, 32);
            rart.anchoredPosition = new Vector2(0, -6);

            // ---- saved groups ----
            var groupsLabel = UIFactory.CreateText(_panel, "EXISTING GROUPS", 14, UiTheme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            var glrt = groupsLabel.rectTransform;
            glrt.anchorMin = new Vector2(0, 0.42f); glrt.anchorMax = new Vector2(1, 0.42f);
            glrt.pivot = new Vector2(0.5f, 1f);
            glrt.sizeDelta = new Vector2(-36, 24);
            glrt.anchoredPosition = new Vector2(0, -42);

            var scroll = UIFactory.CreateScrollView(_panel, out _listContent);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 0.42f);
            srt.offsetMin = new Vector2(10, 10);
            srt.offsetMax = new Vector2(-10, -70);
            TightenRows(_listContent);

            Hide();
        }

        public void SetSelection(IReadOnlyList<UnitActor> selection)
        {
            _currentSelection = selection;
            if (selection.Count < 2) { Hide(); return; }
            _panel.gameObject.SetActive(true);
            _title.text = $"{selection.Count} UNITS SELECTED";
            RefreshUnitList();
            RefreshGroupList();
        }

        /// <summary>Row per selected unit: icon, name, and a ✕ that deletes it from the map.</summary>
        void RefreshUnitList()
        {
            for (int i = _unitsContent.childCount - 1; i >= 0; i--)
                Destroy(_unitsContent.GetChild(i).gameObject);

            foreach (var u in _currentSelection)
            {
                if (u == null || !u.IsAlive) continue;

                var row = UIFactory.CreatePanel(_unitsContent, "Unit_" + u.State.instanceId, UiTheme.Surface);
                row.sizeDelta = new Vector2(0, 34);

                string folder = u.State.TeamEnum == Team.User ? "Friendly" : "Enemy";
                var sprite = UIFactory.LoadIconSprite(folder, u.Def.id);
                if (sprite != null)
                {
                    var icon = UIFactory.CreateImage(row, sprite, "Icon");
                    UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(4, 0), new Vector2(28, 28));
                    ((RectTransform)icon.transform).pivot = new Vector2(0, 0.5f);
                }

                string name = string.IsNullOrEmpty(u.State.customName) ? u.Def.name : u.State.customName;
                var lbl = UIFactory.CreateText(row, $"{name}  ({u.State.echelon})", 12,
                    UiTheme.Text, TextAnchor.MiddleLeft);
                var lr = lbl.rectTransform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(36, 0);
                lr.offsetMax = new Vector2(-34, 0);

                // Captured per row — the list is rebuilt whenever the selection
                // changes, so the reference can't go stale behind the button.
                var target = u;
                var del = UIFactory.CreateButton(row, "✕", () => RemoveUnit(target),
                    new Color(0.55f, 0.18f, 0.18f), UiTheme.Text, 13);
                UIFactory.Place((RectTransform)del.transform, new Vector2(1f, 0.5f), new Vector2(-4, 0), new Vector2(26, 26));
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

        public void Hide()
        {
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

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
            RefreshGroupList();
        }

        void RefreshGroupList()
        {
            foreach (Transform c in _listContent) Destroy(c.gameObject);
            var seen = new HashSet<string>();
            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || string.IsNullOrEmpty(u.State.groupId)) continue;
                if (!seen.Add(u.State.groupId)) continue;
                CreateGroupRow(u.State.groupId, u.State.groupName);
            }
        }

        void CreateGroupRow(string groupId, string groupName)
        {
            var row = UIFactory.CreateButton(_listContent, groupName, () => SelectGroup(groupId),
                UiTheme.Surface, UiTheme.Text, 15);
            ((RectTransform)row.transform).sizeDelta = new Vector2(0, 40);
        }

        void SelectGroup(string groupId)
        {
            var members = new List<UnitActor>();
            foreach (var u in UnitRegistry.All)
                if (u != null && u.IsAlive && u.State.groupId == groupId) members.Add(u);

            if (members.Count == 0)
            {
                Flash?.Invoke("That group has no units left.");
                RefreshGroupList();
                return;
            }
            SelectGroupRequested?.Invoke(members);
        }
    }
}
