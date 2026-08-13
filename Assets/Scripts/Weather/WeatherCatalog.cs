using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Audio;

namespace IronMeridian.Weather
{
    /// <summary>
    /// What the sky is doing. Deliberately separate from
    /// <see cref="WeatherCondition"/>: a night storm and a midday storm are both
    /// real, and folding light into the weather list would make them mutually
    /// exclusive. It is also what lets the clock drive the sky automatically
    /// while the player still chooses the weather.
    /// </summary>
    public enum SkyPhase
    {
        Day,
        Sunset,
        Night
    }

    /// <summary>What is falling out of the sky, and how far you can see through it.</summary>
    public enum WeatherCondition
    {
        Clear,
        Overcast,
        Fog,
        Rain,
        Storm,
        Snow
    }

    /// <summary>Which precipitation builder a condition uses, if any.</summary>
    public enum Precipitation { None, Rain, Snow }

    /// <summary>Sun angle, colour and ambient level for one time of day.</summary>
    public class SkyDef
    {
        public SkyPhase phase;
        public string name;
        public string detail;

        /// <summary>Sun rotation. X is elevation above the horizon; negative puts it below.</summary>
        public Vector3 sunEuler;
        public Color sunColour;
        public float sunIntensity;
        public Color ambient;
        /// <summary>Camera clear colour, seen where no terrain has streamed in.</summary>
        public Color horizon;
    }

    /// <summary>Everything one weather condition changes.</summary>
    public class WeatherDef
    {
        public WeatherCondition condition;
        public string name;
        public string detail;

        public Precipitation precipitation;
        /// <summary>Particles per second at the reference altitude; scaled with the camera.</summary>
        public float particleRate;

        /// <summary>Multiplies the sky's sun intensity — cloud cover, in one number.</summary>
        public float lightMultiplier = 1f;
        /// <summary>Tints the sun toward the overcast grey of the condition.</summary>
        public Color lightTint = Color.white;

        public bool fog;
        /// <summary>Unity exponential-squared fog density. Tiny numbers: the map is in metres.</summary>
        public float fogDensity;

        public AmbienceTrack ambience = AmbienceTrack.None;
    }

    /// <summary>
    /// The register of skies and weather in code. Keep it in step with
    /// docs/14-WEATHER.md, the human-readable version of these tables.
    /// </summary>
    public static class WeatherCatalog
    {
        // Sun elevation is what sells time of day. Day is high and white,
        // sunset is low and warm with long shadows, night is below the horizon
        // with a cold moonlit ambient standing in for the sun.
        static readonly SkyDef[] Skies =
        {
            new SkyDef
            {
                phase = SkyPhase.Day, name = "DAY", detail = "High sun — full observation",
                sunEuler = new Vector3(55f, -35f, 0f),
                sunColour = new Color(1.00f, 0.98f, 0.94f),
                sunIntensity = 1.35f,
                ambient = new Color(0.42f, 0.45f, 0.50f),
                horizon = new Color(0.42f, 0.55f, 0.70f)
            },
            new SkyDef
            {
                phase = SkyPhase.Sunset, name = "SUNSET", detail = "Low sun — long shadows, glare",
                sunEuler = new Vector3(8f, -60f, 0f),
                sunColour = new Color(1.00f, 0.66f, 0.38f),
                sunIntensity = 1.05f,
                ambient = new Color(0.30f, 0.26f, 0.28f),
                horizon = new Color(0.38f, 0.26f, 0.24f)
            },
            new SkyDef
            {
                phase = SkyPhase.Night, name = "NIGHT", detail = "Darkness — movement under cover",
                // Below the horizon, pointing the other way: what remains reads
                // as moonlight rather than a dim sun.
                sunEuler = new Vector3(-18f, 200f, 0f),
                sunColour = new Color(0.55f, 0.62f, 0.85f),
                sunIntensity = 0.28f,
                ambient = new Color(0.10f, 0.12f, 0.18f),
                horizon = new Color(0.05f, 0.07f, 0.12f)
            }
        };

        static readonly WeatherDef[] Conditions =
        {
            new WeatherDef
            {
                condition = WeatherCondition.Clear, name = "CLEAR", detail = "No cloud — best visibility",
                precipitation = Precipitation.None,
                lightMultiplier = 1f, lightTint = Color.white,
                fog = false
            },
            new WeatherDef
            {
                condition = WeatherCondition.Overcast, name = "OVERCAST", detail = "Cloud cover — flat, grey light",
                precipitation = Precipitation.None,
                lightMultiplier = 0.62f, lightTint = new Color(0.80f, 0.84f, 0.90f),
                fog = true, fogDensity = 0.000012f
            },
            new WeatherDef
            {
                condition = WeatherCondition.Fog, name = "FOG", detail = "Visibility collapses to near zero",
                precipitation = Precipitation.None,
                lightMultiplier = 0.55f, lightTint = new Color(0.85f, 0.87f, 0.90f),
                fog = true, fogDensity = 0.000075f
            },
            new WeatherDef
            {
                condition = WeatherCondition.Rain, name = "RAIN", detail = "Steady rainfall — reduced observation",
                precipitation = Precipitation.Rain, particleRate = 900f,
                lightMultiplier = 0.55f, lightTint = new Color(0.76f, 0.82f, 0.90f),
                fog = true, fogDensity = 0.000030f,
                ambience = AmbienceTrack.Rain
            },
            new WeatherDef
            {
                condition = WeatherCondition.Storm, name = "STORM", detail = "Driving rain, wind and thunder",
                precipitation = Precipitation.Rain, particleRate = 2000f,
                lightMultiplier = 0.34f, lightTint = new Color(0.62f, 0.70f, 0.84f),
                fog = true, fogDensity = 0.000055f,
                ambience = AmbienceTrack.Storm
            },
            new WeatherDef
            {
                condition = WeatherCondition.Snow, name = "SNOW", detail = "Snowfall — slow going, muffled",
                precipitation = Precipitation.Snow, particleRate = 700f,
                lightMultiplier = 0.70f, lightTint = new Color(0.90f, 0.93f, 1.00f),
                fog = true, fogDensity = 0.000045f,
                ambience = AmbienceTrack.Snow
            }
        };

        static Dictionary<SkyPhase, SkyDef> _skies;
        static Dictionary<WeatherCondition, WeatherDef> _conditions;

        public static SkyDef GetSky(SkyPhase phase)
        {
            if (_skies == null)
            {
                _skies = new Dictionary<SkyPhase, SkyDef>(Skies.Length);
                foreach (var s in Skies) _skies[s.phase] = s;
            }
            return _skies.TryGetValue(phase, out var def) ? def : Skies[0];
        }

        public static WeatherDef Get(WeatherCondition condition)
        {
            if (_conditions == null)
            {
                _conditions = new Dictionary<WeatherCondition, WeatherDef>(Conditions.Length);
                foreach (var c in Conditions) _conditions[c.condition] = c;
            }
            return _conditions.TryGetValue(condition, out var def) ? def : Conditions[0];
        }

        public static IReadOnlyList<SkyDef> AllSkies => Skies;
        public static IReadOnlyList<WeatherDef> AllConditions => Conditions;

        // ------------------------------------------------------ day/night rule

        /// <summary>Daylight begins at 05:00.</summary>
        public const int DayStartHour = 5;
        /// <summary>Daylight ends at 23:00 — 23:01 onward is night.</summary>
        public const int NightStartHour = 23;
        /// <summary>Either side of the day/night boundary reads as sunset for this long.</summary>
        const int SunsetWindowMinutes = 60;

        /// <summary>
        /// The sky phase implied by a clock reading, used when automatic
        /// day/night is on: day from 05:00 to 23:00, night from 23:01 to 04:59,
        /// with an hour of sunset either side of dusk so the transition is not
        /// a hard cut.
        /// </summary>
        public static SkyPhase PhaseForTime(System.DateTime now)
        {
            int minutes = now.Hour * 60 + now.Minute;
            const int dayStart = DayStartHour * 60;          // 05:00
            const int nightStart = NightStartHour * 60;      // 23:00

            if (minutes < dayStart || minutes > nightStart) return SkyPhase.Night;
            // Dusk: the last hour of daylight.
            if (minutes >= nightStart - SunsetWindowMinutes) return SkyPhase.Sunset;
            // Dawn: the first hour after daybreak.
            if (minutes <= dayStart + SunsetWindowMinutes) return SkyPhase.Sunset;
            return SkyPhase.Day;
        }
    }
}
