/***************************************************************************
// File       : HUDModel.cs
// Author     : Panyuxuan
// Created    : 2025/11/1
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using Sim;
using UnityEngine;

public class HUDModel
{

    #region 人口

    /// <summary>
    /// 人口总量
    /// </summary>
    public Property<int> PopulationAmount = new();

    /// <summary>
    /// 工作人口的总量
    /// </summary>
    public Property<int> WorkPeopleAmount = new();

    /// <summary>
    /// 失业人口的总量
    /// </summary>
    public Property<int> UnEmployeeAmount = new();

    /// <summary>
    /// 幸福度
    /// </summary>
    public Property<int> HappinessValue = new();

    /// <summary>
    /// 声望
    /// </summary>
    public Property<int> ReputationValue = new();
    #endregion

    #region 资源

    public Property<int> Gold = new();

    public Property<int> Water = new();

    public Property<int> Food = new();

    public Property<int> Wood = new();

    public Property<int> Stone = new();

    public Property<int> TechPoint = new();

    #endregion

    #region 二级子页面

    public Dictionary<string, B_Second_Widget> BuildingSecondWidgetDic=new();

    #endregion
}
