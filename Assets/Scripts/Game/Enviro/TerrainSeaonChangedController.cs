/***************************************************************************
// File       : TerrainSeasonChangedController.cs
// Author     : Panyuxuan
// Created    : 2026/03/06
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections;
using UnityEngine;

public class TerrainSeaonChangedController : MonoBehaviour
{
    public Terrain terrain;
    public Color Spring;
    public Color Summer;
    public Color Autumn;
    public Color Winter;

    private Color currentColor;
    // 可选：复用临时数组，减少GC（在 Awake 初始化）
    private float[,,] _blended;

    void Awake()
    {
        if (!terrain) terrain = Terrain.activeTerrain;

        if (terrain)
        {
            var td = terrain.terrainData;
            _blended = new float[td.alphamapHeight, td.alphamapWidth, td.alphamapLayers];
        }
    }

    private void Start()
    {
        switch (SeasonChangedController.Instance.CurrentSeason)
        {
            case SeasonId.Spring:
                currentColor = Spring;
                break;
            case SeasonId.Summer:
                currentColor = Summer;
                break;
            case SeasonId.Autumn:
                currentColor = Autumn;
                break;
            case SeasonId.Winter:
                currentColor = Winter;
                break;
        }
        
    }

    public void TransitionAToB(SeasonId id, float t)
    {
        TerrainLayer layer = terrain.terrainData.terrainLayers[4];
        Color target=new Color();
        switch (id)
        {
            case SeasonId.Spring:
                target = Spring;
                break;
            case SeasonId.Summer:
                target = Summer;
                break;
            case SeasonId.Autumn:
                target = Autumn;
                break;
            case SeasonId.Winter:
                target = Winter;
                break;
        }


        layer.diffuseRemapMax = Color.Lerp(currentColor, target, t);
    }

}
