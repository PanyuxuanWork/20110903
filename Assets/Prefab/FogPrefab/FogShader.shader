Shader "Custom/FogOfWarURP"
{
    Properties
    {
        _MaskTex("Mask (R=Explored, G=Visible)", 2D) = "black" {}
        _NoiseTex("Noise", 2D) = "gray" {}

        _UnexploredColor("Unexplored Color", Color) = (0,0,0,1)
        _ExploredFogColor("Explored Fog Color", Color) = (0,0,0,1)

        _UnexploredAlpha("Unexplored Alpha", Range(0,1)) = 1
        _ExploredAlpha("Explored Alpha", Range(0,1)) = 0.65

        _NoiseScale("Noise Scale", Float) = 2
        _NoiseStrength("Noise Strength", Range(0,1)) = 0.35
        _NoiseSpeed("Noise Speed (XY)", Vector) = (0.03, 0.02, 0, 0)

        _VisibleClear("Visible Clear (0=full fog,1=fully clear)", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalRenderPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_MaskTex);  SAMPLER(sampler_MaskTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            float4 _UnexploredColor;
            float4 _ExploredFogColor;
            float _UnexploredAlpha;
            float _ExploredAlpha;

            float _NoiseScale;
            float _NoiseStrength;
            float4 _NoiseSpeed;

            float _VisibleClear;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                float4 mask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv);
                float explored = mask.r; // 0..1
                float visible  = mask.g; // 0..1

                float2 nuv = uv * _NoiseScale + _Time.y * _NoiseSpeed.xy;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).r;
                float noise = lerp(1.0, n, _NoiseStrength);

                float3 fogColor = lerp(_UnexploredColor.rgb, _ExploredFogColor.rgb, explored);
                float baseAlpha = lerp(_UnexploredAlpha, _ExploredAlpha, explored);

                float clearFactor = visible * _VisibleClear;
                float alpha = lerp(baseAlpha, 0.0, clearFactor);

                float exploredMask = step(0.001, explored);          // explored>0 -> 1£¬·ñÔò 0
                float noiseFactor  = lerp(1.0, noise, exploredMask); // unexplored -> 1£¬ explored -> noise

                alpha *= noiseFactor;
                return half4(fogColor, alpha);
            }
            ENDHLSL
        }
    }
}