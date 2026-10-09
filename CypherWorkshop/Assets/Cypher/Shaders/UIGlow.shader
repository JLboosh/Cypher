// Holographic look for UI elements (buttons, input boxes, date picker cells) on world-space canvases.
// Border mode (_BorderPx > 0): a crisp glowing outline of constant pixel width with a soft inner halo
// and a faint fill. Flat mode (_BorderPx = 0): the whole rect glows, used for selected states and
// the text cursor. The Image color (incl. alpha) tints it; _Intensity pushes it into HDR for Bloom.
Shader "Cypher/UIGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Intensity ("Intensity (HDR multiplier)", Range(0, 8)) = 1.2
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.1
        _BorderPx ("Border Width (pixels, 0 = flat)", Range(0, 6)) = 1.4
        _HaloPx ("Inner Halo (pixels)", Range(0.01, 30)) = 4
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        ZTest [unity_GUIZTestMode]

        Pass
        {
            Name "UIGlow"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity;
                half _FillAlpha;
                float _BorderPx;
                float _HaloPx;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                half3 rgb = IN.color.rgb * _Intensity;
                if (_BorderPx <= 0.0)
                    return half4(rgb, IN.color.a);

                // Distance to the nearest edge in screen pixels, so borders stay crisp at any size.
                float2 px = min(IN.uv, 1.0 - IN.uv) / max(fwidth(IN.uv), 1e-5);
                float d = min(px.x, px.y);
                float border = 1.0 - smoothstep(_BorderPx, _BorderPx + 1.0, d);
                float halo = exp(-d / _HaloPx) * 0.35;
                return half4(rgb, saturate(_FillAlpha + border + halo) * IN.color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
