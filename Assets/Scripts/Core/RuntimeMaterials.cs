using UnityEngine;

namespace IronMeridian.Core
{
    /// <summary>
    /// Build-safe material construction. Every material in this project is
    /// created from code (no .mat assets), so <see cref="Shader.Find"/> is the
    /// only lookup path — and it can only find shaders the player actually
    /// shipped. Unity strips any shader that no material asset references and
    /// that isn't in Graphics Settings > Always Included Shaders, so
    /// "Unlit/Color" resolves in the editor but returns null in a build,
    /// producing the magenta error material. "Sprites/Default" is a built-in
    /// default and is always present, so route everything through it.
    /// </summary>
    public static class RuntimeMaterials
    {
        // Ordered by preference: the first is always shipped, the rest are
        // fallbacks in case a future render pipeline change drops it.
        static readonly string[] UnlitCandidates =
        {
            "Sprites/Default",
            "Unlit/Color",
            "Legacy Shaders/Transparent/Diffuse"
        };

        static Shader _unlit;

        static Shader UnlitShader()
        {
            if (_unlit != null) return _unlit;
            foreach (var name in UnlitCandidates)
            {
                _unlit = Shader.Find(name);
                if (_unlit != null) return _unlit;
            }
            Debug.LogError("[RuntimeMaterials] No unlit shader available — visuals will render magenta. " +
                "Add 'Sprites/Default' to Project Settings > Graphics > Always Included Shaders.");
            return null;
        }

        /// <summary>Flat, unlit, camera-independent colour (strength bars, rings, lines).</summary>
        public static Material UnlitColor(Color color)
        {
            // A null shader yields Unity's magenta error material rather than
            // throwing; UnlitShader() has already logged what went wrong.
            var mat = new Material(UnlitShader());
            mat.color = color;
            return mat;
        }

        /// <summary>Unlit textured quad (unit icons, ground markers).</summary>
        public static Material UnlitTexture(Texture texture)
        {
            var mat = UnlitColor(Color.white);
            mat.mainTexture = texture;
            return mat;
        }
    }
}
