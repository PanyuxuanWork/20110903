/***************************************************************************
// File       : TreeGrassSeasonChangedController.cs
// Author     : Panyuxuan
// Created    : 2026/03/03
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class TreeGrassSeasonChangedController : MonoBehaviour
{
    [Header("配置 - 全局材质(运行时被写入)")]
    [SerializeField] private Material TreeMat;
    [SerializeField] private Material GrassMat;

    [Header("预设 - 树")]
    [SerializeField] private Material Tree_Spring_sd;
    [SerializeField] private Material Tree_Summer_sd;
    [SerializeField] private Material Tree_Autumn_sd;
    [SerializeField] private Material Tree_Winter_sd;

    [Header("预设 - 草")]
    [SerializeField] private Material Grass_Spring_sd;
    [SerializeField] private Material Grass_Summer_sd;
    [SerializeField] private Material Grass_Autumn_sd;
    [SerializeField] private Material Grass_Winter_sd;

    // 让子组件能知道“当前季节”（作为 fromSeason）
    private SeasonChangedController _seasonCtrl;

    // 缓存：一段换季期间固定不变
    private bool _cached;
    private SeasonId _cachedTarget;

    private Material _treeFrom, _treeTo;
    private Material _grassFrom, _grassTo;

    // ===== Colors =====
    private static readonly string[] TreeColorProps =
    {
        "_EmissionColor",
        "_BaseAlbedoColor",
        "_BaseSSSColor",
        "_BaseEmissiveColor",
        "_GradientColor",
        "_SecondColor",
        "_TintColor1",
        "_TintColor2",
        "_TopLayerColor",
        "_TopLayerSSSColor",
    };

    // ===== Toggles (Float 0/1) =====
    private static readonly (string prop, string keyword)[] TreeToggleMap =
    {
        ("_EnableFlipNormals", "_ENABLEFLIPNORMALS_ON"),
        ("_EnableGlancingAngleCut", "_ENABLEGLANCINGANGLECUT_ON"),
        ("_EnableGradientColor", "_ENABLEGRADIENTCOLOR_ON"),
        ("_EnableSecondColor", "_ENABLESECONDCOLOR_ON"),
        ("_EnableTintColor", "_ENABLETINTCOLOR_ON"),
        ("_EnableTopLayerBlend", "_ENABLETOPLAYERBLEND_ON"),
    };

    private static readonly string[] TreeCutoffProps =
    {
        "_AlphaCutoff",
        "_CutOff",
    };

    private static readonly string[] TreeInvertMaskProps =
    {
        "_GradientColorInvertMask",
        "_SecondColorInvertMask",
    };

    // ===== Floats =====
    private static readonly string[] TreeFloatProps =
    {
        "_BaseGlancingAngleCut",

        "_BaseAlbedoBrightness",
        "_BaseAlbedoDesaturation",
        "_BaseNormalIntensity",
        "_BaseSmoothnessMin",
        "_BaseSmoothnessMax",
        "_BaseTreeAOIntensity",
        "_BaseLerpBetweenColorandTexture",

        "_BaseSSSIntensity",
        "_BaseSSSAOInfluence",
        "_BaseSSSNormalDistortion",
        "_BaseSSSScattering",
        "_BaseSSSDirect",
        "_BaseSSSAmbiet",
        "_BaseSSSShadow",

        "_BaseEmissiveIntensity",
        "_BaseEmissiveMaskContrast",
        "_BaseEmissiveAOMask",

        "_GradientColorIntensity",
        "_GradientColorOffset",
        "_GradientColorContrast",

        "_SecondColorIntensity",
        "_SecondColorOffset",
        "_SecondColorContrast",

        "_TintNoiseIntensity",
        "_TintNoiseOffset",
        "_TintNoiseContrast",

        "_TopLayerSSSIntensity",
        "_TopLayerSmoothnessIntensity",
        "_TopLayerIntensity",
        "_TopLayerOffset",
        "_TopLayerContrast",
        "_TopLayerAOMask",
    };

    private void Awake()
    {
        _seasonCtrl = GetComponent<SeasonChangedController>();
    }

    /// <summary>
    /// 由 SeasonChangedController 的 OnSeasonContinuousChanged 调用：
    /// target = 正在切到的季节；t01 = 0..1 绝对进度。
    /// </summary>
    public void OnSeasonChanged(SeasonId target, float t01)
    {
        if (TreeMat == null || GrassMat == null) return;

        t01 = Mathf.Clamp01(t01);

        // 换了一次目标季节（或第一次进入），就缓存 from/to
        if (!_cached || _cachedTarget != target)
        {
            _cached = true;
            _cachedTarget = target;

            // fromSeason：优先取 SeasonChangedController.CurrentSeason（更准确）
            SeasonId fromSeason = _seasonCtrl != null ? _seasonCtrl.CurrentSeason : GuessFromByTarget(target);

            _treeFrom = GetTreePreset(fromSeason);
            _treeTo = GetTreePreset(target);

            _grassFrom = GetGrassPreset(fromSeason);
            _grassTo = GetGrassPreset(target);

            // 关键：keyword/toggle 只在开始同步一次（别每帧做）
            ApplyEspecialBlend(_treeTo);
        }

        // 每 tick 只做一次 Apply（不创建 Handle，不调 ContinuousInvoke）
        ApplyBlend(_treeFrom, _treeTo, t01);
        ApplyGrassBlend(_grassFrom, _grassTo, t01);

        // 结束时释放缓存，便于下次换季重新取 from/to
        if (t01 >= 1f)
        {
            _cached = false;
        }
    }

    private Material GetTreePreset(SeasonId id) => id switch
    {
        SeasonId.Spring => Tree_Spring_sd,
        SeasonId.Summer => Tree_Summer_sd,
        SeasonId.Autumn => Tree_Autumn_sd,
        SeasonId.Winter => Tree_Winter_sd,
        _ => Tree_Summer_sd
    };

    private Material GetGrassPreset(SeasonId id) => id switch
    {
        SeasonId.Spring => Grass_Spring_sd,
        SeasonId.Summer => Grass_Summer_sd,
        SeasonId.Autumn => Grass_Autumn_sd,
        SeasonId.Winter => Grass_Winter_sd,
        _ => Grass_Summer_sd
    };

    // 如果没拿到 SeasonChangedController，这里用一个“环状推断”
    // Spring->Summer->Autumn->Winter->Spring
    private static SeasonId GuessFromByTarget(SeasonId target) => target switch
    {
        SeasonId.Spring => SeasonId.Winter,
        SeasonId.Summer => SeasonId.Spring,
        SeasonId.Autumn => SeasonId.Summer,
        SeasonId.Winter => SeasonId.Autumn,
        _ => SeasonId.Summer
    };

    #region Apply

    private void ApplyBlend(Material a, Material b, float t)
    {
        if (a == null || b == null || TreeMat == null) return;

        t = Mathf.Clamp01(t);

        // Colors
        foreach (var p in TreeColorProps)
        {
            if (!a.HasProperty(p) || !b.HasProperty(p) || !TreeMat.HasProperty(p)) continue;
            TreeMat.SetColor(p, Color.Lerp(a.GetColor(p), b.GetColor(p), t));
        }

        // Floats
        foreach (var p in TreeFloatProps)
        {
            if (!a.HasProperty(p) || !b.HasProperty(p) || !TreeMat.HasProperty(p)) continue;
            TreeMat.SetFloat(p, Mathf.Lerp(a.GetFloat(p), b.GetFloat(p), t));
        }

        // Cutoff：只在结束时收敛一次（不插值）
        if (t >= 1f)
        {
            foreach (var p in TreeCutoffProps)
            {
                if (!b.HasProperty(p) || !TreeMat.HasProperty(p)) continue;
                TreeMat.SetFloat(p, b.GetFloat(p));
            }
        }
    }

    /// <summary>
    /// keyword/toggle 同步：只在“开始缓存 from/to”时调用一次。
    /// </summary>
    private void ApplyEspecialBlend(Material b)
    {
        if (b == null || TreeMat == null) return;

        // Toggle(float 0/1) + keyword
        foreach (var (prop, keyword) in TreeToggleMap)
        {
            if (!b.HasProperty(prop) || !TreeMat.HasProperty(prop)) continue;

            bool on = b.GetFloat(prop) > 0.5f;
            TreeMat.SetFloat(prop, on ? 1f : 0f);

            if (on) TreeMat.EnableKeyword(keyword);
            else TreeMat.DisableKeyword(keyword);
        }

        // InvertMask：当作 0/1，不插值
        foreach (var p in TreeInvertMaskProps)
        {
            if (!b.HasProperty(p) || !TreeMat.HasProperty(p)) continue;
            TreeMat.SetFloat(p, As01(b.GetFloat(p)));
        }
    }

    private void ApplyGrassBlend(Material a, Material b, float t)
    {
        if (a == null || b == null || GrassMat == null) return;
        t = Mathf.Clamp01(t);

        // 下面这些属性你原脚本是硬编码的，我保留，但加了 HasProperty 防护
        LerpColor("_BaseAlbedoColor");
        LerpColor("_BottomColor");
        LerpColor("_TintColor");

        LerpFloat("_BaseAlbedoBrightness");
        LerpFloat("_BottomColorOffset");
        LerpFloat("_BottomColorContrast");
        LerpFloat("_TintNoiseUVScale");
        LerpFloat("_TintNoiseIntensity");
        LerpFloat("_TintNoiseContrast");
        LerpFloat("_TintNoiseInvertMask");

        void LerpColor(string p)
        {
            if (!a.HasProperty(p) || !b.HasProperty(p) || !GrassMat.HasProperty(p)) return;
            GrassMat.SetColor(p, Color.Lerp(a.GetColor(p), b.GetColor(p), t));
        }

        void LerpFloat(string p)
        {
            if (!a.HasProperty(p) || !b.HasProperty(p) || !GrassMat.HasProperty(p)) return;
            GrassMat.SetFloat(p, Mathf.Lerp(a.GetFloat(p), b.GetFloat(p), t));
        }
    }

    private static float As01(float v) => v >= 0.5f ? 1f : 0f;

    #endregion
}
