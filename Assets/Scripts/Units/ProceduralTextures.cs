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

        /// <summary>
        /// Drop-placement reticle: a soft-edged ring with four cardinal ticks
        /// and a small centre dot, so the exact ground point is readable
        /// against busy satellite imagery.
        /// </summary>
        public static Texture2D Reticle(Color color, int size = 256)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float c = size / 2f;
            float aa = 1.6f / size;                 // feather width in normalised units

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - c) / size, dy = (y - c) / size;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;

                    // Main ring
                    a = Mathf.Max(a, Band(d, 0.360f, 0.400f, aa));
                    // Thin outer ring
                    a = Mathf.Max(a, Band(d, 0.452f, 0.464f, aa) * 0.65f);
                    // Centre dot
                    a = Mathf.Max(a, 1f - Mathf.SmoothStep(0.022f, 0.022f + aa, d));

                    // Cardinal ticks bridging the two rings
                    float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                    bool inTickBand = d > 0.400f && d < 0.452f;
                    if (inTickBand && (ax < 0.010f || ay < 0.010f)) a = Mathf.Max(a, 0.9f);

                    px[y * size + x] = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(a));
                }

            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        /// <summary>Anti-aliased 1 where inner &lt;= d &lt;= outer, fading over `aa`.</summary>
        static float Band(float d, float inner, float outer, float aa) =>
            Mathf.SmoothStep(inner - aa, inner + aa, d) * (1f - Mathf.SmoothStep(outer - aa, outer + aa, d));

        /// <summary>
        /// Dash strip for a LineRenderer in Tile mode: U runs along the line,
        /// so scrolling the material's texture offset marches the dashes around
        /// a range ring.
        /// </summary>
        public static Texture2D Dash(Color color, int width = 64, float dutyCycle = 0.55f, float feather = 0.06f)
        {
            var tex = new Texture2D(width, 4, TextureFormat.RGBA32, false);
            var px = new Color[width * 4];
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                // Fade both ends of the dash so it doesn't crawl with aliasing.
                float a = Mathf.SmoothStep(0f, feather, u) *
                          (1f - Mathf.SmoothStep(dutyCycle - feather, dutyCycle, u));
                for (int y = 0; y < 4; y++)
                    px[y * width + x] = new Color(color.r, color.g, color.b, color.a * Mathf.Clamp01(a));
            }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        // ------------------------------------------------------- action icons

        /// <summary>Arrow pointing right — the Move order.</summary>
        public static Texture2D MoveIcon(Color color, int size = 64) => Draw(color, size, (u, v) =>
        {
            float shaft = Inside(u > 0.16f && u < 0.60f && Mathf.Abs(v - 0.5f) < 0.085f);
            // Head: a triangle whose half-height shrinks to zero at the tip.
            float t = Mathf.InverseLerp(0.86f, 0.54f, u);
            float head = Inside(u >= 0.54f && u <= 0.86f && Mathf.Abs(v - 0.5f) < 0.26f * t);
            return Mathf.Max(shaft, head);
        });

        /// <summary>Crossed swords — the Attack order.</summary>
        public static Texture2D AttackIcon(Color color, int size = 64) => Draw(color, size, (u, v) =>
        {
            float a = Blade(u, v, 0.20f, 0.18f, 0.80f, 0.84f);
            float b = Blade(u, v, 0.80f, 0.18f, 0.20f, 0.84f);
            // Crossguard, so the glyph doesn't read as a plain ✕.
            float guard = Inside(Mathf.Abs(v - 0.34f) < 0.045f && Mathf.Abs(u - 0.5f) < 0.24f);
            return Mathf.Max(Mathf.Max(a, b), guard);
        });

        /// <summary>Shield — the Defence order.</summary>
        public static Texture2D ShieldIcon(Color color, int size = 64) => Draw(color, size, (u, v) =>
        {
            float y = 1f - v;                       // 0 at the top of the glyph
            if (y < 0.12f || y > 0.92f) return 0f;

            float half;
            if (y < 0.58f)
            {
                // Straight flanks with the top corners rounded off.
                half = 0.30f;
                float corner = Mathf.InverseLerp(0.12f, 0.22f, y);
                half *= Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(corner));
            }
            else
            {
                half = 0.30f * Mathf.Clamp01(1f - (y - 0.58f) / 0.36f);
            }
            return Inside(Mathf.Abs(u - 0.5f) < half);
        });

        /// <summary>Thick capsule between two normalised points — one sword blade.</summary>
        static float Blade(float u, float v, float x0, float y0, float x1, float y1)
        {
            var p = new Vector2(u, v);
            var a = new Vector2(x0, y0);
            var b = new Vector2(x1, y1);
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float d = Vector2.Distance(p, a + ab * t);
            // Taper toward the tip so it reads as a blade rather than a bar.
            float thickness = Mathf.Lerp(0.055f, 0.018f, t);
            return Inside(d < thickness);
        }

        static float Inside(bool b) => b ? 1f : 0f;

        static Texture2D Draw(Color color, int size, System.Func<float, float, float> shape)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            // 2x2 supersample: these glyphs are all hard edges and look ragged
            // at icon sizes otherwise.
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            a += shape((x + 0.25f + sx * 0.5f) / size, (y + 0.25f + sy * 0.5f) / size);
                    a *= 0.25f;
                    px[y * size + x] = new Color(color.r, color.g, color.b, color.a * a);
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
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
