/***************************************************************************
// File       : UIIconUnlockSystem.cs
// Author     : Panyuxuan
// Created    : 2026/03/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;


public class UIIconUnlockSystem : MonoSingleton<UIIconUnlockSystem>
{
    [Header("Icon级别")]
    [SerializeField] private List<GameObject> level_1_Icon;
    [SerializeField] private List<GameObject> level_2_Icon;
    [SerializeField] private List<GameObject> level_3_Icon;
    [SerializeField] private List<GameObject> level_4_Icon;
    [SerializeField] private List<GameObject> level_5_Icon;

    private readonly Dictionary<Func<bool>, List<GameObject>> _unlockRules = new();

    private void Start()
    {
        RegisterInit();
    }

    public void UnlockedCheck()
    {
        foreach (var v in _unlockRules)
        {
            if (v.Value == null) continue;
            if (v.Key.Invoke())
            {
                foreach (var build in v.Value)
                {
                    foreach (var icon in v.Value)
                    {
                        icon.SetActive(true);
                    }
                }
            }
        }
    }

    private void RegisterInit()
    {
        Register(level_1_UnlockFunc,level_1_Icon);
    }

    private void Register(Func<bool> f, List<GameObject> go)
    {
        _unlockRules.TryAdd(f, go);
    }

    private bool level_1_UnlockFunc()
    {
        foreach (var v in AreaContext.Instance.Areas)
        {
            if (v.hallTown)
                return true;
        }
        return false;
    }
}
