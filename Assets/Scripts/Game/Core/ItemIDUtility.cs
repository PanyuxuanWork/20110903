/***************************************************************************
// File       : ItemIDUtility.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sim.Resources;

namespace Game.Items
{
    /// <summary>
    /// ItemID 解析工具类。
    /// 统一处理位解析，避免其他系统到处写位运算。
    /// </summary>
    public static class ItemIDUtility
    {
        /// <summary>
        /// 获取高8位
        /// </summary>
        public static int GetHigh8Bits(int itemCode)
        {
            return (itemCode >> 24) & 0xFF;
        }

        /// <summary>
        /// 获取第二组8位
        /// </summary>
        public static int GetSecond8Bits(int itemCode)
        {
            return (itemCode >> 16) & 0xFF;
        }

        /// <summary>
        /// 获取第三组8位
        /// </summary>
        public static int GetThird8Bits(int itemCode)
        {
            return (itemCode >> 8) & 0xFF;
        }

        /// <summary>
        /// 获取低8位
        /// </summary>
        public static int GetLow8Bits(int itemCode)
        {
            return itemCode & 0xFF;
        }

        /// <summary>
        /// 是否包含某种类型
        /// 
        /// 
        /// groupIndex:
        /// 0 = 高8位
        /// 1 = 第二组8位
        /// 2 = 第三组8位
        /// 3 = 低8位
        /// </summary>
        public static bool ContainsType(int itemCode, int groupIndex, int typeValue)
        {
            switch (groupIndex)
            {
                case 0:
                    return GetHigh8Bits(itemCode) == typeValue;
                case 1:
                    return GetSecond8Bits(itemCode) == typeValue;
                case 2:
                    return GetThird8Bits(itemCode) == typeValue;
                case 3:
                    return GetLow8Bits(itemCode) == typeValue;
                default:
                    return false;
            }
        }

        public static bool TryGetResourceId(int itemCode,out ResourceId id)
        {
            // 资源ID 由第二组8位和第三组8位组成
            if (GetSecond8Bits(itemCode) == ItemID.SecondType_Resource)
            {
                id = (ResourceId)GetThird8Bits(itemCode);
                return true;
            }
            id = ResourceId.None;
            return false;
        }
    }
}