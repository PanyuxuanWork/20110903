Shader "Custom/OutlineUnlit"
{
    Properties { _OutlineColor("Color", Color)=(1,0.85,0.2,1) _OutlineWidth("Width", Float)=0.02 }
    SubShader{
        Tags{"Queue"="Transparent+100" "RenderType"="Transparent"}
        Cull Front
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass{
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _OutlineColor; float _OutlineWidth;
            struct app{float4 vertex:POSITION; float3 normal:NORMAL;};
            struct v2f{float4 pos:SV_POSITION; fixed4 col:COLOR;};
            v2f vert(app v){
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wn = UnityObjectToWorldNormal(v.normal);
                wp += normalize(wn) * _OutlineWidth;
                o.pos = UnityWorldToClipPos(float4(wp,1));
                o.col = _OutlineColor;
                return o;
            }
            fixed4 frag(v2f i):SV_Target { return i.col; }
            ENDCG
        }
    }
    FallBack Off
}
