/***************************************************************************
// File       : TradeSlot.cs
// Author     : Panyuxuan
// Created    : 2026/03/14
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

namespace Game.Trade
{
    /// <summary>
    /// 贸易槽位类型
    /// </summary>
    public enum TradeSlotType
    {
        None = 0,

        /// <summary>
        /// 想要 / 收购槽
        /// </summary>
        Wanted = 1,

        /// <summary>
        /// 出售槽
        /// </summary>
        Sell = 2
    }

    /// <summary>
    /// 贸易槽位
    /// 
    /// 当前版本职责：
    /// 1. 描述一个槽位里是什么资源
    /// 2. 描述这个槽位当前还能交易多少
    /// 3. 描述这个槽位是否启用
    /// 
    /// 当前版本不负责：
    /// - UI 图标/高亮
    /// - 价格计算
    /// - 真实库存逻辑
    /// </summary>
    [System.Serializable]
    public class TradeSlot
    {
        /// <summary>
        /// 槽位唯一ID
        /// </summary>
        public int SlotId;

        /// <summary>
        /// 槽位类型：想要 / 出售
        /// </summary>
        public TradeSlotType SlotType = TradeSlotType.None;

        /// <summary>
        /// 槽位中的资源数据
        /// </summary>
        public TradeResourceStack ResourceStack;

        /// <summary>
        /// 当前剩余额度
        /// 
        /// 例如：
        /// - Wanted 槽：还能收多少
        /// - Sell 槽：还能卖多少
        /// </summary>
        public int RemainingAmount;

        /// <summary>
        /// 当前是否启用
        /// </summary>
        public bool IsEnabled = true;

        public TradeSlot()
        {
        }

        public TradeSlot(
            int slotId,
            TradeSlotType slotType,
            TradeResourceStack resourceStack,
            int remainingAmount,
            bool isEnabled = true)
        {
            SlotId = slotId;
            SlotType = slotType;
            ResourceStack = resourceStack;
            RemainingAmount = remainingAmount;
            IsEnabled = isEnabled;
        }

        /// <summary>
        /// 当前槽位是否有效
        /// </summary>
        public bool IsValid()
        {
            return SlotId > 0
                && SlotType != TradeSlotType.None
                && ResourceStack.IsValid()
                && RemainingAmount >= 0;
        }

        /// <summary>
        /// 是否可以交易
        /// </summary>
        public bool CanTrade()
        {
            return IsEnabled && IsValid() && RemainingAmount > 0;
        }

        /// <summary>
        /// 当前槽位里的资源编码
        /// </summary>
        public int GetItemCode()
        {
            return ResourceStack.ItemCode;
        }

        /// <summary>
        /// 当前槽位单次展示/定义数量
        /// </summary>
        public int GetStackAmount()
        {
            return ResourceStack.Amount;
        }

        /// <summary>
        /// 扣减额度
        /// 
        /// 注意：
        /// 这里扣的是 RemainingAmount，不改 ResourceStack.Amount
        /// 因为 ResourceStack.Amount 表示这格槽的资源定义量
        /// </summary>
        public bool ConsumeAmount(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            if (RemainingAmount < amount)
            {
                return false;
            }

            RemainingAmount -= amount;
            return true;
        }

        /// <summary>
        /// 恢复额度
        /// </summary>
        public void RestoreAmount(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            RemainingAmount += amount;
        }

        /// <summary>
        /// 设置启用状态
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            IsEnabled = enabled;
        }

        public override string ToString()
        {
            return $"SlotId={SlotId}, Type={SlotType}, ItemCode={ResourceStack.ItemCode}, StackAmount={ResourceStack.Amount}, Remaining={RemainingAmount}, Enabled={IsEnabled}";
        }
    }
}