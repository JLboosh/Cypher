// Crumples a hologram like a sheet of paper. Drawn on a finely subdivided plane whose texture is
// a snapshot of the hologram (text included). _Crumple 0 -> 1 runs, all in the vertex shader:
//   creases fold in one after another (fixed lines, so it's identical every time),
//   the sheet squeezes toward its center unevenly (that unevenness makes the wrinkles),
//   and from ~0.5 it wraps onto a lumpy sphere: the crumpled ball, which starts to glow.
Shader "Cypher/HologramCrumple"
{
    Properties
    {
        _MainTex ("Snapshot", 2D) = "black" {}
        [HDR] _FoldColor ("Fold Highlight", Color) = (0.4, 1.5, 2.4, 1)
        [HDR] _BallColor ("Ball Glow", Color) = (0.35, 1.3, 2.2, 1)
        _Crumple ("Crumple", Range(0, 1)) = 0
        _HalfSize ("Half Size (m)", Vector) = (0.21, 0.13, 0, 0)
        _BallRadius ("Ball Radius (object m)", Float) = 0.045
        _Seed ("Seed", Float) = 0.37
        _Brightness ("Brightness", Range(0, 4)) = 1
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
            Name "Crumple"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _FoldColor;
                half4 _BallColor;
                float _Crumple;
                float4 _HalfSize;
                float _BallRadius;
                float _Seed;
                half _Brightness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash31(i), Hash31(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            // q = position on the sheet, -1..1 on both axes.
            float3 Displace(float2 q, float c)
            {
                // Creases: sharp triangle-wave folds along fixed lines, each joining in turn.
                float crease = 0.0;
                [unroll]
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 2.39996 + _Seed * 6.2831;
                    float2 dir = float2(cos(a), sin(a));
                    float2 origin = float2(frac(sin(i * 12.9898 + _Seed) * 43758.5453), frac(sin(i * 78.233 + _Seed) * 43758.5453)) * 1.2 - 0.6;
                    float d = dot(q - origin, dir);
                    float tri = abs(frac(d * (1.5 + i * 0.7)) - 0.5) * 2.0 - 0.5;
                    float stage = saturate((c - i * 0.09) * 3.0);
                    crease += tri * stage * (0.5 + 0.12 * i);
                }

                float n = Noise3(float3(q * 4.0, _Seed * 10.0)) - 0.5;
                float n2 = Noise3(float3(q * 9.0 + 3.1, _Seed * 10.0)) - 0.5;

                // Squeeze toward the center, unevenly, so the paper buckles.
                float pull = saturate(c * (1.05 + 0.6 * n));
                pull = pull * pull * (3.0 - 2.0 * pull);
                float2 xy = q * _HalfSize.xy * lerp(1.0, 0.16, pull);
                float z = (crease * 0.4 + n2 * 0.5) * _HalfSize.y * 0.6 * sin(saturate(c * 1.3) * PI);
                float3 squeezed = float3(xy, z);

                // The ball: wrap the sheet around a lumpy sphere, its center facing the viewer (-Z).
                float theta = q.x * PI;
                float phi = q.y * 1.5;
                float3 dirB = float3(cos(phi) * sin(theta), sin(phi), -cos(phi) * cos(theta));
                float3 ball = dirB * _BallRadius * (1.0 + crease * 0.2 + n * 0.35); // lumpy, roughly 0.55x-1.45x

                return lerp(squeezed, ball, smoothstep(0.5, 1.0, c));
            }

            Varyings vert (Attributes IN)
            {
                float2 q = IN.positionOS.xy / max(_HalfSize.xy, 1e-4);
                float3 p = Displace(q, _Crumple);
                const float e = 0.02;
                float3 px = Displace(q + float2(e, 0), _Crumple);
                float3 py = Displace(q + float2(0, e), _Crumple);
                float3 normalOS = normalize(cross(px - p, py - p) + float3(0, 0, -1e-5));

                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(p);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(normalOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float facing = abs(dot(n, v));
                float c = _Crumple;

                half3 snap = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).rgb;
                half shade = lerp(0.35, 1.0, facing);                       // folds turned away go darker
                half3 fold = _FoldColor.rgb * pow(1.0 - facing, 3.0) * saturate(c * 3.0);
                half ballK = smoothstep(0.6, 1.0, c);
                half3 glow = _BallColor.rgb * ballK * (0.55 + 0.45 * facing);

                half3 color = snap * shade * (1.0 - 0.45 * ballK) + fold + glow;
                return half4(color * _Brightness, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
