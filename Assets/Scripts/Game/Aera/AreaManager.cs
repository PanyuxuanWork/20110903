/***************************************************************************
// File       : AreaManager.cs
// Author     : Panyuxuan
// Created    : 2026/02/22
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

/// <summary>
/// Area - Mode/Control 替代品
/// </summary>
public class AreaManager : MonoBehaviour
{
    private Area area;

    private void Awake()
    {
        area = GetComponent<Area>();
    }

 
}
