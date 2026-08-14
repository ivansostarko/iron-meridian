using System.Collections.Generic;
using UnityEngine;

namespace IronMeridian.UI
{
    /// <summary>
    /// The typeface used by world-space text on the map — unit captions
    /// (<c>UnitLabel</c>) and boundary/phase-line captions
    /// (<c>IronMeridian.Lines.MapLabel</c>). One authority so the map never
    /// mixes two fonts.
    ///
    /// Unity's built-in font is Arial: not condensed, and not especially legible
    /// small. Unit names are long and a caption must not out-measure the icon it
    /// belongs to, so this prefers a condensed technical grotesque from the OS.
    /// Bahnschrift is the DIN 1451 derivative shipped with Windows 10/11 — the
    /// lettering standard behind road signage and military plates, and exactly
    /// the register an operational map wants. The list degrades through other
    /// condensed faces and finally to Unity's built-in font, so a machine with
    /// none of them still gets readable text.
    ///
    /// Legacy <c>Font</c>/<c>TextMesh</c> throughout, per the project's
    /// uGUI/no-TextMeshPro rule.
    /// </summary>
    public static class MapFont
    {
        /// <summary>
        /// Rasterisation size. Independent of on-screen size — that comes from
        /// each label's transform scale, so a larger value here only buys
        /// crispness when the camera is close.
        /// </summary>
        public const int FontSize = 48;

        /// <summary>Best first.</summary>
        static readonly string[] Candidates =
        {
            "Bahnschrift SemiCondensed",
            "Bahnschrift Condensed",
            "Bahnschrift",
            "Segoe UI Semibold",
            "Roboto Condensed",
            "Arial Narrow",
            "Tahoma",
        };

        static Font _font;
        static bool _resolved;

        /// <summary>
        /// The shared map font. Resolved once: a dynamic font carries its own
        /// glyph atlas, so this is one atlas for every label on the map rather
        /// than one per unit.
        /// </summary>
        public static Font Font
        {
            get
            {
                if (_resolved) return _font;
                _resolved = true;

                var names = Font.GetOSInstalledFontNames();
                var installed = new HashSet<string>(names ?? new string[0]);
                foreach (var candidate in Candidates)
                {
                    if (!installed.Contains(candidate)) continue;
                    _font = Font.CreateDynamicFontFromOSFont(candidate, FontSize);
                    if (_font != null) break;
                }

                // Not a warning. On a machine without any of the candidates the
                // built-in font is a reasonable outcome, not a fault anyone can act on.
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                Font.textureRebuilt += OnTextureRebuilt;
                return _font;
            }
        }

        // TextMeshes that were handed this font, so their glyph UVs can be
        // rebuilt when the atlas moves underneath them.
        static readonly List<TextMesh> _users = new List<TextMesh>();

        /// <summary>
        /// Puts the map font on a <see cref="TextMesh"/> and keeps it correct.
        ///
        /// Two things a caller must not have to remember: a TextMesh given a
        /// font but not that font's atlas material renders as solid white boxes;
        /// and a dynamic font repacks its atlas whenever an unseen glyph is
        /// requested, invalidating the UVs already baked into every mesh that
        /// uses it — which shows up as labels turning to garbage the first time
        /// a unit with a new character is deployed. Re-issuing the text is what
        /// forces those meshes to regenerate.
        /// </summary>
        public static void Apply(TextMesh mesh)
        {
            if (mesh == null) return;

            var font = Font;
            mesh.font = font;
            mesh.fontSize = FontSize;

            var renderer = mesh.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = font.material;

            // Units are created and destroyed continuously through a battle, so
            // without this the list would accumulate a dead entry per casualty
            // for the whole session. Compacting on a high-water mark keeps it
            // amortised — an atlas rebuild may never come to do the pruning.
            if (_users.Count >= _compactAt)
            {
                _users.RemoveAll(m => m == null);
                _compactAt = Mathf.Max(64, _users.Count * 2);
            }

            _users.Add(mesh);
        }

        /// <summary>Size at which the label list is compacted; grows with real usage.</summary>
        static int _compactAt = 64;

        static void OnTextureRebuilt(Font font)
        {
            if (font != _font) return;

            // Destroyed labels compare equal to null; prune them on the way past
            // rather than asking every caller to deregister.
            for (int i = _users.Count - 1; i >= 0; i--)
            {
                var mesh = _users[i];
                if (mesh == null) { _users.RemoveAt(i); continue; }
                mesh.text = mesh.text;
            }
        }
    }
}
