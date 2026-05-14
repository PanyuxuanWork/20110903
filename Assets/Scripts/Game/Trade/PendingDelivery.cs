/***************************************************************************
// File       : PendingDelivery.cs
// Author     : Panyuxuan
// Created    : 2026/03/12
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/
using System.Collections.Generic;

namespace Game.Trade
{
    /// <summary>
    /// 待交付资源
    /// 
    /// 商队已完成运输，但资源未能全部放入仓库时，
    /// 剩余部分挂在这里，等待后续再次尝试交付。
    /// </summary>
    [System.Serializable]
    public class PendingDelivery
    {
        /// <summary>
        /// 对应的商队任务ID
        /// </summary>
        public int TaskId;

        /// <summary>
        /// 未能交付成功的剩余资源
        /// </summary>
        public List<TradeResourceStack> RemainingResources = new List<TradeResourceStack>();

        public PendingDelivery()
        {
        }

        public PendingDelivery(int taskId, List<TradeResourceStack> remainingResources)
        {
            TaskId = taskId;
            RemainingResources = CloneResourceList(remainingResources);
        }

        /// <summary>
        /// 是否有效
        /// </summary>
        public bool IsValid()
        {
            return TaskId > 0 && HasAnyValidStack(RemainingResources);
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
            return $"TaskId: {TaskId}, RemainingCount: {RemainingResources.Count}";
        }
    }
}