/***************************************************************************
// File       : FarmGrow.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

public class FarmGrow : MonoBehaviour
{
    private List<GameObject> crops=new();
    private void Awake()
    {
        var go = transform.GetChild(1);
        if (go != null)
        {
            for (int i = 0; i < go.childCount; i++)
            {
                crops.Add(go.GetChild(i).gameObject);
            }
        }
    }

    public void SetGrowthStage(int stage)
    {
        for (int i = 0; i < crops.Count; i++)
        {
            crops[i].SetActive(i == stage);
        }
    }
}
