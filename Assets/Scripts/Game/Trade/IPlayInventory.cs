/***************************************************************************
// File       : IPlayInventory.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;

namespace Game.Trade
{
    /// <summary>
    /// 玩家库存接口
    /// 贸易系统只依赖这个最小接口，不直接依赖具体仓库实现。
    /// </summary>
    public interface IPlayerInventory
    {
        /// <summary>
        /// 是否拥有足够的一组资源
        /// </summary>
        bool HasEnough(List<TradeResourceStack> costResources);

        /// <summary>
        /// 扣除一组资源
        /// 调用方应先通过 HasEnough 校验
        /// </summary>
        bool Remove(List<TradeResourceStack> costResources);

        /// <summary>
        /// 尝试添加一组资源到库存。
        /// 返回未能放入的剩余资源列表：
        /// - 全部放入成功：返回空列表
        /// - 部分/全部无法放入：返回剩余部分
        /// </summary>
        List<TradeResourceStack> TryAdd(List<TradeResourceStack> rewardResources);
    }

    /// <summary>
    /// 路线阻断检查接口
    /// 由外部系统（强盗、季节、地图）决定当前路线是否被阻断
    /// </summary>
    public interface ITradeRouteBlockChecker
    {
        /// <summary>
        /// 检查当前任务是否被阻断
        /// </summary>
        bool IsBlocked(TradeCaravanTask task, out TradeBlockReason reason);
    }
}