using Sim.Resources;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Storage : MonoBehaviour, IStorage
{
    [SerializeField] public List<FixedResourceSlot> _slots = new();
    [SerializeField] private bool _enableTtl = true;
    [SerializeField] private float _defaultTtlSeconds = 300f;
    public Area parentArea;
    /// <summary>时间源（默认为 Time.unscaledTime）。可在测试/模拟时替换。</summary>
    public Func<float> NowProvider { get; set; } = () => Time.unscaledTime;

    public IReadOnlyList<FixedResourceSlot> Slots => _slots;

    private int _nextTicket = 1;

    private struct GoodsReserve
    {
        public ResourceId Id;
        public int Amount;
        public float ExpireAt;
    }

    private struct CapReserve
    {
        public ResourceId Id;
        public int Amount;
        public float ExpireAt;
    }

    private readonly Dictionary<int, GoodsReserve> _goodsRes = new();
    private readonly Dictionary<int, CapReserve> _capRes = new();

    private readonly Dictionary<ResourceId, int> _lockedGoods = new();
    private readonly Dictionary<ResourceId, int> _lockedCap = new();

    private readonly List<int> _tmpKeys = new();

    public event Action<ResourceId, int> OnTotalChanged;

    /// <summary>
    /// 启用或关闭 TTL 机制。
    /// </summary>
    public void SetTtlEnabled(bool enabled) => _enableTtl = enabled;

    /// <summary>
    /// 设置默认票据 TTL 秒数。
    /// </summary>
    public void SetDefaultTtlSeconds(float sec) => _defaultTtlSeconds = Mathf.Max(0f, sec);

    private void Start()
    {
        if (TryGetComponent<Warehouse>(out var warehouse))
        {
            warehouse.Area.productionFlowHub.supplyStorages.Add(this);
            warehouse.Area.productionFlowHub.sinkStorages.Add(this);
        }

        parentArea = AreaContext.Instance.GetDebugArea();
    }

    private void OnDisable()
    {
        ClearAllReservations();
    }

    private void OnDestroy()
    {
        ClearAllReservations();
    }

    #region 槽布局辅助

    /// <summary>
    /// 查询当前仓库所有资源总量。
    /// </summary>
    public int GetTotalAmountAll()
    {
        int sum = 0;
        foreach (var slot in _slots)
        {
            if (slot == null)
                continue;

            sum += Mathf.Max(0, slot.Amount);
        }
        return sum;
    }

    /// <summary>
    /// 是否为空仓。
    /// 这里只检查真实库存，不检查预约票据。
    /// </summary>
    public bool IsEmpty()
    {
        return GetTotalAmountAll() <= 0;
    }

    /// <summary>
    /// 清空所有槽位定义，并清理所有预约/锁定。
    /// 注意：这会直接丢弃当前槽中的资源，因此只应在空仓时调用。
    /// </summary>
    public void ClearAllSlots()
    {
        ClearAllReservations();
        _slots.Clear();
    }

    /// <summary>
    /// 仅当空仓时，重建整套槽位布局。
    /// 常用于商队、运输容器、临时仓库这类“阶段化装载”场景。
    /// </summary>
    public bool RebuildSlotsIfEmpty(params (ResourceId id, int cap, int initial)[] defs)
    {
        if (!IsEmpty())
            return false;

        ConfigureSlots(defs);
        return true;
    }

    /// <summary>
    /// 确保存在某种资源的槽位；如果没有则自动补一个。
    /// </summary>
    public void EnsureSlot(ResourceId id, int capacity, int initial = 0)
    {
        if (!IsConcreteResource(id))
            return;

        foreach (var slot in _slots)
        {
            if (slot != null && slot.Id == id)
                return;
        }

        AddOneSlot(id, capacity, initial);
    }

    /// <summary>
    /// 按计划确保槽位存在，但不清空现有布局。
    /// 适合“只补槽，不重建”的场景。
    /// </summary>
    public void EnsureSlots(params (ResourceId id, int cap, int initial)[] defs)
    {
        if (defs == null || defs.Length == 0)
            return;

        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            EnsureSlot(d.id, d.cap, d.initial);
        }
    }

    #endregion

    /// <summary>
    /// 入库提交：根据容量预约票据，把资源放入匹配的具体资源槽。
    /// </summary>
    public int GetResource(int capTicket, int amount)
    {
        SweepExpiredReservations();
        if (!_capRes.TryGetValue(capTicket, out var rec) || amount <= 0)
            return 0;

        int toPut = Mathf.Min(amount, rec.Amount);
        int accepted = AddToAnySlot(rec.Id, toPut);
        if (accepted <= 0)
            return 0;

        int left = rec.Amount - accepted;
        if (left > 0)
        {
            rec.Amount = left;
            _capRes[capTicket] = rec;
        }
        else
        {
            _capRes.Remove(capTicket);
        }

        DecreaseLocked(_lockedCap, rec.Id, accepted);
        return accepted;
    }

    /// <summary>
    /// 出库提交：根据出库预约票据，从对应资源槽取货。
    /// </summary>
    public int OfferResource(int goodsTicket, int maxAmount)
    {
        SweepExpiredReservations();
        if (!_goodsRes.TryGetValue(goodsTicket, out var rec) || maxAmount <= 0)
            return 0;

        int move = Mathf.Min(maxAmount, rec.Amount);
        int removed = RemoveFromAnySlot(rec.Id, move);
        if (removed <= 0)
            return 0;

        int left = rec.Amount - removed;
        if (left > 0)
        {
            rec.Amount = left;
            _goodsRes[goodsTicket] = rec;
        }
        else
        {
            _goodsRes.Remove(goodsTicket);
        }

        DecreaseLocked(_lockedGoods, rec.Id, removed);
        return removed;
    }

    /// <summary>
    /// 入库预留。
    /// 语义：id 必须是具体资源类型。
    /// </summary>
    public bool TryReserveCapacity(ResourceId id, int amount, out int capTicket, float ttlSec = -1f)
    {
        SweepExpiredReservations();
        capTicket = 0;

        if (!IsConcreteResource(id) || amount <= 0)
            return false;
        if (!CanAccept(id))
            return false;
        if (GetFreeCapacity(id) < amount)
            return false;

        capTicket = NextTicket();
        _capRes[capTicket] = new CapReserve
        {
            Id = id,
            Amount = amount,
            ExpireAt = BuildExpireAt(ttlSec)
        };
        IncreaseLocked(_lockedCap, id, amount);
        return true;
    }

    /// <summary>
    /// 出库预留。
    /// </summary>
    public bool TryReserveResource(ResourceId id, int amount, out int goodsTicket, float ttlSec = -1f)
    {
        SweepExpiredReservations();
        goodsTicket = 0;

        if (!IsConcreteResource(id) || amount <= 0)
            return false;
        if (GetAvailable(id) < amount)
            return false;

        goodsTicket = NextTicket();
        _goodsRes[goodsTicket] = new GoodsReserve
        {
            Id = id,
            Amount = amount,
            ExpireAt = BuildExpireAt(ttlSec)
        };
        IncreaseLocked(_lockedGoods, id, amount);
        return true;
    }

    /// <summary>
    /// 取消一个出库预约票据。
    /// </summary>
    public void CancelGoodsReserve(int goodsTicket)
    {
        SweepExpiredReservations();
        if (_goodsRes.Remove(goodsTicket, out var rec))
        {
            DecreaseLocked(_lockedGoods, rec.Id, rec.Amount);
        }
    }

    /// <summary>
    /// 取消一个入库容量预约票据。
    /// </summary>
    public void CancelCapacityReserve(int capTicket)
    {
        SweepExpiredReservations();
        if (_capRes.Remove(capTicket, out var rec))
        {
            DecreaseLocked(_lockedCap, rec.Id, rec.Amount);
        }
    }

    #region 查询

    /// <summary>
    /// 当前仓是否存在可接收该资源的槽位。
    /// </summary>
    public bool CanAccept(ResourceId id)
    {
        SweepExpiredReservations();
        if (!IsConcreteResource(id))
            return false;

        foreach (var slot in _slots)
        {
            if (SlotMatches(slot.Id, id))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 查询当前可出库数量（总量 - 已锁定出库量）。
    /// </summary>
    public int GetAvailable(ResourceId id)
    {
        SweepExpiredReservations();
        if (!IsConcreteResource(id))
            return 0;

        int total = GetTotalAmount(id);
        _lockedGoods.TryGetValue(id, out int locked);
        return Mathf.Max(0, total - locked);
    }

    /// <summary>
    /// 查询当前可入库容量（总容量 - 当前库存 - 已锁定容量）。
    /// </summary>
    public int GetFreeCapacity(ResourceId id)
    {
        SweepExpiredReservations();
        if (!IsConcreteResource(id))
            return 0;

        int totalCapacity = 0;
        int totalAmount = 0;

        foreach (var slot in _slots)
        {
            if (!SlotMatches(slot.Id, id))
                continue;

            totalCapacity += Mathf.Max(0, slot.Capacity);
            totalAmount += Mathf.Clamp(slot.Amount, 0, slot.Capacity);
        }

        _lockedCap.TryGetValue(id, out int lockedThis);
        return Mathf.Max(0, totalCapacity - totalAmount - lockedThis);
    }

    #endregion

    #region 续约

    /// <summary>
    /// 续约一个出库预约票据。
    /// </summary>
    public bool RenewGoods(int goodsTicket, float ttlSec = -1f)
    {
        if (!_enableTtl)
            return false;
        if (!_goodsRes.TryGetValue(goodsTicket, out var rec))
            return false;

        rec.ExpireAt = BuildExpireAt(ttlSec);
        _goodsRes[goodsTicket] = rec;
        return true;
    }

    /// <summary>
    /// 续约一个入库容量预约票据。
    /// </summary>
    public bool RenewCapacity(int capTicket, float ttlSec = -1f)
    {
        if (!_enableTtl)
            return false;
        if (!_capRes.TryGetValue(capTicket, out var rec))
            return false;

        rec.ExpireAt = BuildExpireAt(ttlSec);
        _capRes[capTicket] = rec;
        return true;
    }

    #endregion

    #region 槽操作

    /// <summary>
    /// 按匹配的具体资源槽入库。
    /// </summary>
    public int AddToAnySlot(ResourceId id, int amount)
    {
        if (!IsConcreteResource(id) || amount <= 0)
            return 0;

        int left = amount;
        int before = GetTotalAmount(id);

        left = AddToMatchingSlots(id, left);

        int accepted = amount - left;
        NotifyTotalChangedIfNeeded(id, before);
        return accepted;
    }

    /// <summary>
    /// 从匹配资源槽中移除资源。
    /// </summary>
    public int RemoveFromAnySlot(ResourceId id, int amount)
    {
        if (!IsConcreteResource(id) || amount <= 0)
            return 0;

        int left = amount;
        int before = GetTotalAmount(id);

        foreach (var slot in _slots)
        {
            if (slot.Id != id)
                continue;
            if (left <= 0)
                break;

            left -= slot.Remove(left);
        }

        int removed = amount - left;
        NotifyTotalChangedIfNeeded(id, before);
        return removed;
    }

    /// <summary>
    /// 增加一个槽位定义。
    /// </summary>
    public void AddOneSlot(ResourceId id, int capacity, int amount)
    {
        int safeCapacity = Mathf.Max(0, capacity);
        int safeAmount = Mathf.Clamp(amount, 0, safeCapacity);
        _slots.Add(new FixedResourceSlot(id, safeCapacity, safeAmount));
    }

    /// <summary>
    /// 查询某个具体资源的当前总库存。
    /// </summary>
    public int GetTotalAmount(ResourceId id)
    {
        if (!IsConcreteResource(id))
            return 0;

        int sum = 0;
        foreach (var slot in _slots)
        {
            if (slot.Id == id)
                sum += slot.Amount;
        }
        return sum;
    }

    /// <summary>
    /// 重新配置槽位定义。
    /// 注意：会先清空所有未完成预约与锁定量，再重建槽位。
    /// </summary>
    public void ConfigureSlots(params (ResourceId id, int cap, int initial)[] defs)
    {
        ClearAllReservations();
        _slots.Clear();

        if (defs == null || defs.Length == 0)
            return;

        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            AddOneSlot(d.id, d.cap, d.initial);
        }
    }

    #endregion

    #region TTL

    private void SweepExpiredReservations()
    {
        if (!_enableTtl)
            return;

        float now = NowProvider();

        if (_goodsRes.Count > 0)
        {
            _tmpKeys.Clear();
            foreach (var kv in _goodsRes)
            {
                if (kv.Value.ExpireAt <= now)
                    _tmpKeys.Add(kv.Key);
            }

            for (int i = 0; i < _tmpKeys.Count; i++)
            {
                int key = _tmpKeys[i];
                if (_goodsRes.Remove(key, out var rec))
                {
                    DecreaseLocked(_lockedGoods, rec.Id, rec.Amount);
                }
            }
        }

        if (_capRes.Count > 0)
        {
            _tmpKeys.Clear();
            foreach (var kv in _capRes)
            {
                if (kv.Value.ExpireAt <= now)
                    _tmpKeys.Add(kv.Key);
            }

            for (int i = 0; i < _tmpKeys.Count; i++)
            {
                int key = _tmpKeys[i];
                if (_capRes.Remove(key, out var rec))
                {
                    DecreaseLocked(_lockedCap, rec.Id, rec.Amount);
                }
            }
        }
    }

    #endregion

    #region 内部工具

    private void ClearAllReservations()
    {
        _goodsRes.Clear();
        _capRes.Clear();
        _lockedGoods.Clear();
        _lockedCap.Clear();
        _tmpKeys.Clear();
    }

    private static bool IsConcreteResource(ResourceId id)
    {
        return id != ResourceId.None;
    }

    private static bool SlotMatches(ResourceId slotId, ResourceId requestedId)
    {
        return slotId == requestedId;
    }

    private int AddToMatchingSlots(ResourceId id, int left)
    {
        foreach (var slot in _slots)
        {
            if (left <= 0)
                break;
            if (!SlotMatches(slot.Id, id))
                continue;

            left -= slot.Add(left);
        }
        return left;
    }

    private void NotifyTotalChangedIfNeeded(ResourceId id, int before)
    {
        int after = GetTotalAmount(id);
        if (after != before)
        {
            OnTotalChanged?.Invoke(id, after);
        }
    }

    private float BuildExpireAt(float ttlSec)
    {
        if (!_enableTtl)
            return float.PositiveInfinity;

        float finalTtl = ttlSec <= 0f ? _defaultTtlSeconds : ttlSec;
        if (finalTtl <= 0f)
            return float.PositiveInfinity;

        return NowProvider() + finalTtl;
    }

    private int NextTicket()
    {
        if (_nextTicket == int.MaxValue)
        {
            _nextTicket = 1;
        }
        return _nextTicket++;
    }

    private static void IncreaseLocked(Dictionary<ResourceId, int> map, ResourceId id, int amount)
    {
        if (amount <= 0)
            return;

        map[id] = (map.TryGetValue(id, out var current) ? current : 0) + amount;
    }

    private static void DecreaseLocked(Dictionary<ResourceId, int> map, ResourceId id, int amount)
    {
        if (amount <= 0)
            return;

        if (!map.TryGetValue(id, out var current))
            return;

        int next = current - amount;
        if (next > 0)
            map[id] = next;
        else
            map.Remove(id);
    }

    #endregion
}


