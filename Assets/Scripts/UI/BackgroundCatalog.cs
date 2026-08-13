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
        Default
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
                resourcePath = "Backgrounds/default_background",
                scrimAlpha = 0.62f,
                description = "Shared artwork behind every menu screen: main menu, settings, " +
                              "testing, units list and the East France placeholder."
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
