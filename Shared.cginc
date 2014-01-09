float4 _LightPos;

float2 GetEpipolarLineEntryPoint(float2 exit)
{
#if defined(LIGHT_ON_SCREEN)
	// If light is on screen, it's the entry point of every epipolar line
	return _LightPos.xy;

#else
	// If light is outside of the screen, the entry point is intersection of
	// the epipolar line with the screen edge

	float2 dir = exit.xy - _LightPos.xy;
	float distToExitEdge = length(dir);
	dir /= distToExitEdge;
	
	// Signed distances from light to interections with screen edges
	// (1 - validExit) to avoid division by 0
	bool4 validExit = abs(dir.xyxy) > 1e-5;
	float4 distToEdges = (float4(-1,-1,1,1) - _LightPos.xyxy) / (dir.xyxy + (1 - validExit));

	// Find which intersection is the one before exit - that will be the entry.
	// 3 other are just intersections with extended screen edges, outside of the screen.
	// TODO: Not sure about the 1e-3 offset here, maybe it should be resolution-dependent?
	validExit = validExit * (distToEdges < (distToExitEdge - 1e-3));
	// Workaround a compiler bug on osx with temp
	float4 temp = -(1 - validExit);
	temp *= 1.0+38;
	distToEdges = validExit * distToEdges + temp;

	float entryDist = 0;
	entryDist = max(entryDist, distToEdges.x);
	entryDist = max(entryDist, distToEdges.y);
	entryDist = max(entryDist, distToEdges.z);
	entryDist = max(entryDist, distToEdges.w);

	return _LightPos.xy + dir * entryDist;
#endif
}

// Every _InterpolationStep pixels we need to force a raymarched sample to sample the low freq changes of
// intensity along the epipolar line.
// Closer to the light we should make that minimal sampling more dense, due to higher gradient in light intensity,
// but in those directions the full raymarching takes fewer steps anyway.
float _InterpolationStep;

int GetInterpolationStep(float uvx)
{
	int step = _InterpolationStep;
	if ( uvx*8 < 1)
		step = step/4;

	return step;
}

struct appdata_pos
{
    float4 vertex : POSITION;
};

struct posuv
{
	float4 pos : SV_POSITION;
	float2 uv : TEXCOORD0;
};

posuv vert_simple (appdata_pos v)
{
	posuv o;
	o.pos = v.vertex;
	o.uv = o.pos.xy*0.5 + 0.5;
	#if !UNITY_UV_STARTS_AT_TOP
		o.pos.y *= -1;
	#endif
	o.uv.y = 1 - o.uv.y;
	
	return o;
}

// Cube() by Simon Green
inline bool Cube(float3 org, float3 dir, out float tnear, out float tfar)
{
	// compute intersection of ray with all six bbox planes
	float3 invR = 1.0 / dir;
	float3 tbot = invR * (- 0.5f - org);
	float3 ttop = invR * (  0.5f - org);
	
	// re-order intersections to find smallest and largest on each axis
	float3 tmin = min (ttop, tbot);
	float3 tmax = max (ttop, tbot);
	
	// find the largest tmin and the smallest tmax
	float2 t0 = max (tmin.xx, tmin.yz);
	tnear = max (t0.x, t0.y);
	t0 = min (tmax.xx, tmax.yz);
	tfar = min (t0.x, t0.y);

	// check for hit
	return tnear < tfar && tfar > 0;
}

// frustum inscribed in a unit cube centered at 0, apex on x
#define INF 1.0e38
inline bool Frustum(float3 org, float3 dir, float apex, out float near, out float far)
{
	float2 dirf = float2(0.5 - apex, 0.5); 
	float3 tbot, ttop;
	
	// intersection with near and far planes
	float invdirz = 1.0 / dir.z;
	tbot.z = invdirz * (-0.5 - org.z);
	ttop.z = invdirz * (0.5 - org.z);

	float temp = dirf.y * (org.z - apex);
	
	// intersection with inclined planes on y
	tbot.y = (-temp - dirf.x * org.y) / (dirf.x * dir.y + dirf.y * dir.z);
	ttop.y = ( temp - dirf.x * org.y) / (dirf.x * dir.y - dirf.y * dir.z);
	
	// intersection with inclined planes on x
	tbot.x = (-temp - dirf.x * org.x) / (dirf.x * dir.x + dirf.y * dir.z);
	ttop.x = ( temp - dirf.x * org.x) / (dirf.x * dir.x - dirf.y * dir.z);
	
	// if intersecting behind the apex, set t to ray's end
	float4 tempt = float4(tbot.xy, ttop.xy);
	tempt = lerp(tempt, INF * sign(dir.zzzz), step(org.zzzz + tempt * dir.zzzz, apex.xxxx));
	tbot.xy = tempt.xy;
	ttop.xy = tempt.zw;

	// re-order intersections to find smallest and largest on each axis
	float3 tmin = min(ttop, tbot);
	float3 tmax = max(ttop, tbot);
	
	// find the largest tmin and the smallest tmax
	float2 t0 = max(tmin.xx, tmin.yz);
	near = max(t0.x, t0.y);
	t0 = min(tmax.xx, tmax.yz);
	far = min(t0.x, t0.y);

	// check for hit
	return near < far && far > 0.0;
}

float4x4 _FrustumRays;
inline float3 FrustumRay(float2 uv, out float rayLength)
{
	float3 ray0 = lerp(_FrustumRays[0].xyz, _FrustumRays[1].xyz, uv.x);
	float3 ray1 = lerp(_FrustumRays[3].xyz, _FrustumRays[2].xyz, uv.x);
	float3 ray = lerp(ray0, ray1, uv.y);
	rayLength = length(ray);
	return ray/rayLength;
}

float4 _CameraPosLocal;
float _FrustumApex;
inline bool IntersectVolume(float2 uv, out float near, out float far, out float3 rayN, out float rayLength)
{
	rayN = FrustumRay(uv, rayLength);
	#if defined(DIRECTIONAL_SHAFTS)
	return Cube(_CameraPosLocal.xyz, rayN, near, far);
	#else
	return Frustum(_CameraPosLocal.xyz, rayN, _FrustumApex, near, far);
	#endif
}