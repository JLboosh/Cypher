// Soft projector light beam (a cone/cylinder mesh): bright at the base, fading upward,
// brighter at the silhouette edges, with slow bands drifting up. Additive.
// uv.y = 0 at the base, 1 at the top.
Shader "Cypher/HoloBeam"
{
    Properties
    {
        [HDR] _Tint ("Tint", Color) = (0.3, 1.0, 1.8, 1)
        _Intensity ("Intensity", Range(0, 4)) = 0.6
        _BandSpeed ("Band Speed", Float) = 0.6
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
            Name "HoloBeam"
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
                float _BandSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 v = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float edge = 1.0 - abs(dot(normalize(IN.normalWS), v));
                float fade = pow(1.0 - IN.uv.y, 1.6);
                float bands = 0.75 + 0.25 * sin((IN.uv.y * 9.0 - _Time.y * _BandSpeed) * 6.2831);
                float a = fade * (0.25 + edge * edge * 0.9) * bands;
                return half4(_Tint.rgb * _Intensity, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
