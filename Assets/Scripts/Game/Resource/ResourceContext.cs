/***************************************************************************
// File       : ResourceContext.cs
// Author     : Panyuxuan
// Created    : 2026/02/22
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using Sim.Resources;
using UnityEngine;

public class ResourceContext : MonoBehaviour, IStepListener
{
    public Area ParentArea;

    private ProductionFlowHub producerContext;//耦合产物，后续需要优化掉

    public Dictionary<ResourceId, int> resourceDic;

    private void Awake()
    {
        producerContext = GetComponent<ProductionFlowHub>();
    }

    public void UpdateResourceCount()
    {
        var Storages = producerContext.supplyStorages;

        foreach (var v in resourceDic)
        {
            resourceDic[v.Key] = 0;
        }

        foreach (var v in Storages)
        {
            foreach (var slot in v.Slots)
            {
                if (resourceDic.ContainsKey(slot.Id))
                {
                    resourceDic[slot.Id] += slot.Amount;
                }
                else
                {
                    resourceDic.Add(slot.Id, slot.Amount);
                }
            }
        }
    }

    public int GetResourceTotalAmountByType(ResourceId id)
    {
        return resourceDic.GetValueOrDefault(id);
    }

    public int Priority { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public void OnTick(in TickContext ctx)
    {
        UpdateResourceCount();
    }
}
