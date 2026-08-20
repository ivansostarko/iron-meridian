using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Save;

namespace IronMeridian.UI
{
    /// <summary>
    /// The save browser: what you have saved, where it is, and the two things
    /// you can do with it.
    ///
    /// **What it replaced.** SAVE and LOAD on the pause menu wrote and read one
    /// file with no name, no list and no confirmation — the status line said
    /// "Game saved." whether or not anything had been. You could not keep the
    /// position before an attack and the position after it, could not tell what
    /// a save was of without loading it, and could not tell a failed save from a
    /// successful one. This is a list with names and dates, a confirm before an
    /// overwrite, and a status line that reports what actually happened.
    ///
    /// **Two destinations, one list.** LOCAL and CLOUD are tabs over the same
    /// list, because they hold the same kind of thing and the question "where is
    /// this save" should not change what a save *is*. The cloud tab is a
    /// **preview** and is labelled as one everywhere it appears — see
    /// <see cref="SaveSlots"/>'s remarks. It is wired end to end against a
    /// stand-in store so the flow is real; nothing on it claims a file has left
    /// the machine.
    ///
    /// **Save and load are the same screen in two modes**, not two screens. They
    /// ask the same question — which of these — and the only difference is
    /// whether a new name is allowed. Two dialogs would be two lists to keep
    /// looking the same.
    ///
    /// Follows the pop-up shape the rest of this interface uses: a static
    /// <see cref="Open"/>, an <see cref="IsOpen"/> flag the map's input guards
    /// read, its own sorting layer, and a backdrop that swallows the click that
    /// dismisses it. Runs on **unscaled time** and does not touch
    /// <c>Time.timeScale</c> — the pause menu owns that, and a dialog that
    /// resumed the game when it closed would resume it under the pause menu.
    ///
    /// See docs/45-SAVE-AND-LOAD.md.
    /// </summary>
    public class SaveLoadDialog : MonoBehaviour
    {
        public enum Mode { Save, Load }

        /// <summary>True while the browser is up, so the map's own input can stand down.</summary>
        public static bool IsOpen { get; private set; }

        static SaveLoadDialog _active;

        // ------------------------------------------------------------ layout
        const float BoxWidth = 720f, BoxHeight = 620f;
        const float Pad = 22f;
        const float HeaderHeight = 70f;
        const float TabHeight = 34f;
        const float RowHeight = 54f;
        const float FooterHeight = 118f;

        Mode _mode;
        SaveDestination _destination = SaveDestination.Local;

        /// <summary>Writes the current game into <paramref name="save"/>; returns false to refuse.</summary>
        System.Func<GameSave, bool> _onSave;
        /// <summary>Applies a chosen save; returns false to refuse.</summary>
        System.Func<GameSave, bool> _onLoad;

        RectTransform _listContent;
        InputField _nameField;
        Text _status, _destinationNote, _title;
        Button _commit;
        Text _commitLabel;
        readonly List<(string slot, RectTransform frame, Image fill)> _rows =
            new List<(string, RectTransform, Image)>();
        readonly Dictionary<SaveDestination, (RectTransform underline, Text caption)> _tabs =
            new Dictionary<SaveDestination, (RectTransform, Text)>();

        string _selected = "";
        int _openedFrame;

        /// <summary>
        /// Opens the browser. <paramref name="onSave"/> is asked to fill a
        /// <see cref="GameSave"/>; <paramref name="onLoad"/> is handed one to
        /// apply. Either may be null, in which case that mode is unavailable.
        /// </summary>
        public static void Open(Canvas canvas, Mode mode,
            System.Func<GameSave, bool> onSave, System.Func<GameSave, bool> onLoad,
            string suggestedName = null)
        {
            if (canvas == null) return;
            Close();

            var go = new GameObject("SaveLoadDialog");
            go.transform.SetParent(canvas.transform, false);
            _active = go.AddComponent<SaveLoadDialog>();
            _active._mode = mode;
            _active._onSave = onSave;
            _active._onLoad = onLoad;
            _active.Build((RectTransform)go.transform, suggestedName);
            IsOpen = true;
        }

        public static void Close()
        {
            if (_active != null) Destroy(_active.gameObject);
            _active = null;
            IsOpen = false;
        }

        void Build(RectTransform root, string suggestedName)
        {
            _openedFrame = Time.frameCount;
            UIFactory.Stretch(root);

            // Its own sorting layer, above the pause menu that opened it.
            var sorter = root.gameObject.AddComponent<Canvas>();
            sorter.overrideSorting = true;
            sorter.sortingOrder = 140;
            root.gameObject.AddComponent<GraphicRaycaster>();

            var backdrop = UIFactory.CreatePanel(root, "Backdrop", new Color(0, 0, 0, 0.78f));
            UIFactory.Stretch(backdrop);
            backdrop.gameObject.AddComponent<Button>().onClick.AddListener(Close);

            var box = UIFactory.CreateBorderedPanel(root, "Box", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BoxWidth, BoxHeight));
            // Swallows clicks that land on the box so they do not reach the
            // dismiss handler on the backdrop behind it.
            box.gameObject.AddComponent<Button>();

            BuildHeader(box);
            BuildTabs(box);
            BuildList(box);
            BuildFooter(box, suggestedName);

            Refresh();
        }

        // ------------------------------------------------------------ header

        void BuildHeader(RectTransform box)
        {
            _title = UIFactory.CreateText(box, _mode == Mode.Save ? "SAVE GAME" : "LOAD GAME",
                28, UiTheme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(_title.rectTransform, Pad, 18f, BoxWidth - Pad * 2f - 60f, 34f);

            var close = UIFactory.CreateIconButton(box, UiIcons.Close, Close,
                new Color(0, 0, 0, 0), UiTheme.TextDim);
            UIFactory.PlaceTopLeft((RectTransform)close.transform, BoxWidth - Pad - 28f, 20f, 28f, 28f);
            UiTooltip.Attach(close.gameObject, "Close  ·  Esc", UiTooltip.Side.Left);

            var rule = UIFactory.CreateDivider(box, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.offsetMin = new Vector2(Pad, 0); rule.offsetMax = new Vector2(-Pad, 0);
            rule.anchoredPosition = new Vector2(0, -HeaderHeight + 12f);
        }

        // -------------------------------------------------------------- tabs

        /// <summary>
        /// LOCAL and CLOUD. The cloud tab carries **PREVIEW** in its own caption
        /// rather than only in a note underneath: a player who never reads the
        /// note must still not come away believing their campaign is backed up
        /// somewhere.
        /// </summary>
        void BuildTabs(RectTransform box)
        {
            float tabWidth = (BoxWidth - Pad * 2f) / 2f;
            Tab(box, SaveDestination.Local, "LOCAL", UiIcons.Folder, Pad, tabWidth,
                "Saved games on this computer.");
            Tab(box, SaveDestination.Cloud,
                SaveSlots.CloudIsMock ? "CLOUD  ·  PREVIEW" : "CLOUD",
                UiIcons.Cloud, Pad + tabWidth, tabWidth,
                SaveSlots.CloudIsMock
                    ? "Cloud saves are a preview. The flow works end to end, but the files stay on this "
                      + "machine — nothing is uploaded anywhere."
                    : "Saved games on your account.");

            _destinationNote = UIFactory.CreateText(box, "", UiTheme.FontLabel, UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(_destinationNote.rectTransform, Pad,
                HeaderHeight + TabHeight + 6f, BoxWidth - Pad * 2f, 16f);
        }

        void Tab(RectTransform box, SaveDestination destination, string label, Sprite glyph,
            float x, float w, string tooltip)
        {
            var frame = UIFactory.CreatePanel(box, "Tab_" + destination, new Color(0, 0, 0, 0));
            UIFactory.PlaceTopLeft(frame, x, HeaderHeight, w, TabHeight);

            var btn = UIFactory.CreateButton(frame, "", () => SetDestination(destination),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);
            UiTooltip.Attach(frame.gameObject, tooltip, UiTooltip.Side.Below);

            var icon = UIFactory.CreateImage(frame, glyph, "Glyph");
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(10f, 0f), new Vector2(16, 16));

            var caption = UIFactory.CreateText(frame, label, UiTheme.FontBody, UiTheme.TextFaint,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            caption.raycastTarget = false;
            UIFactory.Place(caption.rectTransform, new Vector2(0f, 0.5f),
                new Vector2(34f, 0f), new Vector2(w - 44f, 18f));
            UIFactory.Fit(caption, 9);

            var underline = UIFactory.CreatePanel(frame, "Underline", UiTheme.Accent);
            underline.anchorMin = new Vector2(0, 0); underline.anchorMax = new Vector2(1, 0);
            underline.pivot = new Vector2(0.5f, 0);
            underline.sizeDelta = new Vector2(0, 2);
            underline.anchoredPosition = Vector2.zero;
            underline.GetComponent<Image>().raycastTarget = false;

            _tabs[destination] = (underline, caption);
        }

        void SetDestination(SaveDestination destination)
        {
            if (_destination == destination) return;
            _destination = destination;
            _selected = "";
            Refresh();
        }

        // -------------------------------------------------------------- list

        void BuildList(RectTransform box)
        {
            var scroll = UIFactory.CreateScrollView(box, out _listContent, withScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(Pad, FooterHeight);
            rt.offsetMax = new Vector2(-Pad, -(HeaderHeight + TabHeight + 28f));

            var layout = _listContent.GetComponent<VerticalLayoutGroup>();
            if (layout != null) { layout.spacing = 4; layout.padding = new RectOffset(0, 0, 2, 8); }
        }

        // ------------------------------------------------------------ footer

        void BuildFooter(RectTransform box, string suggestedName)
        {
            var rule = UIFactory.CreateDivider(box, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 0); rule.anchorMax = new Vector2(1, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.offsetMin = new Vector2(Pad, 0); rule.offsetMax = new Vector2(-Pad, 0);
            rule.anchoredPosition = new Vector2(0, FooterHeight - 6f);

            _status = UIFactory.CreateText(box, "", UiTheme.FontSmall, UiTheme.TextDim,
                TextAnchor.MiddleLeft);
            UIFactory.Place(_status.rectTransform, new Vector2(0f, 0f),
                new Vector2(Pad, 16f), new Vector2(BoxWidth - Pad * 2f, 20f));

            float buttonWidth = 150f;
            float fieldWidth = BoxWidth - Pad * 2f - buttonWidth - 10f;

            if (_mode == Mode.Save)
            {
                // The name field carries the suggestion rather than the field
                // being empty with a placeholder: SAVE is usually the whole of
                // what the player wants, and a dialog that makes them type
                // before they can press it has put a step in front of the verb.
                var frame = UIFactory.CreateBorderedPanel(box, "NameFrame", UiTheme.Surface, UiTheme.Border);
                UIFactory.Place(frame, new Vector2(0f, 0f), new Vector2(Pad, 48f),
                    new Vector2(fieldWidth, 40f));

                _nameField = UIFactory.CreateInputField(frame, "Name this save…", UiTheme.FontBody);
                UIFactory.Stretch((RectTransform)_nameField.transform);
                _nameField.GetComponent<Image>().color = new Color(0, 0, 0, 0);
                _nameField.characterLimit = SaveSlots.MaxNameLength;
                _nameField.text = string.IsNullOrEmpty(suggestedName)
                    ? SaveSlots.NextFreeName(_destination)
                    : suggestedName;
                _nameField.onValueChanged.AddListener(_ => RefreshCommit());
            }

            var commitFrame = UIFactory.CreateBorderedPanel(box, "Commit",
                _mode == Mode.Save ? UiTheme.Success : UiTheme.Accent,
                _mode == Mode.Save ? UiTheme.Success : UiTheme.Accent);
            UIFactory.Place(commitFrame, new Vector2(1f, 0f), new Vector2(-Pad, 48f),
                new Vector2(buttonWidth, 40f));
            _commit = UIFactory.CreateButton(commitFrame, _mode == Mode.Save ? "SAVE" : "LOAD",
                Commit, new Color(0, 0, 0, 0), Color.white, UiTheme.FontBody);
            UIFactory.Stretch((RectTransform)_commit.transform);
            _commitLabel = _commit.GetComponentInChildren<Text>(true);
        }

        // ------------------------------------------------------------ repaint

        void Refresh()
        {
            foreach (var kv in _tabs)
            {
                bool on = kv.Key == _destination;
                kv.Value.underline.gameObject.SetActive(on);
                kv.Value.caption.color = on ? UiTheme.Accent : UiTheme.TextFaint;
            }

            if (_destinationNote != null)
            {
                _destinationNote.text = _destination == SaveDestination.Cloud
                    ? (SaveSlots.CloudIsMock
                        ? $"PREVIEW  ·  signed in as {SaveSlots.CloudAccountLabel}  ·  files stay on this machine"
                        : $"Signed in as {SaveSlots.CloudAccountLabel}")
                    : SaveSlots.LocalDir;
                _destinationNote.color = _destination == SaveDestination.Cloud && SaveSlots.CloudIsMock
                    ? UiTheme.Warning : UiTheme.TextFaint;
            }

            RebuildList();
            RefreshCommit();
        }

        void RebuildList()
        {
            _rows.Clear();
            for (int i = _listContent.childCount - 1; i >= 0; i--)
            {
                var child = _listContent.GetChild(i);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }

            var saves = SaveSlots.List(_destination);
            if (saves.Count == 0)
            {
                var empty = UIFactory.CreateText(_listContent,
                    _mode == Mode.Save
                        ? "Nothing saved here yet. Name it below and press SAVE."
                        : "Nothing saved here yet.",
                    UiTheme.FontBody, UiTheme.TextFaint, TextAnchor.UpperLeft);
                ((RectTransform)empty.transform).sizeDelta = new Vector2(0, 44);
                return;
            }

            foreach (var save in saves) Row(save);
        }

        /// <summary>
        /// One save: its name over what it is of, with a delete on the end.
        ///
        /// The whole row is the click target rather than a radio button beside
        /// it — the row *is* the choice, and a 640 px strip with a 16 px hit area
        /// is a dialog that feels broken before it feels precise.
        /// </summary>
        void Row(GameSave save)
        {
            var frame = UIFactory.CreateBorderedPanel(_listContent, "Save_" + save.slot,
                UiTheme.Surface, UiTheme.Border);
            frame.sizeDelta = new Vector2(0, RowHeight);

            var btn = UIFactory.CreateButton(frame, "", () => Select(save.slot),
                new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            var name = UIFactory.CreateText(frame, save.slot.ToUpperInvariant(),
                UiTheme.FontBody, UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            name.raycastTarget = false;
            UIFactory.PlaceTopLeft(name.rectTransform, 14f, 8f, BoxWidth - 160f, 18f);
            UIFactory.Fit(name, 10);

            var detail = UIFactory.CreateText(frame, save.Describe(), UiTheme.FontLabel,
                UiTheme.TextDim, TextAnchor.MiddleLeft);
            detail.raycastTarget = false;
            UIFactory.PlaceTopLeft(detail.rectTransform, 14f, 28f, BoxWidth - 160f, 16f);
            UIFactory.Fit(detail, 8);

            string slot = save.slot;
            var del = UIFactory.CreateIconButton(frame, UiIcons.Trash,
                () => ConfirmDelete(slot), new Color(0, 0, 0, 0), UiTheme.TextFaint);
            UIFactory.Place((RectTransform)del.transform, new Vector2(1f, 0.5f),
                new Vector2(-10f, 0f), new Vector2(26, 26));
            UiTooltip.Attach(del.gameObject, "Delete this save", UiTooltip.Side.Left);

            _rows.Add((slot, frame, frame.Find("Fill").GetComponent<Image>()));
            PaintRows();
        }

        void Select(string slot)
        {
            _selected = slot;
            // Choosing an existing save in SAVE mode fills the name in: the
            // common reason to click one there is to write over it, and making
            // the player retype the name it already has to do that is the sort
            // of thing that gets typed slightly wrong.
            if (_mode == Mode.Save && _nameField != null) _nameField.text = slot;
            PaintRows();
            RefreshCommit();
        }

        void PaintRows()
        {
            foreach (var (slot, frame, fill) in _rows)
            {
                bool on = slot == _selected;
                if (fill != null) fill.color = on ? UiTheme.AccentWash : UiTheme.Surface;
                if (frame != null)
                    frame.GetComponent<Image>().color = on ? UiTheme.Accent : UiTheme.Border;
            }
        }

        void RefreshCommit()
        {
            bool can = _mode == Mode.Save
                ? _onSave != null && !string.IsNullOrEmpty(SaveSlots.Sanitise(_nameField?.text))
                : _onLoad != null && !string.IsNullOrEmpty(_selected);

            if (_commit != null) _commit.interactable = can;
            if (_commitLabel != null)
                _commitLabel.color = can ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }

        // ------------------------------------------------------------ actions

        void Commit()
        {
            if (_mode == Mode.Save) DoSave();
            else DoLoad();
        }

        void DoSave()
        {
            string slot = SaveSlots.Sanitise(_nameField?.text);
            if (string.IsNullOrEmpty(slot)) { SetStatus("Give the save a name first.", true); return; }

            if (SaveSlots.Exists(_destination, slot))
            {
                ConfirmDialog.Open(GetComponentInParent<Canvas>(), "OVERWRITE SAVE?",
                    $"'{slot}' already exists in {Where()}. Writing over it cannot be undone.",
                    "OVERWRITE", () => WriteSlot(slot));
                return;
            }
            WriteSlot(slot);
        }

        void WriteSlot(string slot)
        {
            var save = new GameSave { slot = slot };
            if (_onSave == null || !_onSave(save))
            {
                SetStatus("Nothing was saved — the scenario could not be read.", true);
                return;
            }

            if (!SaveSlots.Write(_destination, save))
            {
                SetStatus($"Could not write '{slot}' to {Where()}.", true);
                return;
            }

            _selected = slot;
            SetStatus($"Saved '{slot}' to {Where()}.", false);
            RebuildList();
            RefreshCommit();
        }

        void DoLoad()
        {
            var save = SaveSlots.Read(_destination, _selected);
            if (save == null) { SetStatus($"'{_selected}' could not be read.", true); return; }

            if (_onLoad == null || !_onLoad(save))
            {
                SetStatus($"'{_selected}' could not be loaded.", true);
                return;
            }
            Close();
        }

        void ConfirmDelete(string slot)
        {
            ConfirmDialog.Open(GetComponentInParent<Canvas>(), "DELETE SAVE?",
                $"'{slot}' will be removed from {Where()}. This cannot be undone.",
                "DELETE", () =>
                {
                    bool gone = SaveSlots.Delete(_destination, slot);
                    if (_selected == slot) _selected = "";
                    SetStatus(gone ? $"Deleted '{slot}'." : $"Could not delete '{slot}'.", !gone);
                    RebuildList();
                    RefreshCommit();
                });
        }

        string Where() => _destination == SaveDestination.Cloud
            ? (SaveSlots.CloudIsMock ? "the cloud (preview)" : "the cloud")
            : "this computer";

        void SetStatus(string message, bool bad)
        {
            if (_status == null) return;
            _status.text = message;
            _status.color = bad ? UiTheme.Warning : UiTheme.TextDim;
        }

        void Update()
        {
            // Not the frame it opened on: whether a component created during
            // another's Update gets its own Update in the same frame is
            // undefined, and the failure is a dialog that closes on the very
            // key that opened it.
            if (Time.frameCount == _openedFrame) return;
            if (ConfirmDialog.IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        void OnDestroy()
        {
            if (_active == this) { _active = null; IsOpen = false; }
        }
    }
}
