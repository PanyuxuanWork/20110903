/***************************************************************************
// File       : TradeUnit.cs
// Author     : Panyuxuan
// Created    : 2026/03/14
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;

namespace Game.Trade
{
    /// <summary>
    /// 贸易单位
    /// 
    /// 当前版本将它定义为：
    /// 一个持有贸易槽位的中立贸易单位 / 城邦贸易面板数据容器
    /// 
    /// 它只负责：
    /// - 持有 WantedSlots
    /// - 持有 SellSlots
    /// - 提供槽位查询
    /// - 提供可用槽位列表
    /// 
    /// 它不负责：
    /// - 真实库存模拟
    /// - 价格运算
    /// - 商队推进
    /// - 玩家仓库结算
    /// </summary>
    [System.Serializable]
    public class TradeUnit
    {
        /// <summary>
        /// 贸易单位唯一ID
        /// </summary>
        public int UnitId;

        /// <summary>
        /// 贸易单位名称
        /// </summary>
        public string UnitName;

        /// <summary>
        /// 想要 / 收购槽
        /// </summary>
        public List<TradeSlot> WantedSlots = new List<TradeSlot>();

        /// <summary>
        /// 出售槽
        /// </summary>
        public List<TradeSlot> SellSlots = new List<TradeSlot>();

        public TradeUnit()
        {
        }

        public TradeUnit(int unitId, string unitName)
        {
            UnitId = unitId;
            UnitName = unitName;
        }

        public TradeUnit(
            int unitId,
            string unitName,
            List<TradeSlot> wantedSlots,
            List<TradeSlot> sellSlots)
        {
            UnitId = unitId;
            UnitName = unitName;
            WantedSlots = wantedSlots ?? new List<TradeSlot>();
            SellSlots = sellSlots ?? new List<TradeSlot>();
        }

        /// <summary>
        /// 是否是有效贸易单位
        /// </summary>
        public bool IsValid()
        {
            return UnitId > 0 && !string.IsNullOrEmpty(UnitName);
        }

        /// <summary>
        /// 添加一个 Wanted 槽
        /// </summary>
        public void AddWantedSlot(TradeSlot slot)
        {
            if (slot == null)
            {
                return;
            }

            if (slot.SlotType != TradeSlotType.Wanted)
            {
                return;
            }

            WantedSlots.Add(slot);
        }

        /// <summary>
        /// 添加一个 Sell 槽
        /// </summary>
        public void AddSellSlot(TradeSlot slot)
        {
            if (slot == null)
            {
                return;
            }

            if (slot.SlotType != TradeSlotType.Sell)
            {
                return;
            }

            SellSlots.Add(slot);
        }

        /// <summary>
        /// 按槽位ID查找 Wanted 槽
        /// </summary>
        public TradeSlot FindWantedSlot(int slotId)
        {
            return FindSlotInternal(WantedSlots, slotId);
        }

        /// <summary>
        /// 按槽位ID查找 Sell 槽
        /// </summary>
        public TradeSlot FindSellSlot(int slotId)
        {
            return FindSlotInternal(SellSlots, slotId);
        }

        /// <summary>
        /// 按槽位ID查找任意槽
        /// 优先找 Wanted，再找 Sell
        /// </summary>
        public TradeSlot FindSlot(int slotId)
        {
            TradeSlot wanted = FindWantedSlot(slotId);
            if (wanted != null)
            {
                return wanted;
            }

            return FindSellSlot(slotId);
        }

        /// <summary>
        /// 获取所有可交易的 Wanted 槽
        /// </summary>
        public List<TradeSlot> GetAvailableWantedSlots()
        {
            return GetAvailableSlotsInternal(WantedSlots);
        }

        /// <summary>
        /// 获取所有可交易的 Sell 槽
        /// </summary>
        public List<TradeSlot> GetAvailableSellSlots()
        {
            return GetAvailableSlotsInternal(SellSlots);
        }

        /// <summary>
        /// 是否存在至少一个可交易槽
        /// </summary>
        public bool HasAnyAvailableSlot()
        {
            return GetAvailableWantedSlots().Count > 0 || GetAvailableSellSlots().Count > 0;
        }

        private static TradeSlot FindSlotInternal(List<TradeSlot> slots, int slotId)
        {
            if (slots == null || slotId <= 0)
            {
                return null;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                TradeSlot slot = slots[i];
                if (slot != null && slot.SlotId == slotId)
                {
                    return slot;
                }
            }

            return null;
        }

        private static List<TradeSlot> GetAvailableSlotsInternal(List<TradeSlot> slots)
        {
            List<TradeSlot> result = new List<TradeSlot>();

            if (slots == null || slots.Count == 0)
            {
                return result;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                TradeSlot slot = slots[i];
                if (slot != null && slot.CanTrade())
                {
                    result.Add(slot);
                }
            }

            return result;
        }

        public override string ToString()
        {
            return $"TradeUnit UnitId={UnitId}, UnitName={UnitName}, WantedSlots={WantedSlots.Count}, SellSlots={SellSlots.Count}";
        }
    }
}
