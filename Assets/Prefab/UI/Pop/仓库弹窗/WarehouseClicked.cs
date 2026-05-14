/***************************************************************************
// File       : WarehouseClicked.cs
// Author     : Panyuxuan
// Created    : 2026/02/25
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

public class WarehouseClicked : ClickedUnit
{
    private void Awake()
    {
        OnClicked += () =>
        {
            PopController.Instance.ShowWareHouse(GetComponent<Warehouse>());
        };
    }
}
