using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Save;

namespace IronMeridian.UI
{
    /// <summary>
    /// A label/value list for any data record, which can be switched from
    /// reading to editing without being rebuilt from a different set of rows.
    ///
    /// The rows come from <see cref="TunableField"/>, so this panel works
    /// unchanged for a <see cref="UnitDefinition"/>, an artillery nature, an
    /// airframe, a UAV, a missile system or a naval gun — and gains any field
    /// those records gain. That is the whole reason it is generic: six
    /// hand-written panels would be six things to remember to update.
    ///
    /// Numbers and text are edited in a field; flags and enumerations in a
    /// **cycle button**, because these rows live inside a scroll view and a uGUI
    /// Dropdown's popup template has to be clipped by and scrolled with its
    /// viewport, which it does badly.
    ///
    /// Edited values are written straight into the live record — the catalogue
    /// the game reads — and recorded in <see cref="TuningStore"/>. Nothing
    /// reaches disk until the screen's SAVE is pressed.
    /// </summary>
    public class StatEditorPanel
    {
        const float RowHeight = 28f;
        const float SectionHeight = 30f;
        /// <summary>Where the label column ends and the value column begins.</summary>
        const float Split = 0.52f;

        readonly RectTransform _content;

        string _catalog = "", _id = "";
        object _record;
        readonly HashSet<string> _readOnly = new HashSet<string>();
        bool _editing;

        /// <summary>Raised after any successful write, so the host can repaint its table row.</summary>
        public System.Action<TunableField> FieldChanged;

        /// <summary>What the panel is currently showing. Null when nothing is selected.</summary>
        public object Record => _record;

        public StatEditorPanel(RectTransform content) => _content = content;

        public bool Editing => _editing;

        public void SetEditing(bool on)
        {
            if (_editing == on) return;
            _editing = on;
            Rebuild();
        }

        /// <summary>Points the panel at a record. Rebuilds even for the same one — the values may have moved.</summary>
        public void Show(string catalog, string id, object record, IEnumerable<string> readOnlyFields)
        {
            _catalog = catalog ?? "";
            _id = id ?? "";
            _record = record;

            _readOnly.Clear();
            if (readOnlyFields != null)
                foreach (var f in readOnlyFields) _readOnly.Add(f);

            Rebuild();
        }

        /// <summary>
        /// Empties the list now rather than at end of frame. `Destroy` is
        /// deferred, so the layout group would spend this frame measuring the
        /// old rows alongside the new ones and the panel would visibly jump on
        /// every selection.
        /// </summary>
        static void ClearChildren(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                child.SetParent(null, false);
                Object.Destroy(child.gameObject);
            }
        }

        public void Rebuild()
        {
            if (_content == null) return;
            ClearChildren(_content);

            if (_record == null)
            {
                var empty = UIFactory.CreateText(_content, "Nothing selected.", 15,
                    GameConfig.UiTextDim, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 40);
                return;
            }

            Section(_editing ? "EDITING — CHANGES APPLY AT ONCE" : "VALUES");

            foreach (var field in TunableField.Of(_record))
                Row(field);
        }

        void Section(string label)
        {
            var t = UIFactory.CreateText(_content, label, 13, GameConfig.UiAccent,
                TextAnchor.LowerLeft, FontStyle.Bold);
            ((RectTransform)t.transform).sizeDelta = new Vector2(0, SectionHeight);
        }

        void Row(TunableField field)
        {
            var row = UIFactory.CreateGroup(_content, "Field_" + field.Name);
            row.sizeDelta = new Vector2(0, RowHeight);

            bool overridden = TuningStore.IsOverridden(_catalog, _id, field.Name);
            bool locked = _readOnly.Contains(field.Name);

            // A leading bullet, not a colour alone: "changed from shipped" has to
            // survive being read on a screen where the accent is already doing
            // several other jobs.
            var label = UIFactory.CreateText(row, (overridden ? "• " : "") + field.Label, 14,
                overridden ? GameConfig.UiAccent : GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = new Vector2(0, 0);
            label.rectTransform.anchorMax = new Vector2(Split, 1);
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = new Vector2(-6, 0);
            UIFactory.Fit(label, 10);

            if (!_editing || locked) { ReadOnlyValue(row, field, locked); return; }

            switch (field.Kind)
            {
                case TunableKind.Flag:
                case TunableKind.Choice:
                    CycleValue(row, field);
                    break;
                default:
                    TextValue(row, field);
                    break;
            }
        }

        void ReadOnlyValue(RectTransform row, TunableField field, bool locked)
        {
            string text = field.Read(_record);
            if (string.IsNullOrEmpty(text)) text = "—";

            var v = UIFactory.CreateText(row, text, 14,
                locked ? GameConfig.UiTextDim : GameConfig.UiText, TextAnchor.MiddleRight);
            StretchValue(v.rectTransform, -4f);
            UIFactory.Fit(v, 10);
        }

        void TextValue(RectTransform row, TunableField field)
        {
            var input = UIFactory.CreateInputField(row, "—", 14);
            var rt = (RectTransform)input.transform;
            StretchValue(rt, -2f);
            rt.offsetMin = new Vector2(rt.offsetMin.x, 2f);
            rt.offsetMax = new Vector2(rt.offsetMax.x, -2f);
            input.GetComponent<Image>().color = UiTheme.Surface;
            input.text = field.Read(_record);
            input.textComponent.alignment = TextAnchor.MiddleRight;

            // End-edit rather than value-changed: a half-typed number is not an
            // instruction, and re-recording an override on every keystroke would
            // write "1", "1.", "1.5" into the tuning file in turn.
            input.onEndEdit.AddListener(raw =>
            {
                if (!field.Write(_record, raw))
                {
                    input.text = field.Read(_record);   // put the old value back
                    return;
                }
                Commit(field);
            });
        }

        void CycleValue(RectTransform row, TunableField field)
        {
            Button button = null;
            button = UIFactory.CreateButton(row, field.Read(_record), () =>
            {
                string next = field.Cycle(_record, 1);
                if (!field.Write(_record, next)) return;
                var caption = button.GetComponentInChildren<Text>();
                if (caption != null) caption.text = field.Read(_record);
                Commit(field);
            }, UiTheme.Surface, GameConfig.UiText, 13);

            var rt = (RectTransform)button.transform;
            StretchValue(rt, -2f);
            rt.offsetMin = new Vector2(rt.offsetMin.x, 2f);
            rt.offsetMax = new Vector2(rt.offsetMax.x, -2f);
            UIFactory.Fit(button.GetComponentInChildren<Text>(), 9);
        }

        static void StretchValue(RectTransform rt, float rightInset)
        {
            rt.anchorMin = new Vector2(Split, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(rightInset, 0);
        }

        /// <summary>
        /// Records the change and repaints, so a value typed back to its shipped
        /// figure loses its bullet immediately rather than at the next selection.
        /// </summary>
        void Commit(TunableField field)
        {
            TuningStore.Record(_catalog, _id, _record, field);
            if (_record is UnitDefinition unit) unit.RefreshDerived();
            FieldChanged?.Invoke(field);
            Rebuild();
        }
    }
}
