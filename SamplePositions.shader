Shader "Hidden/Sample Positions" {
Properties {
}
SubShader {
Pass {
	Cull Off ZWrite Off ZTest Always Fog { Mode Off }

CGPROGRAM
#pragma target 5.0

#pragma vertex vert
#pragma fragment frag

#include "UnityCG.cginc"

sampler2D _Coord;
sampler2D _InterpolationEpi;
float4 _OutputTexDim;
float4 _CoordTexDim;
float _SampleType; // 0 - raymarched, 1 - interpolated along the epi ray
float4 _Color;
RWTexture2D<float4> _OutputTex;

struct v2f {
	float4 pos : POSITION;
	float2 uv : TEXCOORD0;
};

v2f vert (appdata_img v)
{
	v2f o;
	o.pos = v.vertex;
	o.uv = o.pos.xy * 0.5 + 0.5;
	o.uv.y = 1 - o.uv.y;
	return o;
}

half4 frag (v2f i) : COLOR
{
	int2 loc = floor(tex2D(_Coord, i.uv).xy*_OutputTexDim.xy);
	if (_SampleType == all(tex2D(_InterpolationEpi, i.uv).xy))
		_OutputTex[loc] = _Color;
	
	return 0;
}

ENDCG

}
}

Fallback Off
}
