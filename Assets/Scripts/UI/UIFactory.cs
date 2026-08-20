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

        /// <summary>
        /// The resolution every panel in this game is laid out against.
        ///
        /// **A phone gets a smaller one, which makes everything bigger.** The
        /// layout is authored in these units, so halving the reference doubles
        /// the physical size of every control; 1280×720 on a handheld puts the
        /// rail's rows and the map cluster's buttons at roughly a finger's width
        /// on a 6-inch screen, where at the desktop's 1920×1080 they land at
        /// about 6 mm and are missed as often as hit.
        ///
        /// It is a scale change, not a layout change: nothing moves relative to
        /// anything else, and less of the map is visible around the chrome —
        /// which is the trade a small screen makes anyway. See docs/40-ANDROID.md.
        /// </summary>
        public static Vector2 ReferenceResolution =>
            Core.TouchInput.IsTouchPlatform ? new Vector2(1280, 720)
            // A Steam Deck is 1280x800 held at arm's length. Laying out against
            // the panel's own size puts the interface at 1:1 — no scaling blur
            // on a 7-inch screen — and makes every control half again as large
            // as it would be under the desktop reference, which is what a
            // trackpad and a thumb need. 16:10, not 16:9, so nothing is cropped.
            : Core.SteamDeck.IsHandheld
                ? new Vector2(Core.SteamDeck.ScreenWidth, Core.SteamDeck.ScreenHeight)
                : new Vector2(1920, 1080);

        public static Canvas CreateCanvas(string name = "Canvas")
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            if (Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // A notch, a Dynamic Island or a home indicator takes a strip out of
            // the screen, and every panel in this game measures from an edge.
            // Adds nothing at all on a rectangular screen — see SafeAreaCanvas.
            SafeAreaCanvas.Attach(canvas);
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
            AttachHoverSound(rt.gameObject);
            return btn;
        }

        /// <summary>
        /// Gives a control the interface's hover sound.
        ///
        /// An <see cref="EventTrigger"/> rather than Button's own transition
        /// hooks, because the rows that most want it — the menu entries, the
        /// back button — paint their hover by hand and never use Button's
        /// colour tint at all. Whether the sound is actually heard is
        /// <c>AudioManager.UiHoverEnabled</c>'s decision, not this one: the map
        /// editor switches it off, and it does so for controls that were built
        /// long before it had the chance.
        /// </summary>
        public static void AttachHoverSound(GameObject control)
        {
            if (control == null) return;
            var trigger = control.GetComponent<EventTrigger>();
            if (trigger == null) trigger = control.AddComponent<EventTrigger>();

            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entry.callback.AddListener(_ => IronMeridian.Audio.AudioManager.PlayHover(control));
            trigger.triggers.Add(entry);
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

        /// <summary>Width of the vertical scrollbar <see cref="CreateScrollView"/> can add.</summary>
        public const float ScrollbarWidth = 10f;

        /// <summary>
        /// Vertical scroll view. Pass <paramref name="withScrollbar"/> for a
        /// visible bar down the right edge.
        ///
        /// Worth having wherever the content can carry its own drag handlers:
        /// a card that starts a drag-to-deploy swallows the drag, so the list it
        /// sits in cannot be dragged to scroll, and the wheel becomes the only
        /// way to move it — which is invisible to anyone who does not try it.
        /// The bar is both the affordance and the fallback.
        /// </summary>
        /// <param name="autoHideScrollbar">
        /// Take the scrollbar off the screen while the content fits.
        ///
        /// A bar that is always there is a bar that says "there is more below"
        /// when there is not — on a list whose length depends on the window, on
        /// the map, or on what the player has built, that is a permanent small
        /// lie. Unity's own <c>AutoHide</c> does the work; the viewport keeps
        /// its inset either way, so nothing reflows as the bar comes and goes.
        /// </param>
        public static ScrollRect CreateScrollView(Transform parent, out RectTransform content,
            bool withScrollbar = false, bool autoHideScrollbar = false)
        {
            var root = CreatePanel(parent, "ScrollView", new Color(0, 0, 0, 0.25f));
            var scroll = root.gameObject.AddComponent<ScrollRect>();

            var viewport = CreatePanel(root, "Viewport", new Color(0, 0, 0, 0.01f));
            Stretch(viewport);
            if (withScrollbar) viewport.offsetMax = new Vector2(-ScrollbarWidth, 0);
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
            if (withScrollbar)
            {
                scroll.verticalScrollbar = CreateVerticalScrollbar(root);
                if (autoHideScrollbar)
                    scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            }
            return scroll;
        }

        /// <summary>Thin vertical scrollbar pinned to the right edge of a scroll view.</summary>
        static Scrollbar CreateVerticalScrollbar(RectTransform root)
        {
            var track = CreatePanel(root, "Scrollbar", new Color(1f, 1f, 1f, 0.05f));
            track.anchorMin = new Vector2(1, 0);
            track.anchorMax = new Vector2(1, 1);
            track.pivot = new Vector2(1, 0.5f);
            track.sizeDelta = new Vector2(ScrollbarWidth, 0);
            track.anchoredPosition = Vector2.zero;

            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;

            var slidingArea = CreateGroup(track, "SlidingArea");
            Stretch(slidingArea);
            slidingArea.offsetMin = new Vector2(2, 2);
            slidingArea.offsetMax = new Vector2(-2, -2);

            var handle = CreatePanel(slidingArea, "Handle", UiTheme.BorderStrong);
            handle.sizeDelta = Vector2.zero;

            bar.handleRect = handle;
            bar.targetGraphic = handle.GetComponent<Image>();

            var colors = bar.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            bar.colors = colors;
            return bar;
        }

        /// <summary>
        /// True while the player is typing into a text field.
        ///
        /// Every keyboard shortcut in the editor has to ask: W pans the camera,
        /// C faces a unit, Ctrl+Z undoes an edit, and all three are letters
        /// somebody renaming a formation will type. The check is here rather
        /// than in each of them because there is one answer and a dozen askers,
        /// and it goes through the EventSystem rather than a flag any one field
        /// has to remember to set.
        /// </summary>
        public static bool TextFieldFocused
        {
            get
            {
                var selected = EventSystem.current != null
                    ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == null) return false;
                var field = selected.GetComponent<InputField>();
                return field != null && field.isFocused;
            }
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
            AttachHoverSound(rt.gameObject);
            return btn;
        }

        /// <summary>Where every menu screen's BACK control sits: the top-right corner.</summary>
        public static readonly Vector2 BackAnchor = new Vector2(1f, 1f);
        public static readonly Vector2 BackPosition = new Vector2(-80f, -62f);
        public static readonly Vector2 BackSize = new Vector2(300f, 62f);

        /// <summary>
        /// The one BACK control every menu screen uses.
        ///
        /// It used to be <see cref="CreateButton"/> with a "&lt; BACK" caption —
        /// a flat slab whose only affordance was the word on it, sitting on
        /// full-bleed artwork where a mid-grey rectangle has nothing to separate
        /// it from the photograph behind. Three things fix that, and all three
        /// are borrowed from the main menu's own entries so the screens read as
        /// one interface: a **hairline-bordered surface** so the control has an
        /// edge of its own, an **accent strip** down its leading edge that
        /// widens under the cursor, and a **glyph** so the direction is legible
        /// before the label is read.
        ///
        /// **Placement is decided here, not by the caller.** The screens had
        /// drifted into three: top-right on the six data screens, bottom-centre
        /// on the placeholders, bottom-left on the single-player board. A
        /// control that moves between pages is a control the player has to find
        /// again on each one, which is the one thing a back button must never
        /// cost. The top-right corner wins because every menu screen puts its
        /// title top-left and its content below, so that corner is the only one
        /// free on all of them.
        ///
        /// The overrides remain for a screen that one day has a real reason;
        /// nothing passes them today, and "this screen is different" is not a
        /// reason.
        /// </summary>
        public static Button CreateBackButton(Transform parent, string label, UnityAction onClick,
            Vector2? anchor = null, Vector2? position = null, Vector2? size = null)
        {
            Vector2 a = anchor ?? BackAnchor;
            Vector2 p = position ?? BackPosition;
            Vector2 s = size ?? BackSize;

            var frame = CreateBorderedPanel(parent, "BackButton", UiTheme.Surface, UiTheme.BorderStrong);
            Place(frame, a, p, s);

            var btn = CreateButton(frame, "", onClick, new Color(0, 0, 0, 0), UiTheme.Text, 1);
            Stretch((RectTransform)btn.transform);
            // CreateButton always makes a caption; this control draws its own,
            // so the one it made is switched off rather than left to render an
            // empty string over the top of it.
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            var strip = CreatePanel(frame, "Strip", UiTheme.Accent);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.sizeDelta = new Vector2(4f, 0);
            strip.GetComponent<Image>().raycastTarget = false;

            var glyph = CreateImage(frame, UiIcons.ArrowLeft, "Glyph");
            glyph.color = UiTheme.Accent;
            glyph.raycastTarget = false;
            Place((RectTransform)glyph.transform, new Vector2(0f, 0.5f), new Vector2(24f, 0f),
                new Vector2(20f, 20f));

            var caption = CreateText(frame, label, 20, UiTheme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            // Named so SetBackButtonLabel can find it. A search by type would
            // find the hidden caption CreateButton makes first — it is deeper in
            // the hierarchy but earlier in the depth-first walk.
            caption.gameObject.name = BackLabelName;
            PlaceTopLeft(caption.rectTransform, 56f, (s.y - 24f) * 0.5f, s.x - 72f, 24f);
            Fit(caption, 11);

            var fill = frame.Find("Fill").GetComponent<Image>();
            var trigger = frame.gameObject.AddComponent<EventTrigger>();
            AddHover(trigger, EventTriggerType.PointerEnter, () => PaintBack(fill, strip, glyph, caption, true));
            AddHover(trigger, EventTriggerType.PointerExit, () => PaintBack(fill, strip, glyph, caption, false));
            PaintBack(fill, strip, glyph, caption, false);

            return btn;
        }

        const string BackLabelName = "BackLabel";

        /// <summary>
        /// Re-captions a back button made by <see cref="CreateBackButton"/>.
        /// Screens with more than one page back out to different places from
        /// each — a control that says BACK TO MAIN MENU while it goes to the
        /// campaign list is worse than one with no label at all.
        /// </summary>
        public static void SetBackButtonLabel(Button back, string label)
        {
            if (back == null || back.transform.parent == null) return;
            var caption = back.transform.parent.Find(BackLabelName);
            if (caption == null) return;
            var text = caption.GetComponent<Text>();
            if (text != null) text.text = label;
        }

        static void PaintBack(Image fill, RectTransform strip, Image glyph, Text caption, bool hover)
        {
            fill.color = hover ? UiTheme.SurfaceHover : UiTheme.Surface;
            strip.sizeDelta = new Vector2(hover ? 8f : 4f, 0);
            glyph.color = hover ? Color.white : UiTheme.Accent;
            caption.color = hover ? Color.white : UiTheme.Text;
        }

        static void AddHover(EventTrigger trigger, EventTriggerType type, System.Action callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => callback());
            trigger.triggers.Add(entry);
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

            // Follow the fallback chain: a screen names its own artwork and
            // borrows the shared image until that file exists. Bounded rather
            // than recursive, so a catalogue edit that makes a loop cannot hang
            // the screen it was meant to decorate.
            Sprite sprite = null;
            for (int hop = 0; hop < 4 && def != null; hop++)
            {
                sprite = LoadSprite(def.resourcePath);
                if (sprite != null) break;
                if (def.fallback == BackgroundId.None) break;
                def = BackgroundCatalog.Get(def.fallback);
            }

            if (sprite == null || def == null) return root;   // LoadSprite has already warned

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
        /// A panel that fades out horizontally — opaque at its left edge,
        /// transparent at its right.
        ///
        /// uGUI has no gradient fill, and the alternative — three or four
        /// stacked panels at stepping alphas — bands visibly on a dark
        /// photograph. One 64×1 texture stretched across the rect is smooth,
        /// costs a single draw call, and is built once and cached like the
        /// icons are.
        ///
        /// Used for the main menu's board field, so the interface sits on a
        /// darkened column that dissolves into the artwork rather than ending
        /// at a hard vertical seam.
        /// </summary>
        public static RectTransform CreateHorizontalFade(Transform parent, string name,
            Color color, float leftAlpha, float rightAlpha)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.sprite = FadeSprite();
            img.type = Image.Type.Simple;
            // Full alpha on the tint: the ramp is drawn per vertex by FadeTint,
            // and an alpha here would multiply on top of it.
            img.color = new Color(color.r, color.g, color.b, 1f);
            img.raycastTarget = false;

            go.AddComponent<FadeTint>().Set(img, leftAlpha, rightAlpha);

            return (RectTransform)go.transform;
        }

        /// <summary>
        /// Applies the right-hand alpha of a horizontal fade by driving the
        /// image's per-vertex colours. `Image` has no gradient of its own, and
        /// tinting alone cannot say "opaque here, clear there".
        /// </summary>
        class FadeTint : BaseMeshEffect
        {
            float _left = 1f, _right = 0f;

            public void Set(Image image, float left, float right)
            {
                _left = left; _right = right;
                if (image != null) image.SetVerticesDirty();
            }

            public override void ModifyMesh(VertexHelper helper)
            {
                if (!IsActive()) return;

                var rect = ((RectTransform)transform).rect;
                var vertex = new UIVertex();
                for (int i = 0; i < helper.currentVertCount; i++)
                {
                    helper.PopulateUIVertex(ref vertex, i);
                    float t = rect.width <= 0f ? 0f
                        : Mathf.Clamp01((vertex.position.x - rect.xMin) / rect.width);
                    var c = vertex.color;
                    c.a = (byte)Mathf.RoundToInt(Mathf.Lerp(_left, _right, t) * 255f);
                    vertex.color = c;
                    helper.SetUIVertex(vertex, i);
                }
            }
        }

        static Sprite _fadeSprite;

        /// <summary>A plain white 4×4 sprite — the fade's colour comes from the mesh tint.</summary>
        static Sprite FadeSprite()
        {
            if (_fadeSprite != null) return _fadeSprite;

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "FadeRamp" };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.Apply(false);
            tex.wrapMode = TextureWrapMode.Clamp;

            _fadeSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            return _fadeSprite;
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
