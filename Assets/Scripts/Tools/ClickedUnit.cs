/***************************************************************************
// File       : ClickedUnitInterface.cs
// Author     : Panyuxuan
// Created    : 2025/12/28
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using Sirenix.OdinInspector;
using UnityEngine;


public interface ClickedUnitInterface
{
    public string ClickedName { get;  set; }

    public Action OnClicked { get; set; }

    public void ClickedInit();

}

public class ClickedUnit: MonoBehaviour, ClickedUnitInterface
{
    [ShowInInspector]
    public string ClickedName { get; set; }
    public Action OnClicked { get; set; } = null;

    public bool isFirstClick = true;

    public void ClickedInit()
    {
       
    }
}
