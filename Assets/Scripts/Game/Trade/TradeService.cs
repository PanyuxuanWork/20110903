/***************************************************************************
// File       : TradeService.cs
// Author     : Panyuxuan
// Created    : 2026/03/12
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/
using System;
using System.Collections.Generic;

namespace Game.Trade
{
    /// <summary>
    /// 贸易后台服务
    ///
    /// 当前职责：
    /// - 登记一笔贸易，生成 taskId
    /// - 保存 TradeCaravanTask 运行时记录
    /// - 维护贸易状态流转
    /// - 保存 / 清理 PendingDelivery
    ///
    /// 不负责：
    /// - 扣库存
    /// - 检查路线阻断
    /// - 发起搬运
    /// - 执行交换
    /// - 实际入库
    /// </summary>
    public class TradeService
    {
        private readonly TradeRuntimeState _runtimeState;
        private int _nextTaskId;

        public TradeService(TradeRuntimeState runtimeState, int startTaskId = 1)
        {
            _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
            _nextTaskId = startTaskId > 0 ? startTaskId : 1;
        }

        /// <summary>
        /// 登记一笔贸易。
        /// 这里只做“入系统”，不做扣库存。
        /// </summary>
        public TradeStartResult StartTrade(TradeDataContext ctx)
        {
            if (ctx == null || !ctx.IsValid())
            {
                return TradeStartResult.CreateFailed("Trade context is invalid.");
            }

            int taskId = GenerateTaskId();

            TradeCaravanTask task = new TradeCaravanTask(
                ctx.OfferDef.OfferName,
                taskId: taskId,
                offerId: ctx.OfferDef.OfferId,
                routeId: ctx.RouteDef.RouteId,
                transportMode: ctx.TransportMode,
                paidResourcesSnapshot: ctx.OfferDef.CostResources,
                rewardResourcesSnapshot: ctx.OfferDef.RewardResources
            );

            task.Status = TradeCaravanTaskStatus.WaitingResource;
            task.IsBlocked = false;
            task.BlockReason = TradeBlockReason.None;
            task.TotalPathLength = 0f;
            task.TraveledPathLength = 0f;

            _runtimeState.AddTask(task);

            return TradeStartResult.CreateSuccess(taskId);
        }

        public TradeCaravanTask FindTask(int taskId)
        {
            return _runtimeState.FindTask(taskId);
        }

        public PendingDelivery FindPendingDelivery(int taskId)
        {
            return _runtimeState.FindPendingDelivery(taskId);
        }

        public bool MarkWaitingResource(int taskId)
        {
            TradeCaravanTask task = _runtimeState.FindTask(taskId);
            if (task == null)
            {
                return false;
            }

            task.Status = TradeCaravanTaskStatus.WaitingResource;
            task.IsBlocked = false;
            task.BlockReason = TradeBlockReason.None;
            return true;
        }

        public bool MarkTravelling(int taskId, float totalPathLength = 0f)
        {
            TradeCaravanTask task = _runtimeState.FindTask(taskId);
            if (task == null)
            {
                return false;
            }

            task.Status = TradeCaravanTaskStatus.Travelling;
            task.IsBlocked = false;
            task.BlockReason = TradeBlockReason.None;

            if (totalPathLength > 0f)
            {
                task.TotalPathLength = totalPathLength;
            }

            if (task.TraveledPathLength < 0f)
            {
                task.TraveledPathLength = 0f;
            }

            return true;
        }

        public bool UpdateProgress(int taskId, float traveledPathLength, float totalPathLength = 0f)
        {
            TradeCaravanTask task = _runtimeState.FindTask(taskId);
            if (task == null)
            {
                return false;
            }

            if (totalPathLength > 0f)
            {
                task.TotalPathLength = totalPathLength;
            }

            if (traveledPathLength < 0f)
            {
                traveledPathLength = 0f;
            }

            if (task.TotalPathLength > 0f && traveledPathLength > task.TotalPathLength)
            {
                traveledPathLength = task.TotalPathLength;
            }

            task.TraveledPathLength = traveledPathLength;
            return true;
        }

        public bool SetBlocked(int taskId, TradeBlockReason reason)
        {
            TradeCaravanTask task = _runtimeState.FindTask(taskId);
            if (task == null)
            {
                return false;
            }

            task.SetBlocked(reason);
            return true;
        }

        public bool ClearBlocked(int taskId, TradeCaravanTaskStatus resumeStatus = TradeCaravanTaskStatus.Travelling)
        {
            TradeCaravanTask task = _runtimeState.FindTask(taskId);
            if (task == null)
            {
                return false;
            }

            task.IsBlocked = false;
            task.BlockReason = TradeBlockReason.None;
            task.Status = resumeStatus;
            return true;
        }

        public bool SetWaitingDelivery(int taskId, List<TradeResourceStack> remainingResources)
        {
            TradeCaravanTask task = _runtimeState.FindTask(taskId);
            if (task == null)
            {
                return false;
            }

            if (!HasAnyValidStack(remainingResources))
            {
                return CompleteTrade(taskId);
            }

            PendingDelivery pendingDelivery = _runtimeState.FindPendingDelivery(taskId);
            if (pendingDelivery == null)
            {
                pendingDelivery = new PendingDelivery(taskId, remainingResources);
                _runtimeState.AddPendingDelivery(pendingDelivery);
            }
            else
            {
                pendingDelivery.RemainingResources = CloneResourceList(remainingResources);
            }

            task.SetWaitingDelivery();
            return true;
        }

        public bool ClearPendingDelivery(int taskId)
        {
            return _runtimeState.RemovePendingDelivery(taskId);
        }

        public bool CompleteTrade(int taskId)
        {
            TradeCaravanTask task = _runtimeState.FindTask(taskId);
            if (task == null)
            {
                return false;
            }

            _runtimeState.RemovePendingDelivery(taskId);
            task.SetDelivered();
            return true;
        }

        public bool CancelTrade(int taskId)
        {
            TradeCaravanTask task = _runtimeState.FindTask(taskId);
            if (task == null)
            {
                return false;
            }

            _runtimeState.RemovePendingDelivery(taskId);
            task.SetCancelled();
            return true;
        }

        private int GenerateTaskId()
        {
            int taskId = _nextTaskId;
            _nextTaskId++;
            return taskId;
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
    }
}
