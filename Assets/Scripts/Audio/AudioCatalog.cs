using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Audio
{
    /// <summary>
    /// Every music bed the game can ask for, named by role rather than by file.
    /// </summary>
    public enum MusicTrack
    {
        /// <summary>Silence — stops whatever is playing.</summary>
        None,
        /// <summary>The single ambient bed shared by every screen.</summary>
        MenuTheme,
        /// <summary>Single-player campaign screen.</summary>
        SinglePlayerTheme,
        /// <summary>Multiplayer lobby screen.</summary>
        MultiplayerTheme,
        /// <summary>Extras screen.</summary>
        ExtrasTheme
    }

    /// <summary>
    /// Looping environmental beds, played on their own channel so weather sits
    /// under the music rather than replacing it.
    /// </summary>
    public enum AmbienceTrack
    {
        /// <summary>Silence — stops whatever is playing.</summary>
        None,
        Rain,
        Storm,
        Snow
    }

    /// <summary>One music track: where to load it and how loud it should sit.</summary>
    public class MusicDef
    {
        public MusicTrack track;

        /// <summary>Resources path, without extension. Must live under an <c>Assets/Resources</c> folder.</summary>
        public string resourcePath;

        /// <summary>
        /// Playback level before the master volume is applied. Music is a bed:
        /// it sits well under UI feedback rather than competing with it.
        /// </summary>
        public float volume;

        public bool loop;

        /// <summary>What this track is for — mirrored in docs/10-AUDIO.md.</summary>
        public string description;

        /// <summary>
        /// Track to fall back to when this one has no file yet. Same reasoning
        /// as <c>BackgroundDef.fallback</c>: a screen is entitled to its own
        /// music, but silence reads as a bug where the shared bed reads as a
        /// screen that simply has not been scored yet.
        /// </summary>
        public MusicTrack fallback = MusicTrack.None;
    }

    /// <summary>One ambience bed: where to load it and how loud it should sit.</summary>
    public class AmbienceDef
    {
        public AmbienceTrack track;

        /// <summary>Resources path, without extension. Must live under an <c>Assets/Resources</c> folder.</summary>
        public string resourcePath;

        /// <summary>Playback level before the master volume is applied.</summary>
        public float volume;

        /// <summary>What this bed is for — mirrored in docs/10-AUDIO.md.</summary>
        public string description;
    }

    /// <summary>
    /// The register of every audio asset in code. Keep it in step with
    /// docs/10-AUDIO.md, which is the human-readable version of this table.
    /// </summary>
    public static class AudioCatalog
    {
        /// <summary>Seconds the music takes to fade up, so it never starts abruptly.</summary>
        public const float MusicFadeInSeconds = 1.5f;

        static readonly MusicDef[] Music =
        {
            new MusicDef
            {
                track = MusicTrack.MenuTheme,
                resourcePath = "Audio/main-menu/game_menu_background",
                volume = 0.45f,
                loop = true,
                description = "Ambient bed for every screen — menus, testing, units list and the map."
            },

            // The three below have no track of their own yet and fall back to
            // the menu theme. Drop a file at the path named here and it is used
            // automatically — see docs/10-AUDIO.md.
            new MusicDef
            {
                track = MusicTrack.SinglePlayerTheme,
                resourcePath = "Audio/main-menu/single_player",
                volume = 0.45f, loop = true, fallback = MusicTrack.MenuTheme,
                description = "Single-player campaign screen. Awaiting its own track."
            },
            new MusicDef
            {
                track = MusicTrack.MultiplayerTheme,
                resourcePath = "Audio/main-menu/multiplayer",
                volume = 0.45f, loop = true, fallback = MusicTrack.MenuTheme,
                description = "Multiplayer lobby screen. Awaiting its own track."
            },
            new MusicDef
            {
                track = MusicTrack.ExtrasTheme,
                resourcePath = "Audio/main-menu/extras",
                volume = 0.45f, loop = true, fallback = MusicTrack.MenuTheme,
                description = "Extras screen. Awaiting its own track."
            }
        };

        /// <summary>
        /// Weather beds. Levels sit below the music so a storm colours the
        /// scene without drowning it; snow is quietest because real snowfall
        /// is near-silent and a loud loop reads as static.
        /// </summary>
        static readonly AmbienceDef[] Ambience =
        {
            new AmbienceDef
            {
                track = AmbienceTrack.Rain,
                resourcePath = "Audio/weather/rain-background",
                volume = 0.40f,
                description = "Steady rainfall bed for the Rain condition."
            },
            new AmbienceDef
            {
                track = AmbienceTrack.Storm,
                resourcePath = "Audio/weather/storm-background",
                volume = 0.50f,
                description = "Wind and thunder bed for the Storm condition."
            },
            new AmbienceDef
            {
                track = AmbienceTrack.Snow,
                resourcePath = "Audio/weather/snow-background",
                volume = 0.30f,
                description = "Muffled wind bed for the Snow condition."
            }
        };

        static Dictionary<AmbienceTrack, AmbienceDef> _byAmbience;

        public static AmbienceDef GetAmbience(AmbienceTrack track)
        {
            if (_byAmbience == null)
            {
                _byAmbience = new Dictionary<AmbienceTrack, AmbienceDef>(Ambience.Length);
                foreach (var a in Ambience) _byAmbience[a.track] = a;
            }
            return _byAmbience.TryGetValue(track, out var def) ? def : null;
        }

        public static IReadOnlyList<AmbienceDef> AllAmbience => Ambience;

        static Dictionary<MusicTrack, MusicDef> _byTrack;

        public static MusicDef Get(MusicTrack track)
        {
            if (_byTrack == null)
            {
                _byTrack = new Dictionary<MusicTrack, MusicDef>(Music.Length);
                foreach (var m in Music) _byTrack[m.track] = m;
            }
            return _byTrack.TryGetValue(track, out var def) ? def : null;
        }

        public static IReadOnlyList<MusicDef> AllMusic => Music;
    }
}
