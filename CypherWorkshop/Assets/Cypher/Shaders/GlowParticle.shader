// Soft additive glowing dot for particles (hologram motes, sparks, lab dust).
// The particle's own color/alpha (from the Particle System) is multiplied by the HDR tint.
Shader "Cypher/GlowParticle"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (0.6, 1.8, 2.8, 1)
        _Softness ("Softness", Range(0.05, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "GlowParticle"
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
                half _Softness;
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
                float2 c = IN.uv * 2.0 - 1.0;
                float falloff = saturate(1.0 - dot(c, c));
                falloff = pow(falloff, 1.0 / _Softness);
                return half4(_Tint.rgb * IN.color.rgb, falloff * IN.color.a * _Tint.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
