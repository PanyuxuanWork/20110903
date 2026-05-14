/***************************************************************************
// File       : TransferDispatchCenter.cs
// Author     : Panyuxuan
// Created    : 2026/03/17
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using Sim.Resources;
using UnityEngine;


public sealed class TransferWorkerHandle
{
    public ResidentEconomyService WorkerEco { get; }
    public Resident Resident { get; }

    public TransferWorkerHandle(ResidentEconomyService workerEco, Resident resident)
    {
        WorkerEco = workerEco;
        Resident = resident;
    }
}

public enum TransferLifecyclePhase
{
    None = 0,
    Accepted = 1,
    Dispatching = 2,
    Dispatched = 3,
    Completed = 4,
    Failed = 5,
    Canceled = 6
}

public interface ITransferWorkerProvider
{
    int BusyCount { get; }

    bool TryAcquireWorker(
        ResourceId resourceId,
        Storage source,
        out TransferWorkerHandle handle);

    void ReleaseWorker(TransferWorkerHandle handle);
}

/// <summary>
/// 单次 TransferRequest 的上下文。
/// 语义上绑定 Request；运行时由 TransferDispatchCenter 创建、推进、发布。
/// 外部主契约应优先依赖这个 Context，而不是直接依赖内部 Task 的原始生命周期。
/// </summary>
[Serializable]
public sealed class TransferRequestContext
{
    [SerializeField] private string _requestId;

    public string RequestId => _requestId;
    public TransferRequest Request { get; internal set; }

    public Storage Source { get; internal set; }
    public Storage Target { get; internal set; }
    public ResourceId ResourceId { get; internal set; }
    public int Amount { get; internal set; }

    public TransferRequestState RequestState { get; internal set; }
    public TransferLifecyclePhase Phase { get; internal set; }

    public Resident AssignedResident { get; internal set; }

    /// <summary>
    /// 内部真实执行 Task。对外可读，但建议只作为补充信息。
    /// 外部主流程请优先监听 Context 生命周期事件。
    /// </summary>
    public TaskBase Task { get; internal set; }

    public TaskResult TaskResult { get; internal set; }
    public string Message { get; internal set; }

    /// <summary>
    /// true 表示这个上下文已经被 DispatchCenter 接收并托管。
    /// false 表示这是一个“立即拒绝/失败”的 detached context，只用于同步返回结果，不会继续推进生命周期。
    /// </summary>
    public bool IsBoundToCenter { get; internal set; }

    public bool IsTerminal =>
        Phase == TransferLifecyclePhase.Completed ||
        Phase == TransferLifecyclePhase.Failed ||
        Phase == TransferLifecyclePhase.Canceled;

    public event Action<TransferRequestContext> PhaseChanged;
    public event Action<TransferRequestContext> Dispatching;
    public event Action<TransferRequestContext> Dispatched;
    public event Action<TransferRequestContext, TaskBase> TaskBound;
    public event Action<TransferRequestContext> Completed;
    public event Action<TransferRequestContext, string> Failed;
    public event Action<TransferRequestContext, string> Canceled;

    internal static TransferRequestContext CreateLive(TransferRequest request)
    {
        var ctx = new TransferRequestContext();
        ctx.ResetFromRequest(request);
        ctx.IsBoundToCenter = true;
        ctx.Phase = TransferLifecyclePhase.None;
        ctx.RequestState = request != null ? request.State : TransferRequestState.None;
        return ctx;
    }

    internal static TransferRequestContext CreateDetachedRejected(TransferRequest request, string message)
    {
        var ctx = new TransferRequestContext();
        ctx.ResetFromRequest(request);
        ctx.IsBoundToCenter = false;
        ctx.RequestState = request != null ? request.State : TransferRequestState.Failed;
        ctx.Phase = TransferLifecyclePhase.Failed;
        ctx.Message = message;
        return ctx;
    }

    internal bool ApplyState(
        TransferLifecyclePhase phase,
        TransferRequestState requestState,
        string message,
        Resident resident,
        TaskBase task,
        TaskResult taskResult,
        bool notifyLocalSubscribers)
    {
        var oldPhase = Phase;
        var oldState = RequestState;
        var oldResident = AssignedResident;
        var oldTask = Task;
        var oldTaskResult = TaskResult;
        var oldMessage = Message;

        if (Request != null)
            ResetFromRequest(Request);

        Phase = phase;
        RequestState = requestState;
        AssignedResident = resident;
        Task = task;
        TaskResult = taskResult;
        Message = message;

        bool taskBoundNow = !ReferenceEquals(oldTask, Task) && Task != null;
        bool changed =
            oldPhase != Phase ||
            oldState != RequestState ||
            !ReferenceEquals(oldResident, AssignedResident) ||
            !ReferenceEquals(oldTask, Task) ||
            !ReferenceEquals(oldTaskResult, TaskResult) ||
            !string.Equals(oldMessage, Message, StringComparison.Ordinal);

        if (!notifyLocalSubscribers)
            return changed || taskBoundNow;

        if (taskBoundNow)
            TaskBound?.Invoke(this, Task);

        if (!changed)
            return taskBoundNow;

        PhaseChanged?.Invoke(this);

        switch (Phase)
        {
            case TransferLifecyclePhase.Dispatching:
                Dispatching?.Invoke(this);
                break;
            case TransferLifecyclePhase.Dispatched:
                Dispatched?.Invoke(this);
                break;
            case TransferLifecyclePhase.Completed:
                Completed?.Invoke(this);
                break;
            case TransferLifecyclePhase.Failed:
                Failed?.Invoke(this, Message);
                break;
            case TransferLifecyclePhase.Canceled:
                Canceled?.Invoke(this, Message);
                break;
        }

        return true;
    }

    private void ResetFromRequest(TransferRequest request)
    {
        Request = request;
        _requestId = request != null ? request.RequestId : null;
        Source = request != null ? request.SourceStorage : null;
        Target = request != null ? request.TargetStorage : null;
        ResourceId = request != null ? request.ResourceId : ResourceId.None;
        Amount = request != null ? request.Amount : 0;
    }
}

/// <summary>
/// 单次搬运调度中心：
/// 1. 外部提交 TransferRequest
/// 2. 中心负责找 source/target、找 worker、创建 reservation、创建 task、下发给 resident
/// 3. 以 Request-bound Context 作为外部主契约
/// 4. 不负责多次任务编排；失败后的“是否重新发起新 Request”交给上层 Node / 状态机
/// </summary>
public class TransferDispatchCenter : MonoBehaviour, IStepListener
{
    #region Inspector

    [Header("归属")]
    public Area ParentArea;

    [Header("可选：接入生产需求中心，仅作为 request 输入源")]
    public ProductionFlowHub FlowHub;

    [Header("搬运工来源")]
    [SerializeField] private MonoBehaviour workerProviderBehaviour;

    [Header("调度节流")]
    [SerializeField] private float dispatchInterval = 0.2f;
    [SerializeField] private int dispatchBudgetPerTick = 32;

    [Header("预约参数")]
    [SerializeField] private float reservationTtlSec = 30f;

    [Header("日志")]
    [SerializeField] private bool enableLogs = false;

    #endregion

    #region Center-level events

    /// <summary>
    /// Center 级广播事件。外部更推荐直接持有 TryEnqueueRequest 返回的 context。
    /// </summary>
    public event Action<TransferRequestContext> LifecycleChanged;

    // 兼容旧调用方，可按需保留
    public event Action<TransferRequest> RequestAccepted;
    public event Action<TransferRequest, Resident> RequestDispatched;
    public event Action<TransferRequest> RequestCompleted;
    public event Action<TransferRequest, string> RequestFailed;
    public event Action<TransferRequest, string> RequestCanceled;

    #endregion

    #region Runtime

    private readonly LinkedList<TransferRequest> _pending = new();
    private readonly Dictionary<string, LinkedListNode<TransferRequest>> _pendingNodesByRequestId = new();
    private readonly Dictionary<string, ActiveTransferExecution> _activeByRequestId = new();
    private readonly Dictionary<string, string> _activeRequestIdByBusinessKey = new();
    private readonly Dictionary<string, TransferRequestContext> _contextsByRequestId = new();

    private ITransferWorkerProvider _workerProvider;

    private float _acc;

    #endregion

    #region Debug

    public int GetBusyWorkerCount() => _workerProvider != null ? _workerProvider.BusyCount : 0;
    public int GetPendingRequestCount() => _pending.Count;
    public int GetActiveExecutionCount() => _activeByRequestId.Count;

    public bool TryGetContext(string requestId, out TransferRequestContext context)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            context = null;
            return false;
        }

        return _contextsByRequestId.TryGetValue(requestId, out context) && context != null;
    }

    #endregion

    #region Unity / Step

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    private void Awake()
    {
        if (FlowHub == null)
            FlowHub = GetComponent<ProductionFlowHub>();

        if (ParentArea == null)
            ParentArea = GetComponent<Area>() ?? FlowHub?.ParentArea;

        ResolveWorkerProvider();
    }

    private void Start()
    {
        if (ParentArea == null && FlowHub != null)
            ParentArea = FlowHub.ParentArea;

        if (_workerProvider == null)
            ResolveWorkerProvider();

        if (_workerProvider is AreaResidentTransferWorkerProvider areaProvider && areaProvider.ParentArea == null)
            areaProvider.ParentArea = ParentArea;
    }

    private void OnEnable()
    {
        if (GlobalStep.Instance != null)
            GlobalStep.Instance.AddListener(this);

        if (FlowHub != null)
        {
            FlowHub.InputRequestCreated += OnRequestCreated;
            FlowHub.OutputRequestCreated += OnRequestCreated;
        }
    }

    private void OnDisable()
    {
        if (GlobalStep.Instance != null)
            GlobalStep.Instance.RemoveListener(this);

        if (FlowHub != null)
        {
            FlowHub.InputRequestCreated -= OnRequestCreated;
            FlowHub.OutputRequestCreated -= OnRequestCreated;
        }

        CancelAllActiveExecutions("TransferDispatchCenter disabled");
        CancelAllPending("TransferDispatchCenter disabled");
    }

    public void OnTick(in TickContext ctx)
    {
        if (!IsActive)
            return;

        _acc += ctx.DeltaTime;
        if (_acc < dispatchInterval)
            return;

        _acc = 0f;
        DispatchPending(dispatchBudgetPerTick);
    }

    private void ResolveWorkerProvider()
    {
        _workerProvider = workerProviderBehaviour as ITransferWorkerProvider;
        if (_workerProvider != null)
            return;

        var monoBehaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < monoBehaviours.Length; i++)
        {
            var mb = monoBehaviours[i];
            if (mb is ITransferWorkerProvider provider)
            {
                _workerProvider = provider;
                workerProviderBehaviour = mb;
                break;
            }
        }

        if (_workerProvider != null)
            return;

        // 兜底：自动挂一个默认 Area resident provider，保持旧逻辑可用
        var fallback = GetComponent<AreaResidentTransferWorkerProvider>();
        if (fallback == null)
            fallback = gameObject.AddComponent<AreaResidentTransferWorkerProvider>();

        if (fallback.ParentArea == null)
            fallback.ParentArea = ParentArea;

        _workerProvider = fallback;
        workerProviderBehaviour = fallback;
    }

    #endregion

    #region Intake

    private void OnRequestCreated(TransferRequest request)
    {
        TryEnqueueRequest(request, out _);
    }

    /// <summary>
    /// 推荐入口：成功时返回一个 live context，外部可直接围绕这个 context 绑定后续生命周期。
    ///
    /// 约定：
    /// - 返回 true：context 为 center 托管的 live context。
    /// - 返回 false：
    ///   1) 若是同一 RequestId 已在 center 中，则返回已有 live context；
    ///   2) 其他立即拒绝场景，返回一个 detached rejected context（IsBoundToCenter = false）。
    /// </summary>
    public bool TryEnqueueRequest(TransferRequest request, out TransferRequestContext context)
    {
        context = null;

        if (request == null)
            return false;

        string requestId = request.RequestId;
        if (!string.IsNullOrWhiteSpace(requestId) &&
            _contextsByRequestId.TryGetValue(requestId, out var existingContext) &&
            existingContext != null)
        {
            context = existingContext;
            if (_pendingNodesByRequestId.ContainsKey(requestId) || _activeByRequestId.ContainsKey(requestId))
                return false;
        }

        if (!ValidateRequest(request, out string reason))
        {
            request.State = TransferRequestState.Failed;
            context = TransferRequestContext.CreateDetachedRejected(request, $"Invalid request: {reason}");
            RequestFailed?.Invoke(request, context.Message);
            Log($"Reject request={request?.RequestId}, reason={context.Message}");
            return false;
        }

        string businessKey = BuildBusinessKey(request);

        if (_activeByRequestId.ContainsKey(requestId) || _pendingNodesByRequestId.ContainsKey(requestId))
        {
            context = EnsureContext(request);
            return false;
        }

        if (_activeRequestIdByBusinessKey.ContainsKey(businessKey))
        {
            request.State = TransferRequestState.Failed;
            context = TransferRequestContext.CreateDetachedRejected(request, $"Business key already active: {businessKey}");
            RequestFailed?.Invoke(request, context.Message);
            Log($"Reject request={request.RequestId}, reason={context.Message}");
            return false;
        }

        request.State = TransferRequestState.Pending;

        var node = _pending.AddLast(request);
        _pendingNodesByRequestId[requestId] = node;

        context = EnsureContext(request);
        UpdateContext(context, TransferLifecyclePhase.Accepted, request.State, null, null, null, null, false);

        RequestAccepted?.Invoke(request);
        Log($"Enqueue request={requestId}, key={businessKey}");
        return true;
    }

    // 兼容旧调用方
    public bool EnqueueRequest(TransferRequest request)
    {
        return TryEnqueueRequest(request, out _);
    }

    /// <summary>
    /// 多资源请求入口：将 mixed 中的多个资源项拆分成多个单资源 TransferRequest，
    /// 然后逐个调用现有的 TryEnqueueRequest。
    ///
    /// 约定：
    /// - 返回 true：所有子请求都成功入队。
    /// - 返回 false：至少一个子请求入队失败。
    /// - 不做失败回滚；已经成功入队的子请求会继续保留。
    /// - ctx 中会保留所有子 context，并额外记录成功/失败列表。
    /// </summary>
    public bool TryEnqueueRequestList(TransferRequestMixed mixed, TransferRequestMixedContext ctx)
    {
        if (ctx == null)
            return false;

        ctx.Reset(mixed);

        if (!ValidateMixedRequest(mixed, out string reason))
        {
            if (mixed != null)
                mixed.State = TransferRequestState.Failed;

            ctx.Message = reason;
            Log($"Reject mixed request={mixed?.RequestId}, reason={reason}");
            return false;
        }

        var children = mixed.BuildChildRequests();
        bool allSucceeded = true;

        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            bool enqueueSucceeded = TryEnqueueRequest(child, out var childContext);
            ctx.AddResult(childContext, enqueueSucceeded);

            if (!enqueueSucceeded)
                allSucceeded = false;
        }

        mixed.State = allSucceeded
            ? TransferRequestState.Pending
            : TransferRequestState.Failed;

        if (!allSucceeded)
        {
            ctx.Message = $"Enqueue finished with failures. success={ctx.SuccessCount}, failed={ctx.FailedCount}";
            return false;
        }

        return true;
    }

    #endregion

    #region External cancel API

    public bool CancelRequest(string requestId, string reason = "Canceled by caller")
    {
        if (string.IsNullOrWhiteSpace(requestId))
            return false;

        if (_pendingNodesByRequestId.TryGetValue(requestId, out var pendingNode) && pendingNode != null)
        {
            var request = pendingNode.Value;
            RemovePendingNode(pendingNode);
            CancelRequestInternal(request, reason);
            return true;
        }

        if (_activeByRequestId.TryGetValue(requestId, out var execution) && execution != null)
        {
            execution.IsCompletionHandled = true;

            try
            {
                CancelReservationPair(execution.Pair);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            TryCancelTask(execution.Task);
            CancelRequestInternal(execution.Request, reason, execution.Resident, execution.Task, null);
            CleanupExecution(execution);
            return true;
        }

        return false;
    }

    #endregion

    #region Dispatch loop

    private void DispatchPending(int budget)
    {
        if (_pending.Count == 0 || budget <= 0)
            return;

        int processed = 0;
        var node = _pending.First;

        while (node != null && processed < budget)
        {
            var next = node.Next;
            var request = node.Value;
            bool removeFromPending = TryDispatch(request);
            if (removeFromPending)
                RemovePendingNode(node);

            processed++;
            node = next;
        }
    }

    private bool TryDispatch(TransferRequest request)
    {
        if (!ValidateRequest(request, out string invalidReason))
        {
            FailRequest(request, invalidReason);
            return true;
        }

        string requestId = request.RequestId;
        string businessKey = BuildBusinessKey(request);

        if (_activeByRequestId.ContainsKey(requestId))
            return true;

        if (_activeRequestIdByBusinessKey.ContainsKey(businessKey))
            return false;

        var context = EnsureContext(request);
        UpdateContext(context, TransferLifecyclePhase.Dispatching, request.State, null, null, null, null, true);

        if (!TryResolveEndpoints(request, out Storage source, out Storage target, out string endpointReason))
        {
            Log($"Resolve endpoints failed. request={requestId}, reason={endpointReason}");
            return false;
        }

        if (!TryAcquireWorker(request.ResourceId, source, out TransferWorkerHandle workerHandle))
        {
            Log($"No worker available. request={requestId}");
            return false;
        }

        if (!TryCreateReservationPair(request, source, target, out ReservationPair pair, out string reserveReason))
        {
            ReleaseWorker(workerHandle);
            Log($"Reservation failed. request={requestId}, reason={reserveReason}");
            return false;
        }

        try
        {
            if (!TryCreateTask(request, workerHandle.Resident, source, target, pair, out TaskBase task, out string taskReason))
            {
                CancelReservationPair(pair);
                ReleaseWorker(workerHandle);
                Log($"Create task failed. request={requestId}, reason={taskReason}");
                return false;
            }

            request.State = TransferRequestState.InProgress;

            var execution = new ActiveTransferExecution(
                request,
                businessKey,
                workerHandle,
                source,
                target,
                pair,
                task);

            _activeByRequestId[requestId] = execution;
            _activeRequestIdByBusinessKey[businessKey] = requestId;

            BindTaskCallbacks(execution);
            workerHandle.Resident.taskService.Enqueue(task);

            UpdateContext(context, TransferLifecyclePhase.Dispatched, request.State, null, workerHandle.Resident, task, null, true);
            RequestDispatched?.Invoke(request, workerHandle.Resident);
            Log($"Dispatch success. request={requestId}, worker={workerHandle.Resident.name}, source={source.name}, target={target.name}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            CancelReservationPair(pair);
            ReleaseWorker(workerHandle);
            return false;
        }
    }

    #endregion

    #region Endpoint resolving

    private bool TryResolveEndpoints(
        TransferRequest request,
        out Storage source,
        out Storage target,
        out string reason)
    {
        source = request?.SourceStorage;
        target = request?.TargetStorage;
        reason = null;

        if (source == null)
        {
            reason = "SourceStorage is null";
            return false;
        }

        if (target == null)
        {
            reason = "TargetStorage is null";
            return false;
        }

        if (source == target)
        {
            reason = "SourceStorage and TargetStorage are the same";
            return false;
        }

        if (!IsStorageValid(source))
        {
            reason = "Source storage invalid";
            return false;
        }

        if (!IsStorageValid(target))
        {
            reason = "Target storage invalid";
            return false;
        }

        return true;
    }

    private static bool IsStorageValid(Storage s)
    {
        return s != null && s.isActiveAndEnabled;
    }

    #endregion

    #region Worker allocation

    private bool TryAcquireWorker(
        ResourceId resourceId,
        Storage source,
        out TransferWorkerHandle handle)
    {
        handle = null;

        if (_workerProvider == null)
            return false;

        return _workerProvider.TryAcquireWorker(resourceId, source, out handle);
    }

    private void ReleaseWorker(TransferWorkerHandle handle)
    {
        if (_workerProvider == null || handle == null)
            return;

        _workerProvider.ReleaseWorker(handle);
    }

    #endregion

    #region Reservation pair

    private readonly struct ReservationPair
    {
        public readonly Storage Source;
        public readonly int GoodsTicket;
        public readonly Storage Target;
        public readonly int CapTicket;

        public ReservationPair(Storage source, int goodsTicket, Storage target, int capTicket)
        {
            Source = source;
            GoodsTicket = goodsTicket;
            Target = target;
            CapTicket = capTicket;
        }
    }

    private bool TryCreateReservationPair(
        TransferRequest request,
        Storage source,
        Storage target,
        out ReservationPair pair,
        out string reason)
    {
        pair = default;
        reason = null;

        int goodsTicket = 0;
        int capTicket = 0;

        try
        {
            if (!source.TryReserveResource(request.ResourceId, request.Amount, out goodsTicket, reservationTtlSec))
            {
                reason = "Source goods reserve failed";
                return false;
            }

            if (!target.TryReserveCapacity(request.ResourceId, request.Amount, out capTicket, reservationTtlSec))
            {
                source.CancelGoodsReserve(goodsTicket);
                reason = "Target capacity reserve failed";
                return false;
            }

            pair = new ReservationPair(source, goodsTicket, target, capTicket);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            try { if (goodsTicket != 0 && source != null) source.CancelGoodsReserve(goodsTicket); } catch { }
            try { if (capTicket != 0 && target != null) target.CancelCapacityReserve(capTicket); } catch { }
            reason = "Exception while creating reservation pair";
            return false;
        }
    }

    private void CancelReservationPair(ReservationPair pair)
    {
        try
        {
            if (pair.Source != null && pair.GoodsTicket != 0)
                pair.Source.CancelGoodsReserve(pair.GoodsTicket);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        try
        {
            if (pair.Target != null && pair.CapTicket != 0)
                pair.Target.CancelCapacityReserve(pair.CapTicket);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    #endregion

    #region Task creation

    private bool TryCreateTask(
        TransferRequest request,
        Resident resident,
        Storage source,
        Storage target,
        ReservationPair pair,
        out TaskBase task,
        out string reason)
    {
        task = null;
        reason = null;

        if (resident == null)
        {
            reason = "Resident is null";
            return false;
        }

        try
        {
            task = CarryFromStorageToStorage.Create(
                resident,
                source,
                target,
                request.ResourceId,
                request.Amount,
                pair.GoodsTicket,
                pair.CapTicket);

            if (task == null)
            {
                reason = "CarryFromStorageToStorage.Create returned null";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            reason = "Exception while creating task";
            return false;
        }
    }

    #endregion

    #region Task lifecycle binding

    private void BindTaskCallbacks(ActiveTransferExecution execution)
    {
        if (execution == null || execution.Task == null)
            return;

        execution.CompletedHandler = (t, result) => HandleTaskCompleted(execution, result);
        execution.Task.Completed += execution.CompletedHandler;
    }

    private void UnbindTaskCallbacks(ActiveTransferExecution execution)
    {
        if (execution?.Task == null || execution.CompletedHandler == null)
            return;

        execution.Task.Completed -= execution.CompletedHandler;
        execution.CompletedHandler = null;
    }

    private void HandleTaskCompleted(ActiveTransferExecution execution, TaskResult result)
    {
        if (execution == null || execution.IsCompletionHandled)
            return;

        execution.IsCompletionHandled = true;

        try
        {
            if (result == null)
            {
                OnExecutionFailure(execution, null, "Task completed with null result");
                return;
            }

            if (result.IsSuccess)
            {
                OnExecutionSuccess(execution, result);
                return;
            }

            string message = !string.IsNullOrWhiteSpace(result.Message)
                ? result.Message
                : result.ToString();

            OnExecutionFailure(execution, result, message);
        }
        finally
        {
            CleanupExecution(execution);
        }
    }

    private void OnExecutionSuccess(ActiveTransferExecution execution, TaskResult result)
    {
        var request = execution.Request;
        request.State = TransferRequestState.Completed;

        var context = EnsureContext(request);
        UpdateContext(context, TransferLifecyclePhase.Completed, request.State, result?.Message, execution.Resident, execution.Task, result, true);

        RequestCompleted?.Invoke(request);
        Log($"Execution success. request={request.RequestId}");
    }

    private void OnExecutionFailure(ActiveTransferExecution execution, TaskResult result, string message)
    {
        var request = execution.Request;
        request.State = TransferRequestState.Failed;

        CancelReservationPair(execution.Pair);

        var context = EnsureContext(request);
        UpdateContext(context, TransferLifecyclePhase.Failed, request.State, message, execution.Resident, execution.Task, result, true);

        RequestFailed?.Invoke(request, message);
        Log($"Execution failed. request={request.RequestId}, reason={message}");
    }

    private void CleanupExecution(ActiveTransferExecution execution)
    {
        if (execution == null)
            return;

        UnbindTaskCallbacks(execution);
        _activeByRequestId.Remove(execution.Request.RequestId);
        _activeRequestIdByBusinessKey.Remove(execution.BusinessKey);
        ReleaseWorker(execution.WorkerHandle);
    }

    #endregion

    #region Failure / cancel / cleanup

    private void CancelAllActiveExecutions(string reason)
    {
        if (_activeByRequestId.Count == 0)
            return;

        var list = new List<ActiveTransferExecution>(_activeByRequestId.Values);
        foreach (var execution in list)
        {
            if (execution == null)
                continue;

            execution.IsCompletionHandled = true;

            try
            {
                CancelReservationPair(execution.Pair);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            TryCancelTask(execution.Task);
            CancelRequestInternal(execution.Request, reason, execution.Resident, execution.Task, null);
            CleanupExecution(execution);
        }

        _activeByRequestId.Clear();
        _activeRequestIdByBusinessKey.Clear();
    }

    private void CancelAllPending(string reason)
    {
        if (_pending.Count == 0)
        {
            _pendingNodesByRequestId.Clear();
            return;
        }

        var list = new List<TransferRequest>(_pending);
        _pending.Clear();
        _pendingNodesByRequestId.Clear();

        foreach (var request in list)
            CancelRequestInternal(request, reason);
    }

    private void CancelRequestInternal(
        TransferRequest request,
        string message,
        Resident resident = null,
        TaskBase task = null,
        TaskResult result = null)
    {
        if (request == null)
            return;

        request.State = TransferRequestState.Canceled;

        var context = EnsureContext(request);
        UpdateContext(context, TransferLifecyclePhase.Canceled, request.State, message, resident, task, result, true);

        RequestCanceled?.Invoke(request, message);
        Log($"Request canceled. request={request.RequestId}, reason={message}");
    }

    private void RemovePendingNode(LinkedListNode<TransferRequest> node)
    {
        if (node == null)
            return;

        var req = node.Value;
        if (req != null)
            _pendingNodesByRequestId.Remove(req.RequestId);

        _pending.Remove(node);
    }

    private bool ValidateRequest(TransferRequest request, out string reason)
    {
        reason = null;

        if (request == null)
        {
            reason = "Request is null";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            reason = "RequestId is null or empty";
            return false;
        }

        if (request.Amount <= 0)
        {
            reason = "Amount must be > 0";
            return false;
        }

        if (request.SourceStorage == null)
        {
            reason = "SourceStorage is null";
            return false;
        }

        if (request.TargetStorage == null)
        {
            reason = "TargetStorage is null";
            return false;
        }

        if (request.SourceStorage == request.TargetStorage)
        {
            reason = "SourceStorage and TargetStorage cannot be the same";
            return false;
        }

        if (request.ResourceId == ResourceId.None)
        {
            reason = "Invalid resource id";
            return false;
        }

        return true;
    }

    private bool ValidateMixedRequest(TransferRequestMixed mixed, out string reason)
    {
        reason = null;

        if (mixed == null)
        {
            reason = "Mixed request is null";
            return false;
        }

        if (string.IsNullOrWhiteSpace(mixed.RequestId))
        {
            reason = "RequestId is null or empty";
            return false;
        }

        if (mixed.SourceStorage == null)
        {
            reason = "SourceStorage is null";
            return false;
        }

        if (mixed.TargetStorage == null)
        {
            reason = "TargetStorage is null";
            return false;
        }

        if (mixed.SourceStorage == mixed.TargetStorage)
        {
            reason = "SourceStorage and TargetStorage cannot be the same";
            return false;
        }

        if (mixed.ResourceAmounts == null || mixed.ResourceAmounts.Count == 0)
        {
            reason = "ResourceAmounts is null or empty";
            return false;
        }

        for (int i = 0; i < mixed.ResourceAmounts.Count; i++)
        {
            var item = mixed.ResourceAmounts[i];

            if (item.ResourceId == ResourceId.None)
            {
                reason = $"Invalid resource id at index={i}";
                return false;
            }

            if (item.Amount <= 0)
            {
                reason = $"Amount must be > 0 at index={i}";
                return false;
            }
        }

        return true;
    }

    private void FailRequest(TransferRequest request, string message)
    {
        if (request == null)
            return;

        request.State = TransferRequestState.Failed;

        var context = EnsureContext(request);
        UpdateContext(context, TransferLifecyclePhase.Failed, request.State, message, null, null, null, true);

        RequestFailed?.Invoke(request, message);
        Log($"Request failed. request={request.RequestId}, reason={message}");
    }

    #endregion

    #region Helpers

    private TransferRequestContext EnsureContext(TransferRequest request)
    {
        if (request == null)
            return null;

        if (_contextsByRequestId.TryGetValue(request.RequestId, out var found) && found != null)
            return found;

        var created = TransferRequestContext.CreateLive(request);
        _contextsByRequestId[request.RequestId] = created;
        return created;
    }

    private void UpdateContext(
        TransferRequestContext context,
        TransferLifecyclePhase phase,
        TransferRequestState requestState,
        string message,
        Resident resident,
        TaskBase task,
        TaskResult taskResult,
        bool notifyLocalSubscribers)
    {
        if (context == null)
            return;

        bool changed = context.ApplyState(
            phase,
            requestState,
            message,
            resident,
            task,
            taskResult,
            notifyLocalSubscribers);

        if (changed)
            LifecycleChanged?.Invoke(context);
    }

    private static string BuildBusinessKey(TransferRequest request)
    {
        int sourceId = request.SourceStorage != null ? request.SourceStorage.GetInstanceID() : 0;
        int targetId = request.TargetStorage != null ? request.TargetStorage.GetInstanceID() : 0;
        return $"{sourceId}|{targetId}|{request.ResourceId}|{request.Amount}";
    }

    private void TryCancelTask(TaskBase task)
    {
        if (task == null)
            return;

        try
        {
            var method = task.GetType().GetMethod("Cancel", Type.EmptyTypes);
            method?.Invoke(task, null);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void Log(string msg)
    {
        if (!enableLogs)
            return;

        Debug.Log($"[TransferDispatchCenter] {msg}", this);
    }

    #endregion

    #region Active execution

    private sealed class ActiveTransferExecution
    {
        public TransferRequest Request { get; }
        public string BusinessKey { get; }
        public TransferWorkerHandle WorkerHandle { get; }
        public Resident Resident => WorkerHandle != null ? WorkerHandle.Resident : null;
        public ResidentEconomyService WorkerEco => WorkerHandle != null ? WorkerHandle.WorkerEco : null;
        public Storage Source { get; }
        public Storage Target { get; }
        public ReservationPair Pair { get; }
        public TaskBase Task { get; }
        public Action<TaskBase, TaskResult> CompletedHandler { get; set; }
        public bool IsCompletionHandled { get; set; }

        public ActiveTransferExecution(
            TransferRequest request,
            string businessKey,
            TransferWorkerHandle workerHandle,
            Storage source,
            Storage target,
            ReservationPair pair,
            TaskBase task)
        {
            Request = request;
            BusinessKey = businessKey;
            WorkerHandle = workerHandle;
            Source = source;
            Target = target;
            Pair = pair;
            Task = task;
        }
    }

    #endregion
}

