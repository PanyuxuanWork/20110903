/***************************************************************************
// File       : RegionMaskDebugSetup.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sirenix.OdinInspector;
using UnityEngine;
using static Enviro.EnviroManager;

public class RegionMaskDebugSetup : MonoBehaviour
{
    public SeasonChangedController _seasonCtrl;
    public Color SpringTint = new(0.95f, 1.05f, 0.95f, 1f);
    public Color SummerTint = Color.white;
    public Color AutumnTint = new(0.92f, 0.85f, 0.72f, 1f);
    public Color WinterTint = new(0.85f, 0.9f, 0.95f, 1f);
    private SeasonId _cachedTarget;
    private bool _hasTransition;
    private Color _fromTint;
    private Color _toTint;


    public Texture2D RegionMask;
    public Vector2 WorldMin = Vector2.zero;
    public Vector2 WorldSize = new Vector2(4096, 4096);
    public Material TerrainMat;

    [Button("Test")]
    public void Test()
    {
        if (TerrainMat == null)
        {
            Debug.LogError("TerrainMat is null");
            return;
        }

        TerrainMat.SetTexture("_RegionMask", RegionMask);
        TerrainMat.SetVector("_RegionWorldMin", new Vector4(WorldMin.x, WorldMin.y, 0, 0));
        TerrainMat.SetVector("_RegionWorldSize", new Vector4(WorldSize.x, WorldSize.y, 0, 0));

        TerrainMat.SetColor("_RegionTintR", new Color(0.82f, 0.78f, 0.70f, 1f));
        TerrainMat.SetColor("_RegionTintG", new Color(1f, 1f, 1f, 1f));
        TerrainMat.SetColor("_RegionTintB", new Color(1f, 1f, 1f, 1f));
        TerrainMat.SetColor("_RegionTintA", new Color(1f, 1f, 1f, 1f));
        TerrainMat.SetVector("_RegionSmoothnessOffset", new Vector4(0.08f,0,0,0));
        Debug.Log("RegionMask test applied");
    }
    [Button]
    public void ResetRegionMask()
    {
        if (TerrainMat == null) return;

        TerrainMat.SetTexture("_RegionMask", Texture2D.blackTexture);
        TerrainMat.SetVector("_RegionWorldMin", Vector4.zero);
        TerrainMat.SetVector("_RegionWorldSize", new Vector4(WorldSize.x, WorldSize.y, 0, 0));

        TerrainMat.SetColor("_RegionTintR", Color.white);
        TerrainMat.SetColor("_RegionTintG", Color.white);
        TerrainMat.SetColor("_RegionTintB", Color.white);
        TerrainMat.SetColor("_RegionTintA", Color.white);

        TerrainMat.SetVector("_RegionSmoothnessOffset", Vector4.zero);
    }

    private void OnEnable()
    {
        if (_seasonCtrl != null)
            _seasonCtrl.OnSeasonContinuousChanged += OnSeasonChanged;
    }

    private void OnDisable()
    {
        if (_seasonCtrl != null)
            _seasonCtrl.OnSeasonContinuousChanged -= OnSeasonChanged;
    }
    public void OnSeasonChanged(SeasonId target, float t01)
    {
        if (TerrainMat == null || _seasonCtrl == null) return;

        if (!_hasTransition || _cachedTarget != target)
        {
            _cachedTarget = target;
            _hasTransition = true;

            _fromTint = GetTint(_seasonCtrl.CurrentSeason);
            _toTint = GetTint(target);

            TerrainMat.SetColor("_SeasonFromTint", _fromTint);
            TerrainMat.SetColor("_SeasonToTint", _toTint);
        }

        TerrainMat.SetFloat("_SeasonBlend", t01);

        if (t01 >= 1f)
            _hasTransition = false;
    }

    private Color GetTint(SeasonId id) => id switch
    {
        SeasonId.Spring => SpringTint,
        SeasonId.Summer => SummerTint,
        SeasonId.Autumn => AutumnTint,
        SeasonId.Winter => WinterTint,
        _ => Color.white
    };
}