using UnityEngine;
using UnityEngine.UI;

namespace IronMeridian.Map
{
    /// <summary>
    /// Shrinks Cesium's on-screen credit overlay and pushes it behind the game's
    /// own interface.
    ///
    /// **What this is and is not.** Cesium for Unity instantiates a credit
    /// canvas of its own — a logo and an attribution line — at whatever size and
    /// sorting order its prefab specifies, which on this map lands a bright
    /// watermark over the terrain and, worse, *over the HUD*, because it is a
    /// screen-space overlay created after ours. This makes it small, faint, and
    /// sorted behind everything the game draws.
    ///
    /// It is deliberately **not removed**. Cesium ion's terms of service require
    /// the attribution to be present, and a game that deleted it would be
    /// shipping in breach of the licence it streams its terrain under. What is
    /// adjustable is how loudly it shouts: the credit stays on screen, findable,
    /// and behind the interface rather than in front of it.
    ///
    /// **It retries.** The credit system is created lazily — the first time a
    /// tileset has something to attribute — so it does not exist when the map is
    /// built. Rather than guessing at a delay, this keeps looking for a few
    /// seconds and re-applies whenever the credit canvas is rebuilt (the system
    /// rebuilds its children as credits come and go).
    ///
    /// See docs/02-CESIUM.md.
    /// </summary>
    public class CesiumCreditStyler : MonoBehaviour
    {
        /// <summary>The GameObject Cesium gives its default credit system.</summary>
        const string CreditSystemName = "CesiumCreditSystemDefault";

        /// <summary>Scale the credit canvas is drawn at. Small enough to read as a mark, not a banner.</summary>
        const float Scale = 0.45f;
        /// <summary>
        /// Opacity. Low enough to disappear into the terrain at a glance and
        /// high enough to still be an attribution — which is the line this class
        /// has to walk, see the remarks.
        /// </summary>
        const float Alpha = 0.16f;
        /// <summary>Behind every canvas the game creates, all of which sort at 0 or above.</summary>
        const int SortingOrder = -500;

        /// <summary>Seconds spent looking for the credit system before giving up.</summary>
        const float SearchSeconds = 20f;
        /// <summary>Seconds between attempts, and between re-applications once found.</summary>
        const float IntervalSeconds = 1f;

        float _elapsed;
        float _timer;

        /// <summary>Adds the styler to a host object, once.</summary>
        public static void Attach(GameObject host)
        {
            if (host != null && host.GetComponent<CesiumCreditStyler>() == null)
                host.AddComponent<CesiumCreditStyler>();
        }

        void Update()
        {
            _elapsed += Time.unscaledDeltaTime;
            if (_elapsed > SearchSeconds) { enabled = false; return; }

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = IntervalSeconds;

            Apply();
        }

        void Apply()
        {
            var host = GameObject.Find(CreditSystemName);
            if (host == null) return;

            // Every canvas under it: the package has used one for the on-screen
            // line and a second for the "data attribution" popup, and which is
            // which is not a contract worth depending on.
            foreach (var canvas in host.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = SortingOrder;
                }

                // The scaler would otherwise fight the scale factor set below —
                // it recomputes it from the window size every frame.
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null && scaler.enabled) scaler.enabled = false;
                canvas.scaleFactor = Scale;

                var group = canvas.GetComponent<CanvasGroup>();
                if (group == null) group = canvas.gameObject.AddComponent<CanvasGroup>();
                group.alpha = Alpha;
                // Never intercept a click: at this opacity a credit line that
                // still swallowed input would be an invisible dead zone over the
                // map, which is the worst of both worlds.
                group.blocksRaycasts = false;
                group.interactable = false;
            }
        }
    }
}
