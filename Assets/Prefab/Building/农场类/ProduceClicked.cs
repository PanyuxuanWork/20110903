/***************************************************************************
// File       : ProduceClicked.cs
// Author     : Panyuxuan
// Created    : 2026/01/03
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

public class ProduceClicked : ClickedUnit
{
    private void Awake()
    {
        OnClicked += () =>
        {
            Debug.Log($"Clicked on {ClickedName}");
        };
    }
}
