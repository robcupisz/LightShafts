Shader "Hidden/InterpolateAlongRays" {
SubShader {
Pass {
	ZWrite Off Fog { Mode Off }
	Blend Off
	Cull Off
	
CGPROGRAM
#include "UnityCG.cginc"
#include "Shared.cginc"
#pragma target 3.0
#pragma glsl
#pragma vertex vert_simple
#pragma fragment frag

sampler2D _InterpolationEpi;
sampler2D _RaymarchedLightEpi;
float4 _RaymarchedLightEpiTexDim;

float4 frag (posuv i) : COLOR
{
	int intstep = GetInterpolationStep(i.uv.x);
	float stepRcp = 1.0/intstep;
	float2 sample = tex2D(_InterpolationEpi, i.uv).xy;

	float2 weight = 1;
	if (sample.x < 0)
	{
		#ifdef SHADER_API_D3D11
			return 0;
		#else
			weight = 0;
			sample = 0;
		#endif
	}

	// If left (x) is 0, right (y) should be 1 or 0. Let's make it 1 to avoid division by 0 when calculating weights.
	sample.y = lerp(sample.y, 1, sample.x == 0.0);

	sample *= intstep/_RaymarchedLightEpiTexDim.x;

	float2 left = i.uv;
	left.x -= sample.x;

	float2 right = i.uv;
	right.x += sample.y;

	weight *= sample.yx/(sample.x + sample.y);

	float3 leftSample = tex2D(_RaymarchedLightEpi, left);
	float3 rightSample = tex2D(_RaymarchedLightEpi, right);

	return (leftSample*weight.x + rightSample*weight.y).xyzz;
}

ENDCG
}
}
}