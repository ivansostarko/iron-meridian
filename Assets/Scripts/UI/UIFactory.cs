using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// Builds all uGUI widgets from code so the project ships without binary
    /// scene/prefab dependencies. Every screen constructs itself at runtime.
    /// </summary>
    public static class UIFactory
    {
        static Font _font;
        public static Font DefaultFont =>
            _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas CreateCanvas(string name = "Canvas")
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            if (Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return canvas;
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        public static RectTransform CreateGroup(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Text CreateText(Transform parent, string content, int size,
            Color? color = null, TextAnchor anchor = TextAnchor.MiddleCenter,
            FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = DefaultFont;
            t.text = content;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color ?? GameConfig.UiText;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button CreateButton(Transform parent, string label, UnityAction onClick,
            Color? bg = null, Color? textColor = null, int fontSize = 26)
        {
            var rt = CreatePanel(parent, "Button_" + label, bg ?? GameConfig.UiPanelLight);
            var btn = rt.gameObject.AddComponent<Button>();
            var img = rt.GetComponent<Image>();
            btn.targetGraphic = img;

            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            var txt = CreateText(rt, label, fontSize, textColor);
            Stretch(txt.rectTransform);

            btn.onClick.AddListener(() => IronMeridian.Audio.AudioManager.PlayClick(rt.gameObject));
            btn.onClick.AddListener(onClick);
            return btn;
        }

        public static Slider CreateSlider(Transform parent, float value, UnityAction<float> onChanged)
        {
            var root = CreateGroup(parent, "Slider");
            var slider = root.gameObject.AddComponent<Slider>();

            var bg = CreatePanel(root, "Background", new Color(0f, 0f, 0f, 0.5f));
            Stretch(bg); bg.offsetMin = new Vector2(0, 12); bg.offsetMax = new Vector2(0, -12);

            var fillArea = CreateGroup(root, "FillArea");
            Stretch(fillArea); fillArea.offsetMin = new Vector2(6, 12); fillArea.offsetMax = new Vector2(-6, -12);
            var fill = CreatePanel(fillArea, "Fill", GameConfig.UiAccent);
            Stretch(fill);

            var handleArea = CreateGroup(root, "HandleArea");
            Stretch(handleArea); handleArea.offsetMin = new Vector2(10, 0); handleArea.offsetMax = new Vector2(-10, 0);
            var handle = CreatePanel(handleArea, "Handle", Color.white);
            handle.sizeDelta = new Vector2(24, 0);

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.minValue = 0f; slider.maxValue = 1f;
            slider.value = value;
            slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        public static Toggle CreateToggle(Transform parent, string label, bool isOn, UnityAction<bool> onChanged)
        {
            var root = CreateGroup(parent, "Toggle_" + label);
            var toggle = root.gameObject.AddComponent<Toggle>();

            var box = CreatePanel(root, "Box", GameConfig.UiPanelLight);
            box.anchorMin = new Vector2(0, 0.5f); box.anchorMax = new Vector2(0, 0.5f);
            box.pivot = new Vector2(0, 0.5f);
            box.sizeDelta = new Vector2(34, 34);
            box.anchoredPosition = Vector2.zero;

            var check = CreatePanel(box, "Check", GameConfig.UiAccent);
            Stretch(check); check.offsetMin = new Vector2(7, 7); check.offsetMax = new Vector2(-7, -7);

            var txt = CreateText(root, label, 24, null, TextAnchor.MiddleLeft);
            Stretch(txt.rectTransform);
            txt.rectTransform.offsetMin = new Vector2(48, 0);

            toggle.targetGraphic = box.GetComponent<Image>();
            toggle.graphic = check.GetComponent<Image>();
            toggle.isOn = isOn;
            toggle.onValueChanged.AddListener(onChanged);
            return toggle;
        }

        public static Dropdown CreateDropdown(Transform parent, System.Collections.Generic.List<string> options,
            int value, UnityAction<int> onChanged)
        {
            var rt = CreatePanel(parent, "Dropdown", GameConfig.UiPanelLight);
            var dd = rt.gameObject.AddComponent<Dropdown>();
            dd.targetGraphic = rt.GetComponent<Image>();

            var caption = CreateText(rt, "", 24, null, TextAnchor.MiddleLeft);
            Stretch(caption.rectTransform);
            caption.rectTransform.offsetMin = new Vector2(16, 0);
            caption.rectTransform.offsetMax = new Vector2(-30, 0);
            dd.captionText = caption;

            var arrow = CreateText(rt, "▼", 18, GameConfig.UiTextDim);
            arrow.rectTransform.anchorMin = new Vector2(1, 0.5f);
            arrow.rectTransform.anchorMax = new Vector2(1, 0.5f);
            arrow.rectTransform.anchoredPosition = new Vector2(-22, 0);
            arrow.rectTransform.sizeDelta = new Vector2(30, 30);

            // Template
            var template = CreatePanel(rt, "Template", GameConfig.UiPanel);
            template.anchorMin = new Vector2(0, 0); template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(0.5f, 1);
            template.sizeDelta = new Vector2(0, 300);
            template.gameObject.AddComponent<ScrollRect>();

            var viewport = CreatePanel(template, "Viewport", new Color(0, 0, 0, 0.01f));
            Stretch(viewport);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var content = CreateGroup(viewport, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, 44);

            var item = CreateGroup(content, "Item");
            item.anchorMin = new Vector2(0, 0.5f); item.anchorMax = new Vector2(1, 0.5f);
            item.sizeDelta = new Vector2(0, 44);
            var itemToggle = item.gameObject.AddComponent<Toggle>();

            var itemBg = CreatePanel(item, "Item Background", GameConfig.UiPanelLight);
            Stretch(itemBg);
            var itemCheckmark = CreatePanel(item, "Item Checkmark", GameConfig.UiAccent);
            itemCheckmarkSetup(itemCheckmark);
            var itemLabel = CreateText(item, "Option", 22, null, TextAnchor.MiddleLeft);
            Stretch(itemLabel.rectTransform);
            itemLabel.rectTransform.offsetMin = new Vector2(40, 0);

            itemToggle.targetGraphic = itemBg.GetComponent<Image>();
            itemToggle.graphic = itemCheckmark.GetComponent<Image>();

            var scroll = template.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;

            dd.template = template;
            dd.itemText = itemLabel;
            template.gameObject.SetActive(false);

            dd.ClearOptions();
            dd.AddOptions(options);
            dd.value = value;
            dd.RefreshShownValue();
            dd.onValueChanged.AddListener(onChanged);
            return dd;

            static void itemCheckmarkSetup(RectTransform c)
            {
                c.anchorMin = new Vector2(0, 0.5f); c.anchorMax = new Vector2(0, 0.5f);
                c.sizeDelta = new Vector2(18, 18);
                c.anchoredPosition = new Vector2(18, 0);
            }
        }

        public static ScrollRect CreateScrollView(Transform parent, out RectTransform content)
        {
            var root = CreatePanel(parent, "ScrollView", new Color(0, 0, 0, 0.25f));
            var scroll = root.gameObject.AddComponent<ScrollRect>();

            var viewport = CreatePanel(root, "Viewport", new Color(0, 0, 0, 0.01f));
            Stretch(viewport);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            content = CreateGroup(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 8;
            layout.padding = new RectOffset(8, 8, 8, 8);
            content.gameObject.AddComponent<ContentSizeFitter>()
                .verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 30;
            return scroll;
        }

        public static InputField CreateInputField(Transform parent, string placeholder, int fontSize = 20)
        {
            var rt = CreatePanel(parent, "InputField", GameConfig.UiPanelLight);
            var input = rt.gameObject.AddComponent<InputField>();

            var text = CreateText(rt, "", fontSize, GameConfig.UiText, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(10, 4);
            text.rectTransform.offsetMax = new Vector2(-10, -4);
            text.raycastTarget = false;

            var placeholderText = CreateText(rt, placeholder, fontSize, GameConfig.UiTextDim, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(placeholderText.rectTransform);
            placeholderText.rectTransform.offsetMin = new Vector2(10, 4);
            placeholderText.rectTransform.offsetMax = new Vector2(-10, -4);
            placeholderText.raycastTarget = false;

            input.textComponent = text;
            input.placeholder = placeholderText;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        public static Image CreateImage(Transform parent, Sprite sprite, string name = "Image")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            return img;
        }

        // ---------------------------------------------------------- console widgets
        // The map editor's chrome (see UiTheme) is built from hairline-bordered
        // surfaces. uGUI has no border property, so a "bordered panel" is an
        // outer Image in the border colour with an inset fill Image on top.
        // Children added afterwards draw above the fill, so callers treat the
        // returned RectTransform as an ordinary container.

        public static RectTransform CreateBorderedPanel(Transform parent, string name,
            Color fill, Color border, float thickness = 1f)
        {
            var outer = CreatePanel(parent, name, border);

            var inner = CreatePanel(outer, "Fill", fill);
            Stretch(inner);
            inner.offsetMin = new Vector2(thickness, thickness);
            inner.offsetMax = new Vector2(-thickness, -thickness);
            inner.GetComponent<Image>().raycastTarget = false;

            return outer;
        }

        /// <summary>A one-pixel rule. Cheaper and crisper than a bordered panel for a divider.</summary>
        public static RectTransform CreateDivider(Transform parent, Color color, float thickness = 1f)
        {
            var rt = CreatePanel(parent, "Divider", color);
            rt.GetComponent<Image>().raycastTarget = false;
            rt.sizeDelta = new Vector2(0, thickness);
            return rt;
        }

        /// <summary>Square button carrying one <see cref="UiIcons"/> glyph.</summary>
        public static Button CreateIconButton(Transform parent, Sprite icon, UnityAction onClick,
            Color? background = null, Color? tint = null, float iconInset = 8f)
        {
            var rt = CreatePanel(parent, "IconButton", background ?? new Color(0, 0, 0, 0));
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = rt.GetComponent<Image>();

            var colors = btn.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            btn.colors = colors;

            var img = CreateImage(rt, icon, "Glyph");
            img.color = tint ?? UiTheme.TextDim;
            img.raycastTarget = false;
            var irt = (RectTransform)img.transform;
            Stretch(irt);
            irt.offsetMin = new Vector2(iconInset, iconInset);
            irt.offsetMax = new Vector2(-iconInset, -iconInset);

            btn.onClick.AddListener(() => IronMeridian.Audio.AudioManager.PlayClick(rt.gameObject));
            btn.onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>
        /// Small-caps section header — letter-spaced, accent-coloured, with an
        /// optional count badge on the right, as in the map editor panels.
        /// </summary>
        public static Text CreateSectionHeader(Transform parent, string label, Color? color = null)
        {
            var t = CreateText(parent, UiTheme.Spaced(label), UiTheme.FontLabel,
                color ?? UiTheme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            return t;
        }

        /// <summary>Rounded-looking count chip used beside section headers.</summary>
        public static Text CreateBadge(Transform parent, string value, Color? fill = null, Color? textColor = null)
        {
            var rt = CreatePanel(parent, "Badge", fill ?? UiTheme.AccentWash);
            rt.GetComponent<Image>().raycastTarget = false;
            var t = CreateText(rt, value, UiTheme.FontLabel, textColor ?? UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform);
            return t;
        }

        /// <summary>
        /// Full-screen artwork behind a screen's UI, with a readability scrim.
        /// Create it first so it sits at the back — uGUI draws in hierarchy
        /// order. Returns the root, which always fills the parent even when the
        /// image is missing, so no screen can end up transparent.
        /// </summary>
        public static RectTransform CreateScreenBackground(Transform parent, BackgroundId id,
            float? scrimAlpha = null)
        {
            // Opaque base first: it shows through if the image fails to load and
            // covers any gap while Resources.Load runs.
            var root = CreatePanel(parent, "Background", GameConfig.UiBackground);
            Stretch(root);
            root.GetComponent<Image>().raycastTarget = false;

            var def = BackgroundCatalog.Get(id);
            if (def == null) return root;

            var sprite = LoadSprite(def.resourcePath);
            if (sprite == null) return root;   // LoadSprite has already warned

            var img = CreateImage(root, sprite, "Artwork");
            var rt = (RectTransform)img.transform;
            // Centre anchors, not stretch: the fitter below drives the size.
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            img.raycastTarget = false;
            // The fitter guarantees the rect's aspect matches the sprite's, so
            // Image's own letterboxing would only fight it.
            img.preserveAspect = false;

            // Cover the screen at any window aspect without distorting the art:
            // envelope the parent and let the overflow fall outside the canvas.
            // A Mask would cost an extra draw call for no visible difference.
            var fitter = img.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.height > 0f
                ? sprite.rect.width / sprite.rect.height
                : 1.777f;

            var scrimColour = GameConfig.UiBackground;
            scrimColour.a = Mathf.Clamp01(scrimAlpha ?? def.scrimAlpha);
            var scrim = CreatePanel(root, "Scrim", scrimColour);
            Stretch(scrim);
            scrim.GetComponent<Image>().raycastTarget = false;

            return root;
        }

        /// <summary>
        /// Raw texture quad — for content uGUI has no sprite for, such as a
        /// camera's <see cref="RenderTexture"/> (see <c>ModelPreview</c>).
        /// </summary>
        public static RawImage CreateRawImage(Transform parent, string name = "RawImage")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RawImage>();
        }

        // Sprite.Create allocates a new object every call, and these are looked
        // up repeatedly (the info panel refreshes ~twice a second, the palette
        // rebuilds on every team switch). Cache by path so each icon is built
        // once. Misses are cached too, so a missing icon warns once, not forever.
        static readonly System.Collections.Generic.Dictionary<string, Sprite> _spriteCache =
            new System.Collections.Generic.Dictionary<string, Sprite>();
        static readonly System.Collections.Generic.HashSet<string> _missingIcons =
            new System.Collections.Generic.HashSet<string>();

        public static Sprite LoadIconSprite(string team, string unitId) =>
            LoadSprite($"Icons/{team}/{unitId}");

        /// <summary>
        /// Loads any texture under Resources as a sprite, cached by path.
        /// Returns null (having warned once) when the path does not resolve.
        /// </summary>
        public static Sprite LoadSprite(string path)
        {
            if (_spriteCache.TryGetValue(path, out var cached))
            {
                // `!= null` is Unity's destroyed-object check: a scene unload
                // (or a domain-reload-disabled play session) can invalidate the
                // sprite while the dictionary entry survives.
                if (cached != null) return cached;
                _spriteCache.Remove(path);
            }
            if (_missingIcons.Contains(path)) return null;

            var tex = Resources.Load<Texture2D>(path);
            if (tex == null)
            {
                Debug.LogWarning($"[UIFactory] Missing texture: Resources/{path}. " +
                    "If the file exists on disk, try Assets > Reimport All (a stale Library cache can hide it).");
                _missingIcons.Add(path);
                return null;
            }

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f);
            _spriteCache[path] = sprite;
            return sprite;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Places a rect by its **top-left corner**, <paramref name="top"/>
        /// pixels down from the parent's top edge.
        ///
        /// Use this for stacked rows. Anchoring two lines to the parent's centre
        /// and nudging them a few pixels apart leaves their rects overlapping —
        /// each is ~16 px tall around the same midpoint — and text drawn inside
        /// overlapping rects collides on screen no matter how it is aligned.
        /// Measuring from the top makes the stacking explicit and safe.
        /// </summary>
        public static void PlaceTopLeft(RectTransform rt, float x, float top, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -top);
            rt.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Lets a label shrink to fit its rect rather than spilling over its
        /// neighbours. Legacy <see cref="Text"/> has no ellipsis, so best-fit
        /// between <paramref name="minSize"/> and the authored size, plus
        /// vertical truncation, is the closest thing to a responsive label —
        /// and it is what keeps long unit names and weather descriptions inside
        /// a 274 px panel at any resolution.
        /// </summary>
        public static Text Fit(Text text, int minSize = 9)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = minSize;
            text.resizeTextMaxSize = text.fontSize;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        /// <summary>
        /// Two-line "title over detail" block, the pattern every card and option
        /// row in the map editor uses. Returns both labels so callers can keep
        /// updating them.
        /// </summary>
        public static (Text title, Text detail) CreateStackedLabels(Transform parent,
            string title, string detail, float x, float width,
            float topInset = 8f, int titleSize = UiTheme.FontSmall, int detailSize = UiTheme.FontLabel,
            Color? titleColor = null, Color? detailColor = null)
        {
            var t = CreateText(parent, title, titleSize, titleColor ?? UiTheme.Text,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            PlaceTopLeft(t.rectTransform, x, topInset, width, 16f);
            Fit(t);

            var d = CreateText(parent, detail, detailSize, detailColor ?? UiTheme.TextFaint,
                TextAnchor.MiddleLeft);
            PlaceTopLeft(d.rectTransform, x, topInset + 17f, width, 15f);
            Fit(d);

            return (t, d);
        }

        public static void Place(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }
    }
}
