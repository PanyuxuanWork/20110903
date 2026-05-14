/***************************************************************************
// File       : TradeProgressCalculator.cs
// Author     : Panyuxuan
// Created    : 2026/03/12

// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

namespace Game.Trade
{
    /// <summary>
    /// 商队进度推进计算器
    /// 
    /// 负责：
    /// - 根据运输方式与外部修正，计算本次推进多少路径长度
    /// 
    /// 不负责：
    /// - 判断路线是否阻断
    /// - 创建任务
    /// - 交付资源
    /// </summary>
    public class TradeProgressCalculator
    {
        /// <summary>
        /// 基础每次推进长度
        /// 你可以把它理解为“每个贸易更新周期默认走多少格”
        /// </summary>
        public float BaseMovePerStep = 1f;

        /// <summary>
        /// 人力运输速度修正
        /// </summary>
        public float HumanMoveModifier = 1f;

        /// <summary>
        /// 马车运输速度修正
        /// </summary>
        public float CartMoveModifier = 1.5f;

        /// <summary>
        /// 海运速度修正
        /// </summary>
        public float SeaMoveModifier = 2f;

        public TradeProgressCalculator()
        {
        }

        public TradeProgressCalculator(
            float baseMovePerStep,
            float humanMoveModifier = 1f,
            float cartMoveModifier = 1.5f,
            float seaMoveModifier = 2f)
        {
            BaseMovePerStep = baseMovePerStep;
            HumanMoveModifier = humanMoveModifier;
            CartMoveModifier = cartMoveModifier;
            SeaMoveModifier = seaMoveModifier;
        }

        /// <summary>
        /// 计算本次推进长度
        /// 
        /// seasonModifier / externalModifier：
        /// - 默认传 1
        /// - 冬季减速可传 0.8
        /// - 科技加成可传 1.2
        /// </summary>
        public float CalculateDeltaPathLength(
            TradeTransportMode transportMode,
            float seasonModifier = 1f,
            float externalModifier = 1f)
        {
            if (BaseMovePerStep <= 0f)
            {
                return 0f;
            }

            float transportModifier = GetTransportModifier(transportMode);
            float delta = BaseMovePerStep * transportModifier * seasonModifier * externalModifier;

            if (delta < 0f)
            {
                return 0f;
            }

            return delta;
        }

        private float GetTransportModifier(TradeTransportMode mode)
        {
            switch (mode)
            {
                case TradeTransportMode.Human:
                    return HumanMoveModifier;

                case TradeTransportMode.Cart:
                    return CartMoveModifier;

                case TradeTransportMode.Sea:
                    return SeaMoveModifier;

                default:
                    return 0f;
            }
        }
    }
}