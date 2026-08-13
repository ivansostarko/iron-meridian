using UnityEngine;

namespace IronMeridian.Audio
{
    /// <summary>
    /// Synthesises the effect sounds so the game is audible with no audio
    /// assets installed — the counterpart to
    /// <see cref="IronMeridian.Vfx.ProceduralVfx"/> for the visuals, and the
    /// same reasoning as <see cref="AudioManager.PlayClick"/>'s UI blip.
    ///
    /// These are honest stand-ins, not a substitute for recorded audio: drop a
    /// file into <c>Resources/Audio/effects/</c> and it wins (see
    /// <see cref="EffectAudio"/>).
    /// </summary>
    public static class ProceduralAudio
    {
        const int Rate = 44100;

        public static AudioClip Build(EffectSound sound) => sound switch
        {
            EffectSound.Explosion => Explosion(),
            EffectSound.Fire => FireLoop(),
            EffectSound.Smoke => SmokeLoop(),
            EffectSound.Impact => Impact(),
            _ => null
        };

        /// <summary>
        /// Detonation: a low body that drops in pitch under a wide noise crack,
        /// with a long tail. The pitch drop is what makes it read as a big
        /// distant blast rather than a click.
        /// </summary>
        static AudioClip Explosion()
        {
            const float duration = 2.4f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(1337);
            float lowPass = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;

                // Body: 90 Hz falling to ~28 Hz over the first second.
                float bodyFreq = Mathf.Lerp(90f, 28f, Mathf.Clamp01(t / 1.0f));
                float body = Mathf.Sin(2f * Mathf.PI * bodyFreq * t) * Mathf.Exp(-t * 2.2f);

                // Crack: white noise, rolled off so it is a roar not a hiss.
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lowPass = Mathf.Lerp(lowPass, noise, 0.18f);
                float crack = lowPass * Mathf.Exp(-t * 5.5f);

                // Very short click to give the transient a leading edge.
                float click = Mathf.Exp(-t * 220f) * (float)(rng.NextDouble() * 2.0 - 1.0);

                data[i] = Mathf.Clamp(body * 0.75f + crack * 0.55f + click * 0.35f, -1f, 1f);
            }

            return Make("fx_explosion", data);
        }

        /// <summary>
        /// Fire: band-limited noise with random pops for crackle. Built to be
        /// seamless — the last 20 ms cross-fades into the first, so the loop
        /// has no click at the wrap.
        /// </summary>
        static AudioClip FireLoop()
        {
            const float duration = 3.0f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(4242);
            float low = 0f, band = 0f;

            for (int i = 0; i < n; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, noise, 0.06f);          // rumble
                band = Mathf.Lerp(band, noise - low, 0.55f);  // hiss

                float sample = low * 0.55f + band * 0.22f;

                // Crackle: sparse, sharp decaying pops.
                if (rng.NextDouble() < 0.0016)
                {
                    float pop = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.8f;
                    int popLen = 300 + (int)(rng.NextDouble() * 500);
                    for (int k = 0; k < popLen && i + k < n; k++)
                        data[i + k] += pop * Mathf.Exp(-k / (float)popLen * 6f);
                }

                data[i] += sample;
            }

            Normalise(data, 0.7f);
            CrossfadeLoop(data);
            return Make("fx_fire", data);
        }

        /// <summary>Smoke: a soft, slow-moving low hiss. Almost sub-audible on purpose.</summary>
        static AudioClip SmokeLoop()
        {
            const float duration = 3.0f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(909);
            float low = 0f, slower = 0f;

            for (int i = 0; i < n; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, noise, 0.03f);
                slower = Mathf.Lerp(slower, low, 0.02f);
                // Gentle swell so it breathes rather than sitting as flat static.
                float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.12f * (i / (float)Rate));
                data[i] = low * 0.5f * swell + slower * 0.5f;
            }

            Normalise(data, 0.45f);
            CrossfadeLoop(data);
            return Make("fx_smoke", data);
        }

        /// <summary>Impact: a short filtered thud for rounds landing.</summary>
        static AudioClip Impact()
        {
            const float duration = 0.5f;
            int n = (int)(Rate * duration);
            var data = new float[n];
            var rng = new System.Random(77);
            float low = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low = Mathf.Lerp(low, noise, 0.22f);
                float thud = Mathf.Sin(2f * Mathf.PI * 120f * t) * Mathf.Exp(-t * 22f);
                data[i] = Mathf.Clamp(thud * 0.6f + low * Mathf.Exp(-t * 14f) * 0.5f, -1f, 1f);
            }

            return Make("fx_impact", data);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>
        /// Blends the tail into the head so a looping clip does not click at
        /// the wrap — the single most audible flaw in synthesised loops.
        /// </summary>
        static void CrossfadeLoop(float[] data, float seconds = 0.02f)
        {
            int fade = Mathf.Min((int)(Rate * seconds), data.Length / 4);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                int tail = data.Length - fade + i;
                data[i] = Mathf.Lerp(data[tail], data[i], k);
            }
        }

        static void Normalise(float[] data, float peak)
        {
            float max = 0f;
            foreach (float s in data) max = Mathf.Max(max, Mathf.Abs(s));
            if (max <= 0.0001f) return;
            float gain = peak / max;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
