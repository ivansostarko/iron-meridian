using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace IronMeridian.UI
{
    /// <summary>
    /// The right-click menu for something on the map — a formation, a logistic
    /// site — opened at the cursor and holding the handful of things that can be
    /// done to that object.
    ///
    /// **Why a menu rather than another key.** Removing a counter used to mean
    /// finding it again in the rail's DEPLOYED list, or selecting it and
    /// reaching for the panel on the far side of the screen. Both are a trip
    /// away from the thing you are already pointing at. Right-click is the one
    /// gesture on this map that means "about *this*", and a menu is the only
    /// affordance that can grow: the second and third entry cost nothing where a
    /// second and third shortcut would each need learning.
    ///
    /// **It appears where the cursor is**, clamped to stay on screen, and it
    /// carries the object's own name as a header — a menu that could be about
    /// either of two overlapping counters is a menu you have to test to
    /// understand.
    ///
    /// Follows the same shape as the other pop-ups here: a static
    /// <see cref="Open"/>, an <see cref="IsOpen"/> flag the map's input guards
    /// read, and a full-screen backdrop that swallows the click that dismisses
    /// it so it cannot fall through onto the terrain underneath.
    /// </summary>
    public class ContextMenuUI : MonoBehaviour
    {
        /// <summary>True while a menu is up, so the map's own input can stand down.</summary>
        public static bool IsOpen { get; private set; }

        const float MenuWidth = 216f;
        const float HeaderHeight = 30f;
        const float RowHeight = 30f;
        const float Pad = 4f;
        /// <summary>Margin kept between the menu and the edge of the screen.</summary>
        const float ScreenMargin = 8f;

        static ContextMenuUI _active;

        /// <summary>
        /// The frame the menu went up. Its own Update must not read the very
        /// right-click that opened it: whether a component created during
        /// another component's Update gets its own Update in the same frame is
        /// not defined by Unity, and the failure is a menu that flickers open
        /// and shut on every click.
        /// </summary>
        int _openedFrame;

        /// <summary>One entry: what it says, what it does, and whether it is destructive.</summary>
        public readonly struct Item
        {
            public readonly string Label;
            public readonly System.Action Action;
            /// <summary>Destructive entries are drawn in the danger colour, like every other one in this interface.</summary>
            public readonly bool Destructive;

            public Item(string label, System.Action action, bool destructive = false)
            {
                Label = label; Action = action; Destructive = destructive;
            }
        }

        /// <summary>
        /// Opens the menu at <paramref name="screenPos"/>. Any menu already up
        /// is replaced — two of these on screen at once would be two answers to
        /// "what am I pointing at".
        /// </summary>
        public static void Open(Canvas canvas, Vector2 screenPos, string title, List<Item> items)
        {
            if (canvas == null || items == null || items.Count == 0) return;
            Close();

            var go = new GameObject("ContextMenu");
            go.transform.SetParent(canvas.transform, false);
            _active = go.AddComponent<ContextMenuUI>();
            _active.Build(canvas, go.transform, screenPos, title, items);
            IsOpen = true;
        }

        public static void Close()
        {
            if (_active != null) Destroy(_active.gameObject);
            _active = null;
            IsOpen = false;
        }

        void Build(Canvas canvas, Transform root, Vector2 screenPos, string title, List<Item> items)
        {
            _openedFrame = Time.frameCount;

            var rootRect = (RectTransform)root;
            UIFactory.Stretch(rootRect);

            // Its own sorting layer rather than trusting sibling order: the
            // strike dock's icon strip already overrides sorting to stay on top,
            // and a menu opened under it would be a menu nobody can click. The
            // same device LoadingScreenUI uses.
            var sorter = rootRect.gameObject.AddComponent<Canvas>();
            sorter.overrideSorting = true;
            sorter.sortingOrder = 120;
            rootRect.gameObject.AddComponent<GraphicRaycaster>();

            // Backdrop first: invisible, but a raycast target, so the click that
            // dismisses the menu is *consumed* rather than also landing on the
            // terrain behind it and moving whatever was selected.
            var backdrop = UIFactory.CreatePanel(rootRect, "Backdrop", new Color(0, 0, 0, 0.001f));
            UIFactory.Stretch(backdrop);
            var dismiss = backdrop.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = backdrop.GetComponent<Image>();
            dismiss.onClick.AddListener(Close);

            float height = HeaderHeight + items.Count * RowHeight + Pad * 2f;

            var menu = UIFactory.CreateBorderedPanel(rootRect, "Menu", UiTheme.Panel, UiTheme.BorderStrong);
            menu.anchorMin = menu.anchorMax = new Vector2(0f, 0f);
            menu.pivot = new Vector2(0f, 1f);        // grows down-right from the cursor
            menu.sizeDelta = new Vector2(MenuWidth, height);
            menu.anchoredPosition = ClampToCanvas(canvas, screenPos, MenuWidth, height);

            var caption = UIFactory.CreateSectionHeader(menu, title, UiTheme.TextDim);
            UIFactory.PlaceTopLeft(caption.rectTransform, 10f, 8f, MenuWidth - 20f, 16f);
            UIFactory.Fit(caption, 8);

            var rule = UIFactory.CreateDivider(menu, UiTheme.Border);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1);
            rule.pivot = new Vector2(0.5f, 1);
            rule.anchoredPosition = new Vector2(0, -HeaderHeight + 2f);

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var btn = UIFactory.CreateButton(menu, item.Label,
                    () => { Close(); item.Action?.Invoke(); },
                    UiTheme.Surface, item.Destructive ? new Color(1f, 0.62f, 0.62f) : UiTheme.Text,
                    UiTheme.FontSmall);

                UIFactory.PlaceTopLeft((RectTransform)btn.transform, Pad,
                    HeaderHeight + i * RowHeight, MenuWidth - Pad * 2f, RowHeight - 2f);

                var label = btn.GetComponentInChildren<Text>(true);
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(10, 0);
                UIFactory.Fit(label, 8);
            }
        }

        /// <summary>
        /// The menu's position in canvas space, kept wholly on screen.
        ///
        /// The canvas scales with the window (1920×1080 reference), so a raw
        /// mouse position is in the wrong units the moment the window is not
        /// that size — hence the conversion. Flipping to the other side of the
        /// cursor rather than merely clamping: a menu shoved back from the right
        /// edge would otherwise sit *under* the pointer, and the first entry
        /// would be the one the release lands on.
        /// </summary>
        static Vector2 ClampToCanvas(Canvas canvas, Vector2 screenPos, float width, float height)
        {
            var canvasRect = (RectTransform)canvas.transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPos, null, out Vector2 local))
                local = Vector2.zero;

            // Local space is centred on the canvas; the menu is anchored to its
            // bottom-left corner, so shift into that frame.
            Vector2 size = canvasRect.rect.size;
            Vector2 pos = local + size * 0.5f;

            if (pos.x + width > size.x - ScreenMargin) pos.x -= width;
            if (pos.y - height < ScreenMargin) pos.y += height;

            pos.x = Mathf.Clamp(pos.x, ScreenMargin, Mathf.Max(ScreenMargin, size.x - width - ScreenMargin));
            pos.y = Mathf.Clamp(pos.y, Mathf.Min(size.y, height + ScreenMargin), size.y - ScreenMargin);
            return pos;
        }

        void Update()
        {
            if (Time.frameCount == _openedFrame) return;

            // Escape closes it, like every other pop-up here. Right-clicking
            // again does too: the gesture that opened it is the one a player
            // reaches for to get rid of it.
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) Close();
        }

        void OnDestroy()
        {
            if (_active == this) { _active = null; IsOpen = false; }
        }
    }
}
