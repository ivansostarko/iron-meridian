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
    /// and behind the interface rather than in front of it. It is currently
    /// pinned to the **bottom-right corner** of the map and drawn at about **one
    /// pixel**, near-transparent — which is as quiet as a thing can be while
    /// still being on the screen. Whether it is quiet enough to still count as
    /// attribution is a licence question, not a code one: **if the project's ion
    /// terms are ever reviewed, this is the class to look at**, and
    /// <see cref="Scale"/> alone undoes it.
    ///
    /// **It retries, and it keeps retrying.** The credit system is created
    /// lazily — the first time a tileset has something to attribute — so it
    /// does not exist when the map is built, and it rebuilds its children as
    /// credits come and go (a new tileset, a style change, the "Data
    /// attribution" popup opening). A one-shot pass would therefore be undone by
    /// the next rebuild, so this re-applies for as long as the map is up. The
    /// search gives up after <see cref="SearchSeconds"/> only if it never found
    /// the thing at all.
    ///
    /// See docs/02-CESIUM.md.
    /// </summary>
    public class CesiumCreditStyler : MonoBehaviour
    {
        /// <summary>The GameObject Cesium gives its default credit system.</summary>
        const string CreditSystemName = "CesiumCreditSystemDefault";

        /// <summary>
        /// Scale the credit's contents are drawn at. The logo and its line are
        /// a couple of hundred pixels across at scale 1, so this lands the whole
        /// block at roughly a pixel square — present, and no longer a watermark
        /// over the terrain.
        ///
        /// Applied as a **localScale on the credit's own children** rather than
        /// as the canvas's <c>scaleFactor</c>. A scale factor this small would
        /// ask uGUI's dynamic font for a zero-point glyph and blow the canvas's
        /// own rect up to a hundred thousand units; scaling the transform draws
        /// the same mesh smaller and asks nothing of the font at all.
        /// </summary>
        const float Scale = 0.004f;
        /// <summary>
        /// Opacity. At a pixel across this is belt and braces — it stops the
        /// mark reading as a stuck dead pixel on a dark sky.
        /// </summary>
        const float Alpha = 0.05f;
        /// <summary>Behind every canvas the game creates, all of which sort at 0 or above.</summary>
        const int SortingOrder = -500;

        /// <summary>
        /// Margin from the bottom-right corner, in canvas units before
        /// <see cref="Scale"/>. At a pixel across this is barely a nudge; it is
        /// here so the mark is not clipped by the very edge of the window.
        /// </summary>
        const float CornerMargin = 4f;

        /// <summary>
        /// Seconds spent looking for the credit system before giving up. Only
        /// the *search* is bounded — once found, the styling is re-applied for
        /// as long as the map is up, because the credit system rebuilds itself.
        /// </summary>
        const float SearchSeconds = 20f;
        /// <summary>Seconds between attempts, and between re-applications once found.</summary>
        const float IntervalSeconds = 1f;

        float _elapsed;
        float _timer;

        /// <summary>
        /// The credit system once found, so the per-second re-apply is not a
        /// <c>GameObject.Find</c> over the whole scene for the rest of the game.
        /// </summary>
        GameObject _host;

        /// <summary>Adds the styler to a host object, once.</summary>
        public static void Attach(GameObject host)
        {
            if (host != null && host.GetComponent<CesiumCreditStyler>() == null)
                host.AddComponent<CesiumCreditStyler>();
        }

        void Update()
        {
            // The clock only runs while the thing has never been found. Giving
            // up once it *has* been found would hand the corner back to a
            // full-size watermark the next time a tileset rebuilt its credits.
            if (_host == null)
            {
                _elapsed += Time.unscaledDeltaTime;
                if (_elapsed > SearchSeconds) { enabled = false; return; }
            }

            _timer -= Time.unscaledDeltaTime;
            if (_timer > 0f) return;
            _timer = IntervalSeconds;

            Apply();
        }

        void Apply()
        {
            if (_host == null) _host = GameObject.Find(CreditSystemName);
            var host = _host;
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

                // The scaler would otherwise re-derive a scale factor from the
                // window size every frame, on top of the one below.
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null && scaler.enabled) scaler.enabled = false;
                canvas.scaleFactor = 1f;

                // Every direct child, because the package has put the logo, the
                // attribution line and the "upgrade" prompt in siblings before
                // now — and each is re-anchored rather than merely shrunk.
                //
                // Anchoring is what actually decides where these end up: the
                // package pins them to whichever corner its own prefab chose,
                // and scaling about a top-left pivot leaves a one-pixel mark
                // sitting in the top-left. Pinning every one of them to the
                // bottom-right corner with a matching pivot puts the whole block
                // in the corner furthest from anything the player reads — and
                // the map's own bottom-right is the screen's, because the
                // editor chrome only ever insets the left edge.
                foreach (RectTransform child in canvas.transform)
                {
                    child.anchorMin = child.anchorMax = child.pivot = new Vector2(1f, 0f);
                    child.anchoredPosition = new Vector2(-CornerMargin, CornerMargin);
                    child.localScale = new Vector3(Scale, Scale, 1f);
                }

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
