/***************************************************************************
// File       : BuildingUnlockSystem.cs
// Author     : Panyuxuan
// Created    : 2026/03/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;


public class BuildingUnlockSystem : MonoSingleton<BuildingUnlockSystem>
{
    [Header("按建筑级别")]
    [SerializeField] private List<BuildAsset> level_1_buildings;
    [SerializeField] private List<BuildAsset> level_2_buildings;
    [SerializeField] private List<BuildAsset> level_3_buildings;
    [SerializeField] private List<BuildAsset> level_4_buildings;
    [SerializeField] private List<BuildAsset> level_5_buildings;

    [Header("自定义")]
    [SerializeField] private List<BuildAsset> custom_1;
    [SerializeField] private List<BuildAsset> custom_2;
    [SerializeField] private List<BuildAsset> custom_3;

    private readonly Dictionary<Func<bool>, List<BuildAsset>> _unlockRules = new();

    protected override void Awake()
    {
        base.Awake();
        RegisterInit();
    }

    private void Start()
    {
        BuildingService.Instance.OnAfterPlaceGlobal += (a, b) => UnlockedCheck();
    }

    #region 外部API
    /// <summary>
    /// 任何可能触发建筑解锁的时候调用
    /// </summary>
    public void UnlockedCheck()
    {
        foreach (var v in _unlockRules)
        {
            if (v.Value == null) continue;
            if (v.Key.Invoke())
            {
                foreach (var build in v.Value)
                {
                    HUDController.Instance.NotifyBuildUnlock(build.bname);
                }

                UIIconUnlockSystem.Instance.UnlockedCheck();
            }
        }
    }

    #endregion

    #region 解锁建筑事件注册

    private void RegisterInit()
    {
        Register(level_1_UnlockFunc, level_1_buildings);
    }

    private void Register(Func<bool> f, List<BuildAsset> assets)
    {
        _unlockRules.TryAdd(f, assets);
    }

    /// <summary>
    /// 市政厅后解锁
    /// </summary>
    /// <returns></returns>
    private bool level_1_UnlockFunc()
    {
        foreach (var v in AreaContext.Instance.Areas)
        {
            if (v.hallTown)
                return true;
        }
        return false;
    }



    #endregion


}
