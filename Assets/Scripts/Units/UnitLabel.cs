using UnityEngine;
using IronMeridian.UI;

namespace IronMeridian.Units
{
    /// <summary>
    /// The caption above a unit icon — echelon indicator and formation name.
    ///
    /// It is two <see cref="TextMesh"/> copies, not one: a dark one offset down
    /// and right, and the coloured text over it. Coloured text on Cesium's
    /// photographic terrain has no guaranteed contrast — blue on a river, red on
    /// a tiled roof — and the shadow gives every glyph an edge whatever it is
    /// standing on. That is what makes the small default size legible; without
    /// it the captions would have to be big enough to bully the terrain, which
    /// is how they got large in the first place.
    ///
    /// Expects a parent that already faces the camera (the unit's billboard);
    /// this class does no billboarding of its own. For captions pinned to a
    /// geodetic point instead, see <see cref="Lines.MapLabel"/>.
    /// </summary>
    public class UnitLabel : MonoBehaviour
    {
        /// <summary>TextMesh world units per em. With <see cref="MapFont.FontSize"/>
        /// this fixes the size of one font pixel, which the shadow offset uses.</summary>
        public const float CharacterSize = 8f;

        /// <summary>Shadow offset in font pixels, down and to the right.</summary>
        const float ShadowOffsetPx = 2.5f;

        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        TextMesh _main;
        TextMesh _shadow;

        /// <summary>
        /// Builds the label under <paramref name="parent"/>. The returned
        /// component's transform carries both meshes, so scaling it scales the
        /// caption as a unit.
        /// </summary>
        public static UnitLabel Create(Transform parent, Vector3 localPosition, float scale)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = Vector3.one * scale;

            var label = go.AddComponent<UnitLabel>();
            float px = CharacterSize / MapFont.FontSize;

            // The shadow is built first and sits slightly further from the
            // camera. The billboard's +Z points away from the viewer, so a
            // positive local Z puts the shadow behind the text rather than
            // fighting it for the same depth.
            label._shadow = Build(go.transform, "Shadow",
                new Vector3(ShadowOffsetPx * px, -ShadowOffsetPx * px, 0.006f), ShadowColor);
            label._main = Build(go.transform, "Text", Vector3.zero, Color.white);

            return label;
        }

        static TextMesh Build(Transform parent, string name, Vector3 localPosition, Color colour)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var mesh = go.AddComponent<TextMesh>();
            mesh.anchor = TextAnchor.LowerCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = CharacterSize;
            mesh.fontStyle = FontStyle.Bold;   // holds its weight against terrain at this size
            mesh.color = colour;

            // Sets the font, its atlas material and the atlas-rebuild refresh.
            MapFont.Apply(mesh);
            return mesh;
        }

        string _text = "";

        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                if (_main != null) _main.text = _text;
                if (_shadow != null) _shadow.text = _text;
            }
        }

        /// <summary>Colour of the text itself; the shadow stays dark.</summary>
        public Color Color
        {
            set { if (_main != null) _main.color = value; }
        }

        public void SetScale(float scale) => transform.localScale = Vector3.one * scale;
    }
}
