using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using System;


public enum TaskFailurePolicy
{
    Continue,      // 失败后继续后续任务
    StopQueue,     // 失败后停止，但保留队列
    ClearQueue,    // 失败后清空后续队列
    ByTaskResult   // 按 result.IsFatal 决定
}

public enum TaskServiceState
{
    Idle,
    Running,
    Paused,
    Preempting,
    Stopped
}

[Serializable]
public sealed class TaskExecutionRecord
{
    public string TaskName;
    public TaskState FinalState;
    public TaskOutcome Outcome;
    public string Message;
    public TaskFailReason FailReason;
    public bool IsFatal;
    public float Duration;
    public float CompletedAt;
}

public class ResidentTaskService : MonoBehaviour, IStepListener
{
    [SerializeField] public Resident Model;

    [ShowInInspector]
    private readonly LinkedList<TaskBase> _pending = new();

    [ShowInInspector]
    private readonly Queue<TaskExecutionRecord> _history = new();

    [SerializeField] private int _historyCapacity = 64;

    [ShowInInspector]
    public TaskBase Current { get; private set; }

    public int QueueCount => _pending.Count;
    public int HistoryCount => _history.Count;

    [ShowInInspector]
    public TaskServiceState ServiceState { get; private set; } = TaskServiceState.Idle;

    public TaskFailurePolicy FailurePolicy = TaskFailurePolicy.ByTaskResult;

    private bool _useGlobal;
    private Coroutine _preemptCoro;

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    public void OnTick(in TickContext ctx) => TickLocal(ctx.DeltaTime);

    public event Action<TaskBase> TaskStarted;
    public event Action<TaskBase, TaskResult> TaskCompleted;
    public event Action<TaskBase, TaskResult> TaskSucceeded;
    public event Action<TaskBase, TaskResult> TaskFailed;
    public event Action<TaskBase, TaskResult> TaskCanceled;
    public event Action QueueChanged;
    public event Action ServiceBecameIdle;
    public event Action<TaskBase, TaskResult> ExecutionStoppedByFailure;

    private void Awake()
    {
        Model = GetComponent<Resident>();
    }

    private void OnEnable()
    {
        _useGlobal = GlobalStep.Instance != null;
        if (_useGlobal)
            GlobalStep.Instance.AddListener(this);

        enabled = true;
    }

    private void OnDisable()
    {
        if (_useGlobal && GlobalStep.Instance != null)
            GlobalStep.Instance.RemoveListener(this);
    }

    private void Update()
    {
        if (!_useGlobal)
            TickLocal(Time.deltaTime);
    }

    private void TickLocal(float dt)
    {
        if (!IsActive)
            return;

        if (ServiceState == TaskServiceState.Paused || ServiceState == TaskServiceState.Stopped)
            return;

        if (Current != null && !Current.IsDone)
        {
            ServiceState = TaskServiceState.Running;
            Current.Tick(dt);
            return;
        }

        if (Current == null)
            TryStartNext();
    }

    public bool Enqueue(TaskBase task, bool front = false)
    {
        if (!CanAcceptTask(task, front))
            return false;

        // 兼容你当前旧版 TaskBase：若还是 void SetPending，这一行改回 task.SetPending();
        if (!task.SetPending())
            return false;

        if (front)
            _pending.AddFirst(task);
        else
            _pending.AddLast(task);

        QueueChanged?.Invoke();
        TryStartNext();
        return true;
    }

    public void CancelCurrent(
        bool clearQueue = false,
        string cancelMessage = null,
        TaskCancelReason cancelReason = TaskCancelReason.Manual)
    {
        if (Current != null)
        {
            UnbindCurrentTask(Current);
            Current.Cancel(cancelMessage, cancelReason);
            Current = null;
        }

        if (clearQueue)
            ClearPendingQueue();

        if (!HasPendingTasks())
            EnterIdle();
    }

    public void CancelAll(
        string cancelMessage = null,
        TaskCancelReason cancelReason = TaskCancelReason.QueueCleared)
    {
        CancelCurrent(clearQueue: true, cancelMessage: cancelMessage, cancelReason: cancelReason);
    }

    public void Pause()
    {
        if (ServiceState == TaskServiceState.Stopped)
            return;

        ServiceState = TaskServiceState.Paused;
    }

    public void Resume()
    {
        if (ServiceState != TaskServiceState.Paused)
            return;

        ServiceState = TaskServiceState.Idle;
        TryStartNext();
    }

    public void Stop(bool cancelCurrent = true, bool clearQueue = true)
    {
        ServiceState = TaskServiceState.Stopped;

        if (cancelCurrent)
            CancelCurrent(
                clearQueue: false,
                cancelMessage: "Service stopped",
                cancelReason: TaskCancelReason.ServiceStopped);

        if (clearQueue)
            ClearPendingQueue();
    }

    public void ResetService()
    {
        if (_preemptCoro != null)
        {
            StopCoroutine(_preemptCoro);
            _preemptCoro = null;
        }

        if (Current != null)
            UnbindCurrentTask(Current);

        Current = null;
        _pending.Clear();
        _history.Clear();

        QueueChanged?.Invoke();
        EnterIdle();
    }

    public void PreemptNextFrame(TaskBase preempt, TaskBase after = null)
    {
        if (preempt == null)
            return;

        if (_preemptCoro != null)
            StopCoroutine(_preemptCoro);

        _preemptCoro = StartCoroutine(CoPreemptNextFrame(preempt, after));
    }

    private IEnumerator CoPreemptNextFrame(TaskBase preempt, TaskBase after)
    {
        ServiceState = TaskServiceState.Preempting;
        yield return null;

        CancelCurrent(
            clearQueue: true,
            cancelMessage: "Preempted by service",
            cancelReason: TaskCancelReason.Preempted);

        Enqueue(preempt, front: true);

        if (after != null)
            Enqueue(after, front: false);

        _preemptCoro = null;

        if (ServiceState == TaskServiceState.Preempting)
            ServiceState = TaskServiceState.Idle;
    }

    private bool CanAcceptTask(TaskBase task, bool front)
    {
        if (task == null)
            return false;

        if (Model == null || !Model.CanAccept)
            return false;

        if (ServiceState == TaskServiceState.Stopped)
            return false;

        if (!front && Current is TaskSequence { IsExclusiveLooping: true })
            return false;

        return true;
    }

    private void TryStartNext()
    {
        if (ServiceState == TaskServiceState.Paused || ServiceState == TaskServiceState.Stopped)
            return;

        if (Current != null && !Current.IsDone)
            return;

        if (_pending.Count == 0)
        {
            Current = null;
            EnterIdle();
            return;
        }

        var node = _pending.First;
        _pending.RemoveFirst();
        QueueChanged?.Invoke();

        Current = node.Value;
        BindCurrentTask(Current);

        ServiceState = TaskServiceState.Running;

        Debug.Log($"[ResidentTaskService] Start task: {Current.Name ?? Current.GetType().Name}");
        TaskStarted?.Invoke(Current);
        Current.StartTask();
    }

    private void BindCurrentTask(TaskBase task)
    {
        task.Completed += OnTaskCompleted;
    }

    private void UnbindCurrentTask(TaskBase task)
    {
        task.Completed -= OnTaskCompleted;
    }

    private void OnTaskCompleted(TaskBase task, TaskResult result)
    {
        UnbindCurrentTask(task);

        RecordHistory(task, result);
        PublishCompletionEvents(task, result);

        bool shouldContinue = EvaluateContinuation(result);

        Current = null;

        if (!shouldContinue)
        {
            if (FailurePolicy == TaskFailurePolicy.ClearQueue ||
                (FailurePolicy == TaskFailurePolicy.ByTaskResult && result != null && result.IsFailed && result.IsFatal))
            {
                ClearPendingQueue();
            }

            ExecutionStoppedByFailure?.Invoke(task, result);
            EnterIdle();
            return;
        }

        TryStartNext();
    }

    private bool EvaluateContinuation(TaskResult result)
    {
        if (result == null)
            return true;

        if (result.IsSuccess || result.IsCanceled)
            return true;

        switch (FailurePolicy)
        {
            case TaskFailurePolicy.Continue:
                return true;

            case TaskFailurePolicy.StopQueue:
                return false;

            case TaskFailurePolicy.ClearQueue:
                return false;

            case TaskFailurePolicy.ByTaskResult:
                return !result.IsFatal;

            default:
                return true;
        }
    }

    private void PublishCompletionEvents(TaskBase task, TaskResult result)
    {
        TaskCompleted?.Invoke(task, result);

        if (result == null)
            return;

        if (result.IsSuccess)
            TaskSucceeded?.Invoke(task, result);
        else if (result.IsFailed)
            TaskFailed?.Invoke(task, result);
        else if (result.IsCanceled)
            TaskCanceled?.Invoke(task, result);
    }

    private void RecordHistory(TaskBase task, TaskResult result)
    {
        var record = new TaskExecutionRecord
        {
            TaskName = task?.Name ?? task?.GetType().Name,
            FinalState = task != null ? task.State : TaskState.None,
            Outcome = result != null ? result.Outcome : TaskOutcome.None,
            Message = result?.Message,
            FailReason = result != null ? result.FailReason : TaskFailReason.None,
            IsFatal = result != null && result.IsFatal,
            Duration = result != null ? result.Duration : 0f,
            CompletedAt = Time.time
        };

        _history.Enqueue(record);

        while (_history.Count > _historyCapacity)
            _history.Dequeue();
    }

    private void ClearPendingQueue()
    {
        if (_pending.Count == 0)
            return;

        _pending.Clear();
        QueueChanged?.Invoke();
    }

    private bool HasPendingTasks()
    {
        return Current != null || _pending.Count > 0;
    }

    private void EnterIdle()
    {
        ServiceState = TaskServiceState.Idle;

        if (!HasPendingTasks())
            ServiceBecameIdle?.Invoke();
    }

    public IReadOnlyCollection<TaskExecutionRecord> GetHistorySnapshot()
    {
        return _history.ToArray();
    }

    public IReadOnlyCollection<TaskBase> GetPendingSnapshot()
    {
        return new List<TaskBase>(_pending);
    }
}
