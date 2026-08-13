using UnityEngine;

namespace IronMeridian.UI
{
    /// <summary>
    /// Design tokens for the map editor's command-console look: near-black
    /// surfaces, hairline borders, one blue accent, and colour reserved for
    /// meaning (blue = friendly, red = hostile/destructive, green = go).
    ///
    /// Deliberately separate from <see cref="Core.GameConfig"/>'s `Ui*` palette,
    /// which the menu screens still use. Restyling the whole game is then a
    /// decision to make on purpose, not a side effect of touching the HUD.
    /// </summary>
    public static class UiTheme
    {
        // ---------------------------------------------------------- surfaces
        /// <summary>Behind everything; also the letterbox around the map.</summary>
        public static readonly Color AppBackground = new Color(0.039f, 0.055f, 0.078f, 1f);
        /// <summary>Top command bar.</summary>
        public static readonly Color Chrome = new Color(0.051f, 0.078f, 0.110f, 0.98f);
        /// <summary>Side panels.</summary>
        public static readonly Color Panel = new Color(0.055f, 0.086f, 0.125f, 0.98f);
        /// <summary>Cards, inputs, buttons sitting on a panel.</summary>
        public static readonly Color Surface = new Color(0.086f, 0.125f, 0.169f, 1f);
        public static readonly Color SurfaceHover = new Color(0.114f, 0.161f, 0.216f, 1f);
        /// <summary>Faint fill for list rows, so they read as a table without lines.</summary>
        public static readonly Color SurfaceSubtle = new Color(1f, 1f, 1f, 0.028f);

        // ---------------------------------------------------------- borders
        /// <summary>Hairline separating surfaces — the console look lives here.</summary>
        public static readonly Color Border = new Color(0.118f, 0.169f, 0.224f, 1f);
        public static readonly Color BorderStrong = new Color(0.157f, 0.227f, 0.302f, 1f);

        // ------------------------------------------------------------- text
        public static readonly Color Text = new Color(0.906f, 0.929f, 0.953f, 1f);
        public static readonly Color TextDim = new Color(0.533f, 0.588f, 0.647f, 1f);
        public static readonly Color TextFaint = new Color(0.373f, 0.435f, 0.494f, 1f);

        // ---------------------------------------------------------- accents
        public static readonly Color Accent = new Color(0.180f, 0.506f, 0.941f, 1f);
        /// <summary>Accent wash behind a selected row.</summary>
        public static readonly Color AccentWash = new Color(0.180f, 0.506f, 0.941f, 0.14f);
        public static readonly Color Friendly = new Color(0.118f, 0.435f, 0.851f, 1f);
        public static readonly Color Hostile = new Color(0.729f, 0.192f, 0.220f, 1f);
        /// <summary>START BATTLE — the one green in the interface.</summary>
        public static readonly Color Success = new Color(0.106f, 0.631f, 0.361f, 1f);
        /// <summary>Battle running: the same button becomes a stop control.</summary>
        public static readonly Color Warning = new Color(0.706f, 0.412f, 0.129f, 1f);
        /// <summary>REMOVE UNIT and other destructive actions.</summary>
        public static readonly Color Danger = new Color(0.612f, 0.161f, 0.176f, 1f);

        // ----------------------------------------------------------- layout
        public const float TopBarHeight = 68f;
        public const float LeftPanelWidth = 274f;
        public const float RightPanelWidth = 300f;
        /// <summary>Standard inset from a panel's edge to its content.</summary>
        public const float PanelPadding = 12f;
        public const float ControlHeight = 34f;
        public const float RowHeight = 30f;

        // ------------------------------------------------------- type sizes
        public const int FontTitle = 20;
        public const int FontHeading = 15;
        public const int FontBody = 13;
        public const int FontSmall = 12;
        /// <summary>Section headers: small, bold, wide-spaced, accent-coloured.</summary>
        public const int FontLabel = 11;

        /// <summary>
        /// Letter-spaced small caps, the way the mockup's section headers read.
        /// uGUI's legacy Text has no letter-spacing, so space the characters.
        /// </summary>
        public static string Spaced(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new System.Text.StringBuilder(s.Length * 2);
            foreach (char c in s.ToUpperInvariant())
            {
                sb.Append(c);
                sb.Append(' ');
            }
            return sb.ToString().TrimEnd();
        }
    }
}
