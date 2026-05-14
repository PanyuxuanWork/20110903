/***************************************************************************
// File       : EnviroDataAssetEditor.cs
// Author     : Panyuxuan
// Created    : 2026/03/06
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[CustomEditor(typeof(EnviroDataAsset))]
public class EnviroDataAssetEditor : Editor
{
    private Volume _sourceVolume;
    private Light _sourceLight;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Capture From Scene", EditorStyles.boldLabel);

        _sourceVolume = (Volume)EditorGUILayout.ObjectField("Source Volume", _sourceVolume, typeof(Volume), true);
        _sourceLight = (Light)EditorGUILayout.ObjectField("Source Light", _sourceLight, typeof(Light), true);

        using (new EditorGUI.DisabledScope(_sourceVolume == null && _sourceLight == null))
        {
            if (GUILayout.Button("Capture To This Asset"))
                Capture();
        }

        EditorGUILayout.HelpBox(
            "只抓取 EnviroData 里已有字段：\n" +
            "- Light: color/intensity\n" +
            "- Bloom: tint/intensity/threshold -> Illumination*\n" +
            "- ColorAdjustments: postExposure/contrast/saturation\n" +
            "- Vignette: color/intensity/smoothness -> VignetteSlide\n" +
            "- SMH: shadows/midtones/highlights\n",
            MessageType.Info);
    }

    private void Capture()
    {
        var asset = (EnviroDataAsset)target;
        var d = asset.data;

        // Light
        if (_sourceLight != null)
        {
            d.LightColor = _sourceLight.color;
            d.LightIntensity = _sourceLight.intensity;
        }

        // Volume Profile
        if (_sourceVolume != null)
        {
            var profile = _sourceVolume.profile != null ? _sourceVolume.profile : _sourceVolume.sharedProfile;
            if (profile == null)
            {
                Debug.LogWarning("Source Volume 没有 profile/sharedProfile，无法抓取。");
                return;
            }

            // Bloom -> Illumination*
            if (profile.TryGet(out Bloom bloom))
            {
                d.IlluminationColor = bloom.tint.value;
                d.IlluminationIntensity = bloom.intensity.value;
                d.IlluminationThreshold = bloom.threshold.value;
            }

            // Color Adjustments
            if (profile.TryGet(out ColorAdjustments ca))
            {
                d.PostExposure = ca.postExposure.value;
                d.Contrast = ca.contrast.value;
                d.Saturation = ca.saturation.value;
            }

            // Vignette -> Slide 映射为 smoothness
            if (profile.TryGet(out Vignette vig))
            {
                d.VignetteColor = vig.color.value;
                d.VignetteIntensity = vig.intensity.value;
                d.VignetteSlide = vig.smoothness.value;
            }

            // SMH
            if (profile.TryGet(out ShadowsMidtonesHighlights smh))
            {
                d.Shadows = smh.shadows.value;
                d.Midtones = smh.midtones.value;
                d.Highlights = smh.highlights.value;
            }
        }

        asset.data = d;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log($"Captured EnviroData -> {AssetDatabase.GetAssetPath(asset)}");
    }
}
#endif
