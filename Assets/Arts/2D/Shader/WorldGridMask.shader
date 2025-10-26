Shader "Unlit/WorldGridMask"
{
    Properties
    {
        _LineColor      ("Line Color", Color) = (0,1,0,1)
        _FillColor      ("Fill Color", Color) = (0,1,0,0.08)
        _OnlyLines      ("Only Lines (0/1)", Float) = 0

        _GridSpacing    ("Grid Spacing (world units)", Float) = 1.0
        _LineWidthPx    ("Line Width (px)", Float) = 1.5
        _SoftnessPx     ("Edge Softness (px)", Float) = 1.0
        _YOffset        ("Y Offset", Float) = 0.01

        // 与 GridAsset 对齐
        _GridOriginXZ   ("Grid Origin XZ", Vector) = (0,0,0,0)

        // 可建造遮罩（可选）
        _MaskTex        ("Buildable Mask (R8)", 2D) = "gray" {}
        _MaskSizeWH     ("Mask Size (W,H)", Vector) = (0,0,0,0)
        _CellWidth      ("Cell Width", Float) = 1.0
        _ShowOnlyBuildable ("Draw Only Buildable (0/1)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _LineColor, _FillColor;
            float  _OnlyLines;

            float  _GridSpacing, _LineWidthPx, _SoftnessPx, _YOffset;
            float4 _GridOriginXZ;

            sampler2D _MaskTex;
            float4    _MaskTex_TexelSize;        // x=1/width, y=1/height
            float4    _MaskSizeWH;               // x=W, y=H
            float     _CellWidth;
            float     _ShowOnlyBuildable;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wpos: TEXCOORD0;
                float2 guv : TEXCOORD1; // 世界网格坐标（已除以 spacing）
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 w = mul(unity_ObjectToWorld, v.vertex);
                w.y += _YOffset;
                o.pos  = mul(UNITY_MATRIX_VP, w);
                o.wpos = w.xyz;
                // 将世界 XZ 映射到规则网格坐标系（以 GridOrigin 为零点）
                o.guv  = (o.wpos.xz - _GridOriginXZ.xz) / max(_GridSpacing, 1e-6);
                return o;
            }

            // 采样可建造遮罩：根据世界坐标反推到 cell 索引，再去纹理里取
            float SampleBuildable(float2 wXZ)
            {
                // 计算 (x,z) 所在的 cell
                float2 local = (wXZ - _GridOriginXZ.xz) / max(_CellWidth, 1e-6);
                int cx = (int)floor(local.x);
                int cz = (int)floor(local.y);

                int W = (int)_MaskSizeWH.x;
                int H = (int)_MaskSizeWH.y;
                if (cx < 0 || cz < 0 || cx >= W || cz >= H) return 0.0;

                // 行主序：index = x + W * z  -> 采样坐标
                float2 uv = float2((cx + 0.5f) * _MaskTex_TexelSize.x,
                                   (cz + 0.5f) * _MaskTex_TexelSize.y);
                // 通道用 R（R8/Alpha8 都行）
                return tex2D(_MaskTex, uv).r;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 可建造遮罩（可选）
                float build = 1.0;
                if (_MaskSizeWH.x > 0.5 && _MaskSizeWH.y > 0.5) // 有效尺寸才采样
                    build = SampleBuildable(i.wpos.xz);

                if (_ShowOnlyBuildable > 0.5 && build < 0.5)
                {
                    // 完全不在可建造区域就不画
                    discard;
                }

                // 基于世界网格坐标的“到最近栅格线距离”（0 在网格线中心，0.5 在格子中心）
                float2 fracg = frac(i.guv);
                float2 toEdge = min(fracg, 1.0 - fracg);
                float d = min(toEdge.x, toEdge.y);

                // 像素等宽抗锯齿
                float2 fw = fwidth(i.guv);
                float pixToUV = max(fw.x, fw.y);
                float w = abs(_LineWidthPx) * pixToUV;
                float s = max(1e-4, abs(_SoftnessPx)) * pixToUV;

                float lineMask = 1.0 - smoothstep(w, w + s, d);

                // 填充（可根据 build 混色）
                fixed4 col;
                if (_OnlyLines > 0.5)
                {
                    col = fixed4(_LineColor.rgb, _LineColor.a * lineMask * build);
                }
                else
                {
                    fixed3 baseRGB = lerp(_FillColor.rgb, _LineColor.rgb, lineMask);
                    float  baseA   = max(_FillColor.a, _LineColor.a * lineMask);
                    // 如果提供了 build 遮罩，就用它做透明度门控
                    baseA *= (build > 0.5 ? 1.0 : 0.0);
                    col = fixed4(baseRGB, baseA);
                }
                return col;
            }
            ENDCG
        }
    }
}
