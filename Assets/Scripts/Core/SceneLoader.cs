using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using IronMeridian.UI;

namespace IronMeridian.Core
{
    /// <summary>
    /// Loads a scene behind the standard loading overlay.
    ///
    /// **Why this exists rather than a bare `SceneManager.LoadScene`.** A
    /// synchronous load blocks the player's last frame until the whole scene is
    /// built, which reads as the game hanging on the click — and the Game
    /// scene is the heaviest thing here, because `GameController.Start` builds
    /// every system and every panel before it yields. Loading asynchronously
    /// with an overlay in the *outgoing* scene means the click is acknowledged
    /// immediately, the bar moves while the work happens, and the Game scene's
    /// own loader picks the story up from there for the terrain streaming.
    ///
    /// The two loaders are deliberately separate stages of the same wait, and
    /// both obey the register in docs/12-LOADERS.md:
    ///
    ///   1. **this one** — building the scene (0–90 % is Unity's own progress);
    ///   2. `GameController`'s — streaming the terrain in.
    ///
    /// **It always finishes.** `AsyncOperation` cannot stall in practice, but
    /// the overlay is given a timeout anyway and the activation is unblocked as
    /// soon as Unity reports the scene ready. A loader that can trap the player
    /// is worse than no loader (golden rule 7).
    /// </summary>
    [DisallowMultipleComponent]
    public class SceneLoader : MonoBehaviour
    {
        /// <summary>
        /// Unity holds an async load at 0.9 until activation is allowed, so
        /// that value is "done building" rather than "nearly done".
        /// </summary>
        const float ReadyProgress = 0.9f;

        /// <summary>Give up waiting and activate anyway. Generous: this is disk, not network.</summary>
        const float TimeoutSeconds = 25f;

        /// <summary>
        /// Shows the overlay and loads <paramref name="scene"/>. The caller's
        /// own screen keeps running underneath until the swap happens, which is
        /// what makes the transition read as a wait rather than as a freeze.
        /// </summary>
        public static void Load(string scene, string title, string subtitle)
        {
            // Its own object, marked DontDestroyOnLoad, because the coroutine
            // has to outlive the scene that started it — a loader parented to
            // the outgoing screen would be destroyed halfway through its own job.
            var go = new GameObject("SceneLoader");
            DontDestroyOnLoad(go);
            go.AddComponent<SceneLoader>().StartCoroutine(LoadRoutine(go, scene, title, subtitle));
        }

        static IEnumerator LoadRoutine(GameObject owner, string scene, string title, string subtitle)
        {
            var overlay = LoadingScreenUI.Show(title, subtitle);
            // The overlay is on its own canvas in this scene and would be
            // destroyed by the load; keeping it alive means the new scene comes
            // up behind it rather than flashing past it.
            DontDestroyOnLoad(overlay.gameObject);

            // One above the incoming scene's own loader, which uses the standard
            // order. For the moment both exist, this one is the outgoing layer
            // and must be the one on top — equal sorting orders would let the
            // draw order fall out of instantiation order, which is a coin toss.
            var canvas = overlay.GetComponent<Canvas>();
            if (canvas != null) canvas.sortingOrder = LoadingScreenUI.SortingOrder + 1;

            var op = SceneManager.LoadSceneAsync(scene);
            if (op == null)
            {
                // The scene is not in the build settings — run Tools > Iron
                // Meridian > Setup Project. Say so and get out of the way rather
                // than leaving the player under an overlay that never lifts.
                Debug.LogError($"[SceneLoader] Could not load scene '{scene}'. " +
                    "Is it in the build settings? Run Tools > Iron Meridian > Setup Project.");
                overlay.Dismiss("That screen is not available in this build.");
                Destroy(owner);
                yield break;
            }

            op.allowSceneActivation = false;

            // Unity's own build progress drives the bar for this stage.
            overlay.Track(() => Mathf.Clamp01(op.progress / ReadyProgress),
                          () => false,               // dismissal is ours, below
                          TimeoutSeconds);

            float startedAt = Time.unscaledTime;
            while (op.progress < ReadyProgress && Time.unscaledTime - startedAt < TimeoutSeconds)
                yield return null;

            // **The shipped data has to be in hand before the new scene builds.**
            // On WebGL it is fetched over the network rather than read off a
            // disk (StreamingAssetsFile), and a screen that built its unit list
            // before the catalogue arrived would come up empty and stay empty.
            // Everywhere else Ready is already true and this costs one test.
            if (!StreamingAssetsFile.Ready)
            {
                overlay.SetStatus("Loading the catalogue…");
                while (!StreamingAssetsFile.Ready &&
                       Time.unscaledTime - startedAt < TimeoutSeconds)
                    yield return null;

                // Timed out: go in anyway. A screen missing its unit list is bad;
                // an overlay that never lifts is worse (golden rule 7), and the
                // reader logs loudly enough to say which it was.
                if (!StreamingAssetsFile.Ready)
                    Debug.LogError("[SceneLoader] Shipped data did not arrive in time — " +
                                   "entering anyway. See docs/41-WEB.md.");
            }

            overlay.SetStatus("Entering the map…");
            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            // Hand over to the incoming scene's own loader. Dismissing here
            // rather than earlier is what stops a single frame of empty map
            // showing between the two overlays.
            overlay.Dismiss();
            Destroy(owner);
        }
    }
}
