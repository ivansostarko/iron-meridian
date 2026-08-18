#if IRONMERIDIAN_STEAM
using Steamworks;
using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// Drives Steam's callback queue and closes the connection on exit.
    ///
    /// A <see cref="Object.DontDestroyOnLoad"/> singleton, for the same reason
    /// <see cref="Audio.MusicManager"/> is one: callbacks have to be pumped
    /// every frame of the *process*, not every frame of a scene, and a
    /// per-scene component would drop them across every navigation.
    ///
    /// The whole file is inside the Steam define — there is nothing here worth
    /// compiling without the SDK. <see cref="SteamIntegration"/> is the part
    /// the rest of the game talks to.
    /// </summary>
    internal class SteamPump : MonoBehaviour
    {
        static SteamPump _active;
        Callback<GameOverlayActivated_t> _overlay;

        internal static void Ensure()
        {
            if (_active != null) return;
            var go = new GameObject("[Steam]");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideInHierarchy;
            _active = go.AddComponent<SteamPump>();
        }

        void Awake() => _overlay = Callback<GameOverlayActivated_t>.Create(
            p => SteamIntegration.SetOverlay(p.m_bActive != 0));

        void Update() => SteamAPI.RunCallbacks();

        void OnApplicationQuit()
        {
            _overlay?.Dispose();
            SteamIntegration.Stopped();
            SteamAPI.Shutdown();
        }
    }
}
#endif
