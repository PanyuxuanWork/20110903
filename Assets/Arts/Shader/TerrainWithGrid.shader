Shader "Custom/TerrainWithGridURP"
{
    Properties
    {
        _MainTex ("Base Texture", 2D) = "white" {}
        _LineColor ("Line Color", Color) = (0,1,0,1)
        _FillColor ("Fill Color", Color) = (0,1,0,0.08)
        _GridSpacing ("Grid Spacing", Float) = 1.0
        _LineWidthPx ("Line Width (px)", Float) = 1.5
        _SoftnessPx ("Edge Softness (px)", Float) = 1.0
        _YOffset ("Y Offset", Float) = 0.01
        _GridOriginXZ ("Grid Origin XZ", Vector) = (0,0,0,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        
        Pass
        {
            Name "ForwardBase"
            Tags { "LightMode"="UniversalForward" }

            // 绑定到 URP 标准 Terrain shader
            HLSLPROGRAM
            #pragma multi_compile_fog
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Shader 属性
            half4 _LineColor, _FillColor; // 使用 half4 替代 fixed4
            float _OnlyLines;
            float _GridSpacing, _LineWidthPx, _SoftnessPx, _YOffset;
            float4 _GridOriginXZ;

            struct Attributes
            {
                float4 vertex : POSITION;
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float2 guv : TEXCOORD1; // 世界网格坐标
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float4 w = mul(unity_ObjectToWorld, v.vertex);
                w.y += _YOffset;
                o.pos = mul(UNITY_MATRIX_VP, w);
                o.worldPos = w.xyz;
                o.guv = (o.worldPos.xz - _GridOriginXZ.xz) / max(_GridSpacing, 1e-6);
                return o;
            }

            // 网格效果（线条和填充）
            half4 frag(Varyings i) : SV_Target
            {
                // 基于世界网格坐标的“到最近栅格线距离”
                float2 fracg = frac(i.guv);
                float2 toEdge = min(fracg, 1.0 - fracg);
                float d = min(toEdge.x, toEdge.y);

                // 像素等宽抗锯齿
                float2 fw = fwidth(i.guv);
                float pixToUV = max(fw.x, fw.y);
                float w = abs(_LineWidthPx) * pixToUV;
                float s = max(1e-4, abs(_SoftnessPx)) * pixToUV;

                float lineMask = 1.0 - smoothstep(w, w + s, d);

                // 填充和线条颜色
                half4 col = lerp(_FillColor, _LineColor, lineMask);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Terrain/Lit"
}
