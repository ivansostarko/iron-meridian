using System.IO;
using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// Central place for the Cesium ion access token.
    ///
    /// >>> ADD YOUR CESIUM ION API TOKEN IN ONE OF TWO PLACES <<<
    ///
    ///   1. Assets/StreamingAssets/cesium-token.txt   (preferred — git-ignored)
    ///      Replace the placeholder line with your token.
    ///
    ///   2. The constant below (quick local testing only — do NOT commit a
    ///      real token to a public repository).
    ///
    /// Get a token at https://ion.cesium.com/tokens (free account).
    /// See docs/02-CESIUM.md for the full guide.
    /// </summary>
    public static class CesiumTokenConfig
    {
        public const string Placeholder = "PASTE_YOUR_CESIUM_ION_TOKEN_HERE";

        /// <summary>Option 2: paste your token between the quotes.</summary>
        public const string IonAccessToken = Placeholder;

        static string _cached;

        public static string GetToken()
        {
            if (!string.IsNullOrEmpty(_cached)) return _cached;

            // Option 1: StreamingAssets/cesium-token.txt
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "cesium-token.txt");
                if (File.Exists(path))
                {
                    string txt = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(txt) && txt != Placeholder)
                        return _cached = txt;
                }
            }
            catch { /* fall through to constant */ }

            if (IonAccessToken != Placeholder) return _cached = IonAccessToken;

            Debug.LogWarning(
                "[Cesium] No Cesium ion token configured. The 3D map will not load. " +
                "Put your token in Assets/StreamingAssets/cesium-token.txt or in " +
                "CesiumTokenConfig.IonAccessToken. See docs/02-CESIUM.md.");
            return string.Empty;
        }
    }
}
