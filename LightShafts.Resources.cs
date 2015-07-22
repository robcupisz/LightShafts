using UnityEngine;
using System.Collections;

public partial class LightShafts : MonoBehaviour
{
	public LightShaftsShadowmapMode m_ShadowmapMode = LightShaftsShadowmapMode.Dynamic;
	LightShaftsShadowmapMode m_ShadowmapModeOld = LightShaftsShadowmapMode.Dynamic;
	public Camera[] m_Cameras;
	public Camera m_CurrentCamera;
	bool m_ShadowmapDirty = true;
	public Vector3 m_Size = new Vector3(10, 10, 20);
	public float m_SpotNear = 0.1f;
	public float m_SpotFar = 1.0f;
	public LayerMask m_CullingMask = ~0;
	public LayerMask m_ColorFilterMask = 0;
	public float m_Brightness = 5;
	public float m_BrightnessColored = 5;
	public float m_Extinction = 0.5f;
	public float m_MinDistFromCamera = 0.0f;
	
	public int m_ShadowmapRes = 1024;
	Camera m_ShadowmapCamera;
	RenderTexture m_Shadowmap;
	public Shader m_DepthShader;
	RenderTexture m_ColorFilter;
	public Shader m_ColorFilterShader;
	public bool m_Colored = false;
	public float m_ColorBalance = 1.0f;
	
	public int m_EpipolarLines = 256;
	public int m_EpipolarSamples = 512;
	RenderTexture m_CoordEpi;
	RenderTexture m_DepthEpi;
	public Shader m_CoordShader;
	Material m_CoordMaterial;
	Camera m_CoordsCamera;

	RenderTexture m_InterpolationEpi;
	public Shader m_DepthBreaksShader;
	Material m_DepthBreaksMaterial;

	RenderTexture m_RaymarchedLightEpi;
	Material m_RaymarchMaterial;
	public Shader m_RaymarchShader;

	RenderTexture m_InterpolateAlongRaysEpi;
	public Shader m_InterpolateAlongRaysShader;
	Material m_InterpolateAlongRaysMaterial;
	
	RenderTexture m_SamplePositions;
	public Shader m_SamplePositionsShader;
	Material m_SamplePositionsMaterial;
	bool m_SamplePositionsShaderCompiles = false;
	
	public Shader m_FinalInterpolationShader;
	Material m_FinalInterpolationMaterial;

	public float m_DepthThreshold = 0.5f;
	public int m_InterpolationStep = 32;

	public bool m_ShowSamples = false;
	public bool m_ShowInterpolatedSamples = false;
	public float m_ShowSamplesBackgroundFade = 0.8f;

	public bool m_AttenuationCurveOn = false;
	public AnimationCurve m_AttenuationCurve;
	Texture2D m_AttenuationCurveTex;

	Light m_Light;
	LightType m_LightType = LightType.Directional;
	bool m_DX11Support = false;
	bool m_MinRequirements = false;

	void InitLUTs ()
	{
		if (m_AttenuationCurveTex)
			return;

		m_AttenuationCurveTex = new Texture2D (256, 1, TextureFormat.ARGB32, false, true);
		m_AttenuationCurveTex.wrapMode = TextureWrapMode.Clamp;
		m_AttenuationCurveTex.hideFlags = HideFlags.HideAndDontSave;

		if (m_AttenuationCurve == null || m_AttenuationCurve.length == 0)
			m_AttenuationCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));

		if (m_AttenuationCurveTex)
			UpdateLUTs ();
	}
	
	public void UpdateLUTs ()
	{
		InitLUTs ();

		if (m_AttenuationCurve == null)
			return;

		for (int i = 0; i < 256; ++i)
		{
			float v = Mathf.Clamp (m_AttenuationCurve.Evaluate(i/255.0f), 0.0f, 1.0f);
			m_AttenuationCurveTex.SetPixel (i, 0, new Color(v,v,v,v));
		}
		m_AttenuationCurveTex.Apply ();
	}
	
	void InitRenderTexture(ref RenderTexture rt, int width, int height, int depth, RenderTextureFormat format, bool temp = true)
	{
		if (temp)
		{
			rt = RenderTexture.GetTemporary(width, height, depth, format);
		}
		else
		{
			if (rt != null)
			{
				if (rt.width == width && rt.height == height && rt.depth == depth && rt.format == format)
					return;

				rt.Release();
				DestroyImmediate(rt);
			}

			rt = new RenderTexture(width, height, depth, format);
			rt.hideFlags = HideFlags.HideAndDontSave;
		}
	}

	void InitShadowmap()
	{
		bool dynamic = (m_ShadowmapMode == LightShaftsShadowmapMode.Dynamic);
		if (dynamic && m_ShadowmapMode != m_ShadowmapModeOld)
		{
			// Destroy static render textures, we only need temp now
			if (m_Shadowmap)
				m_Shadowmap.Release();
			if (m_ColorFilter)
				m_ColorFilter.Release();
		}
		InitRenderTexture(ref m_Shadowmap, m_ShadowmapRes, m_ShadowmapRes, 24, RenderTextureFormat.RFloat, dynamic);
		m_Shadowmap.filterMode = FilterMode.Point;
		m_Shadowmap.wrapMode = TextureWrapMode.Clamp;

		if (m_Colored)
			InitRenderTexture(ref m_ColorFilter, m_ShadowmapRes, m_ShadowmapRes, 0, RenderTextureFormat.ARGB32, dynamic);

		m_ShadowmapModeOld = m_ShadowmapMode;
	}

	void ReleaseShadowmap()
	{
		if (m_ShadowmapMode == LightShaftsShadowmapMode.Static)
			return;

		RenderTexture.ReleaseTemporary(m_Shadowmap);
		RenderTexture.ReleaseTemporary(m_ColorFilter);
	}
	
	void InitEpipolarTextures()
	{
		m_EpipolarLines = m_EpipolarLines < 8 ? 8 : m_EpipolarLines;
		m_EpipolarSamples = m_EpipolarSamples < 4 ? 4 : m_EpipolarSamples;
		
		InitRenderTexture(ref m_CoordEpi, m_EpipolarSamples, m_EpipolarLines, 0, RenderTextureFormat.RGFloat);
		m_CoordEpi.filterMode = FilterMode.Point;
		InitRenderTexture(ref m_DepthEpi, m_EpipolarSamples, m_EpipolarLines, 0, RenderTextureFormat.RFloat);
		m_DepthEpi.filterMode = FilterMode.Point;
		InitRenderTexture(ref m_InterpolationEpi, m_EpipolarSamples, m_EpipolarLines, 0, m_DX11Support ? RenderTextureFormat.RGInt : RenderTextureFormat.RGFloat);
		m_InterpolationEpi.filterMode = FilterMode.Point;
		
		InitRenderTexture(ref m_RaymarchedLightEpi, m_EpipolarSamples, m_EpipolarLines, 24, RenderTextureFormat.ARGBFloat);
		m_RaymarchedLightEpi.filterMode = FilterMode.Point;
		InitRenderTexture(ref m_InterpolateAlongRaysEpi, m_EpipolarSamples, m_EpipolarLines, 0, RenderTextureFormat.ARGBFloat);
		m_InterpolateAlongRaysEpi.filterMode = FilterMode.Point;
	}
	
	void InitMaterial(ref Material material, Shader shader)
	{
		if (material || !shader)
			return;
		material = new Material(shader);
		material.hideFlags = HideFlags.HideAndDontSave;
	}

	void InitMaterials()
	{
		InitMaterial(ref m_FinalInterpolationMaterial, m_FinalInterpolationShader);
		InitMaterial(ref m_CoordMaterial, m_CoordShader);
		InitMaterial(ref m_SamplePositionsMaterial, m_SamplePositionsShader);
		InitMaterial(ref m_RaymarchMaterial, m_RaymarchShader);
		InitMaterial(ref m_DepthBreaksMaterial, m_DepthBreaksShader);
		InitMaterial(ref m_InterpolateAlongRaysMaterial, m_InterpolateAlongRaysShader);
	}

	Mesh m_SpotMesh;
	float m_SpotMeshNear = -1;
	float m_SpotMeshFar = -1;
	float m_SpotMeshAngle = -1;
	float m_SpotMeshRange = -1;

	void InitSpotFrustumMesh()
	{
		if (!m_SpotMesh)
		{
			m_SpotMesh = new Mesh();
			m_SpotMesh.hideFlags = HideFlags.HideAndDontSave;
		}

		Light l = m_Light;
		if (m_SpotMeshNear != m_SpotNear || m_SpotMeshFar != m_SpotFar || m_SpotMeshAngle != l.spotAngle || m_SpotMeshRange != l.range)
		{
			float far = l.range * m_SpotFar;
			float near = l.range * m_SpotNear;
			float tan = Mathf.Tan(l.spotAngle * Mathf.Deg2Rad * 0.5f);
			float halfwidthfar = far * tan;
			float halfwidthnear = near * tan;

			Vector3[] vertices = (m_SpotMesh.vertices != null && m_SpotMesh.vertices.Length == 8) ? m_SpotMesh.vertices : new Vector3[8];
			vertices[0] = new Vector3(-halfwidthfar,  -halfwidthfar,  far);
			vertices[1] = new Vector3( halfwidthfar,  -halfwidthfar,  far);
			vertices[2] = new Vector3( halfwidthfar,   halfwidthfar,  far);
			vertices[3] = new Vector3(-halfwidthfar,   halfwidthfar,  far);
			vertices[4] = new Vector3(-halfwidthnear, -halfwidthnear, near);
			vertices[5] = new Vector3( halfwidthnear, -halfwidthnear, near);
			vertices[6] = new Vector3( halfwidthnear,  halfwidthnear, near);
			vertices[7] = new Vector3(-halfwidthnear,  halfwidthnear, near);
			m_SpotMesh.vertices = vertices;

			if (m_SpotMesh.GetTopology( 0 ) != MeshTopology.Triangles || m_SpotMesh.triangles == null || m_SpotMesh.triangles.Length != 36)
			{
				//                          far           near          top           right         left          bottom
				int[] triangles = new int[]{0,1,2, 0,2,3, 6,5,4, 7,6,4, 3,2,6, 3,6,7, 2,1,5, 2,5,6, 0,3,7, 0,7,4, 5,1,0, 5,0,4};
				m_SpotMesh.triangles = triangles;
			}

			m_SpotMeshNear = m_SpotNear;
			m_SpotMeshFar = m_SpotFar;
			m_SpotMeshAngle = l.spotAngle;
			m_SpotMeshRange = l.range;
		}
	}

	public void UpdateLightType()
	{
		if (m_Light == null)
			m_Light = GetComponent<Light>();
		
		m_LightType = m_Light.type;
	}

	bool ShaderCompiles(Shader shader)
	{
		if (!shader.isSupported)
		{
			Debug.LogError("LightShafts' " + shader.name + " didn't compile on this platform.");
			return false;
		}

		return true;
	}

	public bool CheckMinRequirements()
	{
		m_DX11Support = SystemInfo.graphicsShaderLevel >= 50;

		m_MinRequirements = SystemInfo.graphicsShaderLevel >= 30;
		m_MinRequirements &= SystemInfo.supportsRenderTextures;
		m_MinRequirements &= SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat);
		m_MinRequirements &= SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat);

		if (!m_MinRequirements)
			Debug.LogError("LightShafts require Shader Model 3.0 and render textures (including the RGFloat and RFloat) formats. Disabling.");

		bool shadersCompile = 	ShaderCompiles(m_DepthShader) &&
								ShaderCompiles(m_ColorFilterShader) &&
								ShaderCompiles(m_CoordShader) &&
								ShaderCompiles(m_DepthBreaksShader) &&
								ShaderCompiles(m_RaymarchShader) &&
								ShaderCompiles(m_InterpolateAlongRaysShader) &&
								ShaderCompiles(m_FinalInterpolationShader);

		if (!shadersCompile)
			Debug.LogError("LightShafts require above shaders. Disabling.");

		m_MinRequirements &= shadersCompile;

		m_SamplePositionsShaderCompiles = m_SamplePositionsShader.isSupported;

		return m_MinRequirements;
	}

	void InitResources()
	{
		UpdateLightType();
		
		InitMaterials();
		InitEpipolarTextures();
		InitLUTs();
		InitSpotFrustumMesh();
	}

	void ReleaseResources()
	{
		ReleaseShadowmap();
		RenderTexture.ReleaseTemporary(m_CoordEpi);
		RenderTexture.ReleaseTemporary(m_DepthEpi);
		RenderTexture.ReleaseTemporary(m_InterpolationEpi);
		RenderTexture.ReleaseTemporary(m_RaymarchedLightEpi);
		RenderTexture.ReleaseTemporary(m_InterpolateAlongRaysEpi);
	}
}
