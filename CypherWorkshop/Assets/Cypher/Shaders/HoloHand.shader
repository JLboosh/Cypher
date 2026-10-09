// Translucent holographic hand: bright rim where the surface turns away from you (fresnel),
// a faint fill, scanlines, and a noisy dissolve driven by _Materialize (0 = gone, 1 = solid).
Shader "Cypher/HoloHand"
{
    Properties
    {
        [HDR] _RimColor ("Rim Color", Color) = (0.4, 1.6, 2.6, 1)
        [HDR] _FillColor ("Fill Color", Color) = (0.06, 0.3, 0.55, 1)
        _Materialize ("Materialize", Range(0, 1)) = 1
        _RimPower ("Rim Power", Range(0.5, 6)) = 2.2
        _ScanDensity ("Scanlines per Meter", Float) = 320
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
            Name "HoloHand"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half4 _FillColor;
                half _Materialize;
                half _RimPower;
                float _ScanDensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float rim = pow(1.0 - saturate(abs(dot(n, v))), _RimPower);

                // Dissolve in/out with a glowing edge.
                float noise = Hash(floor(IN.positionWS * 140.0));
                float cut = _Materialize * 1.1 - 0.05;
                clip(cut - noise);
                float edge = 1.0 - saturate((cut - noise) / 0.08);

                float scan = 0.75 + 0.25 * sin(IN.positionWS.y * _ScanDensity * 6.2831 + _Time.y * 2.0);
                half3 color = (_FillColor.rgb * 0.6 + _RimColor.rgb * rim) * scan + _RimColor.rgb * edge * 1.5;
                half alpha = saturate(0.12 + rim * 0.9 + edge);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
