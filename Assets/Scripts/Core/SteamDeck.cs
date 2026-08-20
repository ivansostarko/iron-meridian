using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// Whether this is running on a Steam Deck, and the handful of defaults that
    /// follow from the answer.
    ///
    /// **A Deck is not "a small PC".** It is a 1280×800 seven-inch screen held
    /// at arm's length, an RDNA 2 APU with a 15 W budget, and — the part that
    /// actually breaks a game like this one — **no keyboard**. Half the map
    /// editor's verbs are keys: WASD pans, Q/E rotate, R/F zoom, C faces a
    /// formation, Ctrl+Z undoes, F5/F9 save and load, Tab opens the casualty
    /// list. A build that ignores the Deck is a build where none of that exists.
    ///
    /// **Detection is by environment variable first.** Valve sets
    /// <c>SteamDeck=1</c> in the game's environment on every Deck, and it costs
    /// nothing and needs no SDK — which matters here, because Steam integration
    /// is behind the <c>IRONMERIDIAN_STEAM</c> define and a build without it
    /// would otherwise have no way to know. Steamworks' own
    /// <c>IsSteamRunningOnSteamDeck</c> is asked as well where it is compiled
    /// in, because the variable can be spoofed and a player who has done that
    /// deliberately gets what they asked for either way.
    ///
    /// **Everything here is also reachable by hand.** A Deck-shaped device that
    /// is not a Deck — a ROG Ally, a Legion Go, a small laptop with a pad — wants
    /// the same defaults, so <see cref="ForceHandheld"/> turns them on without
    /// pretending to be Valve's hardware. See docs/42-STEAM-DECK.md.
    /// </summary>
    public static class SteamDeck
    {
        /// <summary>The Deck's panel, and the resolution the interface is tuned against.</summary>
        public const int ScreenWidth = 1280;
        public const int ScreenHeight = 800;

        /// <summary>
        /// The LCD Deck's panel refresh. The OLED can do 90; capping at 60 is
        /// the honest default for a game that streams terrain over wifi, and the
        /// player can raise it in SETTINGS like anywhere else.
        /// </summary>
        public const int DefaultFrameCap = 60;

        /// <summary>
        /// Treat this machine as a handheld whatever it actually is. Set from
        /// the command line with <c>-handheld</c>, for the devices that are
        /// Decks in everything but name.
        /// </summary>
        public static bool ForceHandheld { get; private set; }

        static bool? _detected;

        /// <summary>
        /// True on a Steam Deck, or anywhere <see cref="ForceHandheld"/> was
        /// asked for. Worked out once and remembered — nothing about it can
        /// change while the game is running.
        /// </summary>
        public static bool IsHandheld
        {
            get
            {
                if (_detected.HasValue) return _detected.Value;
                _detected = ForceHandheld || DetectDeck();
                return _detected.Value;
            }
        }

        static bool DetectDeck()
        {
            // Valve's own marker. Present on every Deck, in the game's
            // environment, with no SDK involved.
            try
            {
                string flag = System.Environment.GetEnvironmentVariable("SteamDeck");
                if (flag == "1") return true;
            }
            catch { /* a platform with no environment is not a Deck */ }

#if IRONMERIDIAN_STEAM
            // Belt and braces where the SDK is compiled in. Steam knows better
            // than the environment does, and this also catches a Deck running
            // the game through a launcher that has scrubbed the variable.
            try
            {
                if (SteamIntegration.Running && Steamworks.SteamUtils.IsSteamRunningOnSteamDeck())
                    return true;
            }
            catch { /* the SDK is optional and its absence is not an error */ }
#endif
            return false;
        }

        /// <summary>
        /// Applies the handheld defaults, once, before the first scene.
        ///
        /// **Only what the machine cannot work out for itself.** The screen is a
        /// fixed size and the frame cap is a battery decision, so those are set;
        /// quality is *not*, because the player owns that and a port that
        /// overwrote their choice every launch would be a bug rather than a
        /// default. <see cref="DisplaySettings"/> still has the last word — this
        /// runs before it and only seeds what has never been set.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyDefaults()
        {
            foreach (var arg in System.Environment.GetCommandLineArgs())
                if (arg == "-handheld") ForceHandheld = true;

            if (!IsHandheld) return;

            // Native resolution, borderless. The Deck's compositor scales
            // anything else, which on a 7-inch panel is the difference between
            // readable type and a blur.
            Screen.SetResolution(ScreenWidth, ScreenHeight, FullScreenMode.FullScreenWindow);

            if (!DisplaySettings.HasFrameCap) DisplaySettings.SeedFrameCap(DefaultFrameCap);

            Debug.Log($"[SteamDeck] Handheld defaults applied " +
                      $"({ScreenWidth}x{ScreenHeight}, {DefaultFrameCap} fps cap). " +
                      $"{(ForceHandheld ? "Forced with -handheld." : "Detected.")}");
        }
    }
}
