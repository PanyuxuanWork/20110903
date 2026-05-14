/***************************************************************************
// File       : BuildingCheckBeforePlace.cs
// Author     : Panyuxuan
// Created    : 2026/03/02
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

/// <summary>
/// 建筑放置前检测 - 注册机
/// </summary>
public class BuildingCheckBeforePlace : MonoBehaviour
{
    private BuildingService service;

    private void Awake()
    {
        service ??= GetComponent<BuildingService>();
    }

    private void Start()
    {
        service.RegisterBeforeAction(CheckHallTownLogic);
        service.RegisterBeforeAction(CheckHallTownNotNull);
    }

    #region 放置前注册

    private bool CheckHallTownLogic(BuildCheckContext context)
    {
        if (context.asset.buildMinor == BuildMinor.市政厅)
        {
            if (context.cArea.hallTown)
            {
                context.msg = "市政厅已存在";
                return false;
            }
        }
        return true;
    }

    private bool CheckHallTownNotNull(BuildCheckContext context)
    {
        if (context.asset.buildMinor != BuildMinor.市政厅)
        {
            if (!context.cArea.hallTown)
            {
                context.msg = "请先建造市政厅";
                return false;
            }
        }
        return true;
    }

    #endregion
}
