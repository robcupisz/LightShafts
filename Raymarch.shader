Shader "Hidden/Raymarch" {
SubShader {
Pass {
	ZWrite Off Fog { Mode Off }
	Blend Off
	Cull Back
	Stencil
	{
		Ref 0
		Comp equal
	}
	
CGPROGRAM
#include "UnityCG.cginc"
#include "Shared.cginc"
#pragma target 3.0
#pragma glsl
#pragma vertex vert_simple
#pragma fragment frag
#pragma multi_compile COLORED_ON COLORED_OFF
#pragma multi_compile ATTENUATION_CURVE_ON ATTENUATION_CURVE_OFF
#pragma multi_compile COOKIE_TEX_ON COOKIE_TEX_OFF
#pragma multi_compile DIRECTIONAL_SHAFTS SPOT_SHAFTS

sampler2D _CameraDepthTexture;
sampler2D _Coord;
sampler2D _Shadowmap;
sampler2D _ColorFilter;
sampler2D _AttenuationCurveTex;
sampler2D _Cookie;
float4 _ShadowmapDim;
float4 _ScreenTexDim;
float4 _LightColor;
float _Extinction;
float _Brightness;
float _MinDistFromCamera;

inline float attenuation(float distance)
{
	#if ATTENUATION_CURVE_ON
		return tex2Dlod (_AttenuationCurveTex, float4(distance, 0, 0, 0)).r;
	#else
		#if defined(DIRECTIONAL_SHAFTS)
			return exp(_Extinction * distance);
		#else
			return 1.0 / (1.0 + 25.0 * distance * distance);
		#endif
	#endif
}

inline float cookie(float2 pos)
{
	#if defined(COOKIE_TEX_ON)
		return tex2Dlod(_Cookie, float4(pos.xy, 0, 0)).w;
	#else
		float2 a = pos*2.0 - 1.0;
		return saturate(1.0 - pow(dot(a, a), 3.0));
	#endif
}

#ifdef SHADER_API_D3D9
#define MAX_STEPS 256
#else
#define MAX_STEPS 512
#endif

float4 frag(posuv i) : COLOR
{
	float2 uv = tex2D(_Coord, i.uv).xy;
	float2 uv05 = (floor(uv*_ScreenTexDim.xy) + 0.5)*_ScreenTexDim.zw;
	float sceneDepth = Linear01Depth(UNITY_SAMPLE_DEPTH(tex2D (_CameraDepthTexture, uv05)));

	float3 cameraPos = _CameraPosLocal.xyz;
	float near, far, rayLength;
	float3 rayN;

	// In the current space the light volume is either the full unit cube
	// centered at 0, or a frustum inscribed in that cube. We proceed the same,
	// except that for frustum pos.xy (shadowmap UVs) will need to be stretched out
	// closer to the near plane, to match shadowmap's perspective.
	// (We know we'll intersect the volume, since this shader shouldn't be run for
	// samples which don't.)
	IntersectVolume(uv, near, far, rayN, rayLength);

	sceneDepth *= rayLength;

	// Don't want to raymarch behind the camera, so clamp near intersection to 0.
	near = max(near, 0);
	near = max(near, _MinDistFromCamera);

	// The box is centered around 0,0,0. Offset it by 0.5 to
	// (0,0,0) (1,1,1) to make shadow sampling easier.
	cameraPos += 0.5;

	float3 frontPos = near*rayN + cameraPos;
	float depthAlongView = (min(sceneDepth, far) - near);
	float3 frontToBack = depthAlongView*rayN;

	// Number of steps is the length of frontToBack in shadowmap texels,
	// either along shadomap width or height, whichever projection is longer.
	float2 frontToBackTemp = floor (frontToBack.xy * _ShadowmapDim.xy);
	int steps = 2 * max(abs(frontToBackTemp.x), abs(frontToBackTemp.y));
	steps = clamp((fixed)steps, 4, MAX_STEPS);
	float oneOverSteps = 1.0 / float(steps);
	frontToBack.xy = frontToBackTemp.xy * _ShadowmapDim.zw;
	float3 frontToBackOverSteps = frontToBack*oneOverSteps;

	#if defined(SPOT_SHAFTS)
	float3 params = float3(0.5, 0.5 - _FrustumApex, - 0.5 - _FrustumApex);
	#endif

	float3 inscatter = 0;
	for (int i = 0; i < steps; i++)
	{
		float3 pos = frontPos + i * frontToBackOverSteps;
		#if defined(SPOT_SHAFTS)
		// stretch out UVs as we get closer to the near plane
		pos.xy = (pos.xy - params.x) * params.y / (pos.z + params.z) + params.x;
		#endif
		
		// Important to use tex2Dlod to save on calculating derivatives, and we're
		// sampling once every texel anyway. tex2D is 6x slower.
		float3 sample = tex2Dlod(_Shadowmap, float4(pos.xy, 0, 0)).x > pos.z;

		sample *= attenuation(pos.z);

		#if defined(COLORED_ON)
			sample *= tex2Dlod(_ColorFilter, float4(pos.xy, 0, 0)).xyz;
		#endif

		#if defined(SPOT_SHAFTS)
			sample *= cookie(pos.xy);
		#endif

		inscatter += sample;
	}

	// Normalize inscattered light depending on how many steps we took and what part
	// of the entire depth did we raymarch.
	inscatter *= _LightColor.rgb * _Brightness * oneOverSteps * depthAlongView;
	return saturate(inscatter).xyzz;
}

ENDCG
}
}
}