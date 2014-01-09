Shader "Hidden/Depth" {
SubShader {
    Tags { "RenderType"="Opaque" }
    Pass {
        Fog { Mode Off }
		Cull Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"

struct v2f {
    float4 pos : SV_POSITION;
    float depth : TEXCOORD0;
};

v2f vert (appdata_base v) {
    v2f o;
    o.pos = mul (UNITY_MATRIX_MVP, v.vertex);

    // We want [0,1] linear depth, so that 0.5 is half way between near and far.
    COMPUTE_EYEDEPTH(o.depth);
    o.depth = (o.depth - _ProjectionParams.y)/(_ProjectionParams.z - _ProjectionParams.y);

    return o;
}

float4 frag(v2f i) : COLOR {
    return i.depth;
}
ENDCG
    }
}
}