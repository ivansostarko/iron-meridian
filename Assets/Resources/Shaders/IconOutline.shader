// IconOutline.shader — outline for APP-6 unit icons on the map.
//
// Why this exists rather than QuickOutline: QuickOutline extrudes geometry
// along vertex normals. A unit icon is a single camera-facing quad, so every
// normal points at the camera and the extrusion collapses to a depth offset —
// no visible outline at all. And a geometric outline would trace the quad's
// rectangle, not the shape of the icon drawn on it.
//
// This traces the artwork instead: the icon's alpha channel is dilated by
// sampling a ring of taps around each pixel, and anything the dilation covers
// that the icon itself does not becomes outline. The result hugs the friendly
// rectangle, the hostile diamond and the echelon pips exactly.
//
// The artwork is inset into the quad by _Padding so the outline has empty
// texture to grow into; UnitActor scales the icon quad by the matching factor,
// so the icon reads at exactly the size it did before.
//
// Setting _OutlineWidth to 0 skips the whole dilation loop, so unselected
// units — almost all of them, almost always — cost the same as before.

Shader "IronMeridian/IconOutline"
{
    Properties
    {
        _MainTex ("Icon", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _OutlineColor ("Outline Colour", Color) = (1,1,1,1)
        // Radius in texture UV units. Keep at or below _Padding or the outline
        // is clipped by the edge of the quad.
        _OutlineWidth ("Outline Width (UV)", Range(0,0.25)) = 0
        _Padding ("Artwork Inset (UV)", Range(0,0.25)) = 0.1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _Padding;

            // Number of directions in the dilation ring. 12 is enough that the
            // outline reads as a smooth band rather than a star at the corners.
            #define OUTLINE_TAPS 12

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // Alpha of the artwork at a point, treating everything outside the
            // texture as empty. Without the explicit test, clamped edge texels
            // would smear outwards and the dilation would find "icon" in the
            // margin that exists precisely to hold the outline.
            float IconAlpha(float2 uv)
            {
                float inside = step(0.0, uv.x) * step(uv.x, 1.0) *
                               step(0.0, uv.y) * step(uv.y, 1.0);
                // Mip 0: the outline must stay crisp and must not thin out into
                // nothing when the icon is minified.
                return tex2Dlod(_MainTex, float4(saturate(uv), 0, 0)).a * inside;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Inset the artwork, leaving a transparent margin of _Padding
                // (in texture units) on every side for the outline.
                float2 uv = (i.uv - 0.5) * (1.0 + 2.0 * _Padding) + 0.5;

                float inside = step(0.0, uv.x) * step(uv.x, 1.0) *
                               step(0.0, uv.y) * step(uv.y, 1.0);
                fixed4 c = tex2D(_MainTex, saturate(uv)) * _Color;
                c.a *= inside;

                if (_OutlineWidth <= 0.0) return c;

                // Dilate the icon's alpha. Two radii per direction: the outer
                // ring sets the thickness, the inner one fills the band so it
                // does not read as a hollow halo.
                float dilated = 0.0;
                [unroll]
                for (int k = 0; k < OUTLINE_TAPS; k++)
                {
                    float angle = (6.28318530718 / OUTLINE_TAPS) * k;
                    float2 dir = float2(cos(angle), sin(angle));
                    dilated = max(dilated, IconAlpha(uv + dir * _OutlineWidth));
                    dilated = max(dilated, IconAlpha(uv + dir * (_OutlineWidth * 0.55)));
                }

                // Fade the outline with the icon so a dying unit does not leave
                // a glowing shell behind as it dissolves.
                float outlineA = saturate(dilated) * _OutlineColor.a * _Color.a;

                fixed4 result;
                result.rgb = lerp(_OutlineColor.rgb, c.rgb, saturate(c.a));
                result.a = max(c.a, outlineA);
                return result;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
