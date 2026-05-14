/***************************************************************************
// File       : TradeTools.cs
// Author     : Panyuxuan
// Created    : 2026/03/28
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Trade exchange utilities for caravan-based trade flow.
// ***************************************************************************/

using System;
using System.Collections.Generic;
using Game.Items;
using Game.Trade;
using Sim.Resources;
using UnityEngine;

public static class TradeInstantExchangeUtility
{
    /// <summary>
    /// 到站瞬时执行一次贸易交换：
    /// - MoveUnit.storage 向 ToEndpoint.Storage 支付 CostResources
    /// - ToEndpoint.Storage 向 MoveUnit.storage 支付 RewardResources
    ///
    /// 交换阶段的商队仓库要求：
    /// 1. 先装着 CostResources 到站
    /// 2. 交出 CostResources 后变为空仓
    /// 3. 空仓后重建为 RewardResources 槽位布局
    /// 4. 再从终点仓库取回 RewardResources
    /// </summary>
    public static bool TryApplyInstantExchange(
        TradeDataContext context,
        out string reason,
        int autoCreateSlotCapacity = 999999)
    {
        reason = null;

        if (context == null)
        {
            reason = "TradeDataContext is null.";
            return false;
        }

        var offer = context.OfferDef;
        var caravanStorage = context.MoveUnit != null ? context.MoveUnit.storage : null;
        var toStorage = context.ToEndpoint?.Storage;

        if (offer == null)
        {
            reason = "OfferDef is null.";
            return false;
        }

        if (caravanStorage == null)
        {
            reason = "MoveUnit.storage is null.";
            return false;
        }

        if (toStorage == null)
        {
            reason = "ToEndpoint.Storage is null.";
            return false;
        }

        if (caravanStorage == toStorage)
        {
            reason = "Caravan storage and target storage cannot be the same.";
            return false;
        }

        if (autoCreateSlotCapacity <= 0)
        {
            reason = "autoCreateSlotCapacity must be > 0.";
            return false;
        }

        // caravan -> to
        Dictionary<ResourceId, int> caravanPays = BuildResourceMap(
            offer.CostResources,
            nameof(offer.CostResources),
            out reason);
        if (caravanPays == null)
            return false;

        // to -> caravan
        Dictionary<ResourceId, int> toPays = BuildResourceMap(
            offer.RewardResources,
            nameof(offer.RewardResources),
            out reason);
        if (toPays == null)
            return false;

        if (caravanPays.Count == 0 && toPays.Count == 0)
        {
            reason = "Offer contains no valid trade resources.";
            return false;
        }

        // 1) 校验商队身上是否有足够的成本货
        foreach (var kv in caravanPays)
        {
            int available = caravanStorage.GetAvailable(kv.Key);
            if (available < kv.Value)
            {
                reason = $"CaravanStorage lacks resource {kv.Key}, need={kv.Value}, available={available}.";
                return false;
            }
        }

        // 2) 校验终点仓库是否有足够的奖励货
        foreach (var kv in toPays)
        {
            int available = toStorage.GetAvailable(kv.Key);
            if (available < kv.Value)
            {
                reason = $"ToStorage lacks resource {kv.Key}, need={kv.Value}, available={available}.";
                return false;
            }
        }

        // 3) 确保终点仓库可以接收成本货
        foreach (var kv in caravanPays)
        {
            EnsureWritable(toStorage, kv.Key, kv.Value, autoCreateSlotCapacity);
        }

        // 4) 商队先交出成本货 -> 终点仓库
        foreach (var kv in caravanPays)
        {
            int removed = caravanStorage.RemoveFromAnySlot(kv.Key, kv.Value);
            if (removed != kv.Value)
            {
                reason = $"Remove failed on CaravanStorage for {kv.Key}, need={kv.Value}, removed={removed}.";
                return false;
            }

            int added = toStorage.AddToAnySlot(kv.Key, kv.Value);
            if (added != kv.Value)
            {
                reason = $"Add failed on ToStorage for {kv.Key}, need={kv.Value}, added={added}.";
                return false;
            }
        }

        // 5) 成本货交完后，商队必须为空仓，然后切换成 Reward 槽位布局
        if (!caravanStorage.IsEmpty())
        {
            reason = "CaravanStorage is not empty after paying cost resources.";
            return false;
        }

        var rewardSlotDefs = BuildSlotDefsFromTradeStacks(offer.RewardResources, out reason);
        if (rewardSlotDefs == null)
            return false;

        if (!caravanStorage.RebuildSlotsIfEmpty(rewardSlotDefs))
        {
            reason = "CaravanStorage cannot rebuild reward slots because it is not empty.";
            return false;
        }

        // 6) 终点仓库支付奖励货 -> 商队
        foreach (var kv in toPays)
        {
            int removed = toStorage.RemoveFromAnySlot(kv.Key, kv.Value);
            if (removed != kv.Value)
            {
                reason = $"Remove failed on ToStorage for {kv.Key}, need={kv.Value}, removed={removed}.";
                return false;
            }

            int added = caravanStorage.AddToAnySlot(kv.Key, kv.Value);
            if (added != kv.Value)
            {
                reason = $"Add failed on CaravanStorage for {kv.Key}, need={kv.Value}, added={added}.";
                return false;
            }
        }

        return true;
    }

    private static Dictionary<ResourceId, int> BuildResourceMap(
        List<TradeResourceStack> stacks,
        string label,
        out string reason)
    {
        reason = null;
        Dictionary<ResourceId, int> map = new Dictionary<ResourceId, int>();

        if (stacks == null || stacks.Count == 0)
            return map;

        for (int i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            if (!stack.IsValid())
                continue;

            if (!ItemIDUtility.TryGetResourceId(stack.ItemCode, out var resourceId) || resourceId == ResourceId.None)
            {
                reason = $"{label}[{i}] itemCode={stack.ItemCode} cannot convert to ResourceId.";
                return null;
            }

            try
            {
                int oldAmount = map.TryGetValue(resourceId, out var value) ? value : 0;
                map[resourceId] = checked(oldAmount + stack.Amount);
            }
            catch (OverflowException)
            {
                reason = $"{label}[{i}] amount overflow on resourceId={resourceId}.";
                return null;
            }
        }

        return map;
    }

    private static (ResourceId id, int cap, int initial)[] BuildSlotDefsFromTradeStacks(
        List<TradeResourceStack> stacks,
        out string reason)
    {
        reason = null;

        Dictionary<ResourceId, int> map = BuildResourceMap(stacks, nameof(stacks), out reason);
        if (map == null)
            return null;

        var defs = new (ResourceId id, int cap, int initial)[map.Count];
        int index = 0;

        foreach (var kv in map)
        {
            defs[index++] = (kv.Key, kv.Value, 0);
        }

        return defs;
    }

    private static void EnsureWritable(
        Storage storage,
        ResourceId id,
        int amount,
        int autoCreateSlotCapacity)
    {
        if (storage == null || id == ResourceId.None || amount <= 0)
            return;

        if (!storage.CanAccept(id))
        {
            storage.AddOneSlot(id, Mathf.Max(autoCreateSlotCapacity, amount), 0);
        }

        while (storage.GetFreeCapacity(id) < amount)
        {
            int needMore = amount - storage.GetFreeCapacity(id);
            storage.AddOneSlot(id, Mathf.Max(autoCreateSlotCapacity, needMore), 0);
        }
    }
}
