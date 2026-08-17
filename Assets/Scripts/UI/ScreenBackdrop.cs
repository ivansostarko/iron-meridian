using UnityEngine;

namespace IronMeridian.UI
{
    /// <summary>
    /// A screen's background, when the screen changes it.
    ///
    /// Two menus now do: the main menu previews what is behind an entry as the
    /// cursor crosses it, and the single-player board shows the theatre a
    /// campaign is fought in. Both want the same three things — swap the
    /// artwork, keep it behind everything else, and go back to the page's own
    /// picture when the cursor leaves — so both get them from here rather than
    /// from two copies that drift.
    ///
    /// **A page image and a preview over it.** The page is what the screen is
    /// showing; the preview is what the cursor is currently promising. Held
    /// separately because leaving a row has to restore the page without the row
    /// needing to know what the page is.
    ///
    /// **Applied in <see cref="LateUpdate"/>, not on the event.** uGUI sends
    /// PointerExit for the row being left and PointerEnter for the row being
    /// entered in the same frame, so applying immediately would rebuild the
    /// background twice — once back to the page and once to the new row — every
    /// time the cursor crossed a list. Collapsing to one apply per frame makes
    /// dragging the pointer down six rows cost six rebuilds rather than twelve,
    /// and removes the flash of the page image in between.
    ///
    /// **Rebuilt rather than re-pointed.** A background is three layers — base,
    /// artwork, scrim — and the artwork's size comes from the sprite's aspect
    /// through an <c>AspectRatioFitter</c>; swapping the sprite alone would
    /// leave the fitter set for the picture before it. Sprites are cached by
    /// path in <see cref="UIFactory.LoadSprite"/>, so a picture is read off disk
    /// once however many times it is shown.
    ///
    /// See docs/11-GAME-MENU.md §3.4.
    /// </summary>
    public class ScreenBackdrop : MonoBehaviour
    {
        Transform _parent;
        float? _scrim;

        RectTransform _root;
        BackgroundId _shown = BackgroundId.None;

        /// <summary>The screen's own picture — what a cleared preview goes back to.</summary>
        BackgroundId _page = BackgroundId.None;
        /// <summary>What the cursor is promising, or null when it is over nothing.</summary>
        BackgroundId? _preview;

        bool _dirty;

        /// <summary>
        /// Puts a backdrop on a screen and shows its first page image. The host
        /// is the screen's own behaviour, so the backdrop dies with the screen.
        /// </summary>
        public static ScreenBackdrop Attach(GameObject host, Transform parent,
            BackgroundId page, float? scrimAlpha = null)
        {
            var backdrop = host.AddComponent<ScreenBackdrop>();
            backdrop._parent = parent;
            backdrop._scrim = scrimAlpha;
            backdrop._page = page;
            backdrop.Apply();          // at once: a screen must not open on a black frame
            return backdrop;
        }

        /// <summary>The picture this page shows when nothing is hovered.</summary>
        public void SetPage(BackgroundId id)
        {
            if (_page == id) return;
            _page = id;
            _dirty = true;
        }

        /// <summary>Show <paramref name="id"/> while the cursor is on something.</summary>
        public void Preview(BackgroundId id)
        {
            if (_preview.HasValue && _preview.Value == id) return;
            _preview = id;
            _dirty = true;
        }

        /// <summary>The cursor has left; fall back to the page's own picture.</summary>
        public void ClearPreview()
        {
            if (!_preview.HasValue) return;
            _preview = null;
            _dirty = true;
        }

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            Apply();
        }

        void Apply()
        {
            var target = _preview ?? _page;
            if (_shown == target && _root != null) return;
            _shown = target;

            if (_root != null)
            {
                // Switched off before it is destroyed: `Destroy` is deferred to
                // the end of the frame, and the outgoing artwork is later in the
                // hierarchy than the incoming one — so for one frame the old
                // picture would draw over the new.
                _root.gameObject.SetActive(false);
                Destroy(_root.gameObject);
            }

            _root = UIFactory.CreateScreenBackground(_parent, target, _scrim);
            // uGUI draws in hierarchy order and this is built after the screen,
            // so it has to be pushed behind everything on it.
            _root.SetAsFirstSibling();
        }
    }
}
