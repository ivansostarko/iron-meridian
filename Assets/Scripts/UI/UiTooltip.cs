using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IronMeridian.UI
{
    /// <summary>
    /// Hover captions for controls that are icons only.
    ///
    /// The on-map cluster is five unlabelled glyphs; the shortcut hint line at
    /// the bottom of the screen names the keys but not the buttons, so without
    /// this the only way to learn what the third one does is to press it. A
    /// caption on hover is the standard answer and costs nothing when unused.
    ///
    /// One shared label is created lazily per canvas and moved around, rather
    /// than a label per control: the tooltip is by definition singular, and a
    /// hidden <c>Text</c> behind every icon button would be dozens of extra
    /// uGUI objects for something at most one of which is ever visible.
    ///
    /// The label is placed beside its control rather than under the cursor —
    /// a caption that chases the mouse is harder to read, and these controls sit
    /// at the screen edge where a cursor-anchored label would run off it.
    /// </summary>
    public class UiTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>Gap between the control and its caption.</summary>
        const float Gap = 8f;
        const float PadX = 9f, PadY = 5f;

        /// <summary>
        /// Widest a caption is drawn before it wraps, canvas units.
        ///
        /// Captions started as four-word labels for icon buttons and were laid
        /// out on one line, which is right for those. They are now also where
        /// the longer explanations live — the GENERAL panel's toggles carry a
        /// paragraph each — and a paragraph on one line is both unreadable and
        /// wider than the screen. Past this width the caption wraps instead.
        /// </summary>
        const float MaxWidth = 340f;
        /// <summary>Seconds the pointer must rest before the caption appears.</summary>
        const float DelaySeconds = 0.25f;

        /// <summary>Which side of the control the caption sits on.</summary>
        public enum Side { Right, Left, Above, Below }

        string _text;
        Side _side;
        RectTransform _target;
        float _showAt = -1f;

        static RectTransform _label;
        static Text _labelText;
        static RectTransform _canvasRect;
        static UiTooltip _owner;

        /// <summary>
        /// Gives a control a hover caption. Safe to call on anything with a
        /// <see cref="RectTransform"/>; the component is added to the control
        /// itself so it dies with it.
        /// </summary>
        public static void Attach(GameObject control, string text, Side side = Side.Right)
        {
            if (control == null || string.IsNullOrEmpty(text)) return;
            var tip = control.GetComponent<UiTooltip>() ?? control.AddComponent<UiTooltip>();
            tip._text = text;
            tip._side = side;
            tip._target = (RectTransform)control.transform;
        }

        public void OnPointerEnter(PointerEventData eventData) => _showAt = Time.unscaledTime + DelaySeconds;

        public void OnPointerExit(PointerEventData eventData)
        {
            _showAt = -1f;
            if (_owner == this) Hide();
        }

        void OnDisable()
        {
            _showAt = -1f;
            if (_owner == this) Hide();
        }

        void Update()
        {
            if (_showAt < 0f || Time.unscaledTime < _showAt) return;
            _showAt = -1f;
            Show();
        }

        void Show()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null || _target == null) return;

            EnsureLabel(canvas);
            _owner = this;
            _labelText.text = _text;

            // Size to the text, then place relative to the control. Both rects
            // are converted into canvas space so the maths is independent of
            // where in the hierarchy the control happens to live.
            //
            // Measured unwrapped first, because that is the width a short
            // caption wants and a one-line label is the right answer for one.
            // Only when that runs past MaxWidth is the text wrapped — and the
            // height then has to be asked for *at the wrapped width*, since
            // preferredHeight is meaningless without one.
            _labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = _labelText.preferredWidth + PadX * 2f;
            float h;

            if (w <= MaxWidth)
            {
                h = _labelText.preferredHeight + PadY * 2f;
            }
            else
            {
                _labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
                w = MaxWidth;
                var settings = _labelText.GetGenerationSettings(new Vector2(w - PadX * 2f, 0f));
                h = _labelText.cachedTextGeneratorForLayout.GetPreferredHeight(_text, settings)
                    / _labelText.pixelsPerUnit + PadY * 2f;
            }

            _label.sizeDelta = new Vector2(w, h);

            Vector2 centre = CanvasPoint(_target);
            Vector2 half = _target.rect.size * 0.5f;

            Vector2 pos = _side switch
            {
                Side.Left => centre + new Vector2(-(half.x + Gap + w * 0.5f), 0f),
                Side.Above => centre + new Vector2(0f, half.y + Gap + h * 0.5f),
                Side.Below => centre + new Vector2(0f, -(half.y + Gap + h * 0.5f)),
                _ => centre + new Vector2(half.x + Gap + w * 0.5f, 0f)
            };

            // Keep it on screen: a caption that runs off the edge is worse than
            // none, and these controls sit against the edges by design.
            Vector2 limit = _canvasRect.rect.size * 0.5f;
            pos.x = Mathf.Clamp(pos.x, -limit.x + w * 0.5f + 4f, limit.x - w * 0.5f - 4f);
            pos.y = Mathf.Clamp(pos.y, -limit.y + h * 0.5f + 4f, limit.y - h * 0.5f - 4f);

            _label.anchoredPosition = pos;
            _label.SetAsLastSibling();
            _label.gameObject.SetActive(true);
        }

        static void Hide()
        {
            _owner = null;
            if (_label != null) _label.gameObject.SetActive(false);
        }

        /// <summary>
        /// A control's centre in canvas-local coordinates. Going via world space
        /// keeps this independent of where in the hierarchy the control sits and
        /// of its anchors — the cluster buttons are nested two panels deep.
        /// </summary>
        static Vector2 CanvasPoint(RectTransform rt) =>
            _canvasRect.InverseTransformPoint(rt.TransformPoint(rt.rect.center));

        static void EnsureLabel(Canvas canvas)
        {
            _canvasRect = (RectTransform)canvas.transform;
            if (_label != null) return;

            var frame = UIFactory.CreateBorderedPanel(_canvasRect, "Tooltip", UiTheme.Chrome, UiTheme.BorderStrong);
            frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
            frame.GetComponent<Image>().raycastTarget = false;
            // The caption must never swallow a click meant for the control it is
            // describing — it is drawn on top of the map by definition.
            frame.Find("Fill").GetComponent<Image>().raycastTarget = false;

            _labelText = UIFactory.CreateText(frame, "", UiTheme.FontSmall, UiTheme.Text);
            UIFactory.Stretch(_labelText.rectTransform);
            _labelText.horizontalOverflow = HorizontalWrapMode.Overflow;

            _label = frame;
            _label.gameObject.SetActive(false);
        }
    }
}
