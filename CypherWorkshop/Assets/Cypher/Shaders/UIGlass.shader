// Frosted holographic glass behind UI panels: a navy-to-teal vertical gradient (lighter at the
// top), faint scanlines, and a softly lit inner edge. Alpha-blended (not additive), so it
// actually covers whatever is behind the panel and gives text a calm, even background.
// Image color tints it; CanvasGroup alpha fades it.
Shader "Cypher/UIGlass"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _TopColor ("Top Color", Color) = (0.07, 0.17, 0.26, 0.9)
        _BottomColor ("Bottom Color", Color) = (0.03, 0.08, 0.14, 0.93)
        _EdgeColor ("Inner Edge Light", Color) = (0.35, 0.8, 1.0, 0.35)
        _EdgePx ("Edge Light Width (pixels)", Range(1, 40)) = 12
        _ScanStrength ("Scanline Strength", Range(0, 0.2)) = 0.035
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

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        ZTest [unity_GUIZTestMode]

        Pass
        {
            Name "UIGlass"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _BottomColor;
                half4 _EdgeColor;
                float _EdgePx;
                half _ScanStrength;
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
                half4 c = lerp(_BottomColor, _TopColor, IN.uv.y);

                float2 px = min(IN.uv, 1.0 - IN.uv) / max(fwidth(IN.uv), 1e-5);
                float d = min(px.x, px.y);
                c.rgb += _EdgeColor.rgb * exp(-d / _EdgePx) * _EdgeColor.a;

                float row = IN.uv.y / max(fwidth(IN.uv.y), 1e-5);
                c.rgb *= 1.0 - _ScanStrength * (0.5 + 0.5 * sin(row * 1.5708));

                return half4(c.rgb * IN.color.rgb, c.a * IN.color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
