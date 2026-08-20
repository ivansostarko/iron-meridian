using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronMeridian.Audio;
using IronMeridian.Core;
using IronMeridian.Data;

namespace IronMeridian.UI
{
    /// <summary>
    /// CREDITS — who built Iron Meridian, and what it was built from.
    ///
    /// **A roll, not a page.** The credits are laid out as a single centred
    /// column that scrolls, the way a film's are and the way every player
    /// already expects them to be. That is not decoration: a credits screen is
    /// read down, one role at a time, and a two-column grid or a set of cards
    /// makes the reader choose an order the list does not have.
    ///
    /// **The column is narrow on purpose.** 640 px of a 1920 px screen. Names
    /// are short lines, and a short line set across the full width of a monitor
    /// puts a metre of whitespace between the role and the person who held it —
    /// the two things the reader is trying to associate.
    ///
    /// **Roles are set against their names, not above them.** The role is small,
    /// dim and right-aligned in the left half; the names are larger, bright and
    /// left-aligned in the right half, sharing a centre line. It is the layout a
    /// film's end roll uses, for the reason a film uses it: the eye runs down
    /// the seam and both columns stay findable without a rule between them.
    ///
    /// **Everything on it comes from `Data/credits.json`** — see
    /// <see cref="CreditsData"/>. Adding a person is a text edit, not a code
    /// change, which is the only arrangement that survives a project where
    /// people join between builds.
    ///
    /// Built at runtime like every other screen (golden rule 2). See
    /// docs/44-CREDITS.md.
    /// </summary>
    public class CreditsUI : MonoBehaviour
    {
        // ------------------------------------------------------------ layout

        /// <summary>Width of the roll. Narrow — see the class remarks.</summary>
        const float ColumnWidth = 660f;
        /// <summary>The seam the two halves meet at, as a fraction of the column.</summary>
        const float SeamFraction = 0.42f;
        /// <summary>Gap either side of the seam, px.</summary>
        const float SeamGap = 18f;

        const float TitleTop = 96f;
        /// <summary>Where the scrolling roll starts and stops, measured from the screen edges.</summary>
        const float RollTop = -232f, RollBottom = 96f;

        const float SectionHeadingHeight = 62f;
        const float RoleRowHeight = 26f;
        const float RoleGap = 14f;

        void Start()
        {
            AudioManager.Apply();
            MusicManager.Play(MusicTrack.ExtrasTheme);

            var canvas = UIFactory.CreateCanvas("CreditsCanvas");
            // The picture the EXTRAS board promised behind its CREDITS row, held
            // back further than that board holds it: this screen is a column of
            // small text read at length, and the artwork is a backdrop to it
            // rather than the subject.
            UIFactory.CreateScreenBackground(canvas.transform, BackgroundId.ExtrasCredits, 0.80f);

            var credits = CreditsData.Load();

            BuildMasthead(canvas.transform, credits);
            BuildRoll(canvas.transform, credits);

            UIFactory.CreateBackButton(canvas.transform, "BACK TO EXTRAS", GoBack);
        }

        // ---------------------------------------------------------- masthead

        /// <summary>
        /// The game's name over one line of what it is, with a rule under both.
        ///
        /// Fixed rather than scrolling with the roll: it is the answer to "whose
        /// credits are these", and an answer that scrolls off the top is one the
        /// reader has to scroll back for.
        /// </summary>
        void BuildMasthead(Transform parent, CreditsData credits)
        {
            var title = UIFactory.CreateText(parent,
                string.IsNullOrEmpty(credits.title) ? GameConfig.GameName.ToUpperInvariant() : credits.title,
                52, UiTheme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f),
                new Vector2(0, -TitleTop), new Vector2(1200, 64));
            UIFactory.Fit(title, 28);

            if (!string.IsNullOrEmpty(credits.subtitle))
            {
                var sub = UIFactory.CreateText(parent, credits.subtitle, 19,
                    UiTheme.TextDim, TextAnchor.MiddleCenter);
                UIFactory.Place(sub.rectTransform, new Vector2(0.5f, 1f),
                    new Vector2(0, -TitleTop - 52f), new Vector2(1200, 28));
            }

            var rule = UIFactory.CreatePanel(parent, "MastheadRule", new Color(1f, 1f, 1f, 0.10f));
            UIFactory.Place(rule, new Vector2(0.5f, 1f), new Vector2(0, -TitleTop - 92f),
                new Vector2(ColumnWidth, 1));
            rule.GetComponent<Image>().raycastTarget = false;

            // A small accent tick centred on the rule. The one piece of colour
            // between the masthead and the roll, and what stops the rule reading
            // as the bottom of a window.
            var tick = UIFactory.CreatePanel(parent, "MastheadTick", UiTheme.Accent);
            UIFactory.Place(tick, new Vector2(0.5f, 1f), new Vector2(0, -TitleTop - 89f),
                new Vector2(46, 3));
            tick.GetComponent<Image>().raycastTarget = false;
        }

        // -------------------------------------------------------------- roll

        void BuildRoll(Transform parent, CreditsData credits)
        {
            var scroll = UIFactory.CreateScrollView(parent, out RectTransform content,
                withScrollbar: true, autoHideScrollbar: true);
            scroll.GetComponent<Image>().color = new Color(0, 0, 0, 0);

            var rt = (RectTransform)scroll.transform;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(ColumnWidth + UIFactory.ScrollbarWidth + 8f, 0f);
            rt.offsetMin = new Vector2(rt.offsetMin.x, RollBottom);
            rt.offsetMax = new Vector2(rt.offsetMax.x, RollTop);

            var layout = content.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = 0;
                layout.padding = new RectOffset(0, 0, 8, 40);
            }

            foreach (var section in credits.sections)
            {
                if (section == null) continue;
                SectionHeading(content, section.heading);
                if (section.roles == null) continue;
                foreach (var role in section.roles) RoleBlock(content, role);
                Spacer(content, 26f);
            }

            BuildAcknowledgements(content, credits);
            BuildWebsite(content, credits);
            BuildCopyright(content, credits);
        }

        /// <summary>
        /// A block heading — PRODUCTION, AUDIO — centred over its roles with a
        /// hairline under it.
        ///
        /// Centred while the roles below are set against a seam, deliberately:
        /// the heading is a label for the block and belongs to the whole width,
        /// and centring it is what makes the seam read as a device inside the
        /// block rather than as the page's own alignment.
        /// </summary>
        void SectionHeading(RectTransform content, string heading)
        {
            var row = UIFactory.CreatePanel(content, "Section_" + heading, new Color(0, 0, 0, 0));
            row.sizeDelta = new Vector2(0, SectionHeadingHeight);
            row.GetComponent<Image>().raycastTarget = false;

            var text = UIFactory.CreateText(row, heading, 15, UiTheme.Accent,
                TextAnchor.LowerCenter, FontStyle.Bold);
            text.raycastTarget = false;
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = new Vector2(0, 16f);
            trt.offsetMax = new Vector2(0, -18f);

            var rule = UIFactory.CreatePanel(row, "Rule", new Color(1f, 1f, 1f, 0.07f));
            rule.anchorMin = new Vector2(0.5f, 0); rule.anchorMax = new Vector2(0.5f, 0);
            rule.pivot = new Vector2(0.5f, 0);
            rule.sizeDelta = new Vector2(180f, 1f);
            rule.anchoredPosition = new Vector2(0, 8f);
            rule.GetComponent<Image>().raycastTarget = false;
        }

        /// <summary>
        /// One role and everyone who held it: the title on the left of the seam,
        /// the names stacked on the right.
        ///
        /// The row grows with the number of names rather than each name being
        /// its own row with the title repeated. Repeating "Programmers" three
        /// times would say there were three jobs.
        /// </summary>
        void RoleBlock(RectTransform content, CreditRole role)
        {
            if (role == null || string.IsNullOrEmpty(role.role)) return;

            int count = role.names != null ? Mathf.Max(1, role.names.Count) : 1;
            float height = count * RoleRowHeight + RoleGap;

            var row = UIFactory.CreatePanel(content, "Role_" + role.role, new Color(0, 0, 0, 0));
            row.sizeDelta = new Vector2(0, height);
            row.GetComponent<Image>().raycastTarget = false;

            float seam = ColumnWidth * SeamFraction;

            var title = UIFactory.CreateText(row, role.role.ToUpperInvariant(), 13,
                UiTheme.TextFaint, TextAnchor.UpperRight, FontStyle.Bold);
            title.raycastTarget = false;
            var lrt = title.rectTransform;
            lrt.anchorMin = new Vector2(0, 1); lrt.anchorMax = new Vector2(0, 1);
            lrt.pivot = new Vector2(0, 1);
            lrt.anchoredPosition = new Vector2(0, -4f);
            lrt.sizeDelta = new Vector2(seam - SeamGap, RoleRowHeight);

            var names = UIFactory.CreateText(row,
                role.names == null || role.names.Count == 0 ? "—" : string.Join("\n", role.names),
                17, UiTheme.Text, TextAnchor.UpperLeft);
            names.raycastTarget = false;
            names.lineSpacing = RoleRowHeight / 17f;
            var nrt = names.rectTransform;
            nrt.anchorMin = new Vector2(0, 1); nrt.anchorMax = new Vector2(0, 1);
            nrt.pivot = new Vector2(0, 1);
            nrt.anchoredPosition = new Vector2(seam + SeamGap, 0f);
            nrt.sizeDelta = new Vector2(ColumnWidth - seam - SeamGap, count * RoleRowHeight);
        }

        void BuildAcknowledgements(RectTransform content, CreditsData credits)
        {
            if (credits.acknowledgements == null || credits.acknowledgements.Count == 0) return;

            SectionHeading(content, "WITH THANKS TO");

            foreach (var line in credits.acknowledgements)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var text = UIFactory.CreateText(content, line, 15, UiTheme.TextDim,
                    TextAnchor.UpperCenter);
                text.raycastTarget = false;
                ((RectTransform)text.transform).sizeDelta = new Vector2(0, 26);
            }
            Spacer(content, 30f);
        }

        /// <summary>
        /// The address, given a block of its own.
        ///
        /// It is the one line on this screen a reader might want to act on, and
        /// burying it in a paragraph of thanks is how a link goes unread. Set in
        /// the accent colour at the size of a heading, with air round it.
        /// </summary>
        void BuildWebsite(RectTransform content, CreditsData credits)
        {
            if (string.IsNullOrEmpty(credits.website)) return;

            var rule = UIFactory.CreatePanel(content, "WebRule", new Color(1f, 1f, 1f, 0.07f));
            rule.sizeDelta = new Vector2(0, 1);
            rule.GetComponent<Image>().raycastTarget = false;

            Spacer(content, 26f);

            var text = UIFactory.CreateText(content, credits.website, 24, UiTheme.Accent,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            text.raycastTarget = false;
            ((RectTransform)text.transform).sizeDelta = new Vector2(0, 34);

            Spacer(content, 22f);
        }

        void BuildCopyright(RectTransform content, CreditsData credits)
        {
            string line = string.IsNullOrEmpty(credits.copyright)
                ? $"{GameConfig.GameName}  ·  {GameConfig.Version}"
                : $"{credits.copyright}\n{GameConfig.GameName}  ·  {GameConfig.Version}";

            var text = UIFactory.CreateText(content, line, 13, UiTheme.TextFaint,
                TextAnchor.UpperCenter);
            text.raycastTarget = false;
            ((RectTransform)text.transform).sizeDelta = new Vector2(0, 44);
        }

        static void Spacer(RectTransform content, float height)
        {
            var gap = UIFactory.CreatePanel(content, "Gap", new Color(0, 0, 0, 0));
            gap.sizeDelta = new Vector2(0, height);
            gap.GetComponent<Image>().raycastTarget = false;
        }

        void GoBack() => SceneManager.LoadScene(GameConfig.SceneExtras);

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) GoBack();
        }
    }
}
