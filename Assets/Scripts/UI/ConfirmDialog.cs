using UnityEngine;
using UnityEngine.UI;

namespace IronMeridian.UI
{
    /// <summary>
    /// A modal "are you sure?" for actions that throw work away.
    ///
    /// The map editor has no undo for wholesale operations — Ctrl+Z tracks
    /// individual edits, not a reset — so anything that discards the whole
    /// scenario has to ask first. Kept generic because RESET will not be the
    /// last such action.
    ///
    /// Follows the same shape as the other modals here: a static
    /// <see cref="Open"/>, an <see cref="IsOpen"/> flag the map's input guards
    /// read so clicks do not fall through to the terrain, and a scrim that
    /// swallows anything aimed past it.
    /// </summary>
    public class ConfirmDialog : MonoBehaviour
    {
        /// <summary>True while the modal is up, so the map's own input can stand down.</summary>
        public static bool IsOpen { get; private set; }

        const float PanelW = 460f;
        const float PanelH = 210f;

        static ConfirmDialog _active;

        System.Action _onConfirm;

        /// <summary>
        /// Puts up the modal. <paramref name="confirmLabel"/> names the action
        /// rather than saying "OK" — a destructive button should say what it
        /// destroys, so a mis-click is caught by reading it.
        /// </summary>
        public static void Open(Canvas canvas, string title, string body, string confirmLabel,
            System.Action onConfirm)
        {
            if (canvas == null) return;
            Close();

            var go = new GameObject("ConfirmDialog");
            go.transform.SetParent(canvas.transform, false);
            _active = go.AddComponent<ConfirmDialog>();
            _active._onConfirm = onConfirm;
            _active.Build(go.transform, title, body, confirmLabel);
            IsOpen = true;
        }

        public static void Close()
        {
            if (_active != null) Destroy(_active.gameObject);
            _active = null;
            IsOpen = false;
        }

        void Build(Transform root, string title, string body, string confirmLabel)
        {
            // Scrim first, filling the screen: it darkens the editor and, more
            // importantly, is a raycast target, so a click anywhere outside the
            // panel cannot reach the map behind it.
            var scrim = UIFactory.CreatePanel(root, "Scrim", new Color(0.02f, 0.03f, 0.05f, 0.72f));
            UIFactory.Stretch(scrim);

            var panel = UIFactory.CreateBorderedPanel(root, "Panel", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(PanelW, PanelH));

            var heading = UIFactory.CreateText(panel, title, UiTheme.FontTitle, UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(heading.rectTransform, 22f, 20f, PanelW - 44f, 28f);
            UIFactory.Fit(heading);

            var message = UIFactory.CreateText(panel, body, UiTheme.FontBody, UiTheme.TextDim,
                TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(message.rectTransform, 22f, 58f, PanelW - 44f, 76f);

            const float btnW = 190f, btnH = 40f;

            var cancel = UIFactory.CreateButton(panel, "CANCEL", Close,
                UiTheme.Surface, UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)cancel.transform, new Vector2(0f, 0f),
                new Vector2(22, 22), new Vector2(btnW, btnH));

            var confirm = UIFactory.CreateButton(panel, confirmLabel, Confirm,
                UiTheme.Danger, UiTheme.Text, UiTheme.FontSmall);
            UIFactory.Place((RectTransform)confirm.transform, new Vector2(1f, 0f),
                new Vector2(-22, 22), new Vector2(btnW, btnH));
        }

        void Confirm()
        {
            var action = _onConfirm;
            Close();
            action?.Invoke();
        }

        void Update()
        {
            // Esc cancels. Enter deliberately does not confirm — the whole point
            // of this dialog is that the destructive path needs a deliberate click.
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        void OnDestroy()
        {
            if (_active == this) { _active = null; IsOpen = false; }
        }
    }
}
