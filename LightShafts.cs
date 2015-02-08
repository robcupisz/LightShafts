using UnityEngine;
using System.Collections;

[ExecuteInEditMode]
[RequireComponent(typeof(Light))]
public partial class LightShafts : MonoBehaviour
{
	public void Start()
	{
		CheckMinRequirements();

		if (m_Cameras == null || m_Cameras.Length == 0)
			m_Cameras = new Camera[]{Camera.main};

		UpdateCameraDepthMode();
	}

	void UpdateShadowmap()
	{
		if (m_ShadowmapMode == LightShaftsShadowmapMode.Static && !m_ShadowmapDirty)
			return;

		InitShadowmap();

		if (m_ShadowmapCamera == null)
		{
			GameObject go = new GameObject("Depth Camera");
			go.AddComponent(typeof(Camera));
			m_ShadowmapCamera = go.GetComponent<Camera>();
			go.hideFlags = HideFlags.HideAndDontSave;
			m_ShadowmapCamera.enabled = false;
			m_ShadowmapCamera.clearFlags = CameraClearFlags.SolidColor;
		}
		Transform cam = m_ShadowmapCamera.transform;
		cam.position = transform.position;
		cam.rotation = transform.rotation;

		if (directional)
		{
			m_ShadowmapCamera.orthographic = true;
			m_ShadowmapCamera.nearClipPlane = 0;
			m_ShadowmapCamera.farClipPlane = m_Size.z;
			m_ShadowmapCamera.orthographicSize = m_Size.y * 0.5f;
			m_ShadowmapCamera.aspect = m_Size.x / m_Size.y;
		}
		else
		{
			m_ShadowmapCamera.orthographic = false;
			m_ShadowmapCamera.nearClipPlane = m_SpotNear * m_Light.range;
			m_ShadowmapCamera.farClipPlane = m_SpotFar * m_Light.range;
			m_ShadowmapCamera.fieldOfView = m_Light.spotAngle;
			m_ShadowmapCamera.aspect = 1.0f;
		}
		m_ShadowmapCamera.renderingPath = RenderingPath.Forward;
		m_ShadowmapCamera.targetTexture = m_Shadowmap;
		m_ShadowmapCamera.cullingMask = m_CullingMask;
		m_ShadowmapCamera.backgroundColor = Color.white;

		m_ShadowmapCamera.RenderWithShader(m_DepthShader, "RenderType");

		if (m_Colored)
		{
			m_ShadowmapCamera.targetTexture = m_ColorFilter;
			m_ShadowmapCamera.cullingMask = m_ColorFilterMask;
			m_ShadowmapCamera.backgroundColor = new Color(m_ColorBalance, m_ColorBalance, m_ColorBalance);
			m_ShadowmapCamera.RenderWithShader(m_ColorFilterShader, "");
		}

		m_ShadowmapDirty = false;
	}
	
	void RenderCoords(int width, int height, Vector4 lightPos)
	{
		SetFrustumRays(m_CoordMaterial);

		RenderBuffer[] buffers = {m_CoordEpi.colorBuffer, m_DepthEpi.colorBuffer};
		Graphics.SetRenderTarget(buffers, m_DepthEpi.depthBuffer);
		m_CoordMaterial.SetVector("_LightPos", lightPos);
		m_CoordMaterial.SetVector("_CoordTexDim", new Vector4(m_CoordEpi.width, m_CoordEpi.height, 1.0f / m_CoordEpi.width, 1.0f / m_CoordEpi.height));
		m_CoordMaterial.SetVector("_ScreenTexDim", new Vector4(width, height, 1.0f / width, 1.0f / height));
		m_CoordMaterial.SetPass(0);
		RenderQuad();
	}

	void RenderInterpolationTexture(Vector4 lightPos)
	{
		Graphics.SetRenderTarget(m_InterpolationEpi.colorBuffer, m_RaymarchedLightEpi.depthBuffer);
		if (!m_DX11Support && (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsWebPlayer))
		{
			// Looks like in dx9 stencil is not cleared properly with GL.Clear()
			// Edit: fixed in 4.5, so this hack can be removed
			m_DepthBreaksMaterial.SetPass(1);
			RenderQuad();
		}
		else
		{
			GL.Clear(true, true, new Color(0, 0, 0, 1));
		}
		m_DepthBreaksMaterial.SetFloat("_InterpolationStep", m_InterpolationStep);
		m_DepthBreaksMaterial.SetFloat("_DepthThreshold", GetDepthThresholdAdjusted());
		m_DepthBreaksMaterial.SetTexture("_DepthEpi", m_DepthEpi);
		m_DepthBreaksMaterial.SetVector("_DepthEpiTexDim", new Vector4(m_DepthEpi.width, m_DepthEpi.height, 1.0f / m_DepthEpi.width, 1.0f / m_DepthEpi.height));
		m_DepthBreaksMaterial.SetPass(0);
		RenderQuadSections(lightPos);
	}

	void InterpolateAlongRays(Vector4 lightPos)
	{
		Graphics.SetRenderTarget(m_InterpolateAlongRaysEpi);
		m_InterpolateAlongRaysMaterial.SetFloat("_InterpolationStep", m_InterpolationStep);
		m_InterpolateAlongRaysMaterial.SetTexture("_InterpolationEpi", m_InterpolationEpi);
		m_InterpolateAlongRaysMaterial.SetTexture("_RaymarchedLightEpi", m_RaymarchedLightEpi);
		m_InterpolateAlongRaysMaterial.SetVector("_RaymarchedLightEpiTexDim", new Vector4(m_RaymarchedLightEpi.width, m_RaymarchedLightEpi.height, 1.0f / m_RaymarchedLightEpi.width, 1.0f / m_RaymarchedLightEpi.height));
		m_InterpolateAlongRaysMaterial.SetPass(0);
		RenderQuadSections(lightPos);
	}
	
	void RenderSamplePositions(int width, int height, Vector4 lightPos)
	{
		InitRenderTexture (ref m_SamplePositions, width, height, 0, RenderTextureFormat.ARGB32, false);
		// Unfortunately can't be a temporary RT if we want random write
		m_SamplePositions.enableRandomWrite = true;
		m_SamplePositions.filterMode = FilterMode.Point;
				
		Graphics.SetRenderTarget (m_SamplePositions);
		GL.Clear (false, true, new Color(0,0,0,1));
		
		Graphics.ClearRandomWriteTargets();
		Graphics.SetRandomWriteTarget(1, m_SamplePositions);
		
		//We need a render target with m_Coord dimensions, but reading and writing
		//to the same target produces wrong read results, so using a dummy.
		Graphics.SetRenderTarget(m_RaymarchedLightEpi);
		
		m_SamplePositionsMaterial.SetVector("_OutputTexDim", new Vector4(width-1, height-1, 0, 0));
		m_SamplePositionsMaterial.SetVector("_CoordTexDim", new Vector4(m_CoordEpi.width, m_CoordEpi.height, 0, 0));
		m_SamplePositionsMaterial.SetTexture("_Coord", m_CoordEpi);
		m_SamplePositionsMaterial.SetTexture("_InterpolationEpi", m_InterpolationEpi);

		if (m_ShowInterpolatedSamples)
		{
			m_SamplePositionsMaterial.SetFloat("_SampleType", 1);
			m_SamplePositionsMaterial.SetVector("_Color", new Vector4(0.4f, 0.4f, 0, 0));
			m_SamplePositionsMaterial.SetPass(0);
			RenderQuad();
		}

		m_SamplePositionsMaterial.SetFloat("_SampleType", 0);
		m_SamplePositionsMaterial.SetVector("_Color", new Vector4(1, 0, 0, 0));
		m_SamplePositionsMaterial.SetPass(0);
		RenderQuadSections(lightPos);
		
		Graphics.ClearRandomWriteTargets();
	}

	void ShowSamples(int width, int height, Vector4 lightPos)
	{
		bool showSamples = m_ShowSamples && m_DX11Support && m_SamplePositionsShaderCompiles;
		SetKeyword(showSamples, "SHOW_SAMPLES_ON", "SHOW_SAMPLES_OFF");
		if (showSamples)
			RenderSamplePositions(width, height, lightPos);

		m_FinalInterpolationMaterial.SetFloat("_ShowSamplesBackgroundFade", m_ShowSamplesBackgroundFade);
	}

	void Raymarch(int width, int height, Vector4 lightPos)
	{
		SetFrustumRays(m_RaymarchMaterial);

		int shadowmapWidth = m_Shadowmap.width;
		int shadowmapHeight = m_Shadowmap.height;

		Graphics.SetRenderTarget(m_RaymarchedLightEpi.colorBuffer, m_RaymarchedLightEpi.depthBuffer);
		GL.Clear(false, true, new Color(0, 0, 0, 1));
		m_RaymarchMaterial.SetTexture("_Coord", m_CoordEpi);
		m_RaymarchMaterial.SetTexture("_InterpolationEpi", m_InterpolationEpi);
		m_RaymarchMaterial.SetTexture("_Shadowmap", m_Shadowmap);
		float brightness = m_Colored ? m_BrightnessColored/m_ColorBalance : m_Brightness;
		brightness *= m_Light.intensity;
		m_RaymarchMaterial.SetFloat("_Brightness", brightness);
		m_RaymarchMaterial.SetFloat("_Extinction", -m_Extinction);
		m_RaymarchMaterial.SetVector("_ShadowmapDim", new Vector4(shadowmapWidth, shadowmapHeight, 1.0f / shadowmapWidth, 1.0f / shadowmapHeight));
		m_RaymarchMaterial.SetVector("_ScreenTexDim", new Vector4(width, height, 1.0f / width, 1.0f / height));
		m_RaymarchMaterial.SetVector("_LightColor", m_Light.color.linear);
		m_RaymarchMaterial.SetFloat("_MinDistFromCamera", m_MinDistFromCamera);
		SetKeyword(m_Colored, "COLORED_ON", "COLORED_OFF");
		m_RaymarchMaterial.SetTexture("_ColorFilter", m_ColorFilter);
		SetKeyword(m_AttenuationCurveOn, "ATTENUATION_CURVE_ON", "ATTENUATION_CURVE_OFF");
		m_RaymarchMaterial.SetTexture("_AttenuationCurveTex", m_AttenuationCurveTex);
		Texture cookie = m_Light.cookie;
		SetKeyword(cookie != null, "COOKIE_TEX_ON", "COOKIE_TEX_OFF");
		if (cookie != null)
			m_RaymarchMaterial.SetTexture("_Cookie", cookie);
		m_RaymarchMaterial.SetPass(0);

		RenderQuadSections(lightPos);
	}

	public void OnRenderObject ()
	{
		m_CurrentCamera = Camera.current;
		if (!m_MinRequirements || !CheckCamera() || !IsVisible())
			return;

		// Prepare
		RenderBuffer depthBuffer = Graphics.activeDepthBuffer;
		RenderBuffer colorBuffer = Graphics.activeColorBuffer;
		InitResources();
		Vector4 lightPos = GetLightViewportPos();
		bool lightOnScreen = lightPos.x >= -1 && lightPos.x <= 1 && lightPos.y >= -1 && lightPos.y <= 1;
		SetKeyword(lightOnScreen, "LIGHT_ON_SCREEN", "LIGHT_OFF_SCREEN");
		int width = Screen.width;
		int height = Screen.height;
		
		// Render the buffers, raymarch, interpolate along rays
		UpdateShadowmap();
		SetKeyword(directional, "DIRECTIONAL_SHAFTS", "SPOT_SHAFTS");
		RenderCoords(width, height, lightPos);
		RenderInterpolationTexture(lightPos);
		Raymarch(width, height, lightPos);
		InterpolateAlongRays(lightPos);

		ShowSamples(width, height, lightPos);

		// Final interpolation and blending onto the screen
		SetFrustumRays(m_FinalInterpolationMaterial);
		m_FinalInterpolationMaterial.SetTexture("_InterpolationEpi", m_InterpolationEpi);
		m_FinalInterpolationMaterial.SetTexture("_DepthEpi", m_DepthEpi);
		m_FinalInterpolationMaterial.SetTexture("_Shadowmap", m_Shadowmap);
		m_FinalInterpolationMaterial.SetTexture("_Coord", m_CoordEpi);
		m_FinalInterpolationMaterial.SetTexture("_SamplePositions", m_SamplePositions);
		m_FinalInterpolationMaterial.SetTexture("_RaymarchedLight", m_InterpolateAlongRaysEpi);
		m_FinalInterpolationMaterial.SetVector("_CoordTexDim", new Vector4(m_CoordEpi.width, m_CoordEpi.height, 1.0f / m_CoordEpi.width, 1.0f / m_CoordEpi.height));
		m_FinalInterpolationMaterial.SetVector("_ScreenTexDim", new Vector4(width, height, 1.0f / width, 1.0f / height));
		m_FinalInterpolationMaterial.SetVector("_LightPos", lightPos);
		m_FinalInterpolationMaterial.SetFloat("_DepthThreshold", GetDepthThresholdAdjusted());
		bool renderAsQuad = directional || IntersectsNearPlane();
		m_FinalInterpolationMaterial.SetFloat("_ZTest", (float)(renderAsQuad ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.Less));
		SetKeyword(renderAsQuad, "QUAD_SHAFTS", "FRUSTUM_SHAFTS");

		Graphics.SetRenderTarget(colorBuffer, depthBuffer);
		m_FinalInterpolationMaterial.SetPass(0);
		if (renderAsQuad)
			RenderQuad();
		else
			RenderSpotFrustum();

		ReleaseResources();
	}
}
