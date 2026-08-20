// Iron Meridian — the small amount of JavaScript a browser build needs.
//
// See docs/41-WEB.md. Two jobs, both of which have no C# equivalent:
//
//   1. Flushing the virtual filesystem to IndexedDB, so saves survive a reload.
//   2. Stopping the browser's own context menu eating the right-click that
//      orders a move.
//
// Emscripten mangles nothing here: names are called from C# by
// [DllImport("__Internal")] with exactly these spellings.

mergeInto(LibraryManager.library, {

    // ---------------------------------------------------------------- storage
    //
    // Unity maps Application.persistentDataPath onto an in-memory filesystem
    // (IDBFS) that is only written through to IndexedDB when something asks.
    // Unity asks on quit — which in a browser tab is a thing that frequently
    // never happens, because closing a tab is not quitting. Without this, a
    // scenario the player saved is gone the moment they close the page.
    //
    // Asynchronous by nature, and deliberately not awaited: the caller has
    // already written the file and there is nothing useful to do with the
    // result. A failure is logged rather than surfaced, because the alternative
    // is a modal about IndexedDB in the middle of a battle.
    IronMeridian_SyncFilesystem: function () {
        try {
            FS.syncfs(false, function (err) {
                if (err) console.error("[IronMeridian] Filesystem sync failed:", err);
            });
        } catch (e) {
            console.error("[IronMeridian] Filesystem sync threw:", e);
        }
    },

    // ------------------------------------------------------------ right click
    //
    // Right-click is the move order, the context menu and the cancel on every
    // armed tool in this game. In a browser it is also how you open the
    // *browser's* menu, and that menu appears over the canvas and swallows the
    // gesture. Unity's own templates do not suppress it.
    //
    // Bound to the canvas rather than to the document, so right-clicking the
    // page around the game still behaves like a web page.
    IronMeridian_SuppressContextMenu: function () {
        try {
            var canvas = document.querySelector("#unity-canvas") ||
                         document.querySelector("canvas");
            if (!canvas) {
                console.warn("[IronMeridian] No canvas found; right-click will open the browser menu.");
                return;
            }
            canvas.addEventListener("contextmenu", function (e) { e.preventDefault(); });
        } catch (e) {
            console.error("[IronMeridian] Could not suppress the context menu:", e);
        }
    }
});
