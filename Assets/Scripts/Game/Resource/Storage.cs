using Sim.Resources;
using System;
using System.Collections.Generic;
using UnityEngine;

public class Storage : MonoBehaviour, IStorage
{
    [SerializeField] private List<FixedResourceSlot> _slots = new();

    [SerializeField] private List<ResourceId> _acceptWhitelist = new();

    [SerializeField] private bool _enableTtl = true;
    [SerializeField] private float _defaultTtlSeconds = 300f;

    /// <summary>时间源（默认为 Time.unscaledTime）。可在测试/模拟时替换。</summary>
    public Func<float> NowProvider { get; set; } = () => Time.unscaledTime;

    public IReadOnlyList<IResourceSlot> Slots => _slots as IReadOnlyList<IResourceSlot>;

    private int _nextTicket = 1;

    private struct GoodsReserve { public ResourceId Id; public int Amount; public float ExpireAt; }
    private struct CapReserve { public ResourceId Id; public int Amount; public float ExpireAt; }

    // ticket -> record
    private readonly Dictionary<int, GoodsReserve> _goodsRes = new();
    private readonly Dictionary<int, CapReserve> _capRes = new();

    // 统计映射（便于 O(1) 查询被锁定的量）
    private readonly Dictionary<ResourceId, int> _lockedGoods = new();
    private readonly Dictionary<ResourceId, int> _lockedCap = new();

    // 事件（可选）
    public event Action<ResourceId, int> OnTotalChanged; // 资源总量变化

    // ―― 公共参数调节 ―― //
    public void SetTtlEnabled(bool enabled) => _enableTtl = enabled;
    public void SetDefaultTtlSeconds(float sec) => _defaultTtlSeconds = Mathf.Max(0f, sec);

    private void Start()
    {
        if (TryGetComponent<Warehouse>(out var warehouse))
        {
            warehouse.Area.producerContext.supplyStorages.Add(this);
            warehouse.Area.producerContext.sinkStorages.Add(this);
        }
    }

    /// <summary>
    /// 入库
    /// </summary>
    /// <param StepName="capTicket"></param>
    /// <param StepName="amount"></param>
    /// <returns></returns>
    public int GetResource(int capTicket, int amount)
    {
        SweepExpiredReservations();
        if (!_capRes.TryGetValue(capTicket, out var rec) || amount <= 0) return 0;

        int toPut = Mathf.Min(amount, rec.Amount);
        int accepted = AddToAnySlot(rec.Id, toPut);
        if (accepted > 0)
        {
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

            _lockedCap[rec.Id] = Mathf.Max(0, (_lockedCap.TryGetValue(rec.Id, out var v) ? v : 0) - accepted);
            if (_lockedCap[rec.Id] == 0) _lockedCap.Remove(rec.Id);
        }
        return accepted;
    }

    /// <summary>
    /// 出库
    /// </summary>
    /// <param StepName="goodsTicket"></param>
    /// <param StepName="maxAmount"></param>
    /// <returns></returns>
    public int OfferResource(int goodsTicket, int maxAmount)
    {
        SweepExpiredReservations();
        if (!_goodsRes.TryGetValue(goodsTicket, out var rec) || maxAmount <= 0) return 0;

        // 票据可能刚好过期被清理（上面的 SweepExpired 已处理）；此处只需继续逻辑
        int move = Mathf.Min(maxAmount, rec.Amount);
        int removed = RemoveFromAnySlot(rec.Id, move);
        if (removed > 0)
        {
            int left = rec.Amount - removed;
            if (left > 0)
            {
                rec.Amount = left;            // 保留剩余额
                _goodsRes[goodsTicket] = rec; // 过期时间不变
            }
            else
            {
                _goodsRes.Remove(goodsTicket); // 用尽即销毁
            }

            _lockedGoods[rec.Id] = Mathf.Max(0, (_lockedGoods.TryGetValue(rec.Id, out var v) ? v : 0) - removed);
            if (_lockedGoods[rec.Id] == 0) _lockedGoods.Remove(rec.Id);
        }
        return removed;
    }


    /// <summary>
    /// 入库预留
    /// </summary>
    /// <param StepName="id"></param>
    /// <param StepName="amount"></param>
    /// <param StepName="capTicket"></param>
    /// <param StepName="ttlSec"></param>
    /// <returns></returns>
    public bool TryReserveCapacity(ResourceId id, int amount, out int capTicket, float ttlSec=5000)
    {
        SweepExpiredReservations();
        capTicket = 0;
        if (id == ResourceId.None || amount <= 0) return false;
        if (!CanAccept(id)) return false;
        if (GetFreeCapacity(id) < amount) return false;

        capTicket = _nextTicket++;
        float expireAt = (!_enableTtl || ttlSec <= 0f) ? float.PositiveInfinity : (NowProvider() + ttlSec);
        _capRes[capTicket] = new CapReserve { Id = id, Amount = amount, ExpireAt = expireAt };
        _lockedCap[id] = (_lockedCap.TryGetValue(id, out var v) ? v : 0) + amount;
        return true;
    }

    /// <summary>
    /// 出库预留
    /// </summary>
    /// <param StepName="id"></param>
    /// <param StepName="amount"></param>
    /// <param StepName="goodsTicket"></param>
    /// <param StepName="ttlSec"></param>
    /// <returns></returns>
    public bool TryReserveResource(ResourceId id, int amount, out int goodsTicket, float ttlSec = 5000)
    {
        SweepExpiredReservations();
        goodsTicket = 0;
        if (id == ResourceId.None || amount <= 0) return false;
        if (GetAvailable(id) < amount) return false;

        goodsTicket = _nextTicket++;
        float expireAt = (!_enableTtl || ttlSec <= 0f) ? float.PositiveInfinity : (NowProvider() + ttlSec);
        _goodsRes[goodsTicket] = new GoodsReserve { Id = id, Amount = amount, ExpireAt = expireAt };
        _lockedGoods[id] = (_lockedGoods.TryGetValue(id, out var v) ? v : 0) + amount;
        return true;
    }

    public void CancelGoodsReserve(int goodsTicket)
    {
        SweepExpiredReservations();
        if (_goodsRes.Remove(goodsTicket, out var rec))
        {
            _lockedGoods[rec.Id] = Mathf.Max(0, (_lockedGoods.TryGetValue(rec.Id, out var v) ? v : 0) - rec.Amount);
            if (_lockedGoods[rec.Id] == 0) _lockedGoods.Remove(rec.Id);
        }
    }


    public void CancelCapacityReserve(int capTicket)
    {
        SweepExpiredReservations();
        if (_capRes.Remove(capTicket, out var rec))
        {
            _lockedCap[rec.Id] = Mathf.Max(0, (_lockedCap.TryGetValue(rec.Id, out var v) ? v : 0) - rec.Amount);
            if (_lockedCap[rec.Id] == 0) _lockedCap.Remove(rec.Id);
        }
    }



    #region 查询

    // ―― 查询 ―― //
    public bool CanAccept(ResourceId id)
    {
        SweepExpiredReservations();
        if (id == ResourceId.None) return false;
        return _acceptWhitelist == null || _acceptWhitelist.Count == 0 || _acceptWhitelist.Contains(id);
    }

    public int GetAvailable(ResourceId id)
    {
        SweepExpiredReservations();
        int total = GetTotalAmount(id);
        _lockedGoods.TryGetValue(id, out int locked);
        return Mathf.Max(0, total - locked);
    }

    public int GetFreeCapacity(ResourceId id)
    {
        SweepExpiredReservations();

        int capBound = 0, amtBound = 0;   // 已绑定为 id 的槽的容量/存量
        int capNone = 0, amtNone = 0;   // 空槽（Id=None，且接受 id）的容量/存量

        foreach (var s in _slots)
        {
            if (s.Id == id) { capBound += s.Capacity; amtBound += s.Amount; }
            else if (s.Id == ResourceId.None && Accepts(id))
            {
                capNone += s.Capacity; amtNone += s.Amount;
            }
        }

        // 已锁定的容量：当前 id 的锁 & 全部 id 的总锁
        _lockedCap.TryGetValue(id, out int lockedThis);
        int lockedAll = 0;
        foreach (var kv in _lockedCap) lockedAll += kv.Value;

        // 已绑定槽：只减去当前 id 的锁
        int freeBound = capBound - amtBound - lockedThis;

        // 通用槽：要扣掉“所有锁中不属于当前 id 的那一部分”（避免被多 id 重复预约）
        int otherLocks = Math.Max(0, lockedAll - lockedThis);
        int freeNone = capNone - amtNone - otherLocks;

        return Mathf.Max(0, freeBound) + Mathf.Max(0, freeNone);
    }
    private bool Accepts(ResourceId id)
    {
        // 若你已有 SetAcceptWhitelist / CanAccept(id) 之类的函数，直接调用即可
        return true;
    }
    // ―― 出库（给出）预约/提交 ―― //



    // ―― 续约（可选）―― //
    public bool RenewGoods(int goodsTicket, float ttlSec = -1f)
    {
        if (!_enableTtl) return false;
        if (_goodsRes.TryGetValue(goodsTicket, out var rec))
        {
            float add = (ttlSec <= 0f) ? _defaultTtlSeconds : ttlSec;
            rec.ExpireAt = NowProvider() + add;
            _goodsRes[goodsTicket] = rec;
            return true;
        }
        return false;
    }

    public bool RenewCapacity(int capTicket, float ttlSec = -1f)
    {
        if (!_enableTtl) return false;
        if (_capRes.TryGetValue(capTicket, out var rec))
        {
            float add = (ttlSec <= 0f) ? _defaultTtlSeconds : ttlSec;
            rec.ExpireAt = NowProvider() + add;
            _capRes[capTicket] = rec;
            return true;
        }
        return false;
    }

    // ―― 原语：任意槽增减（主要用于 Commit 实现与回滚）―― //
    public int AddToAnySlot(ResourceId id, int amount)
    {
        int left = Mathf.Max(0, amount);
        int before = GetTotalAmount(id);
        foreach (var s in _slots)
        {
            if (s.Id != id) continue;
            if (left <= 0) break;
            int put = s.Add(left);
            left -= put;
        }
        int after = GetTotalAmount(id);
        if (after != before) OnTotalChanged?.Invoke(id, after);
        return amount - left;
    }

    public int RemoveFromAnySlot(ResourceId id, int amount)
    {
        int left = Mathf.Max(0, amount);
        int before = GetTotalAmount(id);
        foreach (var s in _slots)
        {
            if (s.Id != id) continue;
            if (left <= 0) break;
            int take = s.Remove(left);
            left -= take;
        }
        int after = GetTotalAmount(id);
        if (after != before) OnTotalChanged?.Invoke(id, after);
        return amount - left;
    }

    // ―― 小工具 ―― //

    public void AddOneSlot(ResourceId id, int capacity, int amount)
    {
        _slots.Add(new FixedResourceSlot(id, capacity, amount));
    }

    public int GetTotalAmount(ResourceId id)
    {
        int sum = 0; foreach (var s in _slots) if (s.Id == id) sum += s.Amount; return sum;
    }

    /// <summary>
    /// 初始化槽
    /// </summary>
    /// <param StepName="defs">三元组-（资源，最大值，初始值）</param>
    public void ConfigureSlots(params (ResourceId id, int cap, int initial)[] defs)
    {
        _slots.Clear();
        foreach (var d in defs) _slots.Add(new FixedResourceSlot(d.id, d.cap, d.initial));
    }
    /// <summary>
    /// 设置资源白名单
    /// </summary>
    /// <param StepName="ids">资源数组</param>
    public void SetAcceptWhitelist(params ResourceId[] ids)
    {
        _acceptWhitelist.Clear();
        if (ids != null) _acceptWhitelist.AddRange(ids);
    }

    // ―― TTL 过期清理（查询/提交/预约前会被调用）―― //
    private void SweepExpiredReservations()
    {
        if (!_enableTtl) return;
        float now = NowProvider();

        // goods
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
                    _lockedGoods[rec.Id] = Mathf.Max(0, (_lockedGoods.TryGetValue(rec.Id, out var v) ? v : 0) - rec.Amount);
                    if (_lockedGoods[rec.Id] == 0) _lockedGoods.Remove(rec.Id);
                    // （可选：这里可加一条调试日志：释放过期出库预约）
                }
            }
        }

        // cap
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
                    _lockedCap[rec.Id] = Mathf.Max(0, (_lockedCap.TryGetValue(rec.Id, out var v) ? v : 0) - rec.Amount);
                    if (_lockedCap[rec.Id] == 0) _lockedCap.Remove(rec.Id);
                    // （可选：这里可加一条调试日志：释放过期容量预约）
                }
            }
        }
    }

    // 临时列表，避免每次分配
    private static readonly List<int> _tmpKeys = new();

    #endregion

}
