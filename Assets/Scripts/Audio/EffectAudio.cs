using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.Audio
{
    /// <summary>Sounds a particle effect can carry.</summary>
    public enum EffectSound
    {
        None,
        /// <summary>Looping crackle for anything burning.</summary>
        Fire,
        /// <summary>One-shot detonation.</summary>
        Explosion,
        /// <summary>Looping low hiss for a smoke column or screen.</summary>
        Smoke,
        /// <summary>Short one-shot thud for rounds landing.</summary>
        Impact,

        // --- artillery (docs/17-ARTILLERY.md) ---
        // One report per nature. Calibre is audible in real life — a 105 mm
        // round cracks, a 203 mm round is felt before it is heard — so a fire
        // mission that sounds the same whatever was called for wastes the one
        // cue that tells the player which battery answered.

        /// <summary>105 mm round landing — sharp, high crack with a short tail.</summary>
        ArtilleryLight,
        /// <summary>120 mm mortar bomb — a duller thump, more earth than air.</summary>
        ArtilleryMortar,
        /// <summary>155 mm round — the reference report: deep body, long tail.</summary>
        ArtilleryMedium,
        /// <summary>203 mm round — very low, slow to decay, with a rolling echo.</summary>
        ArtilleryHeavy,

        // --- air strikes (docs/18-AIR-STRIKES.md) ---

        /// <summary>Air-dropped weapon — the deepest, longest detonation in the game.</summary>
        AerialBomb,
        /// <summary>Jet passing overhead — a swelling roar that travels with the aircraft.</summary>
        JetPass
    }

    /// <summary>
    /// Positional audio for particle effects — the third channel, alongside
    /// music and weather ambience.
    ///
    /// Unlike those, these are **3D** sources placed in the world, so a fire on
    /// the far side of the map is quiet and one under the camera is not. That
    /// needs rolloff distances in hundreds of metres rather than Unity's
    /// default handful, because this map is measured in kilometres.
    ///
    /// Clips come from <c>Resources/Audio/effects/</c> when present and are
    /// otherwise **synthesised at runtime**, so the game is audible with no
    /// audio assets at all — the same rule <see cref="IronMeridian.Vfx.ProceduralVfx"/>
    /// follows for the visuals.
    /// </summary>
    public static class EffectAudio
    {
        /// <summary>
        /// Concurrent effect voices. A corps-scale battle can have dozens of
        /// fires burning; past this many the oldest is recycled rather than
        /// letting the mix turn to mud.
        /// </summary>
        const int MaxVoices = 14;

        /// <summary>Effect sources are quieter than the music bed — they are texture, not score.</summary>
        const float BaseVolume = 0.55f;

        static readonly List<AudioSource> _live = new List<AudioSource>();
        static readonly Dictionary<EffectSound, AudioClip> _clips = new Dictionary<EffectSound, AudioClip>();
        static Transform _root;

        /// <summary>
        /// Plays a sound at a world position. Looping sounds return the source
        /// so the caller can stop it; one-shots clean themselves up and the
        /// return value can be ignored.
        /// </summary>
        public static AudioSource PlayAt(EffectSound sound, Vector3 world, float audibleRadius,
            Transform parent = null)
        {
            if (sound == EffectSound.None) return null;

            var clip = Resolve(sound);
            if (clip == null) return null;

            MakeRoom();

            var go = new GameObject("EffectAudio_" + sound);
            go.transform.SetParent(parent != null ? parent : Root(), true);
            go.transform.position = world;

            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f;                       // fully 3D
            src.rolloffMode = AudioRolloffMode.Linear;
            // Full volume within the effect itself, fading out over a wide
            // radius. Unity's defaults (1 m / 500 m, logarithmic) are silent
            // almost immediately at this scale.
            src.minDistance = Mathf.Max(20f, audibleRadius);
            src.maxDistance = Mathf.Max(400f, audibleRadius * 26f);
            src.dopplerLevel = 0f;                       // the camera is not a listener in motion
            src.volume = BaseVolume;
            src.priority = 160;                          // below music and ambience
            src.loop = Loops(sound);
            src.Play();

            _live.Add(src);

            // One-shots tidy up after themselves; loops belong to their caller.
            if (!src.loop) Object.Destroy(go, clip.length + 0.2f);
            return src;
        }

        /// <summary>Stops and destroys a looping source. Safe on null or already-destroyed sources.</summary>
        public static void Stop(AudioSource source)
        {
            if (source == null) return;
            _live.Remove(source);
            Object.Destroy(source.gameObject);
        }

        static bool Loops(EffectSound sound) =>
            sound == EffectSound.Fire || sound == EffectSound.Smoke;

        static Transform Root()
        {
            if (_root != null) return _root;
            var go = new GameObject("EffectAudio");
            _root = go.transform;
            return _root;
        }

        static void MakeRoom()
        {
            _live.RemoveAll(s => s == null);
            while (_live.Count >= MaxVoices)
            {
                var oldest = _live[0];
                _live.RemoveAt(0);
                if (oldest != null) Object.Destroy(oldest.gameObject);
            }
        }

        // ------------------------------------------------------ clip resolution

        static readonly Dictionary<EffectSound, string> ResourcePaths = new Dictionary<EffectSound, string>
        {
            [EffectSound.Fire] = "Audio/effects/fire",
            [EffectSound.Explosion] = "Audio/effects/explosion",
            [EffectSound.Smoke] = "Audio/effects/smoke",
            [EffectSound.Impact] = "Audio/effects/impact",
            [EffectSound.ArtilleryLight] = "Audio/effects/artillery_105",
            [EffectSound.ArtilleryMortar] = "Audio/effects/artillery_120",
            [EffectSound.ArtilleryMedium] = "Audio/effects/artillery_155",
            [EffectSound.ArtilleryHeavy] = "Audio/effects/artillery_203",
            [EffectSound.AerialBomb] = "Audio/effects/aerial_bomb",
            [EffectSound.JetPass] = "Audio/effects/jet_pass"
        };

        static AudioClip Resolve(EffectSound sound)
        {
            if (_clips.TryGetValue(sound, out var cached) && cached != null) return cached;

            AudioClip clip = null;
            if (ResourcePaths.TryGetValue(sound, out string path))
                clip = Resources.Load<AudioClip>(path);

            // Nothing installed — synthesise it. The game must be audible with
            // no audio assets present at all.
            if (clip == null) clip = ProceduralAudio.Build(sound);

            _clips[sound] = clip;
            return clip;
        }
    }
}
