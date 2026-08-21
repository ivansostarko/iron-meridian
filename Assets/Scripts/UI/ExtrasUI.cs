using UnityEngine;
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
    /// **It is the main menu's board with different rows on it.** The screen
    /// used to be three bordered cards centred over a dark interior — a second
    /// interface, reached from the first, that happened to do the same job.
    /// Walking from a menu into a submenu should feel like turning a page, not
    /// like opening another program, so the board comes from
    /// <see cref="MenuBoard"/>: the same field down the left-hand edge, the
    /// same spine, the same flat rows with their accent strips, and the same
    /// hover that swaps the artwork to a picture of where the row leads.
    ///
    /// **Each row has its own picture** — UNITS, DLC and CREDITS all show one
    /// as the cursor crosses them, and the destination screens show the same
    /// image again when you get there. That is what makes the preview read as a
    /// promise rather than as decoration. See
    /// <see cref="BackgroundId.ExtrasUnits"/> and its two neighbours.
    ///
    /// **Why the unit reference is here and not only under DEVELOPMENT.** The
    /// two screens answer different questions for different people. DEVELOPMENT
    /// → UNITS LIST is a data table you can *edit*: every field of every
    /// catalogue, sortable, tunable, saved to your own file. This is a *reader's*
    /// encyclopaedia — pick an arm, browse what it fields, look at the model. A
    /// player wanting to know what a Bradley is should not have to walk through
    /// a screen whose first affordance is EDIT.
    /// </summary>
    public class ExtrasUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout

        /// <summary>Masthead: where the page's name sits on the board.</summary>
        const float TitleX = MenuBoard.BoardX + 20f, TitleTop = 96f;

        /// <summary>Y the list starts at, measured from the top of the screen.</summary>
        const float ListTop = -256f;
        /// <summary>Clear space under the list, above the footer line.</summary>
        const float ListBottom = 92f;

        ScreenBackdrop _backdrop;

        void Start()
        {
            AudioManager.Apply();
            MusicManager.Play(MusicTrack.ExtrasTheme);

            var canvas = UIFactory.CreateCanvas("ExtrasCanvas");

            // The main menu's own scrim, and the picture its EXTRAS row
            // promised. Arriving on a different image would make the preview a
            // lie; arriving on the interior wash would make the screen a
            // different product.
            //
            // Through a backdrop rather than a plain call, because the rows
            // change it as the cursor crosses them — see MenuBoard.Entry.
            _backdrop = ScreenBackdrop.Attach(gameObject, canvas.transform,
                BackgroundId.Extras, 0.42f);

            MenuBoard.BuildField(canvas.transform);
            BuildMasthead(canvas.transform);
            BuildMenu(canvas.transform);
            MenuBoard.BuildFooter(canvas.transform, "EXTRAS   ·   REFERENCE AND CREDITS");

            UIFactory.CreateBackButton(canvas.transform, "BACK TO MAIN MENU", GoBack);
        }

        // ---------------------------------------------------------- masthead

        /// <summary>
        /// The page's name over one line of what the page is for.
        ///
        /// Set in the interface typeface rather than in artwork: the logo
        /// belongs to the front door, and repeating it on every room behind it
        /// would stop it meaning anything. The board is the thing that carries
        /// the family resemblance.
        /// </summary>
        void BuildMasthead(Transform parent)
        {
            var title = UIFactory.CreateText(parent, "EXTRAS", 56, UiTheme.Accent,
                TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.PlaceTopLeft(title.rectTransform, TitleX, TitleTop,
                MenuBoard.BoardWidth - 40f, 66f);

            var sub = UIFactory.CreateText(parent,
                "Reference material, additional content and the people who built it.",
                17, UiTheme.TextDim, TextAnchor.UpperLeft);
            UIFactory.PlaceTopLeft(sub.rectTransform, TitleX, TitleTop + 74f,
                MenuBoard.BoardWidth - 40f, 48f);

            // The rule the list hangs off, level with the bottom of the
            // masthead — the same device the main menu gets from its logo's
            // baseline, which this page has no artwork to borrow.
            var rule = UIFactory.CreatePanel(parent, "MastheadRule", MenuBoard.HairLine);
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(0, 1);
            rule.pivot = new Vector2(0, 1);
            rule.sizeDelta = new Vector2(MenuBoard.BoardWidth - 40f, 1f);
            rule.anchoredPosition = new Vector2(TitleX, -(TitleTop + 138f));
            rule.GetComponent<Image>().raycastTarget = false;
        }

        // -------------------------------------------------------------- menu

        void BuildMenu(Transform parent)
        {
            var content = MenuBoard.BuildList(parent, ListTop, ListBottom);

            MenuBoard.Entry(content, UiIcons.Shield, "UNITS",
                "Every formation type both sides field, by arm of service — and the rear area behind them",
                () => SceneManager.LoadScene(GameConfig.SceneUnitLibrary),
                _backdrop, BackgroundId.ExtrasUnits);
            MenuBoard.Entry(content, UiIcons.Layers, "DLC",
                "Additional content",
                () => SceneManager.LoadScene(GameConfig.SceneDlc),
                _backdrop, BackgroundId.ExtrasDlc);
            MenuBoard.Entry(content, UiIcons.Info, "CREDITS",
                "Who built Iron Meridian, and what it was built from",
                () => SceneManager.LoadScene(GameConfig.SceneCredits),
                _backdrop, BackgroundId.ExtrasCredits);
        }

        void GoBack() => SceneManager.LoadScene(GameConfig.SceneMainMenu);

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) GoBack();
        }
    }
}
