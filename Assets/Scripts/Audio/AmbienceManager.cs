using System.Collections;
using UnityEngine;

namespace IronMeridian.Audio
{
    /// <summary>
    /// Plays the looping environmental bed — rain, storm, snow — on a channel
    /// of its own, so weather layers *under* the music instead of replacing it.
    /// Deliberately a separate component from <see cref="MusicManager"/>: they
    /// need to play at the same time, and mixing both onto one AudioSource
    /// would make that impossible.
    ///
    /// Same contract as the music channel: asking for the track already playing
    /// is a no-op, so callers can drive it from state changes without checking
    /// first, and a missing clip warns once instead of throwing.
    /// </summary>
    public class AmbienceManager : MonoBehaviour
    {
        public static AmbienceManager Active { get; private set; }

        /// <summary>What is playing right now, across all scenes.</summary>
        public static AmbienceTrack Current { get; private set; } = AmbienceTrack.None;

        /// <summary>Ambience fades faster than music — weather changes should feel like weather changing.</summary>
        const float FadeSeconds = 1.0f;

        AudioSource _source;
        Coroutine _fade;
        static bool _warnedMissing;

        /// <summary>Starts <paramref name="track"/>, or does nothing if it is already playing.</summary>
        public static void Play(AmbienceTrack track)
        {
            if (track == AmbienceTrack.None) { Stop(); return; }

            var mgr = Ensure();
            if (mgr == null || mgr._source == null) return;
            if (Current == track && mgr._source.isPlaying) return;

            var def = AudioCatalog.GetAmbience(track);
            if (def == null)
            {
                Debug.LogWarning($"[AmbienceManager] No catalogue entry for '{track}'. See docs/10-AUDIO.md.");
                return;
            }

            var clip = Resources.Load<AudioClip>(def.resourcePath);
            if (clip == null)
            {
                if (!_warnedMissing)
                {
                    _warnedMissing = true;
                    Debug.LogWarning($"[AmbienceManager] Missing audio clip: Resources/{def.resourcePath}. " +
                        "Weather audio must live under an Assets/Resources folder — see docs/10-AUDIO.md.");
                }
                return;
            }

            mgr._source.clip = clip;
            mgr._source.loop = true;
            mgr._source.volume = 0f;
            mgr._source.Play();
            Current = track;

            mgr.FadeTo(def.volume, FadeSeconds);
        }

        /// <summary>Fades the bed out and stops it.</summary>
        public static void Stop()
        {
            if (Active == null || Active._source == null) return;
            if (Current == AmbienceTrack.None && !Active._source.isPlaying) return;
            Current = AmbienceTrack.None;
            Active.FadeTo(0f, FadeSeconds, stopWhenDone: true);
        }

        static AmbienceManager Ensure()
        {
            if (Active != null) return Active;
            var go = new GameObject("AmbienceManager");
            DontDestroyOnLoad(go);
            return go.AddComponent<AmbienceManager>();   // Awake wires Active and the source
        }

        void Awake()
        {
            if (Active != null && Active != this) { Destroy(gameObject); return; }
            Active = this;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;    // 2D — weather is everywhere, not at a point
            _source.priority = 72;        // below the music channel
        }

        void OnDestroy()
        {
            if (Active == this) { Active = null; Current = AmbienceTrack.None; }
        }

        void FadeTo(float target, float seconds, bool stopWhenDone = false)
        {
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeRoutine(target, seconds, stopWhenDone));
        }

        IEnumerator FadeRoutine(float target, float seconds, bool stopWhenDone)
        {
            float start = _source.volume;

            // Unscaled: the pause menu zeroes timeScale, and weather freezing
            // mid-fade when the player pauses would be a bug.
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                _source.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }

            _source.volume = target;
            if (stopWhenDone) _source.Stop();
            _fade = null;
        }
    }
}
