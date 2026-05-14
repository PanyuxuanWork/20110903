/***************************************************************************
// File       : ResidentClicked.cs
// Author     : Panyuxuan
// Created    : 2026/02/20
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

public class ResidentClicked : ClickedUnit
{
    private void Awake()
    {
        OnClicked += () =>
        {
            if (TryGetComponent(out Resident resident))
            {
                PopController.Instance.ShowResident(resident);
            }
            else
            {
                TLog.Error(this,"Resident cannot found");
            }
        };
    }
}
