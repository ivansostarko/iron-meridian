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
        MenuTheme
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
            }
        };

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
