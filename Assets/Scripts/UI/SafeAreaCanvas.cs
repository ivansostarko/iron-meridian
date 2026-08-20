using UnityEngine;

namespace IronMeridian.UI
{
    /// <summary>
    /// Keeps a screen-space canvas out from under a notch, a Dynamic Island and
    /// a home indicator.
    ///
    /// **Why this is not a per-panel problem.** Every piece of chrome in this
    /// game measures from a screen edge: the top bar spans the top, the rail
    /// holds the left edge for its full height, the unit inspector holds the
    /// right, the zoom cluster sits in the bottom-left corner. On a device whose
    /// screen is not a rectangle, "the edge" and "the edge you can see" are
    /// different places — and in landscape, which is the only orientation this
    /// game has, the notch eats a strip of exactly the side the rail is on.
    ///
    /// So the fix is one rect, not thirty offsets: a full-canvas child inset to
    /// <see cref="Screen.safeArea"/>, with everything parented into it. Panels
    /// then measure from *its* edges, which are the edges that exist, and not a
    /// single one of them has to learn what a notch is.
    ///
    /// **It reparents, and only where it must.** Everything in this project is
    /// built at runtime into <c>canvas.transform</c> from a hundred-odd call
    /// sites, and rewriting all of them would be a large change nobody could
    /// eyeball. Instead this moves strays into the safe rect as they appear —
    /// which is also what catches the dialogs, menus and tooltips that are
    /// created long after the screen was built.
    ///
    /// **On a rectangular screen it does nothing at all.** <c>Screen.safeArea</c>
    /// is the whole screen on every desktop, every browser, and every phone
    /// without a cut-out, so <see cref="Inset"/> is false and no object is ever
    /// moved. The reparenting only happens on hardware that needs it, which is
    /// what keeps the risk of it where the benefit is.
    ///
    /// See docs/43-IOS.md §3.
    /// </summary>
    [DisallowMultipleComponent]
    public class SafeAreaCanvas : MonoBehaviour
    {
        /// <summary>What the safe rect is called in the hierarchy.</summary>
        public const string RootName = "SafeArea";

        RectTransform _canvasRect;
        RectTransform _safe;
        Rect _applied;
        int _appliedWidth, _appliedHeight;

        /// <summary>
        /// True where the screen has something taken out of it. False
        /// everywhere else, which is nearly everywhere.
        /// </summary>
        public static bool Inset
        {
            get
            {
                var area = Screen.safeArea;
                return area.x > 0.5f || area.y > 0.5f ||
                       area.width < Screen.width - 0.5f ||
                       area.height < Screen.height - 0.5f;
            }
        }

        /// <summary>
        /// Adds the fitter to a canvas. Safe to call on every canvas the game
        /// makes: on a rectangular screen the component is not even added, so
        /// the object graph is byte-for-byte what it was.
        /// </summary>
        public static void Attach(Canvas canvas)
        {
            if (canvas == null || !Inset) return;
            if (canvas.GetComponent<SafeAreaCanvas>() == null)
                canvas.gameObject.AddComponent<SafeAreaCanvas>();
        }

        void Awake()
        {
            _canvasRect = (RectTransform)transform;
            EnsureRoot();
        }

        void EnsureRoot()
        {
            if (_safe != null) return;

            var go = new GameObject(RootName, typeof(RectTransform));
            _safe = (RectTransform)go.transform;
            _safe.SetParent(_canvasRect, false);
            _safe.anchorMin = Vector2.zero;
            _safe.anchorMax = Vector2.one;
            _safe.offsetMin = Vector2.zero;
            _safe.offsetMax = Vector2.zero;
            // First, so anything added later sorts above it exactly as it would
            // have sorted above the canvas itself.
            _safe.SetAsFirstSibling();
        }

        void LateUpdate()
        {
            EnsureRoot();
            Adopt();
            ApplyIfChanged();
        }

        /// <summary>
        /// Moves anything parented straight onto the canvas into the safe rect.
        ///
        /// Every frame, because dialogs, context menus and tooltips are created
        /// long after the screen that owns them — but the common case is a
        /// single integer comparison, and the loop only runs on the frame
        /// something new appeared.
        ///
        /// Sibling order is preserved by walking forwards and appending: a
        /// stray that was drawn last on the canvas is still drawn last inside
        /// the rect, which is what <c>SetAsLastSibling</c> callers are relying
        /// on to put a panel over its neighbours.
        /// </summary>
        void Adopt()
        {
            if (_canvasRect.childCount <= 1) return;

            for (int i = 0; i < _canvasRect.childCount; i++)
            {
                var child = _canvasRect.GetChild(i);
                if (child == _safe) continue;

                // worldPositionStays: false — the rect is being re-anchored to a
                // new parent, and its offsets are meant to be read against that
                // parent. Keeping the world position would leave every panel
                // pinned to the old, unsafe edge, which is the whole bug.
                child.SetParent(_safe, false);
                i--;   // the walk shifted under us
            }
        }

        void ApplyIfChanged()
        {
            var area = Screen.safeArea;
            if (area == _applied &&
                Screen.width == _appliedWidth && Screen.height == _appliedHeight) return;

            _applied = area;
            _appliedWidth = Screen.width;
            _appliedHeight = Screen.height;

            if (Screen.width <= 0 || Screen.height <= 0) return;

            // Anchors rather than offsets, so the inset is a *fraction* of the
            // screen and survives a resolution change without being recomputed
            // in pixels — which matters on a device that rotates, and on the
            // editor's Game view where the safe area is simulated.
            Vector2 min = area.position;
            Vector2 max = area.position + area.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            _safe.anchorMin = min;
            _safe.anchorMax = max;
            _safe.offsetMin = Vector2.zero;
            _safe.offsetMax = Vector2.zero;

            Debug.Log($"[SafeArea] {name}: {area.width}x{area.height} " +
                      $"at ({area.x}, {area.y}) of {Screen.width}x{Screen.height}.");
        }
    }
}
