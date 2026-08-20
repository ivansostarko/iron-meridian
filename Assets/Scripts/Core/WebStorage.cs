using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace IronMeridian.Core
{
    /// <summary>
    /// The browser's side of saving, and the browser's side of the right mouse
    /// button.
    ///
    /// **Why a save needs anything at all.** On desktop and Android,
    /// <c>Application.persistentDataPath</c> is a real directory and a written
    /// file is a written file. In a browser it is an **in-memory filesystem
    /// backed by IndexedDB**, and the write only reaches the database when
    /// something asks it to. Unity asks on quit — and closing a tab is not
    /// quitting, so a scenario the player saved is gone the moment they navigate
    /// away. <see cref="Flush"/> is that ask, and every write path in the game
    /// calls it.
    ///
    /// It costs nothing to call anywhere else: on every other platform the whole
    /// class compiles down to an empty method.
    ///
    /// **Why the right button needs anything.** Right-click is the move order,
    /// the context menu and the cancel on every armed tool in this game. In a
    /// browser it is also how the *browser's* menu opens, over the canvas,
    /// swallowing the gesture — and Unity's templates do not suppress it. See
    /// docs/41-WEB.md.
    /// </summary>
    public static class WebStorage
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void IronMeridian_SyncFilesystem();
        [DllImport("__Internal")] static extern void IronMeridian_SuppressContextMenu();
#endif

        /// <summary>
        /// Pushes everything written under <c>persistentDataPath</c> through to
        /// the browser's IndexedDB. A no-op off the web.
        ///
        /// Call it **after** a write, not before, and do not wait on it: the
        /// file is already in the virtual filesystem, so the game can carry on
        /// while the database catches up. Nothing reads the result because there
        /// is nothing useful to do with a failure mid-battle; it is logged to
        /// the browser console instead.
        /// </summary>
        public static void Flush()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { IronMeridian_SyncFilesystem(); }
            catch (System.Exception e)
            {
                // An old or hand-edited template can be missing the .jslib. That
                // is a build problem rather than a runtime one, and it should be
                // reported once rather than throwing out of every save.
                Debug.LogWarning($"[WebStorage] Could not sync the filesystem: {e.Message}");
            }
#endif
        }

        /// <summary>
        /// Stops the browser's context menu opening over the game canvas.
        ///
        /// Run once, before the first scene, because the alternative is a player
        /// who right-clicks in the first ten seconds and gets a browser menu
        /// instead of a move order — and the first impression of a game that
        /// ignores half its own input.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { IronMeridian_SuppressContextMenu(); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[WebStorage] Could not suppress the context menu: {e.Message}");
            }
#endif
        }
    }
}
