using UnityEngine;
using System.Collections;

public enum LightShaftsShadowmapMode
{
	Dynamic = 0,
	Static = 1	
}

public partial class LightShafts : MonoBehaviour
{
	public bool directional {get{return m_LightType == LightType.Directional;}}
	public bool spot {get{return m_LightType == LightType.Spot;}}

	Bounds GetBoundsLocal()
	{
		if (directional)
			return new Bounds(new Vector3(0, 0, m_Size.z*0.5f), m_Size);

		Light l = light;
		Vector3 offset = new Vector3(0, 0, l.range * (m_SpotFar + m_SpotNear) * 0.5f);
		float height = (m_SpotFar - m_SpotNear) * l.range;
		float baseSize = Mathf.Tan(l.spotAngle * Mathf.Deg2Rad * 0.5f) * m_SpotFar * l.range * 2.0f;
		return new Bounds(offset, new Vector3(baseSize, baseSize, height));
	}

	Matrix4x4 GetBoundsMatrix()
	{
		Bounds bounds = GetBoundsLocal();
		Transform t = transform;
		return Matrix4x4.TRS(t.position + t.forward * bounds.center.z, t.rotation, bounds.size);
	}

	float GetFrustumApex()
	{
		// Assuming the frustum is inscribed in a unit cube centered at 0
		return - m_SpotNear/(m_SpotFar - m_SpotNear) - 0.5f;
	}

	void OnDrawGizmosSelected()
	{
		UpdateLightType();
		
		Gizmos.color = Color.yellow;
		if (directional)
		{
			Gizmos.matrix = GetBoundsMatrix();
			Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
		}
		else if (spot)
		{
			Transform t = transform;
			Light l = light;
			Gizmos.matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one);
			Gizmos.DrawFrustum(t.position, l.spotAngle, l.range * m_SpotFar, l.range * m_SpotNear, 1);
		}
	}

	void RenderQuadSections(Vector4 lightPos)
	{
		for (int i = 0; i < 4; i++)
		{
			// Skip one or two quarters, if the light is off screen
			if (i == 0 && lightPos.y > 1 ||
				i == 1 && lightPos.x > 1 ||
				i == 2 && lightPos.y < -1 ||
				i == 3 && lightPos.x < -1)
				continue;

			// index denotes which quarter of the screen to take up,
			// so start at -1, -0.5, 0 or 0.5
			float top = i / 2.0f - 1.0f;
			float bottom = top + 0.5f;
			GL.Begin(GL.QUADS);
			GL.Vertex3(-1, top, 0);
			GL.Vertex3(1, top, 0);
			GL.Vertex3(1, bottom, 0);
			GL.Vertex3(-1, bottom, 0);
			GL.End();
		}
	}

	void RenderQuad()
	{
		GL.Begin(GL.QUADS);
		GL.TexCoord2( 0, 0);
		GL.Vertex3	(-1,-1, 0);
		GL.TexCoord2( 0, 1);
		GL.Vertex3	(-1, 1, 0);
		GL.TexCoord2( 1, 1);
		GL.Vertex3	( 1, 1, 0);
		GL.TexCoord2( 1, 0);
		GL.Vertex3	( 1,-1, 0);
		GL.End();
	}

	void RenderSpotFrustum()
	{
		Graphics.DrawMeshNow(m_SpotMesh, transform.position, transform.rotation);
	}

	Vector4 GetLightViewportPos()
	{
		Vector3 lightPos = transform.position;
		if (directional)
			lightPos = m_CurrentCamera.transform.position + transform.forward;

		Vector3 lightViewportPos3 = m_CurrentCamera.WorldToViewportPoint(lightPos);
		return new Vector4(lightViewportPos3.x*2.0f - 1.0f, lightViewportPos3.y*2.0f - 1.0f, 0, 0);
	}

	bool IsVisible()
	{
		// Intersect against spot light's OBB (or light frustum's OBB), so AABB in it's space
		Matrix4x4 lightToCameraProjection = m_CurrentCamera.projectionMatrix * m_CurrentCamera.worldToCameraMatrix * transform.localToWorldMatrix;
		return GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(lightToCameraProjection), GetBoundsLocal());
	}

	bool IntersectsNearPlane()
	{
		// Lazy for now:
		// Just check if any vertex is behind the near plane.
		// TODO: same for directional
		Vector3[] vertices = m_SpotMesh.vertices;
		float nearPlaneFudged = m_CurrentCamera.nearClipPlane - 0.001f;
		Transform t = transform;
		for (int i = 0; i < vertices.Length; i++)
		{
			float z = m_CurrentCamera.WorldToViewportPoint(t.TransformPoint(vertices[i])).z;
			if (z < nearPlaneFudged)
				return true;
		}
		return false;
	}

	void SetKeyword(bool firstOn, string firstKeyword, string secondKeyword)
	{
		Shader.EnableKeyword(firstOn ? firstKeyword : secondKeyword);
		Shader.DisableKeyword(firstOn ? secondKeyword : firstKeyword);
	}

	public void SetShadowmapDirty()
	{
		m_ShadowmapDirty = true;
	}

	void GetFrustumRays(out Matrix4x4 frustumRays, out Vector3 cameraPosLocal)
	{
		float far = m_CurrentCamera.farClipPlane;
		Vector3 cameraPos = m_CurrentCamera.transform.position;
		Matrix4x4 m = GetBoundsMatrix().inverse;
		Vector2[] uvs = new Vector2[] {new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1)};
		frustumRays = new Matrix4x4();

		for (int i = 0; i < 4; i++)
		{
			Vector3 ray = m_CurrentCamera.ViewportToWorldPoint(new Vector3(uvs[i].x, uvs[i].y, far)) - cameraPos;
			ray = m.MultiplyVector(ray);
			frustumRays.SetRow(i, ray);
		}

		cameraPosLocal = m.MultiplyPoint3x4(cameraPos);
	}

	void SetFrustumRays(Material material)
	{
		Matrix4x4 frustumRays;
		Vector3 cameraPosLocal;
		GetFrustumRays(out frustumRays, out cameraPosLocal);
		material.SetVector("_CameraPosLocal", cameraPosLocal);
		material.SetMatrix("_FrustumRays", frustumRays);
		material.SetFloat("_FrustumApex", GetFrustumApex());
	}

	float GetDepthThresholdAdjusted()
	{
		return m_DepthThreshold/m_CurrentCamera.farClipPlane;
	}

	bool CheckCamera()
	{
		if (m_Cameras == null)
			return false;

		foreach (Camera cam in m_Cameras)
			if (cam == m_CurrentCamera)
				return true;

		return false;
	}

	public void UpdateCameraDepthMode()
	{
		if (m_Cameras == null)
			return;

		foreach(Camera cam in m_Cameras)
			if (cam)
				cam.depthTextureMode |= DepthTextureMode.Depth;
	}
}
