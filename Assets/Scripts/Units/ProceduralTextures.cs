using UnityEngine;

namespace IronMeridian.Units
{
    /// <summary>Small procedural textures (rings, markers) generated at runtime.</summary>
    public static class ProceduralTextures
    {
        public static Texture2D Ring(Color color, int size = 128, float inner = 0.38f, float outer = 0.48f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / size;
                    bool on = d >= inner && d <= outer;
                    px[y * size + x] = on ? color : Color.clear;
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        public static Texture2D Disc(Color color, int size = 64)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / size;
                    float a = Mathf.Clamp01((0.5f - d) * 12f);
                    px[y * size + x] = new Color(color.r, color.g, color.b, color.a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }
}
