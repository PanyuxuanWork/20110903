/***************************************************************************
// File       : TradeRuntimeState.cs
// Author     : Panyuxuan
// Created    : 2026/03/12
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;

namespace Game.Trade
{
    /// <summary>
    /// 贸易系统运行时状态容器
    /// </summary>
    [System.Serializable]
    public class TradeRuntimeState
    {
        /// <summary>
        /// 正在运行中的商队任务
        /// </summary>
        public List<TradeCaravanTask> ActiveTasks = new List<TradeCaravanTask>();

        /// <summary>
        /// 待交付资源列表
        /// </summary>
        public List<PendingDelivery> PendingDeliveries = new List<PendingDelivery>();

        public TradeRuntimeState()
        {
        }

        /// <summary>
        /// 按任务ID查找商队任务
        /// </summary>
        public TradeCaravanTask FindTask(int taskId)
        {
            for (int i = 0; i < ActiveTasks.Count; i++)
            {
                if (ActiveTasks[i] != null && ActiveTasks[i].TaskId == taskId)
                {
                    return ActiveTasks[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 按任务ID查找待交付
        /// </summary>
        public PendingDelivery FindPendingDelivery(int taskId)
        {
            for (int i = 0; i < PendingDeliveries.Count; i++)
            {
                if (PendingDeliveries[i] != null && PendingDeliveries[i].TaskId == taskId)
                {
                    return PendingDeliveries[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 添加任务
        /// </summary>
        public void AddTask(TradeCaravanTask task)
        {
            if (task == null)
            {
                return;
            }

            ActiveTasks.Add(task);
        }

        /// <summary>
        /// 添加待交付
        /// </summary>
        public void AddPendingDelivery(PendingDelivery pendingDelivery)
        {
            if (pendingDelivery == null)
            {
                return;
            }

            PendingDeliveries.Add(pendingDelivery);
        }

        /// <summary>
        /// 移除待交付
        /// </summary>
        public bool RemovePendingDelivery(int taskId)
        {
            for (int i = 0; i < PendingDeliveries.Count; i++)
            {
                if (PendingDeliveries[i] != null && PendingDeliveries[i].TaskId == taskId)
                {
                    PendingDeliveries.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }
    }
}