using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Audio;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// EXTRAS — the material around the game rather than the game itself.
    ///
    ///   UNITS   -> the encyclopaedia: every formation type by arm of service,
    ///              with filters, full data and its 3D model
    ///   DLC     -> nothing yet
    ///   CREDITS -> nothing yet
    ///
    /// **Why the unit reference is here and not only under DEVELOPMENT.** The
    /// two screens answer different questions for different people. DEVELOPMENT
    /// → UNITS LIST is a data table you can *edit*: every field of every
    /// catalogue, sortable, tunable, saved to your own file. This is a *reader's*
    /// encyclopaedia — pick an arm, browse what it fields, look at the model. A
    /// player wanting to know what a Bradley is should not have to walk through
    /// a screen whose first affordance is EDIT.
    ///
    /// It was a placeholder page; it is a real screen now, so it lives in its own
    /// file rather than at the bottom of <see cref="PlaceholderScreenUI"/>.
    /// </summary>
    public class ExtrasUI : MonoBehaviour
    {
        const float EntryHeight = 84f, EntryGap = 10f;
        const float ListTop = -260f, ListWidth = 720f;

        void Start()
        {
            AudioManager.Apply();
            MusicManager.Play(MusicTrack.ExtrasTheme);

            var canvas = UIFactory.CreateCanvas("ExtrasCanvas");
            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.Interior);

            var title = UIFactory.CreateText(canvas.transform, "EXTRAS", 56,
                GameConfig.UiAccent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0, -120), new Vector2(1200, 80));

            var sub = UIFactory.CreateText(canvas.transform,
                "Reference material, additional content and the people who built it.",
                22, GameConfig.UiTextDim, TextAnchor.MiddleCenter);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0, -184), new Vector2(1200, 34));

            Entry(canvas.transform, 0, UiIcons.Shield, "UNITS",
                "Every formation type both sides field, by arm of service — with its data and its model.",
                GameConfig.SceneUnitLibrary);

            Entry(canvas.transform, 1, UiIcons.Layers, "DLC",
                "Additional content.",
                GameConfig.SceneDlc);

            Entry(canvas.transform, 2, UiIcons.Info, "CREDITS",
                "Who built Iron Meridian, and what it was built from.",
                GameConfig.SceneCredits);

            UIFactory.CreateBackButton(canvas.transform, "BACK TO MAIN MENU", GoBack,
                new Vector2(0.5f, 0f), new Vector2(0, 90), new Vector2(380, 66));
        }

        /// <summary>
        /// One row. Same three devices as the main menu's entries — bordered
        /// surface, accent strip, glyph — so the screens read as one interface.
        /// </summary>
        void Entry(Transform parent, int index, Sprite glyph, string label, string detail, string scene)
        {
            var frame = UIFactory.CreateBorderedPanel(parent, "Extra_" + label,
                UiTheme.Surface, UiTheme.Border);
            UIFactory.Place(frame, new Vector2(0.5f, 1f),
                new Vector2(0, ListTop - index * (EntryHeight + EntryGap)),
                new Vector2(ListWidth, EntryHeight));
            frame.pivot = new Vector2(0.5f, 1f);

            var btn = UIFactory.CreateButton(frame, "",
                () => SceneManager.LoadScene(scene), new Color(0, 0, 0, 0), UiTheme.Text, 1);
            UIFactory.Stretch((RectTransform)btn.transform);
            var made = btn.GetComponentInChildren<Text>(true);
            if (made != null) made.gameObject.SetActive(false);

            var strip = UIFactory.CreatePanel(frame, "Strip", GameConfig.UiAccent);
            strip.anchorMin = new Vector2(0, 0); strip.anchorMax = new Vector2(0, 1);
            strip.pivot = new Vector2(0, 0.5f);
            strip.sizeDelta = new Vector2(4f, 0);
            strip.GetComponent<Image>().raycastTarget = false;

            var icon = UIFactory.CreateImage(frame, glyph, "Glyph");
            icon.color = GameConfig.UiAccent;
            icon.raycastTarget = false;
            UIFactory.Place((RectTransform)icon.transform, new Vector2(0f, 0.5f),
                new Vector2(34, 0), new Vector2(30, 30));

            var t = UIFactory.CreateText(frame, label, 28, GameConfig.UiText,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(t.rectTransform, 84f, 16f, ListWidth - 110f, 32f);
            UIFactory.Fit(t, 16);

            var d = UIFactory.CreateText(frame, detail, 17, GameConfig.UiTextDim, TextAnchor.MiddleLeft);
            UIFactory.PlaceTopLeft(d.rectTransform, 84f, 48f, ListWidth - 110f, 24f);
            UIFactory.Fit(d, 12);

            var fill = frame.Find("Fill").GetComponent<Image>();
            var trigger = frame.gameObject.AddComponent<EventTrigger>();
            AddHover(trigger, EventTriggerType.PointerEnter, () => Paint(fill, strip, icon, true));
            AddHover(trigger, EventTriggerType.PointerExit, () => Paint(fill, strip, icon, false));
            Paint(fill, strip, icon, false);
        }

        static void Paint(Image fill, RectTransform strip, Image glyph, bool hover)
        {
            fill.color = hover ? UiTheme.SurfaceHover : UiTheme.Surface;
            strip.sizeDelta = new Vector2(hover ? 8f : 4f, 0);
            glyph.color = hover ? Color.white : GameConfig.UiAccent;
        }

        static void AddHover(EventTrigger trigger, EventTriggerType type, System.Action callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => callback());
            trigger.triggers.Add(entry);
        }

        void GoBack() => SceneManager.LoadScene(GameConfig.SceneMainMenu);

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) GoBack();
        }
    }
}
