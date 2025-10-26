using Sim.Resources;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

/// <summary>
/// ProducerContext - 精简/优化版
/// - 保持原有调度逻辑（脏集合、步进、预算）
/// - 优化了候选仓库查找（避免每次 LINQ OrderBy 分配）
/// - 增加 NotifyInput/NotifyOutput 的公开方法（任务完成/失败时调用）
/// - 暴露若干事件：OnAssignedInput/Output, OnCompletedInput/Output （可选订阅）
/// </summary>
public class ProducerContext : MonoBehaviour, IStepListener
{
    #region Fields / Inspector

    [Header("归属/接受码")]
    public Area ParentArea;
    public ushort AcceptCode;

    [Header("托管的生产者")]
    public List<ProducerUnit> producers = new();

    [Header("搬运执行者（居民经济服务）")]
    public List<ResidentEconomyService> residents = new();

    [Header("供应仓（原料来源）")]
    public List<Storage> supplyStorages = new();

    [Header("回收仓（产物归集地）")]
    public List<Storage> sinkStorages = new();

    // need sets
    private readonly Dictionary<ProducerUnit, HashSet<NeedResource>> needToInputDic = new();
    private readonly Dictionary<ProducerUnit, HashSet<NeedResource>> needToOfferDic = new();

    private readonly HashSet<ProducerUnit> dirtyInputUnits = new();
    private readonly HashSet<ProducerUnit> dirtyOutputUnits = new();

    // resident handler sets (占用标记)
    private readonly HashSet<ResidentEconomyService> handlerInputSet = new();
    private readonly HashSet<ResidentEconomyService> handlerOutputSet = new();

    [Header("调度节流/预算")]
    [SerializeField] private float stepInterval = 0.2f;
    [SerializeField] private int inputBudgetPerTick = 32;
    [SerializeField] private int outputBudgetPerTick = 32;

    private float _acc;

    #endregion

    #region Events (optional observers)

    /// <summary>当一个输入搬运任务被下发（派单成功）</summary>
    public event Action<ProducerUnit, ResourceId, int> OnAssignedInput;

    /// <summary>当一个输出搬运任务被下发（派单成功）</summary>
    public event Action<ProducerUnit, ResourceId, int> OnAssignedOutput;

    /// <summary>当输入任务被标记完成（任务成功时 ProducerContext 收到通知）</summary>
    public event Action<ProducerUnit, ResourceId, int> OnCompletedInput;

    /// <summary>当输出任务被标记完成</summary>
    public event Action<ProducerUnit, ResourceId, int> OnCompletedOutput;

    #endregion

    #region IStepListener lifecycle

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    private void OnEnable() => GlobalStep.Instance.AddListener(this);
    private void OnDisable() => GlobalStep.Instance.RemoveListener(this);

    public void OnTick(in TickContext ctx)
    {
        _acc += ctx.DeltaTime;
        if (_acc < stepInterval) return;
        _acc = 0f;

        ProcessDirtyInputs(inputBudgetPerTick);
        ProcessDirtyOutputs(outputBudgetPerTick);
    }

    #endregion

    #region Registration helpers

    public void RegisterProducer(ProducerUnit p)
    {
        if (p != null && !producers.Contains(p)) producers.Add(p);
    }

    public void UnregisterProducer(ProducerUnit p)
    {
        if (p == null) return;
        producers.Remove(p);
        needToInputDic.Remove(p);
        needToOfferDic.Remove(p);
        dirtyInputUnits.Remove(p);
        dirtyOutputUnits.Remove(p);
    }

    public void RegisterResident(ResidentEconomyService r)
    {
        if (r != null && !residents.Contains(r)) residents.Add(r);
    }

    public void RegisterSupply(Storage s)
    {
        if (s != null && !supplyStorages.Contains(s)) supplyStorages.Add(s);
    }

    public void RegisterSink(Storage s)
    {
        if (s != null && !sinkStorages.Contains(s)) sinkStorages.Add(s);
    }

    #endregion

    #region External API: Accept needs (mark dirty)

    public void AcceptOneInput(ProducerUnit unit, Ingredient[] ingredients)
    {
        if (unit == null || ingredients == null || ingredients.Length == 0) return;

        if (!needToInputDic.TryGetValue(unit, out var set))
        {
            set = new HashSet<NeedResource>();
            needToInputDic[unit] = set;
        }
        foreach (var v in ingredients) set.Add(new NeedResource(v));
        dirtyInputUnits.Add(unit);
    }

    public void AcceptOneOutput(ProducerUnit unit, Ingredient[] ingredients)
    {
        if (unit == null || ingredients == null || ingredients.Length == 0) return;

        if (!needToOfferDic.TryGetValue(unit, out var set))
        {
            set = new HashSet<NeedResource>();
            needToOfferDic[unit] = set;
        }
        foreach (var v in ingredients) set.Add(new NeedResource(v));
        dirtyOutputUnits.Add(unit);
    }

    #endregion

    #region ServerInput / ServerOutput (派单)

    // ServerInput: 从 supplyStorages 取货运往 unit.inputStorage（外层先做预约并传入）
    private bool ServerInput(ProducerUnit unit, ResourceId id, int amount)
    {
        if (unit == null || amount <= 0) return false;
        if (residents == null || residents.Count == 0) return false;

        var residentEco = FindAvailableResident(true);
        if (residentEco == null) return false;

        var resident = residentEco.GetComponent<Resident>();
        if (resident == null)
        {
            // release just in case (FindAvailableResident already added it)
            ReleaseResident(residentEco, true);
            return false;
        }

        var originPos = resident.transform.position;

        // 找最近且有可用量的仓库 —— 避免 OrderBy 分配开销
        Storage found = null;
        float bestDistSq = float.PositiveInfinity;
        for (int i = 0; i < supplyStorages.Count; i++)
        {
            var s = supplyStorages[i];
            if (s == null || !s.isActiveAndEnabled) continue;
            if (s.GetAvailable(id) < amount) continue;
            float d2 = (s.transform.position - originPos).sqrMagnitude;
            if (d2 < bestDistSq) { bestDistSq = d2; found = s; }
        }

        if (found == null)
        {
            // no candidate; release resident and bail out
            ReleaseResident(residentEco, true);
            return false;
        }

        var sFound = found;
        int residentTicket = 0;
        int storageTicket = 0;
        var backpack = residentEco.backpack;
        if (backpack == null)
        {
            ReleaseResident(residentEco, true);
            return false;
        }

        bool gotBackpack = false;
        bool gotStorage = false;
        try
        {
            gotBackpack = backpack.TryReserveCapacity(id, amount, out residentTicket);
            if (!gotBackpack)
            {
                ReleaseResident(residentEco, true);
                return false;
            }

            gotStorage = sFound.TryReserveResource(id, amount, out storageTicket);
            if (!gotStorage)
            {
                try { backpack.CancelCapacityReserve(residentTicket); } catch { }
                ReleaseResident(residentEco, true);
                return false;
            }

            Debug.Log($"[ProducerContext] Reserved {amount} x {id} from '{sFound.name}' (storageTicket={storageTicket}) " +
                      $"and reserved capacity in backpack (residentTicket={residentTicket}).");

            // ---- 按你既有风格入队子任务（move/get/reserveCapacity） ----
            MoveToTask moveTo = MoveToTask.Create(resident, sFound.transform, residentEco.passMask);
            GetResourceTask getResource = GetResourceTask.Create(resident, sFound, residentTicket, storageTicket);

            // ReserveCapacityTask 的回调需要将后续动作入队。为避免 lambda capture 高频分配，可用局部函数（仍可能 capture，但更清晰）
            ReserveCapacityTask reserveCapacityTask = ReserveCapacityTask.Create(resident, sFound, id, amount, OnReserveCapacityForInput);

            resident.taskService.Enqueue(moveTo);
            resident.taskService.Enqueue(getResource);
            resident.taskService.Enqueue(reserveCapacityTask);

            // 触发分发事件
            OnAssignedInput?.Invoke(unit, id, amount);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            if (gotStorage) try { sFound.CancelGoodsReserve(storageTicket); } catch { }
            if (gotBackpack) try { backpack.CancelCapacityReserve(residentTicket); } catch { }
            ReleaseResident(residentEco, true);
            return false;
        }

        // local callback for ReserveCapacityTask -> enqueues moveBack & put task
        void OnReserveCapacityForInput(int _residentTicket, int _storageTicket)
        {
            try
            {
                MoveToTask moveBack = MoveToTask.Create(resident, unit.transform, residentEco.passMask);
                PutResourceTask putResource = PutResourceTask.Create(resident, unit.inputStorage, _residentTicket, _storageTicket);

                // putResource 完成时，让任务/大任务调用 ProducerContext.NotifyInputCompleted(...)
                // 你可以在 PutResourceTask 完成时在任务里调用 ProducerContext.NotifyInputCompleted(...)
                resident.taskService.Enqueue(moveBack);
                resident.taskService.Enqueue(putResource);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    // ServerOutput: 把 unit.outputStorage 的产物搬到 sinkStorages（回收仓）
    private bool ServerOutput(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null || qty <= 0) return false;
        if (residents == null || residents.Count == 0) return false;

        var residentEco = FindAvailableResident(false);
        if (residentEco == null) return false;

        var resident = residentEco.GetComponent<Resident>();
        if (resident == null)
        {
            ReleaseResident(residentEco, false);
            return false;
        }

        var originPos = resident.transform.position;

        // 找最近且有空余容量且能接受该资源的 sink
        Storage found = null;
        float bestDistSq = float.PositiveInfinity;
        for (int i = 0; i < sinkStorages.Count; i++)
        {
            var s = sinkStorages[i];
            if (s == null || !s.isActiveAndEnabled) continue;
            if (!s.CanAccept(id)) continue;
            if (s.GetFreeCapacity(id) < qty) continue;
            float d2 = (s.transform.position - originPos).sqrMagnitude;
            if (d2 < bestDistSq) { bestDistSq = d2; found = s; }
        }

        if (found == null)
        {
            ReleaseResident(residentEco, false);
            return false;
        }

        int sinkTicket = 0;
        int unitTicket = 0;
        bool gotSink = false;
        bool gotUnit = false;

        try
        {
            gotSink = found.TryReserveCapacity(id, qty, out sinkTicket);
            if (!gotSink) { ReleaseResident(residentEco, false); return false; }

            // 假定 unit.outputStorage 实现了 TryReserveResource
            gotUnit = unit.outputStorage.TryReserveResource(id, qty, out unitTicket);
            if (!gotUnit)
            {
                try { found.CancelCapacityReserve(sinkTicket); } catch { }
                ReleaseResident(residentEco, false);
                return false;
            }

            Debug.Log($"[ProducerContext] Reserved output {qty} x {id} from unit '{unit.name}' (unitTicket={unitTicket}) " +
                      $"and reserved capacity in sink '{found.name}' (sinkTicket={sinkTicket}).");

            MoveToTask moveToUnit = MoveToTask.Create(resident, unit.transform, residentEco.passMask);

            GetResourceTask getResource = GetResourceTask.Create(resident, unit.outputStorage, unitTicket, sinkTicket);

            ReserveCapacityTask reserveCapacityTask = ReserveCapacityTask.Create(resident, found, id, qty, OnReserveCapacityForOutput);

            resident.taskService.Enqueue(moveToUnit);
            resident.taskService.Enqueue(getResource);
            resident.taskService.Enqueue(reserveCapacityTask);

            OnAssignedOutput?.Invoke(unit, id, qty);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            if (gotUnit) try { unit.outputStorage.CancelGoodsReserve(unitTicket); } catch { }
            if (gotSink) try { found.CancelCapacityReserve(sinkTicket); } catch { }
            ReleaseResident(residentEco, false);
            return false;
        }

        void OnReserveCapacityForOutput(int _dstTicket, int _srcTicket)
        {
            try
            {
                MoveToTask moveToSink = MoveToTask.Create(resident, found.transform, residentEco.passMask);
                PutResourceTask putResource = PutResourceTask.Create(resident, found, _dstTicket, _srcTicket);

                // putResource Completed 回调里请在你的大任务中调用 ProducerContext.NotifyOutputCompleted(...)
                resident.taskService.Enqueue(moveToSink);
                resident.taskService.Enqueue(putResource);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    #endregion

    #region Utilities: FindNearestStorage (已保留，但更高效)

    public Storage FindNearestStorage(Vector3 pos, IReadOnlyList<Storage> storages, Func<Storage, bool> extraFilter = null)
    {
        if (storages == null || storages.Count == 0) return null;
        Storage best = null;
        float bestDistSq = float.PositiveInfinity;
        for (int i = 0; i < storages.Count; i++)
        {
            var s = storages[i];
            if (s == null || !s.isActiveAndEnabled) continue;
            if (extraFilter != null && !extraFilter(s)) continue;
            float d2 = (s.transform.position - pos).sqrMagnitude;
            if (d2 < bestDistSq) { bestDistSq = d2; best = s; }
        }
        return best;
    }

    #endregion

    #region Dirty processing (per producer)

    private void ProcessDirtyInputs(int budget)
    {
        if (dirtyInputUnits.Count == 0) return;

        // 收集 up to budget 个单元用于处理（避免全量复制）
        var work = new List<ProducerUnit>(Math.Min(budget, dirtyInputUnits.Count));
        foreach (var u in dirtyInputUnits)
        {
            work.Add(u);
            if (work.Count >= budget) break;
        }

        int processed = 0;
        foreach (var unit in work)
        {
            if (!needToInputDic.TryGetValue(unit, out var set) || set == null || set.Count == 0)
            {
                dirtyInputUnits.Remove(unit);
                needToInputDic.Remove(unit);
                continue;
            }

            HandleInputForUnit(unit, set);

            bool done = set.Count == 0 || set.All(n => n.Status == NeedResourceState.Completed);
            if (done)
            {
                needToInputDic.Remove(unit);
                dirtyInputUnits.Remove(unit);
            }

            processed++;
        }
    }

    private void ProcessDirtyOutputs(int budget)
    {
        if (dirtyOutputUnits.Count == 0) return;

        var work = new List<ProducerUnit>(Math.Min(budget, dirtyOutputUnits.Count));
        foreach (var u in dirtyOutputUnits)
        {
            work.Add(u);
            if (work.Count >= budget) break;
        }

        foreach (var unit in work)
        {
            if (!needToOfferDic.TryGetValue(unit, out var set) || set == null || set.Count == 0)
            {
                dirtyOutputUnits.Remove(unit);
                needToOfferDic.Remove(unit);
                continue;
            }

            HandleOutputForUnit(unit, set);

            bool done = set.Count == 0 || set.All(n => n.Status == NeedResourceState.Completed);
            if (done)
            {
                needToOfferDic.Remove(unit);
                dirtyOutputUnits.Remove(unit);
            }
        }
    }

    private void HandleInputForUnit(ProducerUnit unit, HashSet<NeedResource> set)
    {
        foreach (var need in set.ToArray())
        {
            if (need.Status != NeedResourceState.WaitingServe) continue;

            bool dispatched = ServerInput(unit, need.id, need.Qty);
            if (dispatched)
            {
                set.Remove(need);
                set.Add(new NeedResource(need.id, need.Qty, NeedResourceState.Processing));
            }
        }
    }

    private void HandleOutputForUnit(ProducerUnit unit, HashSet<NeedResource> set)
    {
        foreach (var need in set.ToArray())
        {
            if (need.Status != NeedResourceState.WaitingServe) continue;

            bool dispatched = ServerOutput(unit, need.id, need.Qty);
            if (dispatched)
            {
                set.Remove(need);
                set.Add(new NeedResource(need.id, need.Qty, NeedResourceState.Processing));
            }
        }
    }

    #endregion

    #region Task completion / failure notification helpers (供大任务调用)

    /// <summary>
    /// 调用者：你的大任务在成功完成把物资放进生产单元后调用此方法
    /// - 会把 NeedResource 标为 Completed、添加到 dirty 集合并释放 resident（由调用任务传入）
    /// - 触发 OnCompletedInput 事件
    /// </summary>
    public void NotifyInputCompleted(ProducerUnit unit, ResidentEconomyService resident, ResourceId id, int qty)
    {
        if (unit == null) return;
        MarkInputCompleted(unit, id, qty);
        ReleaseResident(resident, true);
        OnCompletedInput?.Invoke(unit, id, qty);
    }

    /// <summary>
    /// 调用者：你的大任务在失败或取消时调用（input 方向）
    /// - 会把 NeedResource 状态回滚为 WaitingServe、触发脏标、释放 resident
    /// </summary>
    public void NotifyInputFailed(ProducerUnit unit, ResidentEconomyService resident, ResourceId id, int qty)
    {
        if (unit == null) return;
        MarkInputFailed(unit, id, qty);
        ReleaseResident(resident, true);
    }

    public void NotifyOutputCompleted(ProducerUnit unit, ResidentEconomyService resident, ResourceId id, int qty)
    {
        if (unit == null) return;
        MarkOutputCompleted(unit, id, qty);
        ReleaseResident(resident, false);
        OnCompletedOutput?.Invoke(unit, id, qty);
    }

    public void NotifyOutputFailed(ProducerUnit unit, ResidentEconomyService resident, ResourceId id, int qty)
    {
        if (unit == null) return;
        MarkOutputFailed(unit, id, qty);
        ReleaseResident(resident, false);
    }

    #endregion

    #region Raw Markers (内部使用 / 保持兼容原来 API)

    public void MarkInputCompleted(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null) return;
        if (!needToInputDic.TryGetValue(unit, out var set) || set == null) return;

        var old = new NeedResource(id, qty, NeedResourceState.Processing);
        if (set.Remove(old))
        {
            set.Add(new NeedResource(id, qty, NeedResourceState.Completed));
            dirtyInputUnits.Add(unit);
        }
    }

    public void MarkOutputCompleted(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null) return;
        if (!needToOfferDic.TryGetValue(unit, out var set) || set == null) return;

        var old = new NeedResource(id, qty, NeedResourceState.Processing);
        if (set.Remove(old))
        {
            set.Add(new NeedResource(id, qty, NeedResourceState.Completed));
            dirtyOutputUnits.Add(unit);
        }
    }

    public void MarkInputFailed(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null) return;
        if (!needToInputDic.TryGetValue(unit, out var set) || set == null) return;

        var old = new NeedResource(id, qty, NeedResourceState.Processing);
        if (set.Remove(old))
        {
            set.Add(new NeedResource(id, qty, NeedResourceState.WaitingServe));
            dirtyInputUnits.Add(unit);
        }
    }

    public void MarkOutputFailed(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null) return;
        if (!needToOfferDic.TryGetValue(unit, out var set) || set == null) return;

        var old = new NeedResource(id, qty, NeedResourceState.Processing);
        if (set.Remove(old))
        {
            set.Add(new NeedResource(id, qty, NeedResourceState.WaitingServe));
            dirtyOutputUnits.Add(unit);
        }
    }

    #endregion

    #region Resident allocation helpers

    private ResidentEconomyService FindAvailableResident(bool input)
    {
        foreach (var r in residents)
        {
            if (r == null || !r.isActiveAndEnabled) continue;
            if (handlerInputSet.Contains(r) || handlerOutputSet.Contains(r)) continue;

            if (input) handlerInputSet.Add(r);
            else handlerOutputSet.Add(r);
            return r;
        }
        return null;
    }

    private void ReleaseResident(ResidentEconomyService resident, bool input)
    {
        if (resident == null) return;
        if (input) handlerInputSet.Remove(resident);
        else handlerOutputSet.Remove(resident);
    }

    #endregion
}
