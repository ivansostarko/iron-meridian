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

        // --- icon outline ---

        /// <summary>Shader property ids, resolved once rather than hashed per call.</summary>
        public static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        public static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        public static readonly int PaddingId = Shader.PropertyToID("_Padding");

        static Shader _iconOutline;
        static bool _iconOutlineChecked;

        static Shader IconOutlineShader()
        {
            if (_iconOutlineChecked) return _iconOutline;
            _iconOutlineChecked = true;

            // The shader lives under Assets/Resources so it is always built —
            // the same stripping problem the class comment describes. Shader.Find
            // is only a fallback for the case where it has been moved out and
            // added to Always Included Shaders instead.
            _iconOutline = Resources.Load<Shader>("Shaders/IconOutline")
                           ?? Shader.Find("IronMeridian/IconOutline");

            if (_iconOutline == null)
                Debug.LogWarning("[RuntimeMaterials] 'IronMeridian/IconOutline' not found — " +
                    "unit icons will fall back to a plain sprite with no selection outline. " +
                    "Expected at Assets/Resources/Shaders/IconOutline.shader.");

            return _iconOutline;
        }

        /// <summary>
        /// Unit-icon material that can draw an outline traced around the icon's
        /// own alpha (see <c>IconOutline.shader</c>). Falls back to a plain
        /// unlit quad if the shader is missing, so a broken import costs the
        /// outline and nothing else.
        ///
        /// Callers must test <see cref="SupportsOutline"/> before setting the
        /// outline properties, and must scale the quad by
        /// <see cref="IconOutlinePaddingScale"/> so the artwork keeps its size.
        /// </summary>
        public static Material IconWithOutline(Texture texture)
        {
            var shader = IconOutlineShader();
            if (shader == null) return UnlitTexture(texture);

            var mat = new Material(shader);
            mat.mainTexture = texture;
            mat.color = Color.white;
            mat.SetFloat(OutlineWidthId, 0f);
            mat.SetFloat(PaddingId, GameConfig.IconOutlinePadding);
            return mat;
        }

        /// <summary>True if this material was built by <see cref="IconWithOutline"/> and can draw one.</summary>
        public static bool SupportsOutline(Material mat) =>
            mat != null && mat.HasProperty(OutlineWidthId);

        /// <summary>
        /// Factor by which an icon quad must be enlarged to cancel the shader's
        /// artwork inset, so turning the outline on never changes the icon's size.
        /// </summary>
        public const float IconOutlinePaddingScale = 1f + 2f * GameConfig.IconOutlinePadding;
    }
}
