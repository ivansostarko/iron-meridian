using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// Watches for the network going away and tells somebody.
    ///
    /// This matters more here than in most games because the map is **streamed**:
    /// Cesium pulls terrain tiles and imagery over the network as the camera
    /// moves, so losing the connection does not produce an error dialog — it
    /// produces a map that quietly stops filling in. Ground the player has
    /// already visited keeps working from cache, new ground does not, and
    /// without a message the obvious conclusion is that the game has hung.
    ///
    /// **What this can and cannot tell you.** `Application.internetReachability`
    /// reports whether a network *route* exists — a cable in the socket, a
    /// carrier on the radio. It does not know whether anything at the other end
    /// answers, so a router with no upstream still reads as reachable and this
    /// will stay quiet. That case is caught by the other half of the wiring:
    /// `MapManager.LoadError` fires when a tileset request actually fails, and
    /// <see cref="GameController"/> routes it to the same banner. Between the
    /// two, "no route" and "route but no service" are both covered; neither
    /// alone is.
    /// </summary>
    public class ConnectivityWatcher : MonoBehaviour
    {
        /// <summary>
        /// Seconds between checks. The API is a cheap property read, but polling
        /// it every frame would be noise — a network outage is not a
        /// frame-critical event and a second or two of latency on the message
        /// costs nothing.
        /// </summary>
        const float PollSeconds = 2f;

        /// <summary>Raised when the connection is lost, and again when it returns.</summary>
        public System.Action<bool> ReachabilityChanged;

        float _timer;
        bool _reachable = true;
        bool _primed;

        void Update()
        {
            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = PollSeconds;

            bool reachable = Application.internetReachability != NetworkReachability.NotReachable;

            // The first poll establishes the baseline rather than announcing it.
            // Starting the editor with no network should not fire a "connection
            // lost" alert — nothing was lost; it was never there. The loading
            // screen's own failure path covers that case with a better message.
            if (!_primed)
            {
                _primed = true;
                _reachable = reachable;
                return;
            }

            if (reachable == _reachable) return;
            _reachable = reachable;
            ReachabilityChanged?.Invoke(reachable);
        }

        /// <summary>Whether a network route existed at the last poll.</summary>
        public bool IsReachable => _reachable;
    }
}
