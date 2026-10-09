// Soft glowing line for LineRenderers (Cypher's voice ring). Bright core across the line's width,
// fading at the edges. Color = line vertex color x HDR tint x intensity (intensity feeds Bloom).
Shader "Cypher/GlowLine"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (0.4, 1.6, 2.4, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GlowLine"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Intensity;
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
                float across = abs(IN.uv.y * 2.0 - 1.0);
                float core = 1.0 - smoothstep(0.0, 1.0, across);
                core *= core;
                return half4(_Tint.rgb * IN.color.rgb * _Intensity, core * IN.color.a * _Tint.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
