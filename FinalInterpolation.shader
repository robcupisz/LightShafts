Shader "Hidden/Final Interpolation" {
Properties {
	_ZTest ("", Float) = 8.0
}
Subshader {
	ZWrite Off Fog { Mode Off }
	ZTest [_ZTest]
	Cull Back
	Blend One SrcAlpha

	Pass {
CGPROGRAM
#pragma target 3.0
#pragma glsl
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile LIGHT_ON_SCREEN LIGHT_OFF_SCREEN
#pragma multi_compile SHOW_SAMPLES_OFF SHOW_SAMPLES_ON
#pragma multi_compile QUAD_SHAFTS FRUSTUM_SHAFTS
#pragma multi_compile DIRECTIONAL_SHAFTS SPOT_SHAFTS
#pragma multi_compile FLIP_WORKAROUND_OFF FLIP_WORKAROUND_ON
#include "UnityCG.cginc"
#include "Shared.cginc"

struct v2f {
	float4 pos : POSITION;
	float3 uv : TEXCOORD0;
};

sampler2D _DepthEpi;
sampler2D _Coord;
sampler2D _InterpolationEpi;
sampler2D _SamplePositions;
sampler2D _RaymarchedLight;
sampler2D _CameraDepthTexture;
float4 _CoordTexDim;
float4 _ScreenTexDim;
float _DepthThreshold;
float _ShowSamplesBackgroundFade;

inline void FixFlip(inout float x)
{
	// Flip upside-down on DX-like platforms, if the buffer
	// we're rendering into is flipped as well.
	#if UNITY_UV_STARTS_AT_TOP
	// FLIP_WORKAROUND_OFF check is only needed in pre 4.5 Unity, where _ProjectionParams.x has an incorrect value.
	// Can be safely removed in Unity 4.5.
	#if !defined(FLIP_WORKAROUND_ON)
		if (_ProjectionParams.x < 0)
			x *= -1.0;
	#endif
	#endif
}

inline void FixHalfTexelOffset(inout float2 uv)
{
	// DX9 half-texel offset
	#ifdef SHADER_API_D3D9
	uv += 0.5*_ScreenTexDim.zw;
	#endif
}

v2f vert (appdata_img v)
{
	v2f o;
	o.pos = v.vertex;

	#if defined(FRUSTUM_SHAFTS)
		o.pos = mul (UNITY_MATRIX_MVP, v.vertex);
		o.uv = o.pos.xyw;
		FixFlip(o.uv.y);
	#else
		FixFlip(o.pos.y);
		o.uv.xy = v.texcoord;
		FixHalfTexelOffset(o.uv.xy);
	#endif

	return o;
}

float2 ScreenUVToEpipolarUV(float2 screenUV)
{
	// Compute direction of the ray going from the light through the pixel
	float2 viewport = screenUV * 2.0f - 1.0f;
	float2 dir = normalize(viewport - _LightPos.xy);

	// The screen is divided into four triangles/sections, which meet at _LightPos and
	// have the four sides of the screen as their respective bases - screen exit edges.
	// We need to know which exit edge dir (from _LightPos to viewport, which is current pixel)
	// is pointing at.
	// Triangle edges passing through _LightPos (bottom left, br, tr, tl)
	float4 triangleEdgeTemp = (viewport.xxyy - float4(-1,1,-1,1)) * dir.yyxx;
	// Flags for triangle edge sides, so triangleEdgeSide.z == 1 is above the line going
	// through the light and top right corner.
	int4 triangleEdge = triangleEdgeTemp.xyyx < triangleEdgeTemp.zzww;	
	// left, bottom, right, top
	bool4 triangleFlag = triangleEdge.wxyz * (1 - triangleEdge.xyzw);

	// Distances to all four edges
	float4 distToScreenEdge = (float4(-1,-1, 1,1) - _LightPos.xyxy) / (dir.xyxy + float4( abs(dir.xyxy)<1e-6 ));
	// Distance to exit edge
	float distToExitEdge = dot(triangleFlag, distToScreenEdge);
	
	// Exit pos on screen edge, which dir is pointing at
	float2 exit = _LightPos.xy + dir * distToExitEdge;
	// Entry point different than _LightPos if light isn't on screen
	float2 entry = GetEpipolarLineEntryPoint(exit);
	
	// In epipolar space, epipolar lines are unwrapped, with screen's left triangle taking up topmost quarter, etc.
	float4 epipolarLines = float4(0, 0.25, 0.5, 0.75) + (0.5 + float4(-0.5, +0.5, +0.5, -0.5) * exit.yxyx)/4.0;
	float epipolarLine = dot(triangleFlag, epipolarLines);

	// Project current pos onto the epipolar line
	float2 epipolarLineDir = exit - entry.xy;
	float epipolarLineLength = length(epipolarLineDir);
	epipolarLineDir /= max(epipolarLineLength, 1e-6);
	float projected = dot((viewport - entry.xy), epipolarLineDir) / epipolarLineLength;

	float2 uvEpi = float2(projected, epipolarLine);
	uvEpi.x += _CoordTexDim.z;
	uvEpi.x *= (_CoordTexDim.x - 1)*_CoordTexDim.z;
	return uvEpi;
}
 
float3 DepthWeightedInterpolation(in float2 inWeights, in float2 leftBottomEpiUV, in float4 depthEpi, in float depth)
{
	float4 weights = float4(1 - inWeights.x, inWeights.x, inWeights.x, 1 - inWeights.x) * float4(inWeights.y, inWeights.y, 1 - inWeights.y, 1 - inWeights.y);

	// Depth weight = 1 for difference below threshold and fading to 0 above threshold.
	float4 depthWeights = saturate(_DepthThreshold / max(abs(depth - depthEpi), _DepthThreshold));
	depthWeights = pow(depthWeights, 4);
	weights *= depthWeights;

	// Normalize
	float totalWeight = dot(weights, float4(1,1,1,1));
	weights /= totalWeight;

	// Aim between two texels to get 4 samples in 2 texture fetches
	float offset = weights.z / max(weights.z + weights.w, 0.001);
	offset *= _CoordTexDim.z;
	float3 light = (weights.z + weights.w) * tex2D(_RaymarchedLight, leftBottomEpiUV + float2(offset, 0));

	offset = weights.y / max(weights.x + weights.y, 0.001);
	offset *= _CoordTexDim.z;
	light += (weights.x + weights.y) * tex2D(_RaymarchedLight, leftBottomEpiUV + float2(offset, _CoordTexDim.w));

	return light;
}

float2 ClampUVEpiToTexels(float2 uvEpi, out float2 weights)
{
	float2 uvScaled = uvEpi * _CoordTexDim.xy;

	// DX9 half-texel offset
	#ifdef SHADER_API_D3D9
	uvScaled += 0.5;
	#endif

	float2 texel = floor (uvScaled);
	weights = uvScaled - texel;
	texel += 0.5;
	texel = texel * _CoordTexDim.zw;
	return texel;
}

float3 SampleLighting(float2 uvEpi, float depth)
{
	float2 weights;
	float2 texel = ClampUVEpiToTexels(uvEpi, weights);
    float4 texelDepth;
	texelDepth.x = abs(tex2D(_DepthEpi, texel - float2(1,0)*_CoordTexDim.zw).x);
	texelDepth.y = abs(tex2D(_DepthEpi, texel - float2(0,0)*_CoordTexDim.zw).x);
	texelDepth.z = abs(tex2D(_DepthEpi, texel - float2(0,1)*_CoordTexDim.zw).x);
	texelDepth.w = abs(tex2D(_DepthEpi, texel - float2(1,1)*_CoordTexDim.zw).x);
	texel -= _CoordTexDim.zw;
	return DepthWeightedInterpolation(weights, texel, texelDepth, depth);
}

float4 frag(v2f i) : COLOR
{
	#if defined(FRUSTUM_SHAFTS)
		float2 uv = 0.5 + 0.5 * i.uv.xy / i.uv.z;
		FixHalfTexelOffset(uv);
	#else
		float2 uv = i.uv.xy;
	#endif

	half depth = UNITY_SAMPLE_DEPTH(tex2D (_CameraDepthTexture, uv));
	depth = Linear01Depth(depth);

	float near, far, rayLength;
	float3 rayN;
	IntersectVolume(uv, near, far, rayN, rayLength);
	depth = min(depth, far/rayLength);

	half2 unwrapped = ScreenUVToEpipolarUV(uv);

	#ifdef SHADER_API_D3D11
		float4 c = 0;
		if(depth > near/rayLength)
			c = SampleLighting(unwrapped, depth).xyzz;
	#else
		float4 c = step(near/rayLength, depth);
		c *= SampleLighting(unwrapped, depth).xyzz;
	#endif

	#if defined(SHOW_SAMPLES_ON)
		float4 sample = tex2D(_SamplePositions, uv);
		c *= _ShowSamplesBackgroundFade;
		float isRaymarchSample = any(sample.rgb);
		return float4(lerp(c, sample, isRaymarchSample).rgb, _ShowSamplesBackgroundFade*(1 - isRaymarchSample));
	#else
		return float4(c.rgb, 1);
	#endif
}
ENDCG
	}

}

Fallback off
}
