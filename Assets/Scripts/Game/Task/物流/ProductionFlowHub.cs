/***************************************************************************
// File       : ProductionFlowHub.cs
// Author     : Panyuxuan
// Created    : 2026/03/17
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sim.Resources;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ProductionFlowHub : MonoBehaviour, IStepListener
{
    #region Fields / Inspector

    [Header("归属/接受码")]
    public Area ParentArea;
    public ushort AcceptCode;

    [Header("下层调度器")]
    public TransferDispatchCenter DispatchCenter;

    [Header("托管的生产者")]
    public List<ProducerUnit> producers = new();

    [Header("供应仓（原料来源）")]
    public List<Storage> supplyStorages = new();

    [Header("回收仓（产物归集地）")]
    public List<Storage> sinkStorages = new();

    [ShowInInspector] private readonly Dictionary<ProducerUnit, HashSet<NeedResource>> needToInputDic = new();
    [ShowInInspector] private readonly Dictionary<ProducerUnit, HashSet<NeedResource>> needToOfferDic = new();

    [ShowInInspector] private readonly HashSet<ProducerUnit> dirtyInputUnits = new();
    [ShowInInspector] private readonly HashSet<ProducerUnit> dirtyOutputUnits = new();

    [ShowInInspector] private readonly Dictionary<string, RequestBinding> _requestBindings = new();

    [Header("调度节流/预算")]
    [SerializeField] private float stepInterval = 0.2f;
    [SerializeField] private int inputBudgetPerTick = 32;
    [SerializeField] private int outputBudgetPerTick = 32;

    [Header("失败归属")]
    [SerializeField] private bool hubOwnsRetry = true;

    [Header("日志")]
    [SerializeField] private bool enableLogs = false;

    private float _acc;

    #endregion

    #region Events

    /// <summary>
    /// Hub 为生产输入需求生成物流请求时触发。
    /// 下层 DispatchCenter 只需要把它当成通用 Storage->Storage 请求执行。
    /// </summary>
    public event Action<TransferRequest> InputRequestCreated;

    /// <summary>
    /// Hub 为生产输出需求生成物流请求时触发。
    /// 下层 DispatchCenter 只需要把它当成通用 Storage->Storage 请求执行。
    /// </summary>
    public event Action<TransferRequest> OutputRequestCreated;

    public event Action<ProducerUnit, ResourceId, int> OnCompletedInput;
    public event Action<ProducerUnit, ResourceId, int> OnCompletedOutput;

    #endregion

    #region Internal binding model

    private sealed class RequestBinding
    {
        public string RequestId;
        public ProducerUnit Unit;
        public ResourceId ResourceId;
        public int Qty;
        public bool IsInput;
    }

    #endregion

    #region IStepListener lifecycle

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    private void Awake()
    {
        ResolveRefs();
    }

    private void OnEnable()
    {
        ResolveRefs();

        if (GlobalStep.Instance != null)
            GlobalStep.Instance.AddListener(this);

        BindDispatchEvents();
    }

    private void OnDisable()
    {
        if (GlobalStep.Instance != null)
            GlobalStep.Instance.RemoveListener(this);

        UnbindDispatchEvents();
    }

    public void OnTick(in TickContext ctx)
    {
        if (!IsActive)
            return;

        _acc += ctx.DeltaTime;
        if (_acc < stepInterval)
            return;

        _acc = 0f;

        ProcessDirtyInputs(inputBudgetPerTick);
        ProcessDirtyOutputs(outputBudgetPerTick);
    }

    private void ResolveRefs()
    {
        if (ParentArea == null)
            ParentArea = GetComponent<Area>() ?? GetComponentInParent<Area>();

        if (DispatchCenter == null)
            DispatchCenter = GetComponent<TransferDispatchCenter>() ?? GetComponentInParent<TransferDispatchCenter>();
    }

    private void BindDispatchEvents()
    {
        if (DispatchCenter == null)
            return;

        DispatchCenter.RequestDispatched -= HandleDispatchRequestDispatched;
        DispatchCenter.RequestCompleted -= HandleDispatchRequestCompleted;
        DispatchCenter.RequestFailed -= HandleDispatchRequestFailed;

        DispatchCenter.RequestDispatched += HandleDispatchRequestDispatched;
        DispatchCenter.RequestCompleted += HandleDispatchRequestCompleted;
        DispatchCenter.RequestFailed += HandleDispatchRequestFailed;
    }

    private void UnbindDispatchEvents()
    {
        if (DispatchCenter == null)
            return;

        DispatchCenter.RequestDispatched -= HandleDispatchRequestDispatched;
        DispatchCenter.RequestCompleted -= HandleDispatchRequestCompleted;
        DispatchCenter.RequestFailed -= HandleDispatchRequestFailed;
    }

    #endregion

    #region Registration

    public void RegisterProducer(ProducerUnit p)
    {
        if (p != null && !producers.Contains(p))
            producers.Add(p);
    }

    public void UnregisterProducer(ProducerUnit p)
    {
        if (p == null)
            return;

        producers.Remove(p);
        needToInputDic.Remove(p);
        needToOfferDic.Remove(p);
        dirtyInputUnits.Remove(p);
        dirtyOutputUnits.Remove(p);

        var toRemove = _requestBindings
            .Where(kv => kv.Value != null && kv.Value.Unit == p)
            .Select(kv => kv.Key)
            .ToArray();

        foreach (var id in toRemove)
            _requestBindings.Remove(id);
    }

    public void RegisterSupply(Storage s)
    {
        if (s != null && !supplyStorages.Contains(s))
            supplyStorages.Add(s);
    }

    public void UnregisterSupply(Storage s)
    {
        if (s == null)
            return;

        supplyStorages.Remove(s);
    }

    public void RegisterSink(Storage s)
    {
        if (s != null && !sinkStorages.Contains(s))
            sinkStorages.Add(s);
    }

    public void UnregisterSink(Storage s)
    {
        if (s == null)
            return;

        sinkStorages.Remove(s);
    }

    #endregion

    #region External API: Accept needs

    public void AcceptOneInput(ProducerUnit unit, Ingredient[] ingredients)
    {
        if (unit == null || ingredients == null || ingredients.Length == 0)
            return;

        if (!needToInputDic.TryGetValue(unit, out var set))
        {
            set = new HashSet<NeedResource>();
            needToInputDic[unit] = set;
        }

        foreach (var v in ingredients)
            set.Add(new NeedResource(v));

        dirtyInputUnits.Add(unit);
    }

    public void AcceptOneOutput(ProducerUnit unit, Ingredient[] ingredients)
    {
        if (unit == null || ingredients == null || ingredients.Length == 0)
            return;

        if (!needToOfferDic.TryGetValue(unit, out var set))
        {
            set = new HashSet<NeedResource>();
            needToOfferDic[unit] = set;
        }

        foreach (var v in ingredients)
            set.Add(new NeedResource(v));

        dirtyOutputUnits.Add(unit);
    }

    #endregion

    #region Dirty Processing

    private void ProcessDirtyInputs(int budget)
    {
        if (dirtyInputUnits.Count == 0)
            return;

        var work = new List<ProducerUnit>(Math.Min(budget, dirtyInputUnits.Count));
        foreach (var u in dirtyInputUnits)
        {
            work.Add(u);
            if (work.Count >= budget)
                break;
        }

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
        }
    }

    private void ProcessDirtyOutputs(int budget)
    {
        if (dirtyOutputUnits.Count == 0)
            return;

        var work = new List<ProducerUnit>(Math.Min(budget, dirtyOutputUnits.Count));
        foreach (var u in dirtyOutputUnits)
        {
            work.Add(u);
            if (work.Count >= budget)
                break;
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
            if (need.Status != NeedResourceState.WaitingServe)
                continue;

            var source = FindBestInputSource(unit, need.id, need.Qty);
            var target = unit != null ? unit.inputStorage : null;

            if (source == null || target == null)
                continue;

            if (InputRequestCreated == null)
                continue;

            var requestId = BuildRequestId("in", unit, need.id, need.Qty);
            if (_requestBindings.ContainsKey(requestId))
                continue;

            var request = new TransferRequest(requestId, source, target, need.id, need.Qty);
            _requestBindings[requestId] = new RequestBinding
            {
                RequestId = requestId,
                Unit = unit,
                ResourceId = need.id,
                Qty = need.Qty,
                IsInput = true
            };

            ReplaceNeedState(set, need, NeedResourceState.Requested);
            InputRequestCreated?.Invoke(request);
            Log($"Create INPUT request={requestId}, unit={unit?.name}, {source?.name} -> {target?.name}, {need.id} x{need.Qty}");
        }
    }

    private void HandleOutputForUnit(ProducerUnit unit, HashSet<NeedResource> set)
    {
        foreach (var need in set.ToArray())
        {
            if (need.Status != NeedResourceState.WaitingServe)
                continue;

            var source = unit != null ? unit.outputStorage : null;
            var target = FindBestOutputSink(unit, need.id, need.Qty);

            if (source == null || target == null)
                continue;

            if (OutputRequestCreated == null)
                continue;

            var requestId = BuildRequestId("out", unit, need.id, need.Qty);
            if (_requestBindings.ContainsKey(requestId))
                continue;

            var request = new TransferRequest(requestId, source, target, need.id, need.Qty);
            _requestBindings[requestId] = new RequestBinding
            {
                RequestId = requestId,
                Unit = unit,
                ResourceId = need.id,
                Qty = need.Qty,
                IsInput = false
            };

            ReplaceNeedState(set, need, NeedResourceState.Requested);
            OutputRequestCreated?.Invoke(request);
            Log($"Create OUTPUT request={requestId}, unit={unit?.name}, {source?.name} -> {target?.name}, {need.id} x{need.Qty}");
        }
    }

    #endregion

    #region Candidate Selection

    public Storage FindBestInputSource(ProducerUnit unit, ResourceId id, int amount)
    {
        if (unit == null || amount <= 0)
            return null;

        return FindNearestStorage(
            unit.transform.position,
            supplyStorages,
            s => s != null &&
                 s.isActiveAndEnabled &&
                 s.GetAvailable(id) >= amount);
    }

    public Storage FindBestOutputSink(ProducerUnit unit, ResourceId id, int amount)
    {
        if (unit == null || amount <= 0)
            return null;

        return FindNearestStorage(
            unit.transform.position,
            sinkStorages,
            s => s != null &&
                 s.isActiveAndEnabled &&
                 s.CanAccept(id) &&
                 s.GetFreeCapacity(id) >= amount);
    }

    public Storage FindNearestStorage(
        Vector3 pos,
        IReadOnlyList<Storage> storages,
        Func<Storage, bool> extraFilter = null)
    {
        if (storages == null || storages.Count == 0)
            return null;

        Storage best = null;
        float bestDistSq = float.PositiveInfinity;

        for (int i = 0; i < storages.Count; i++)
        {
            var s = storages[i];
            if (s == null || !s.isActiveAndEnabled)
                continue;
            if (extraFilter != null && !extraFilter(s))
                continue;

            float d2 = (s.transform.position - pos).sqrMagnitude;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                best = s;
            }
        }

        return best;
    }

    #endregion

    #region DispatchCenter callbacks

    private void HandleDispatchRequestDispatched(TransferRequest request, Resident resident)
    {
        if (!TryGetBinding(request, out var binding))
            return;

        if (binding.IsInput)
            MarkInputDispatched(binding.Unit, binding.ResourceId, binding.Qty);
        else
            MarkOutputDispatched(binding.Unit, binding.ResourceId, binding.Qty);
    }

    private void HandleDispatchRequestCompleted(TransferRequest request)
    {
        if (!TryGetBinding(request, out var binding))
            return;

        if (binding.IsInput)
        {
            MarkInputCompleted(binding.Unit, binding.ResourceId, binding.Qty);
            OnCompletedInput?.Invoke(binding.Unit, binding.ResourceId, binding.Qty);
        }
        else
        {
            MarkOutputCompleted(binding.Unit, binding.ResourceId, binding.Qty);
            OnCompletedOutput?.Invoke(binding.Unit, binding.ResourceId, binding.Qty);
        }

        _requestBindings.Remove(binding.RequestId);
    }

    private void HandleDispatchRequestFailed(TransferRequest request, string reason)
    {
        if (!TryGetBinding(request, out var binding))
            return;

        if (hubOwnsRetry)
        {
            if (binding.IsInput)
                MarkInputFailed(binding.Unit, binding.ResourceId, binding.Qty);
            else
                MarkOutputFailed(binding.Unit, binding.ResourceId, binding.Qty);

            _requestBindings.Remove(binding.RequestId);
        }
        else
        {
            // 若下层调度器负责自动 retry，则把状态收敛到 Requested，避免 Hub 自己重复派单。
            if (binding.IsInput)
                MarkInputRetryPending(binding.Unit, binding.ResourceId, binding.Qty);
            else
                MarkOutputRetryPending(binding.Unit, binding.ResourceId, binding.Qty);
        }

        Log($"Request failed. request={request?.RequestId}, reason={reason}, hubOwnsRetry={hubOwnsRetry}");
    }

    private bool TryGetBinding(TransferRequest request, out RequestBinding binding)
    {
        binding = null;
        if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
            return false;

        return _requestBindings.TryGetValue(request.RequestId, out binding) && binding != null;
    }

    #endregion

    #region Need State Mutations

    public void NotifyInputDispatched(ProducerUnit unit, ResourceId id, int qty) => MarkInputDispatched(unit, id, qty);
    public void NotifyOutputDispatched(ProducerUnit unit, ResourceId id, int qty) => MarkOutputDispatched(unit, id, qty);

    public void NotifyInputCompleted(ProducerUnit unit, ResourceId id, int qty)
    {
        MarkInputCompleted(unit, id, qty);
        OnCompletedInput?.Invoke(unit, id, qty);
    }

    public void NotifyOutputCompleted(ProducerUnit unit, ResourceId id, int qty)
    {
        MarkOutputCompleted(unit, id, qty);
        OnCompletedOutput?.Invoke(unit, id, qty);
    }

    public void NotifyInputFailed(ProducerUnit unit, ResourceId id, int qty) => MarkInputFailed(unit, id, qty);
    public void NotifyOutputFailed(ProducerUnit unit, ResourceId id, int qty) => MarkOutputFailed(unit, id, qty);

    public void MarkInputDispatched(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null)
            return;
        if (!needToInputDic.TryGetValue(unit, out var set) || set == null)
            return;

        var old = set.FirstOrDefault(n =>
            n.id == id &&
            n.Qty == qty &&
            n.Status == NeedResourceState.Requested);

        if (old != null)
        {
            ReplaceNeedState(set, old, NeedResourceState.Processing);
            dirtyInputUnits.Add(unit);
        }
    }

    public void MarkOutputDispatched(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null)
            return;
        if (!needToOfferDic.TryGetValue(unit, out var set) || set == null)
            return;

        var old = set.FirstOrDefault(n =>
            n.id == id &&
            n.Qty == qty &&
            n.Status == NeedResourceState.Requested);

        if (old != null)
        {
            ReplaceNeedState(set, old, NeedResourceState.Processing);
            dirtyOutputUnits.Add(unit);
        }
    }

    public void MarkInputCompleted(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null)
            return;
        if (!needToInputDic.TryGetValue(unit, out var set) || set == null)
            return;

        var old = set.FirstOrDefault(n =>
            n.id == id &&
            n.Qty == qty &&
            n.Status == NeedResourceState.Processing);

        if (old != null)
        {
            ReplaceNeedState(set, old, NeedResourceState.Completed);
            dirtyInputUnits.Add(unit);
        }
    }

    public void MarkOutputCompleted(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null)
            return;
        if (!needToOfferDic.TryGetValue(unit, out var set) || set == null)
            return;

        var old = set.FirstOrDefault(n =>
            n.id == id &&
            n.Qty == qty &&
            n.Status == NeedResourceState.Processing);

        if (old != null)
        {
            ReplaceNeedState(set, old, NeedResourceState.Completed);
            dirtyOutputUnits.Add(unit);
        }
    }

    public void MarkInputFailed(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null)
            return;
        if (!needToInputDic.TryGetValue(unit, out var set) || set == null)
            return;

        var old = set.FirstOrDefault(n =>
            n.id == id &&
            n.Qty == qty &&
            (n.Status == NeedResourceState.Processing || n.Status == NeedResourceState.Requested));

        if (old != null)
        {
            ReplaceNeedState(set, old, NeedResourceState.WaitingServe);
            dirtyInputUnits.Add(unit);
        }
    }

    public void MarkOutputFailed(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null)
            return;
        if (!needToOfferDic.TryGetValue(unit, out var set) || set == null)
            return;

        var old = set.FirstOrDefault(n =>
            n.id == id &&
            n.Qty == qty &&
            (n.Status == NeedResourceState.Processing || n.Status == NeedResourceState.Requested));

        if (old != null)
        {
            ReplaceNeedState(set, old, NeedResourceState.WaitingServe);
            dirtyOutputUnits.Add(unit);
        }
    }

    public void MarkInputRetryPending(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null)
            return;
        if (!needToInputDic.TryGetValue(unit, out var set) || set == null)
            return;

        var old = set.FirstOrDefault(n =>
            n.id == id &&
            n.Qty == qty &&
            (n.Status == NeedResourceState.Processing || n.Status == NeedResourceState.Requested));

        if (old != null)
        {
            ReplaceNeedState(set, old, NeedResourceState.Requested);
            dirtyInputUnits.Add(unit);
        }
    }

    public void MarkOutputRetryPending(ProducerUnit unit, ResourceId id, int qty)
    {
        if (unit == null)
            return;
        if (!needToOfferDic.TryGetValue(unit, out var set) || set == null)
            return;

        var old = set.FirstOrDefault(n =>
            n.id == id &&
            n.Qty == qty &&
            (n.Status == NeedResourceState.Processing || n.Status == NeedResourceState.Requested));

        if (old != null)
        {
            ReplaceNeedState(set, old, NeedResourceState.Requested);
            dirtyOutputUnits.Add(unit);
        }
    }

    #endregion

    #region Helpers

    private static void ReplaceNeedState(HashSet<NeedResource> set, NeedResource oldNeed, NeedResourceState newState)
    {
        if (set == null || oldNeed == null)
            return;

        set.Remove(oldNeed);
        set.Add(new NeedResource(oldNeed.id, oldNeed.Qty, newState));
    }

    private static string BuildRequestId(string prefix, ProducerUnit unit, ResourceId id, int qty)
    {
        int unitId = unit != null ? unit.GetInstanceID() : 0;
        return $"{prefix}_{unitId}_{id}_{qty}_{Guid.NewGuid():N}";
    }

    private void Log(string msg)
    {
        if (!enableLogs)
            return;

        Debug.Log($"[ProductionFlowHub] {msg}", this);
    }

    #endregion
}
