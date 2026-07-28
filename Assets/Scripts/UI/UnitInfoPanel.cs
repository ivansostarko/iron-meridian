using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Right-side panel showing full data for the clicked unit: identity,
    /// echelon, manpower, training, morale, combat power, ammunition, fuel,
    /// food and current status — as an icon + label + value table, grouped
    /// into sections, rather than a single text blob.
    /// </summary>
    public class UnitInfoPanel : MonoBehaviour
    {
        public System.Action<UnitActor> RemoveRequested;

        const float PanelWidth = 330f;
        /// <summary>Space above the table: close button, icon and title.</summary>
        const float TopBlockHeight = 172f;
        /// <summary>Space below the table: heading row and the remove button.</summary>
        const float BottomBlockHeight = 102f;
        const float RowHeight = 28f;
        /// <summary>Fixed width of a row's value cell, pinned to the row's right edge.</summary>
        const float ValueWidth = 132f;

        RectTransform _panel;
        Image _icon;
        Text _title, _headingLabel;
        RectTransform _tableContent;
        UnitActor _current;

        // Value labels by row name, so the periodic refresh can rewrite values
        // without tearing the whole table down. _builtFor is the unit the
        // current rows belong to.
        readonly System.Collections.Generic.Dictionary<string, Text> _values =
            new System.Collections.Generic.Dictionary<string, Text>();
        UnitActor _builtFor;
        bool _rebuilding;

        public void Build(Canvas canvas)
        {
            _panel = UIFactory.CreatePanel(canvas.transform, "UnitInfoPanel", GameConfig.UiPanel);
            _panel.anchorMin = new Vector2(1, 0); _panel.anchorMax = new Vector2(1, 1);
            _panel.pivot = new Vector2(1, 0.5f);
            _panel.offsetMin = new Vector2(-PanelWidth, 0);
            _panel.offsetMax = new Vector2(0, -50);

            var close = UIFactory.CreateButton(_panel, "✕", Hide, GameConfig.UiPanelLight, null, 15);
            UIFactory.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(-6, -6), new Vector2(30, 30));

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(_panel, false);
            _icon = iconGo.GetComponent<Image>();
            _icon.preserveAspect = true;
            UIFactory.Place((RectTransform)iconGo.transform, new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(76, 76));

            _title = UIFactory.CreateText(_panel, "", 17, GameConfig.UiText, TextAnchor.UpperCenter, FontStyle.Bold);
            UIFactory.Place(_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -126), new Vector2(PanelWidth - 24, 40));

            // ---- stat table ----
            // Top clears the icon + title block; bottom clears the heading row
            // and the remove button pinned to the panel floor.
            var scroll = UIFactory.CreateScrollView(_panel, out _tableContent);
            var srt = (RectTransform)scroll.transform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.offsetMin = new Vector2(10, BottomBlockHeight);
            srt.offsetMax = new Vector2(-10, -TopBlockHeight);

            // The shared scroll-view defaults are tuned for the unit palette's
            // big cards; a dense stat table needs far tighter spacing or the
            // rows push most of the content below the fold.
            var layout = _tableContent.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.padding = new RectOffset(4, 4, 4, 6);

            // ---- facing/heading rotate row ----
            var rotLeft = UIFactory.CreateButton(_panel, "◄", () => Rotate(-15f), GameConfig.UiPanelLight, GameConfig.UiText, 18);
            UIFactory.Place((RectTransform)rotLeft.transform, new Vector2(0f, 0f), new Vector2(12, 60), new Vector2(44, 34));

            _headingLabel = UIFactory.CreateText(_panel, "Heading 0°", 14, GameConfig.UiTextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(_headingLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 60), new Vector2(PanelWidth - 130, 34));

            var rotRight = UIFactory.CreateButton(_panel, "►", () => Rotate(15f), GameConfig.UiPanelLight, GameConfig.UiText, 18);
            UIFactory.Place((RectTransform)rotRight.transform, new Vector2(1f, 0f), new Vector2(-12, 60), new Vector2(44, 34));

            // ---- remove ----
            var remove = UIFactory.CreateButton(_panel, "REMOVE UNIT", RequestRemove,
                new Color(0.55f, 0.18f, 0.18f), GameConfig.UiText, 16);
            UIFactory.Place((RectTransform)remove.transform, new Vector2(0.5f, 0f), new Vector2(0, 12), new Vector2(PanelWidth - 24, 40));

            Hide();
        }

        public void Show(UnitActor unit)
        {
            if (_panel == null) return;          // build failed; don't take the scene down with it
            _current = unit;
            if (unit == null) { Hide(); return; }
            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();           // above GroupPanel, which shares this rect
            string folder = unit.State.TeamEnum == Team.User ? "Friendly" : "Enemy";
            _icon.sprite = UIFactory.LoadIconSprite(folder, unit.Def.id);
            Refresh();
        }

        public void Hide()
        {
            _current = null;
            if (_panel != null) _panel.gameObject.SetActive(false);
        }

        void Update()
        {
            // Stop refreshing a unit that died or was removed while shown —
            // Refresh() would dereference a destroyed actor.
            if (_current != null && !_current.IsAlive) { Hide(); return; }
            if (_current != null && Time.frameCount % 30 == 0) Refresh();
        }

        void Rotate(float delta)
        {
            if (_current == null) return;
            _current.SetHeading(_current.State.headingDeg + delta);
            _headingLabel.text = $"Heading {_current.State.headingDeg:0}°";
        }

        void RequestRemove()
        {
            if (_current == null) return;
            RemoveRequested?.Invoke(_current);
        }

        // ------------------------------------------------------- table
        void Refresh()
        {
            if (_current == null || _tableContent == null) return;

            // Rows only need rebuilding when a different unit is shown; the
            // periodic refresh just rewrites the value labels in place.
            _rebuilding = _builtFor != _current;
            if (_rebuilding)
            {
                // Unparent before Destroy: destruction is deferred to end of
                // frame, so the old rows would otherwise sit in the layout
                // alongside the new ones for a frame. Walk backwards by index —
                // reparenting mutates the child list a foreach is iterating.
                for (int i = _tableContent.childCount - 1; i >= 0; i--)
                {
                    var c = _tableContent.GetChild(i);
                    c.SetParent(null, false);
                    Destroy(c.gameObject);
                }
                _values.Clear();
                _builtFor = _current;
            }

            var s = _current.State;
            var d = _current.Def;
            int manpower = Mathf.RoundToInt(d.manpower *
                EchelonInfo.ManpowerMultiplier(s.EchelonEnum) * Mathf.Clamp01(s.strength));

            _title.text = string.IsNullOrEmpty(s.customName) ? d.name : s.customName;
            _headingLabel.text = $"Heading {s.headingDeg:0}°";

            SectionHeader("IDENTITY");
            Row("type", "Category", d.Category == UnitCategory.Drone ? "Drone" : "Core Ground");
            Row("team", "Team", s.TeamEnum == Team.User ? "User (Blue)" : "Enemy (Red)");
            Row("affiliation", "Affiliation", s.affiliation);
            Row("echelon", "Echelon", $"{s.echelon} {EchelonInfo.Indicator(s.EchelonEnum)}");
            Row("status", "Status", s.status);

            SectionHeader("STRENGTH");
            Row("strength", "Strength", $"{s.strength * 100f:0}%");
            Row("manpower", "Manpower", $"{manpower:n0}");
            Row("training", "Training", $"{d.training:0}/100");
            Row("morale", "Morale", $"{s.morale:0}/100");
            Row("organisation", "Organisation", $"{s.organisation:0}/100");
            Row("power", "Combat power", $"{_current.CurrentPower():n0}");

            SectionHeader("COMBAT");
            Row("attack", "Attack", $"{d.attack:0}");
            Row("hardattack", "Hard attack", $"{d.hardAttack:0}");
            Row("defence", "Defence", $"{d.defence:0}");
            Row("armour", "Armour", $"{d.armour:0}");
            Row("antiair", "Anti-air", $"{d.antiAir:0}");
            Row("weaponrange", "Weapon range", $"{d.weaponRangeKm:0.#} km");
            Row("viewrange", "View range", $"{d.viewRangeKm:0.#} km");
            Row("speed", "Speed", $"{d.speedKmh:0} km/h");

            SectionHeader("SUSTAINMENT");
            Row("ammo", "Ammo", $"{s.ammo:n0} / {d.ammoStock:n0}");
            Row("fuel", "Fuel", $"{s.fuel:n0} / {d.fuelStock:n0} L");
            Row("food", "Food", $"{s.foodDays} days");

            SectionHeader("POSITION");
            Row("position", "Latitude", $"{s.latitude:0.0000}°N");
            Row("position", "Longitude", $"{s.longitude:0.0000}°E");
        }

        void SectionHeader(string label)
        {
            if (!_rebuilding) return;
            var h = UIFactory.CreateText(_tableContent, label, 12, GameConfig.UiAccent, TextAnchor.LowerLeft, FontStyle.Bold);
            h.rectTransform.sizeDelta = new Vector2(0, 24);
        }

        void Row(string iconName, string label, string value)
        {
            if (!_rebuilding)
            {
                if (_values.TryGetValue(label, out var existing) && existing != null) existing.text = value;
                return;
            }

            var row = UIFactory.CreatePanel(_tableContent, "Row_" + label, new Color(1, 1, 1, 0.03f));
            row.sizeDelta = new Vector2(0, RowHeight);

            var sprite = UIFactory.LoadIconSprite("Stats", iconName);
            if (sprite != null)
            {
                var icon = UIFactory.CreateImage(row, sprite, "Icon");
                UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(5, 0), new Vector2(18, 18));
                ((RectTransform)icon.transform).pivot = new Vector2(0, 0.5f);
            }

            // Both cells stretch with the row and are inset in pixels, so they
            // track whatever width the layout group hands the row. Fractional
            // anchors plus HorizontalWrapMode.Overflow let the text render
            // outside the row entirely — labels spilled off the panel's left
            // edge and values off its right.
            var lbl = UIFactory.CreateText(row, label, 12, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            var lr = lbl.rectTransform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(27, 0);
            lr.offsetMax = new Vector2(-(ValueWidth + 6), 0);

            // Pinned to the row's right edge with a fixed width, so the value
            // can never push past the panel no matter how long the label is.
            var val = UIFactory.CreateText(row, value, 12, GameConfig.UiText, TextAnchor.MiddleRight, FontStyle.Bold);
            var vr = val.rectTransform;
            vr.anchorMin = new Vector2(1f, 0f); vr.anchorMax = new Vector2(1f, 1f);
            vr.pivot = new Vector2(1f, 0.5f);
            vr.sizeDelta = new Vector2(ValueWidth, 0);
            vr.anchoredPosition = new Vector2(-6, 0);

            _values[label] = val;
        }
    }
}
