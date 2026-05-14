/***************************************************************************
// File       : Top_CityPeopleInfo.cs
// Author     : Panyuxuan
// Created    : 2025/12/18
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] 城市人口信息显示组件
// ***************************************************************************/

using System;
using UnityEngine;

public class Top_CityPeopleInfo : MonoBehaviour
{
    /// <summary>
    /// 总人口
    /// </summary>
    public VisualizedData<int> population;
    /// <summary>
    /// 劳动人口
    /// </summary>
    public VisualizedData<int> worker;

    /// <summary>
    /// 失业人口
    /// </summary>
    public VisualizedData<int> unemployment;

    /// <summary>
    /// 幸福度
    /// </summary>
    public VisualizedData<int> happiness;

    /// <summary>
    /// 声望
    /// </summary>
    public VisualizedData<int> reputation;


}
