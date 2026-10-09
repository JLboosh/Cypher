// The hologram look: translucent additive fill, glowing edge with a soft halo, world-space
// scanlines, light film grain, glitch bands, and the three-stage materialize effect:
//   _Materialize 0.00-0.40  wireframe: the outline traces itself from the bottom up, a grid fades in
//   _Materialize 0.30-0.75  solid: the fill dissolves in bottom-to-top behind a bright noisy front
//   _Materialize 0.75-1.00  glow: the grid fades out (HologramPanel adds the brightness flash)
// Running it backwards (1 -> 0) is the dematerialize effect.
// Flicker is driven from C# through _Brightness so the text can flicker in sync.
// Values above 1 in the HDR colors are what Bloom turns into glow.
Shader "Cypher/Hologram"
{
    Properties
    {
        [HDR] _Color ("Fill Color", Color) = (0.1, 0.55, 0.9, 1)
        [HDR] _EdgeColor ("Edge Color", Color) = (0.5, 2.2, 3.2, 1)
        [HDR] _WireColor ("Wireframe Color", Color) = (0.3, 1.4, 2.2, 1)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.22
        _Brightness ("Brightness", Range(0, 4)) = 1
        _Materialize ("Materialize", Range(0, 1)) = 1
        _Glitch ("Glitch", Range(0, 1)) = 0
        _PanelSize ("Panel Size (meters, xy)", Vector) = (0.42, 0.26, 0, 0)
        _EdgeWidth ("Edge Width (m)", Range(0.0005, 0.05)) = 0.004
        _EdgeGlow ("Edge Halo Falloff (m)", Range(0.001, 0.1)) = 0.025
        _GridSize ("Wireframe Grid Size (m)", Range(0.005, 0.1)) = 0.02
        _ScanDensity ("Scanlines per Meter", Float) = 240
        _ScanSpeed ("Scanline Speed (m/s)", Float) = 0.03
        _ScanStrength ("Scanline Strength", Range(0, 1)) = 0.35
        _Grain ("Film Grain", Range(0, 0.5)) = 0.08
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
            Name "HologramForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EdgeColor;
                half4 _WireColor;
                half _FillAlpha;
                half _Brightness;
                half _Materialize;
                half _Glitch;
                float4 _PanelSize;
                float _EdgeWidth;
                float _EdgeGlow;
                float _GridSize;
                float _ScanDensity;
                float _ScanSpeed;
                half _ScanStrength;
                half _Grain;
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
                float3 positionWS : TEXCOORD1;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 local = IN.uv * _PanelSize.xy;   // position on the panel in meters

                // Edge: distance to the nearest border in meters, so the line is even on any aspect ratio.
                float2 toEdge = min(IN.uv, 1.0 - IN.uv) * _PanelSize.xy;
                float d = min(toEdge.x, toEdge.y);
                float edgeLine = 1.0 - smoothstep(_EdgeWidth * 0.5, _EdgeWidth, d);
                float edgeHalo = exp(-d / _EdgeGlow) * 0.6;

                // Materialize stages.
                float wireT = saturate(_Materialize / 0.4);
                float fillT = saturate((_Materialize - 0.3) / 0.45);
                float gridFade = 1.0 - saturate((_Materialize - 0.75) / 0.25);

                // Outline traces from bottom-center up both sides (0 at bottom, 1 at top).
                float2 c = IN.uv - 0.5;
                float around = abs(atan2(c.x, -c.y)) / PI;
                float traced = 1.0 - smoothstep(wireT * 1.05 - 0.05, wireT * 1.05, around);

                float2 g = abs(frac(local / _GridSize + 0.5) - 0.5) * _GridSize;
                float gridLine = (1.0 - smoothstep(0.0004, 0.001, min(g.x, g.y))) * traced * gridFade * step(0.001, _Materialize);

                // Fill dissolves upward behind a noisy front.
                float n = ValueNoise(local * 60.0);
                float sweep = IN.uv.y + (n - 0.5) * 0.25;
                float front = fillT * 1.3 - 0.15;
                float fillMask = saturate((front - sweep) / 0.03);
                float band = saturate(1.0 - abs(front - sweep) / 0.04) * saturate((1.0 - fillT) * 10.0) * step(0.001, fillT);

                float scan = sin((IN.positionWS.y + _Time.y * _ScanSpeed) * _ScanDensity * 6.2831853);
                scan = 1.0 - _ScanStrength * (0.5 + 0.5 * scan);

                // Glitch: random thin horizontal bands flare and shift toward white.
                float row = floor(IN.positionWS.y * 90.0);
                float glitchBand = step(0.93, Hash21(float2(row, floor(_Time.y * 24.0)))) * _Glitch;

                float grain = 1.0 + (Hash21(local * 700.0 + frac(_Time.y) * 37.0) - 0.5) * _Grain * 2.0;

                half3 color = _Color.rgb * scan * fillMask
                            + _EdgeColor.rgb * (edgeLine * traced + edgeHalo * fillMask)
                            + _WireColor.rgb * gridLine
                            + _EdgeColor.rgb * band * 2.0
                            + half3(0.6, 1.2, 1.5) * glitchBand;
                half alpha = saturate(_FillAlpha * scan * fillMask + edgeLine * traced + edgeHalo * 0.5 * fillMask
                                      + gridLine + band + glitchBand * 0.5);

                return half4(color * grain * _Brightness, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
