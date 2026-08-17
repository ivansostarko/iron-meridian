using System.Collections.Generic;

namespace IronMeridian.UI
{
    /// <summary>
    /// Every full-screen background image, named by role rather than by file.
    /// </summary>
    public enum BackgroundId
    {
        /// <summary>No image — the flat UI colour only.</summary>
        None,
        /// <summary>The shared menu//screen artwork.</summary>
        Default,
        /// <summary>Single-player campaign board — the list of theatres.</summary>
        SinglePlayer,
        /// <summary>Multiplayer lobby screen.</summary>
        Multiplayer,
        /// <summary>
        /// What the main menu shows behind its EXTRAS row. The Extras *screen*
        /// itself uses <see cref="Interior"/> with the rest of the inner pages —
        /// this is the preview, and a preview is part of the main menu.
        /// </summary>
        Extras,

        // One per campaign, shown while its mission board is open. The theatre
        // is the thing being chosen, so the screen changes with it rather than
        // holding one picture behind six different lists.
        CampaignEurope,
        CampaignAfrica,
        CampaignAsia,
        CampaignNorthAmerica,
        CampaignSouthAmerica,
        CampaignAustralia,

        /// <summary>
        /// The inner screens — settings, extras, and the pages behind extras.
        /// One image for the family rather than five ids naming one file: they
        /// are the pages you pass *through*, and giving each its own artwork
        /// would make the menu read as five different products.
        /// </summary>
        Interior
    }

    /// <summary>One background: where to load it and how far to knock it back.</summary>
    public class BackgroundDef
    {
        public BackgroundId id;

        /// <summary>Resources path, without extension. Must live under an <c>Assets/Resources</c> folder.</summary>
        public string resourcePath;

        /// <summary>
        /// Opacity of the dark scrim laid over the image. Artwork behind live UI
        /// has to lose contrast or the text stops being readable; this is the
        /// dial for how much of the art survives.
        /// </summary>
        public float scrimAlpha;

        /// <summary>What this background is for — mirrored in docs/11-GAME-MENU.md.</summary>
        public string description;

        /// <summary>
        /// Background to fall back to when this one has no image file yet.
        ///
        /// A screen is entitled to its own artwork, and naming it here is how
        /// that artwork gets picked up the moment somebody drops the file in —
        /// but a screen with no art at all is a flat colour, which looks broken
        /// rather than unfinished. The fallback keeps the shared artwork on
        /// screen until the real thing arrives, with no code change at either end.
        /// </summary>
        public BackgroundId fallback = BackgroundId.None;
    }

    /// <summary>
    /// The register of background artwork in code. Keep it in step with
    /// docs/11-GAME-MENU.md, the human-readable version of this table.
    /// </summary>
    public static class BackgroundCatalog
    {
        /// <summary>Scrim used on data-dense screens, where legibility beats atmosphere.</summary>
        public const float DenseScreenScrim = 0.86f;

        /// <summary>
        /// Scrim for loading screens. Lighter than a working screen: there is
        /// little text to read and the artwork is the point while waiting.
        /// </summary>
        public const float LoaderScrim = 0.48f;

        static readonly BackgroundDef[] Defs =
        {
            new BackgroundDef
            {
                id = BackgroundId.Default,
                resourcePath = "Graphics/Backgrounds/default_background",
                scrimAlpha = 0.62f,
                description = "Shared artwork behind every menu screen: main menu, settings, " +
                              "testing, units list and the placeholder pages."
            },

            // Every row below names its own file and falls back if it is not
            // there. Drop a file at the path named here and it is used
            // automatically — see docs/11-GAME-MENU.md.
            new BackgroundDef
            {
                id = BackgroundId.SinglePlayer,
                resourcePath = "Graphics/Backgrounds/single-player",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.Default,
                description = "The single-player campaign board, and the fallback behind any " +
                              "campaign that has no artwork of its own."
            },
            new BackgroundDef
            {
                id = BackgroundId.Multiplayer,
                resourcePath = "Graphics/Backgrounds/multi-player",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.Default,
                description = "Multiplayer lobby screen, and the main menu's preview of it."
            },
            new BackgroundDef
            {
                id = BackgroundId.Extras,
                resourcePath = "Graphics/Backgrounds/extras",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.Default,
                description = "The main menu's preview behind its EXTRAS row."
            },

            // The six theatres. Each falls back to the campaign board's own
            // artwork rather than to the shared menu image: a campaign with no
            // picture yet should look like the screen it was opened from, not
            // like the main menu.
            new BackgroundDef
            {
                id = BackgroundId.CampaignEurope,
                resourcePath = "Graphics/Backgrounds/Missions/Europe/single-player-europe-background",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.SinglePlayer,
                description = "EUROPE mission board."
            },
            new BackgroundDef
            {
                id = BackgroundId.CampaignAfrica,
                resourcePath = "Graphics/Backgrounds/Missions/Africa/single-player-africa-background",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.SinglePlayer,
                description = "AFRICA mission board."
            },
            new BackgroundDef
            {
                id = BackgroundId.CampaignAsia,
                resourcePath = "Graphics/Backgrounds/Missions/Asia/single-player-asia-background",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.SinglePlayer,
                description = "ASIA mission board."
            },
            new BackgroundDef
            {
                id = BackgroundId.CampaignNorthAmerica,
                resourcePath = "Graphics/Backgrounds/Missions/NorthAmerica/single-player-north-america-background",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.SinglePlayer,
                description = "NORTH AMERICA mission board."
            },
            new BackgroundDef
            {
                id = BackgroundId.CampaignSouthAmerica,
                resourcePath = "Graphics/Backgrounds/Missions/SouthAmerica/single-player-south-america-background",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.SinglePlayer,
                description = "SOUTH AMERICA mission board."
            },
            new BackgroundDef
            {
                id = BackgroundId.CampaignAustralia,
                resourcePath = "Graphics/Backgrounds/Missions/Australia/single-player-australia-background",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.SinglePlayer,
                description = "AUSTRALIA mission board."
            },
            new BackgroundDef
            {
                id = BackgroundId.Interior,
                resourcePath = "Graphics/Backgrounds/background",
                // Heavier than the menu's: these screens are tables, rows and
                // sliders read at length, and the artwork is a busy operational
                // map. It stays a picture; it stops being a distraction.
                scrimAlpha = 0.78f,
                fallback = BackgroundId.Default,
                description = "Settings, Extras, Unit Library, DLC and Credits — the screens " +
                              "behind the main menu. A front line seen from altitude, blue " +
                              "against red."
            }
        };

        static Dictionary<BackgroundId, BackgroundDef> _byId;

        public static BackgroundDef Get(BackgroundId id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<BackgroundId, BackgroundDef>(Defs.Length);
                foreach (var d in Defs) _byId[d.id] = d;
            }
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        public static IReadOnlyList<BackgroundDef> All => Defs;
    }
}
