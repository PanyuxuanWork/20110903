/***************************************************************************
// File       : LightVolumeChangedController.cs
// Author     : Panyuxuan
// Created    : 2026/03/05
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable]
public struct EnviroData 
{ 
    //Light
  public Color LightColor; 
  public float LightIntensity; 

  //Volume
  public Color IlluminationColor;
  public float IlluminationIntensity; 
  public float IlluminationThreshold; 

  //Color Adjustments
  public float PostExposure; 
  public float Contrast; 
  public float Saturation; 

  //Vignette
  public Color VignetteColor; 
  public float VignetteIntensity;
  public float VignetteSlide;

  // Shadows / Midtones / Highlights
  public Vector4 Shadows;    // x,y,z: 色偏移，w: 额外通道（保持默认也行）
  public Vector4 Midtones;
  public Vector4 Highlights;
}

public class LightVolumeChangedController : MonoBehaviour
{
    [Header("Config")]
    public Light GlobalLight;
    public Volume GlobalVolume;

    public EnviroDataAsset SpringData;
    public EnviroDataAsset SummerData;
    public EnviroDataAsset AutumnData;
    public EnviroDataAsset WinterData;

    public EnviroData TargetEnviro;
    
    // Cached overrides (URP)
    private Bloom _bloom;
    private ColorAdjustments _colorAdj;
    private Vignette _vignette;
    private ShadowsMidtonesHighlights _smh;

    private bool _inited;

    void EnsureVolumeOverrides()
    {
        if (_inited) return;
        _inited = true;

        if (!GlobalVolume) return;

        if (Application.isPlaying)
        {
            var src = GlobalVolume.profile != null ? GlobalVolume.profile : GlobalVolume.sharedProfile;
            if (src != null) GlobalVolume.profile = Instantiate(src);
            else 
                GlobalVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        }

        _inited = true;

        var p = GlobalVolume.profile;

        if (!p.TryGet(out _bloom)) _bloom = p.Add<Bloom>(true);
        if (!p.TryGet(out _colorAdj)) _colorAdj = p.Add<ColorAdjustments>(true);
        if (!p.TryGet(out _vignette)) _vignette = p.Add<Vignette>(true);

        _bloom.active = true;
        _colorAdj.active = true;
        _vignette.active = true;

        if (!p.TryGet(out _smh)) _smh = p.Add<ShadowsMidtonesHighlights>(true);
        _smh.active = true;
    }

    public void OnSeasonChanged(SeasonId id,float t)
    {
        switch (id)
        {
            case SeasonId.Spring:
            {
                TargetEnviro = SpringData.data;
                break;
            }
            case SeasonId.Summer:
            {
                TargetEnviro = SummerData.data;
                break;
            }
            case SeasonId.Autumn:
            {
                TargetEnviro = AutumnData.data;
                break;
            }
            case SeasonId.Winter:
            {
                TargetEnviro = WinterData.data;
                break;
            }
        }

        var data = TargetEnviro;

        t = Mathf.Clamp01(t);

        var l = GlobalLight;
        var v = GlobalVolume;

        // Light
        if (l != null)
        {
            l.color = Color.Lerp(l.color, data.LightColor, t);
            l.intensity = Mathf.Lerp(l.intensity, data.LightIntensity, t);
        }

        // Volume (URP)
        if (v == null) return;
        EnsureVolumeOverrides();

        // Bloom (你这里的 Illumination 对应 Bloom)
        if (_bloom != null)
        {
            _bloom.tint.value = Color.Lerp(_bloom.tint.value, data.IlluminationColor, t);
            _bloom.intensity.value = Mathf.Lerp(_bloom.intensity.value, data.IlluminationIntensity, t);
            _bloom.threshold.value = Mathf.Lerp(_bloom.threshold.value, data.IlluminationThreshold, t);
        }

        // Color Adjustments
        if (_colorAdj != null)
        {
            _colorAdj.postExposure.value = Mathf.Lerp(_colorAdj.postExposure.value, data.PostExposure, t);
            _colorAdj.contrast.value = Mathf.Lerp(_colorAdj.contrast.value, data.Contrast, t);
            _colorAdj.saturation.value = Mathf.Lerp(_colorAdj.saturation.value, data.Saturation, t);
        }

        // Vignette
        if (_vignette != null)
        {
            _vignette.color.value = Color.Lerp(_vignette.color.value, data.VignetteColor, t);
            _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, data.VignetteIntensity, t);

            // 你这里的 VignetteSlide 我按“过渡柔和度/边缘平滑”来映射（0~1）
            _vignette.smoothness.value = Mathf.Lerp(_vignette.smoothness.value, data.VignetteSlide, t);
        }

        if (_smh != null)
        {
            _smh.shadows.value = Vector4.Lerp(_smh.shadows.value, data.Shadows, t);
            _smh.midtones.value = Vector4.Lerp(_smh.midtones.value, data.Midtones, t);
            _smh.highlights.value = Vector4.Lerp(_smh.highlights.value, data.Highlights, t);
        }
    }
    
}

