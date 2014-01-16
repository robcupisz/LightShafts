
#pragma strict

@CustomEditor (LightShafts)

class LightShaftsEditor extends Editor 
{
	var so : SerializedObject;	
	
	var cameras : SerializedProperty;
	var shadowmapMode : SerializedProperty;
	var size : SerializedProperty;
	var near : SerializedProperty;
	var far : SerializedProperty;
	var cullingMask : SerializedProperty;
	var colorFilterMask : SerializedProperty;
	var brightness : SerializedProperty;
	var brightnessColored : SerializedProperty;
	var extinction : SerializedProperty;
	var minDistFromCamera : SerializedProperty;
	var shadowmapRes : SerializedProperty;
	var colored : SerializedProperty;
	var colorBalance : SerializedProperty;
	var epipolarLines : SerializedProperty;
	var epipolarSamples : SerializedProperty;
	var depthThreshold : SerializedProperty;
	var interpolationStep : SerializedProperty;
	var showSamples : SerializedProperty;
	var showInterpolatedSamples : SerializedProperty;
	var backgroundFade : SerializedProperty;
	var attenuationCurveOn : SerializedProperty;
	var attenuationCurve : SerializedProperty;
	var sizesStr : GUIContent[] = [new GUIContent("64"), new GUIContent("128"), new GUIContent("256"), new GUIContent("512"), new GUIContent("1024"), new GUIContent("2048")];
	var sizes : int[] = [64, 128, 256, 512, 1024, 2048];
	var interpolationStepValuesStr : GUIContent[] = [new GUIContent("32"), new GUIContent("16"), new GUIContent("8")];
	var interpolationStepValues : int[] = [32, 16, 8];
	
	function OnEnable () {
		so = new SerializedObject (target);
			
		cameras = so.FindProperty("m_Cameras");
		shadowmapMode = so.FindProperty("m_ShadowmapMode");
		size = so.FindProperty("m_Size");
		near = so.FindProperty("m_SpotNear");
		far = so.FindProperty("m_SpotFar");
		cullingMask = so.FindProperty("m_CullingMask");
		colorFilterMask = so.FindProperty("m_ColorFilterMask");
		brightness = so.FindProperty("m_Brightness");
		brightnessColored = so.FindProperty("m_BrightnessColored");
		extinction = so.FindProperty("m_Extinction");
		minDistFromCamera = so.FindProperty("m_MinDistFromCamera");
		shadowmapRes = so.FindProperty("m_ShadowmapRes");
		colored = so.FindProperty("m_Colored");
		colorBalance = so.FindProperty("m_ColorBalance");
		epipolarLines = so.FindProperty("m_EpipolarLines");
		epipolarSamples = so.FindProperty("m_EpipolarSamples");
		depthThreshold = so.FindProperty("m_DepthThreshold");
		interpolationStep = so.FindProperty("m_InterpolationStep");
		showSamples = so.FindProperty("m_ShowSamples");
		showInterpolatedSamples = so.FindProperty("m_ShowInterpolatedSamples");
		backgroundFade = so.FindProperty("m_ShowSamplesBackgroundFade");

		attenuationCurveOn = so.FindProperty("m_AttenuationCurveOn");
		attenuationCurve = so.FindProperty("m_AttenuationCurve");
		if (attenuationCurve.animationCurveValue.length == 0)
		{
			attenuationCurve.animationCurveValue = new AnimationCurve(Keyframe(0, 1.0), Keyframe(1, 0.0));
			so.ApplyModifiedProperties ();
			(so.targetObject as LightShafts).gameObject.SendMessage ("UpdateLUTs");
		}
	}

	function Label( text : String)
	{
		EditorGUILayout.Separator();
		EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
	}

	function CheckParamBounds()
	{
		depthThreshold.floatValue = Mathf.Max(0, depthThreshold.floatValue);
		brightness.floatValue = Mathf.Max(0, brightness.floatValue);
		brightnessColored.floatValue = Mathf.Max(0, brightnessColored.floatValue);
		minDistFromCamera.floatValue = Mathf.Max(0, minDistFromCamera.floatValue);
		var minNear : float = 0.05;
		far.floatValue = Mathf.Clamp(far.floatValue, minNear, 1.0f);
		near.floatValue = Mathf.Clamp(near.floatValue, minNear, far.floatValue);
	}
			
	function OnInspectorGUI ()
	{
		so.Update ();
		
		var effect : LightShafts = target as LightShafts;

		if (!effect.CheckMinRequirements())
		{
			EditorGUILayout.HelpBox("Render texture support (including RFloat and RGFloat) and shader model 3.0 required.", MessageType.Error, true);
			return;	
		}

		effect.UpdateLightType();
		if (!effect.directional && !effect.spot)
		{
			EditorGUILayout.HelpBox("Directional or spot lights only.", MessageType.Warning, true);
			return;
		}

		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField(cameras, true);
		var updateCameraDepthMode = EditorGUI.EndChangeCheck();

		Label("Volumetric shadow");
		
		if (effect.directional)
		{
			EditorGUILayout.PropertyField (size, new GUIContent("Volume size"));
		}
		else
		{
			EditorGUILayout.PropertyField (near, new GUIContent("Volume start"));
			EditorGUILayout.PropertyField (far, new GUIContent("Volume end"));
		}
		EditorGUILayout.PropertyField (cullingMask, new GUIContent("Culling mask"));

		EditorGUI.BeginChangeCheck();
		EditorGUILayout.PropertyField (shadowmapMode, new GUIContent("Shadowmap mode"));
		if (shadowmapMode.enumValueIndex == LightShaftsShadowmapMode.Static) {
			if (GUILayout.Button("Update shadowmap") || EditorGUI.EndChangeCheck()) {
				effect.SetShadowmapDirty();
				EditorUtility.SetDirty(target);
			}
		}

		Label("Brightness");
		
		EditorGUILayout.PropertyField (colored, new GUIContent("Colored"));
		if (colored.boolValue)
		{
			EditorGUILayout.PropertyField (brightnessColored, new GUIContent("Brightness (colored)"));
			EditorGUILayout.PropertyField (colorFilterMask, new GUIContent("Color filters mask"));
			EditorGUILayout.Slider (colorBalance, 0.1f, 1, new GUIContent("Color balance"));
		}
		else
		{
			EditorGUILayout.PropertyField (brightness, new GUIContent("Brightness"));
		}
		// Creates a sphere around the camera, in which the light is not accumulated in. Sometimes useful.
		// EditorGUILayout.PropertyField (minDistFromCamera, new GUIContent("Min dist from cam"));

		Label("Attenuation");

		EditorGUILayout.PropertyField (attenuationCurveOn, new GUIContent("Attenuation curve"));
		var updateLUTs = false;
		if (attenuationCurveOn.boolValue)
		{
			EditorGUI.BeginChangeCheck();
			attenuationCurve.animationCurveValue = EditorGUILayout.CurveField (GUIContent ("Attenuation curve"), attenuationCurve.animationCurveValue, Color.white, Rect (0.0,0.0,1.0,1.0));
			updateLUTs = EditorGUI.EndChangeCheck();
		}
		else
		{
			if (effect.directional)
				EditorGUILayout.PropertyField (extinction, new GUIContent("Attenuation"));
			else
				EditorGUILayout.LabelField("Default");
		}
		
		Label("Quality");
		
		EditorGUILayout.IntPopup (shadowmapRes, sizesStr, sizes, new GUIContent("Shadowmap resolution"));
		EditorGUILayout.IntPopup (epipolarSamples, sizesStr, sizes, new GUIContent("Samples along rays"));
		EditorGUILayout.IntPopup (epipolarLines, sizesStr, sizes, new GUIContent("Samples across rays"));
		EditorGUILayout.PropertyField (depthThreshold, new GUIContent("Depth threshold"));
		EditorGUILayout.IntPopup (interpolationStep, interpolationStepValuesStr, interpolationStepValues, new GUIContent("Force sample every"));
		
		Label("Show samples");
		
		EditorGUILayout.PropertyField (showSamples, new GUIContent("Raymarched (slow) samples"));
		if (showSamples.boolValue)
		{
			if (SystemInfo.graphicsShaderLevel >= 50)
			{
				// Maybe not that important to show
				//EditorGUILayout.PropertyField (showInterpolatedSamples, new GUIContent("Interpolated samples"));
				EditorGUILayout.Slider (backgroundFade, 0, 1, new GUIContent("Background fade"));
			}
			else
				EditorGUILayout.HelpBox("Sample visualisation works only in DX11", MessageType.Warning, true);
		}
		
		CheckParamBounds();
		so.ApplyModifiedProperties();
		if (updateLUTs)
			effect.UpdateLUTs();
		if (updateCameraDepthMode)
			effect.UpdateCameraDepthMode();
	}
}
