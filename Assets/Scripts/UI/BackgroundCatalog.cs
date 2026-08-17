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
        /// <summary>Single-player campaign screen.</summary>
        SinglePlayer,
        /// <summary>Multiplayer lobby screen.</summary>
        Multiplayer,
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

            // The three below have no artwork of their own yet and fall back to
            // the shared image. Drop a file at the path named here and it is
            // used automatically — see docs/11-GAME-MENU.md.
            new BackgroundDef
            {
                id = BackgroundId.SinglePlayer,
                resourcePath = "Graphics/Backgrounds/single_player",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.Default,
                description = "Single-player campaign screen. Awaiting artwork."
            },
            new BackgroundDef
            {
                id = BackgroundId.Multiplayer,
                resourcePath = "Graphics/Backgrounds/multiplayer",
                scrimAlpha = 0.62f,
                fallback = BackgroundId.Default,
                description = "Multiplayer lobby screen. Awaiting artwork."
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
