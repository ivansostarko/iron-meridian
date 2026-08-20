using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Core;

namespace IronMeridian.UI
{
    /// <summary>
    /// The main menu, laid out as a **command board down the left-hand edge**
    /// rather than as a stack of buttons across the middle.
    ///
    /// Three things drove the reorganisation:
    ///
    /// • **The artwork was being covered by the thing that needed it least.** A
    ///   centred column of six identical bars sat over the middle of a
    ///   full-bleed background, which is exactly where a photograph has its
    ///   subject. Moving the interface to one side leaves the picture visible
    ///   and gives the menu a place to grow into.
    ///
    /// • **Six unlabelled words are not a menu, they are a list.** "EXTRAS" and
    ///   "DEVELOPMENT" told a returning player nothing and a new one less. Every
    ///   entry now carries a one-line description of what is behind it, which is
    ///   the difference between a menu you read and a menu you guess at.
    ///
    /// • **The board scrolls.** The entries live in a scroll view rather than at
    ///   fixed offsets, so the list can outgrow the window instead of running
    ///   off the bottom of a short one — which is what a fixed column does the
    ///   moment a seventh entry is added or the game is played at 1280×720.
    ///
    /// The OPERATIONS / REFERENCE / SYSTEM group headings and the descriptive
    /// blurb under the masthead are **gone**. Each entry already states what it
    /// is on its own second line, so the headings were captions over captions,
    /// and between them the two devices cost roughly 150 px of the very
    /// vertical space the entries needed. QUIT keeps its place at the bottom of
    /// the list — position, not a heading, is what keeps it clear of the cursor's
    /// resting place.
    ///
    /// **The board itself lives in <see cref="MenuBoard"/>** — the field, the
    /// spine, the scrolling list and the entry rows, along with the hover paint
    /// and the artwork preview. It is shared with the EXTRAS board, because two
    /// copies of a layout are two layouts that drift: change a row height here
    /// and the other screen stops being the same program. What this file owns is
    /// what is particular to the main menu — the logo, the six destinations and
    /// the quit dialog.
    ///
    /// **The logo is artwork**, not a procedural shield beside the game's name
    /// set in the UI font under a two-part rule. See <see cref="BuildMasthead"/>.
    ///
    /// Built entirely at runtime like every other screen (golden rule 2), from
    /// <see cref="UIFactory"/> and the <see cref="UiTheme"/> palette.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout
        /// <summary>Logo block: top-left inset, and the width it is fitted into.</summary>
        const float LogoX = MenuBoard.BoardX + 20f, LogoTop = 74f, LogoWidth = 470f;

        /// <summary>Y the list starts at, measured from the top of the screen.</summary>
        const float MenuTop = -292f;
        /// <summary>Clear space under the list, above the version line.</summary>
        const float MenuBottom = 92f;

        GameObject _quitModal;
        ScreenBackdrop _backdrop;

        void Start()
        {
            IronMeridian.Audio.AudioManager.Apply();
            IronMeridian.Core.DisplaySettings.Apply();

            // The opening film, once per launch. The menu is built behind it
            // either way — the video's canvas sorts on top — so nothing here
            // waits on it except the music, which would otherwise play under a
            // film that has a score of its own.
            IntroVideoUI.PlayOnce(() =>
                IronMeridian.Audio.MusicManager.Play(IronMeridian.Audio.MusicTrack.MenuTheme));

            var canvas = UIFactory.CreateCanvas("MainMenuCanvas");

            // A light scrim overall: the board carries its own darker field, so
            // the artwork on the right stays as bright as it was authored.
            //
            // Through a backdrop rather than a plain call, because the entries
            // change it as the cursor crosses them — see MenuBoard.Entry.
            _backdrop = ScreenBackdrop.Attach(gameObject, canvas.transform,
                BackgroundId.Default, 0.42f);

            MenuBoard.BuildField(canvas.transform);
            BuildMasthead(canvas.transform);
            BuildMenu(canvas.transform);
            BuildFooter(canvas.transform);
            BuildQuitModal(canvas.transform);
        }

        // ---------------------------------------------------------- masthead

        /// <summary>
        /// The masthead is the logo artwork and nothing else.
        ///
        /// It used to be a procedural shield beside the game's name set in the
        /// UI font, with a two-part rule under it — a wordmark assembled at
        /// runtime out of the parts the interface happened to have. The real
        /// logo carries the name, the emblem and the strapline together, in the
        /// typeface they were drawn in, so all three of those devices go.
        ///
        /// Fitted by width with its aspect preserved: the artwork is 2140×735,
        /// and a logo stretched to a rect is the most obvious sign of a menu
        /// built by somebody who was not looking at it.
        /// </summary>
        void BuildMasthead(Transform parent)
        {
            var sprite = UIFactory.LoadSprite(LogoPath);
            if (sprite == null) return;      // LoadSprite has already warned

            var logo = UIFactory.CreateImage(parent, sprite, "Logo");
            logo.raycastTarget = false;
            logo.preserveAspect = true;

            float height = sprite.rect.width > 0f
                ? LogoWidth * (sprite.rect.height / sprite.rect.width)
                : LogoWidth * 0.34f;
            UIFactory.PlaceTopLeft((RectTransform)logo.transform, LogoX, LogoTop, LogoWidth, height);
        }

        /// <summary>Resources path of the wordmark — see docs/11-GAME-MENU.md §2.2.</summary>
        const string LogoPath = "Graphics/Logo/game-logo";

        // -------------------------------------------------------------- menu

        /// <summary>
        /// The entries, in a scroll view spanning the board between the masthead
        /// and the footer.
        ///
        /// The order is the argument the groups used to make, and it makes it
        /// without spending a row: play first, because it is what most people
        /// opened the program to do; reference next; system last, so QUIT sits
        /// at the far end of the list from where the cursor comes to rest.
        /// </summary>
        void BuildMenu(Transform parent)
        {
            var content = MenuBoard.BuildList(parent, MenuTop, MenuBottom);

            // Every entry shows where it leads as the cursor crosses it. The
            // menu says where you are going before you go there, and a still
            // photograph of the place does that better than a line of text
            // under a heading.
            MenuBoard.Entry(content, UiIcons.Layers, "SINGLE PLAYER",
                "Fight a scenario against the enemy commander",
                () => SceneManager.LoadScene(GameConfig.SceneSinglePlayer),
                _backdrop, BackgroundId.SinglePlayer);
            MenuBoard.Entry(content, UiIcons.Swords, "MULTIPLAYER",
                "Take one side against another commander",
                () => SceneManager.LoadScene(GameConfig.SceneMultiplayer),
                _backdrop, BackgroundId.Multiplayer);
            MenuBoard.Entry(content, UiIcons.Blueprint, "DEVELOPMENT",
                "The map editor and the reference labs — units, effects and audio",
                () => SceneManager.LoadScene(GameConfig.SceneTesting),
                _backdrop, BackgroundId.Development);
            MenuBoard.Entry(content, UiIcons.Folder, "EXTRAS",
                "Background material, credits and reference reading",
                () => SceneManager.LoadScene(GameConfig.SceneExtras),
                _backdrop, BackgroundId.Extras);
            MenuBoard.Entry(content, UiIcons.Gear, "SETTINGS",
                "Display, audio and the controls",
                () => SceneManager.LoadScene(GameConfig.SceneSettings),
                _backdrop, BackgroundId.SettingsPreview);
            MenuBoard.Entry(content, UiIcons.Exit, "QUIT",
                "Leave Iron Meridian", ShowQuitModal,
                _backdrop, BackgroundId.Quit);
        }

        // ------------------------------------------------------------ footer

        /// <summary>
        /// The version line, and the small accent tick that marks the foot of
        /// the board's spine.
        ///
        /// Quiet on purpose: a build number is for the person filing a bug, not
        /// for the person about to play, so it is the faintest text on the
        /// screen and sits under everything else. The tick is what stops it
        /// floating — it ties the line back to the rule the whole column hangs
        /// off.
        /// </summary>
        void BuildFooter(Transform parent)
        {
            MenuBoard.BuildFooter(parent, $"{GameConfig.GameName}   ·   {GameConfig.Version}");

            // No "Esc — quit" line. QUIT is the last row of the list, in words,
            // where somebody looking for the way out will look — a shortcut
            // caption under it was a second answer to a question the menu had
            // already answered. Escape still works; see Update.
        }

        // ------------------------------------------------------------- modal

        void BuildQuitModal(Transform canvas)
        {
            var overlay = UIFactory.CreatePanel(canvas, "QuitModal", new Color(0, 0, 0, 0.78f));
            UIFactory.Stretch(overlay);
            overlay.gameObject.AddComponent<Button>()  // click outside = cancel
                .onClick.AddListener(HideQuitModal);

            var box = UIFactory.CreateBorderedPanel(overlay, "Box", UiTheme.Panel, UiTheme.BorderStrong);
            UIFactory.Place(box, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620, 268));

            // Swallows clicks that land on the box so they do not reach the
            // dismiss handler on the overlay behind it.
            box.gameObject.AddComponent<Button>();

            var txt = UIFactory.CreateText(box, "QUIT IRON MERIDIAN?", 32, UiTheme.Text,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(txt.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -58), new Vector2(560, 50));

            var sub = UIFactory.CreateText(box, "Any unsaved map changes will be lost.",
                18, UiTheme.TextDim);
            UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -108), new Vector2(560, 34));

            var yes = UIFactory.CreateButton(box, "QUIT", QuitGame, UiTheme.Danger, Color.white, 22);
            UIFactory.Place((RectTransform)yes.transform, new Vector2(0.5f, 0f),
                new Vector2(-118, 44), new Vector2(210, 60));

            var no = UIFactory.CreateButton(box, "CANCEL", HideQuitModal, UiTheme.Surface, UiTheme.Text, 22);
            UIFactory.Place((RectTransform)no.transform, new Vector2(0.5f, 0f),
                new Vector2(118, 44), new Vector2(210, 60));

            _quitModal = overlay.gameObject;
            _quitModal.SetActive(false);
        }

        void ShowQuitModal() => _quitModal.SetActive(true);
        void HideQuitModal() => _quitModal.SetActive(false);

        void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Update()
        {
            // While the opening film is up, every key belongs to it — Escape
            // skips the video rather than opening the quit dialog behind it.
            if (IntroVideoUI.Showing) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_quitModal != null && _quitModal.activeSelf) HideQuitModal();
                else ShowQuitModal();
            }
        }
    }
}
