Shader "Hidden/Coord" {
Subshader {
	ZTest Always Cull Off ZWrite Off Fog { Mode Off }

	Pass {
CGPROGRAM
#pragma target 3.0
#pragma glsl
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile LIGHT_ON_SCREEN LIGHT_OFF_SCREEN
#pragma multi_compile DIRECTIONAL_SHAFTS SPOT_SHAFTS
#include "UnityCG.cginc"
#include "Shared.cginc"

float4 _CoordTexDim;
float4 _ScreenTexDim;
sampler2D _CameraDepthTexture;

posuv vert (appdata_img v)
{
	posuv o;
	o.pos = v.vertex;
	#if !UNITY_UV_STARTS_AT_TOP
		o.pos.y *= -1;
	#endif
	o.uv = v.texcoord;
	return o;
}

void frag (posuv i, out float4 coord : COLOR0, out float4 depth : COLOR1)
{
	float2 uv = i.uv;

	float sampleOnEpipolarLine = uv.x - 0.5f/_CoordTexDim.x;
	float epipolarLine = saturate(uv.y - 0.5f/_CoordTexDim.y);

	// sampleOnEpipolarLine is now in the range [0, 1 - 1/_CoordTexDim.x]
	// We need to rescale it to be in [0, 1]
	sampleOnEpipolarLine *= _CoordTexDim.x / (_CoordTexDim.x-1);
	sampleOnEpipolarLine = saturate(sampleOnEpipolarLine);

	// epipolarLine is in the range [0, 1 - 1/_CoordTexDim.y]
	int edge = clamp(floor( epipolarLine * 4 ), 0, 3);
	float posOnEdge = frac( epipolarLine * 4 );

	// Left, bottom, right, top
	float edgeCoord = -1 + 2*posOnEdge;
	float4 edgeX = float4(-1, edgeCoord, 1, -edgeCoord);
	float4 edgeY = float4(-edgeCoord, -1, edgeCoord, 1);
	bool4 edgeFlags = bool4(edge.xxxx == int4(0,1,2,3));

	float2 exit = -float2(dot(edgeY, edgeFlags), dot(edgeX, edgeFlags));
	float2 entry = GetEpipolarLineEntryPoint(exit);

	float2 coordTemp = lerp(entry, exit, sampleOnEpipolarLine);
	coordTemp = coordTemp*0.5 + 0.5;
	coord = float4(coordTemp.x, coordTemp.y, 0, 0);

	// Sample depth from the main buffer and store in epipolar space
	coordTemp = (floor(coordTemp*_ScreenTexDim.xy) + 0.5)*_ScreenTexDim.zw;
	depth = Linear01Depth(tex2D(_CameraDepthTexture, coordTemp).x).xxxx;

	// Test against the volume if we've hit at all
	float near, far, rayLength;
	float3 rayN;
	if(!IntersectVolume(coord.xy, near, far, rayN, rayLength) || (depth.x < near/rayLength))
	{
		// When detecting depth breaks, we'll skip this sample (no raymarching)
		depth *= -1.0;
	}
	else
	{
		// Clamp depth to the far end of the volume, to avoid later generation of depth break
		// samples for things behind the volume (wasted computation, artifacts).
		// Requires the same clamp for depth sampled in the final interpolation step.
		depth = min(depth, far/rayLength);
	}

	// TODO: instead of intersecting volume here and in final interpolation, consider
	// rasterizing the light shape into a smaller res buffer.
	// Even though intersection is quite expensive in the final interpolation for every
	// rasterized screen pixel, still beats not doing it by about 10% and gets rid of the artifacts
	// of depth breaks for stuff behind the volume.
}
ENDCG
	}

}

Fallback off
}
