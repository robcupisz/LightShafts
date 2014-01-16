Shader "Hidden/ColorFilter" {
SubShader {
    Pass {
        Fog { Mode Off }
		Cull Off
		Blend Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"

struct v2f {
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};

sampler2D _MainTex;
float4 _MainTex_ST;
float4 _Color;

v2f vert (appdata_base v) {
    v2f o;
    o.pos = mul (UNITY_MATRIX_MVP, v.vertex);
    o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
    return o;
}

float4 frag(v2f i) : COLOR {
    return _Color * tex2D(_MainTex, i.uv);
}
ENDCG
    }
}
}
