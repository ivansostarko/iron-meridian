using UnityEngine;
#if IRONMERIDIAN_STEAM
using Steamworks;
#endif

namespace IronMeridian.Core
{
    /// <summary>
    /// The game's one point of contact with Steam.
    ///
    /// Everything here is behind the <c>IRONMERIDIAN_STEAM</c> scripting define,
    /// which is only set once Steamworks.NET is installed (see
    /// <c>docs/36-STEAM.md</c>). Without it this compiles to a set of no-ops
    /// that report <see cref="Running"/> as false — so the project still opens,
    /// builds and runs for anyone who has not installed the SDK, and **call
    /// sites never need a <c>#if</c> of their own**. That is the whole point of
    /// the class: the conditional compilation stops here.
    ///
    /// Nothing calls it yet beyond its own startup hook. Wire the pieces you
    /// want — <see cref="OverlayChanged"/> to pause a battle,
    /// <see cref="Achieve"/> where a milestone is reached.
    /// </summary>
    public static class SteamIntegration
    {
        /// <summary>
        /// Steam's own test app. Publicly usable, so the integration can be
        /// exercised before the real app id exists — the overlay opens and the
        /// user name comes back; achievements silently do nothing.
        ///
        /// **Replace this with the app id from the partner site before
        /// shipping**, and keep it in step with <c>steam/app_build.vdf</c>.
        /// It is not a secret: it is the number in your store page's URL.
        /// </summary>
        public const uint AppId = 480;

        /// <summary>
        /// True when the SDK is compiled in *and* Steam was there to talk to.
        /// False in a build launched outside Steam, which is a normal way to
        /// run this game and must never be treated as an error.
        /// </summary>
        public static bool Running { get; private set; }

        /// <summary>The Steam persona name, or empty when not running.</summary>
        public static string PlayerName { get; private set; } = string.Empty;

        /// <summary>Whether the Steam overlay is currently covering the game.</summary>
        public static bool OverlayActive { get; private set; }

        /// <summary>
        /// Raised when the overlay opens (true) or closes (false). Steam asks
        /// that a single-player game not carry on running underneath it — hook
        /// this to whatever pauses a battle.
        /// </summary>
        public static event System.Action<bool> OverlayChanged;

        /// <summary>
        /// Unlocks an achievement by its API name — the identifier typed into
        /// the partner site, not the display name. Safe to call repeatedly and
        /// safe to call when Steam is not running; both do nothing.
        /// </summary>
        public static void Achieve(string apiName)
        {
#if IRONMERIDIAN_STEAM
            if (!Running || string.IsNullOrEmpty(apiName)) return;
            // Re-setting one is harmless, but storing on every call would spend
            // Steam's write budget on nothing.
            if (SteamUserStats.GetAchievement(apiName, out bool already) && already) return;
            SteamUserStats.SetAchievement(apiName);
            SteamUserStats.StoreStats();
#endif
        }

        /// <summary>
        /// Clears an achievement, for testing a lock/unlock path. Never call it
        /// in shipping code — players do not expect to lose one.
        /// </summary>
        public static void ResetAchievement(string apiName)
        {
#if IRONMERIDIAN_STEAM
            if (!Running || string.IsNullOrEmpty(apiName)) return;
            SteamUserStats.ClearAchievement(apiName);
            SteamUserStats.StoreStats();
#endif
        }

        // ------------------------------------------------------------ startup

        /// <summary>
        /// Runs before the splash screen, before any scene, on every entry into
        /// play — so nothing has to be placed in a scene and a scene edit
        /// cannot get the order wrong.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void Boot()
        {
#if IRONMERIDIAN_STEAM
            // Relaunch through Steam if the player started the .exe directly.
            // Must happen before Init, and never in the editor — there is no
            // process there to hand over to.
            if (!Application.isEditor && SteamAPI.RestartAppIfNecessary(new AppId_t(AppId)))
            {
                Application.Quit();
                return;
            }

            // Both check the native library against this build, and fail here
            // rather than as an access violation somewhere later on.
            if (!Packsize.Test() || !DllCheck.Test())
            {
                Debug.LogError("[Steam] steam_api64.dll does not match Steamworks.NET — see docs/36-STEAM.md.");
                return;
            }

            // Fails when Steam is not running, which is not an error: the game
            // is playable standalone, and that is what the installer build is.
            if (!SteamAPI.Init())
            {
                Debug.Log("[Steam] Steam not running — continuing without it.");
                return;
            }

            Running = true;
            PlayerName = SteamFriends.GetPersonaName();
            SteamPump.Ensure();
            Debug.Log($"[Steam] Connected as {PlayerName} (app {AppId}).");
#endif
        }

#if IRONMERIDIAN_STEAM
        internal static void SetOverlay(bool active)
        {
            if (OverlayActive == active) return;
            OverlayActive = active;
            OverlayChanged?.Invoke(active);
        }

        internal static void Stopped()
        {
            Running = false;
            OverlayActive = false;
        }
#endif
    }
}
