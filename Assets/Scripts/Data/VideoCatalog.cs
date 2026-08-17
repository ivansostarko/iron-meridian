using System.Collections.Generic;

namespace IronMeridian.Data
{
    /// <summary>Every film the game plays, named by role rather than by file.</summary>
    public enum VideoId
    {
        /// <summary>The opening film, once per launch — see <c>UI.IntroVideoUI</c>.</summary>
        GameIntro
    }

    /// <summary>One video: where to load it and where it is played.</summary>
    public class VideoDef
    {
        public VideoId id;

        /// <summary>Resources path, without extension. Must live under an <c>Assets/Resources</c> folder.</summary>
        public string resourcePath;

        /// <summary>Name as the DEVELOPMENT → VIDEOS screen lists it.</summary>
        public string name;

        /// <summary>Where in the game it plays.</summary>
        public string usedBy;

        /// <summary>What it is — mirrored in docs/32-VIDEO.md.</summary>
        public string description;
    }

    /// <summary>
    /// The register of every video asset in code, and the same shape as the
    /// audio, model, effect and background catalogues: a screen names an id, the
    /// catalogue owns the path, and one lab lists the lot so "is that file
    /// actually installed?" is a question with an answer.
    ///
    /// Keep it in step with docs/32-VIDEO.md.
    /// </summary>
    public static class VideoCatalog
    {
        static readonly VideoDef[] Defs =
        {
            new VideoDef
            {
                id = VideoId.GameIntro,
                resourcePath = "Videos/intro-video/game_intro",
                name = "Game intro",
                usedBy = "Main menu, once per launch",
                description = "The opening film, played over black before the menu is usable. " +
                              "Any input skips it; it always ends. See docs/11-GAME-MENU.md §3.1a."
            }
        };

        static Dictionary<VideoId, VideoDef> _byId;

        public static VideoDef Get(VideoId id)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<VideoId, VideoDef>(Defs.Length);
                foreach (var d in Defs) _byId[d.id] = d;
            }
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        public static IReadOnlyList<VideoDef> All => Defs;
    }
}
