using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sim.Resources
{

    public enum ResourceId : byte
    {
        None = 0, 
        木头 = 1, 
        石头 = 2,
        食物 = 3, 
        水 = 4
    }

    public static class EnumToString
    {
        public static string ConvertResourceID(ResourceId id)
        {
            switch (id)
            {
                case ResourceId.None: return null;
                case ResourceId.木头: return STATICSTRING.C_WOOD; 
                case ResourceId.石头: return STATICSTRING.C_STONE;
                case ResourceId.水: return STATICSTRING.C_WATER;
                case ResourceId.食物: return STATICSTRING.C_FOOD;
            }
            return null;
        }
    }

    public interface IResourceSlot
    {
        ResourceId Id { get; }
        int Capacity { get; }
        int Amount { get; }
        int Add(int amount);      // 向该槽加，返回实际
        int Remove(int amount);   // 从该槽减，返回实际
    }

    [Serializable]
    public class FixedResourceSlot : IResourceSlot
    {
        [SerializeField] private ResourceId _id;
        [SerializeField] private int _capacity;
        [SerializeField] private int _amount;
        
        public ResourceId Id
        {
            get => _id;
            set => _id = value;
        }
        public int Capacity => _capacity;
        public int Amount => _amount;

        public FixedResourceSlot(ResourceId id, int capacity, int initial = 0)
        {
            _id = id; _capacity = Mathf.Max(0, capacity); _amount = Mathf.Clamp(initial, 0, _capacity);
        }
        public int Add(int amount)
        {
            if (amount <= 0) return 0;
            int can = Mathf.Min(amount, Capacity - Amount);
            _amount += can;
            return can;
        }
        public int Remove(int amount)
        {
            if (amount <= 0) return 0;
            int can = Mathf.Min(amount, _amount);
            _amount -= can;
            return can;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="c">上限</param>
        /// <param name="a">数量</param>
        public void Reset(ResourceId id, int c, int a)
        {
            _id = id;
            _capacity = c;
            _amount = a;
        }
    }

    // 4) 仓库接口（由多个槽组成；提供预约与转移相关能力）
    public interface IStorage
    {
        // 查询
        bool CanAccept(ResourceId id);              // 是否愿意接收该资源（过滤/白名单）
        int GetAvailable(ResourceId id);           // 可用量 = 实际库存 - 已被出库预约锁定
        int GetFreeCapacity(ResourceId id);        // 剩余容量 = 上限 - 当前量 - 已被容量预约锁定

        // —— 出库（给出）预约 —— //
        bool TryReserveResource(ResourceId id, int amount, out int goodsTicket,float TTL);
        void CancelGoodsReserve(int goodsTicket);
        // 提交出库：按预约扣减本仓库存，返回实际扣减量（<=预约量）
        int OfferResource(int goodsTicket, int maxAmount);

        // —— 入库（接受）预约 —— //
        bool TryReserveCapacity(ResourceId id, int amount, out int capTicket,float TTL);
        void CancelCapacityReserve(int capTicket);
        // 提交入库：将指定数量写入本仓（<=预约量），返回实际写入量
        int GetResource(int capTicket, int amount);

        // 槽位只用于本地生产/消费或 UI 展示（跨仓请走预约-提交）
        IReadOnlyList<FixedResourceSlot> Slots { get; }

        // 可选：底层原语，便于回滚/编辑器工具（跨仓正式流程仍走预约-提交）
        int AddToAnySlot(ResourceId id, int amount);
        int RemoveFromAnySlot(ResourceId id, int amount);
    }

}
