/***************************************************************************
// File       : TradeCaravanTask.cs
// Author     : Panyuxuan
// Created    : 2026/03/12
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;

namespace Game.Trade
{
    /// <summary>
    /// 一笔正在运行中的商队贸易任务
    /// 
    /// 当前版本职责：
    /// 
    /// 1. 保存本次任务的资源快照
    /// 2. 保存路线与运输方式
    /// 3. 保存总路径长度与当前推进长度
    /// 4. 保存当前运行状态
    /// 
    /// 当前版本不负责：
    /// - 自己计算推进速度
    /// - 自己判断强盗/季节
    /// - 自己操作仓库
    /// </summary>
    [System.Serializable]
    public class TradeCaravanTask
    {
        /// <summary>
        /// 任务唯一ID
        /// </summary>
        public int TaskId;

        /// <summary>
        /// 来源贸易提案ID
        /// </summary>
        public int OfferId;

        /// <summary>
        /// 路线ID
        /// </summary>
        public int RouteId;

        /// <summary>
        /// 运输方式
        /// </summary>
        public TradeTransportMode TransportMode = TradeTransportMode.None;

        /// <summary>
        /// 支付资源快照
        /// 任务发起时固定下来
        /// </summary>
        public List<TradeResourceStack> PaidResourcesSnapshot = new List<TradeResourceStack>();

        /// <summary>
        /// 回报资源快照
        /// 任务发起时固定下来
        /// </summary>
        public List<TradeResourceStack> RewardResourcesSnapshot = new List<TradeResourceStack>();

        /// <summary>
        /// 总路径长度（例如总格子数）
        /// </summary>
        public float TotalPathLength;

        /// <summary>
        /// 已推进路径长度
        /// </summary>
        public float TraveledPathLength;

        /// <summary>
        /// 当前状态
        /// </summary>
        public TradeCaravanTaskStatus Status = TradeCaravanTaskStatus.None;

        /// <summary>
        /// 当前是否被阻断
        /// </summary>
        public bool IsBlocked;

        /// <summary>
        /// 当前阻断原因
        /// </summary>
        public TradeBlockReason BlockReason = TradeBlockReason.None;

        public string TradeName { get; private set; }


        public TradeCaravanTask()
        {
        }

        public TradeCaravanTask(
            string tradeName,
            int taskId,
            int offerId,
            int routeId,
            TradeTransportMode transportMode,
            List<TradeResourceStack> paidResourcesSnapshot,
            List<TradeResourceStack> rewardResourcesSnapshot
            )
        {
            TradeName = tradeName;
            TaskId = taskId;
            OfferId = offerId;
            RouteId = routeId;
            TransportMode = transportMode;
            PaidResourcesSnapshot = CloneResourceList(paidResourcesSnapshot);
            RewardResourcesSnapshot = CloneResourceList(rewardResourcesSnapshot);
            TraveledPathLength = 0f;
            Status = TradeCaravanTaskStatus.WaitingResource;
            IsBlocked = false;
            BlockReason = TradeBlockReason.None;
        }

        /// <summary>
        /// 当前进度（0~1）
        /// </summary>
        public float GetProgress01()
        {
            if (TotalPathLength <= 0f)
            {
                return 0f;
            }

            float progress = TraveledPathLength / TotalPathLength;

            if (progress < 0f)
            {
                return 0f;
            }

            if (progress > 1f)
            {
                return 1f;
            }

            return progress;
        }

        /// <summary>
        /// 当前进度百分比（0~100）
        /// </summary>
        public float GetProgressPercent()
        {
            return GetProgress01() * 100f;
        }

        /// <summary>
        /// 是否已经到达终点
        /// </summary>
        public bool HasReachedDestination()
        {
            return TotalPathLength > 0f && TraveledPathLength >= TotalPathLength;
        }

        /// <summary>
        /// 是否可以推进
        /// </summary>
        public bool CanAdvance()
        {
            return Status == TradeCaravanTaskStatus.Travelling && !IsBlocked;
        }

        /// <summary>
        /// 推进路径长度
        /// </summary>
        public void Advance(float deltaPathLength)
        {
            if (!CanAdvance())
            {
                return;
            }

            if (deltaPathLength <= 0f)
            {
                return;
            }

            TraveledPathLength += deltaPathLength;

            if (TraveledPathLength > TotalPathLength)
            {
                TraveledPathLength = TotalPathLength;
            }
        }

        /// <summary>
        /// 设置阻断状态
        /// </summary>
        public void SetBlocked(TradeBlockReason reason)
        {
            IsBlocked = true;
            BlockReason = reason;
            Status = TradeCaravanTaskStatus.Blocked;
        }

        /// <summary>
        /// 清除阻断状态
        /// </summary>
        public void ClearBlocked()
        {
            IsBlocked = false;
            BlockReason = TradeBlockReason.None;

            if (Status == TradeCaravanTaskStatus.Blocked)
            {
                Status = TradeCaravanTaskStatus.WaitingResource;
            }
        }

        /// <summary>
        /// 设置为待交付
        /// </summary>
        public void SetWaitingDelivery()
        {
            Status = TradeCaravanTaskStatus.WaitingDelivery;
            IsBlocked = false;
            BlockReason = TradeBlockReason.None;
        }

        /// <summary>
        /// 设置为已完成交付
        /// </summary>
        public void SetDelivered()
        {
            Status = TradeCaravanTaskStatus.Delivered;
            IsBlocked = false;
            BlockReason = TradeBlockReason.None;
            TraveledPathLength = TotalPathLength;
        }

        /// <summary>
        /// 设置为取消
        /// </summary>
        public void SetCancelled()
        {
            Status = TradeCaravanTaskStatus.Cancelled;
            IsBlocked = false;
            BlockReason = TradeBlockReason.None;
        }

        /// <summary>
        /// 是否是有效任务
        /// </summary>
        public bool IsValid()
        {
            return TaskId > 0
                && OfferId > 0
                && RouteId > 0
                && TransportMode != TradeTransportMode.None
                && TotalPathLength > 0f
                && HasAnyValidStack(PaidResourcesSnapshot)
                && HasAnyValidStack(RewardResourcesSnapshot);
        }

        private static bool HasAnyValidStack(List<TradeResourceStack> list)
        {
            if (list == null || list.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsValid())
                {
                    return true;
                }
            }

            return false;
        }

        private static List<TradeResourceStack> CloneResourceList(List<TradeResourceStack> source)
        {
            if (source == null || source.Count == 0)
            {
                return new List<TradeResourceStack>();
            }

            List<TradeResourceStack> cloned = new List<TradeResourceStack>(source.Count);

            for (int i = 0; i < source.Count; i++)
            {
                cloned.Add(source[i]);
            }

            return cloned;
        }

        public override string ToString()
        {
            return $"TaskId: {TaskId}, OfferId: {OfferId}, RouteId: {RouteId}, Progress: {GetProgressPercent():0.##}%, Status: {Status}, Blocked: {IsBlocked}, BlockReason: {BlockReason}";
        }
    }
}