/***************************************************************************
// File       : TradeCore.cs
// Author     : Panyuxuan
// Created    : 2026/03/12
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using UnityEngine;

namespace Game.Trade
{
    /// <summary>
    /// 商队任务状态
    /// </summary>
    public enum TradeCaravanTaskStatus
    {
        None = 0,

        /// <summary>
        /// 等待装货中
        /// </summary>
        WaitingResource = 1,

        /// <summary>
        /// 正在运输中
        /// </summary>
        Travelling = 2,

        /// <summary>
        /// 已被阻断，当前不推进
        /// </summary>
        Blocked = 3,

        /// <summary>
        /// 已到达，但还有资源未完全交付到仓库
        /// </summary>
        WaitingDelivery = 4,

        /// <summary>
        /// 已全部完成
        /// </summary>
        Delivered = 5,

        /// <summary>
        /// 已取消
        /// </summary>
        Cancelled = 6
    }

    /// <summary>
    /// 商队阻断原因
    /// MVP阶段先保留少量状态即可
    /// </summary>
    public enum TradeBlockReason
    {
        None = 0,

        /// <summary>
        /// 被强盗阻断
        /// </summary>
        Bandit = 1,

        /// <summary>
        /// 季节原因导致阻断
        /// </summary>
        Season = 2,

        /// <summary>
        /// 路线不可用
        /// </summary>
        RouteUnavailable = 3,

        /// <summary>
        /// 手动暂停
        /// </summary>
        ManualPause = 4
    }
}
