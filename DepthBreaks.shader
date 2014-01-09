Shader "Hidden/DepthBreaks" {
SubShader {
Pass {
	ZWrite Off Fog { Mode Off }
	Blend Off
	Cull Back
	Stencil
	{
		Ref 1
		Comp always
		Pass replace
	}
	
CGPROGRAM
#include "UnityCG.cginc"
#include "Shared.cginc"
#pragma target 3.0
#pragma glsl
#pragma vertex vert_simple
#pragma fragment frag
#pragma exclude_renderers xbox360

sampler2D _DepthEpi;
float4 _DepthEpiTexDim;
float _DepthThreshold;

float SampleDepth(float x, float y)
{
	// tex2Dlod, because tex2D requires calculating derivatives and we can't do that in a loop
	return abs(tex2Dlod(_DepthEpi, float4(x*_DepthEpiTexDim.z, y, 0, 0))).x;
}

float4 frag(posuv i) : COLOR
{
	// _DepthEpi was marked -1 if the ray missed the volume completely.
	// Skip, but don't discard, so it won't be a raymarching sample.
	if (tex2Dlod(_DepthEpi, float4(i.uv.x, i.uv.y, 0, 0)).x < 0.0)
		return -1;

	float y = i.uv.y;
	int step = GetInterpolationStep(i.uv.x);
	float stepRcp = 1.0/float(step);

	int x = floor(i.uv.x*_DepthEpiTexDim.x);
	int start = x*stepRcp;
	start *= step;
	x -= start;
	int left = x;
	int right = x;

	while (left > 0)
	{
		if (abs(SampleDepth(start + left - 1, y) - SampleDepth(start + left, y)) > _DepthThreshold)
			break;
		left--;
	}

	// We're going all the way to STEP, because if there's no depth break, we don't want to have
	// raymarching samples on both sides of whatever is our current step. So e.g. if STEP is 16,
	// we don't want a sample on the 15th and 16th pixel (16th is the leftmost from the next step),
	// because that's redundant and would actually show as discontinuity after interpolated along rays.
	// But if there is a depth break between the 15th and 16th pixel, we want samples on both.
	while (right < step)
	{
		if (abs(SampleDepth(start + right, y) - SampleDepth(start + right + 1, y)) > _DepthThreshold)
			break;
		right++;
	}

	// Because of going all the way to STEP, the very last sample is a pixel too far - clamp it.
	right = min(start + right, _DepthEpiTexDim.x - 1) - start;

	float l = (x - left)*stepRcp;
	float r = (right - x)*stepRcp;
	
	// If either l or r is 0, it's a raymarching sample. The texture has been cleared to black, so
	// we don't have to write anything and since we're discarding, stencil for those pixels will stay at 0.
	// Then we only have to run raymarching for pixels will stencil 0.
	if (l*r == 0)
		discard;
	return float4(l, r, 0, 0);
}

ENDCG
}

// Temporary, to clear the stencil, as GL.Clear doesn't work.
// Edit: fixed in 4.5, so this hack can be removed
Pass {
	ZWrite Off Fog { Mode Off }
	Blend Off
	Cull Front
	Stencil
	{
		Ref 0
		Comp always
		Pass zero
	}
	
CGPROGRAM
#include "UnityCG.cginc"
#include "Shared.cginc"
#pragma target 3.0
#pragma glsl
#pragma vertex vert_simple
#pragma fragment frag

float4 frag (posuv i) : COLOR
{
	return 0;
}
ENDCG
}

}
}