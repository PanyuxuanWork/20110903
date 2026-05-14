/***************************************************************************
// File       : CityContext.cs
// Author     : Panyuxuan
// Created    : 2026/02/22
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using UnityEngine;

public class CityContext : MonoBehaviour
{
    public Area ParentArea;

    /// <summary>
    /// 声望
    /// </summary>
    public int Reputation = 0;

    /// <summary>
    /// 幸福度
    /// </summary>
    public int Happiness = 0;

    /// <summary>
    /// 获取当前的声望
    /// </summary>
    /// <returns></returns>
    public int GetCurReputation()
    {
        return Reputation;
    }

    /// <summary>
    /// 获取当前的幸福度
    /// </summary>
    /// <returns></returns>
    public int GetCurHappiness()
    {
        return Happiness;
    }
}
