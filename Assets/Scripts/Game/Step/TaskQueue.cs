using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class TaskQueueExecutionRecord
{
    public string QueueName;
    public string TaskName;
    public TaskState FinalState;
    public TaskOutcome Outcome;
    public string Message;
    public TaskFailReason FailReason;
    public TaskCancelReason CancelReason;
    public bool IsFatal;
    public float Duration;
    public float CompletedAt;
    public string NodeId;
    public string DependencyKey;
    public string ErrorCode;
}

public enum TaskQueueFailurePolicy
{
    Continue,
    StopQueue,
    ClearQueue,
    ByTaskResult
}

public class TaskQueue
{
    private readonly LinkedList<TaskBase> _pending = new();
    private readonly Queue<TaskQueueExecutionRecord> _history = new();

    private bool _isTicking;
    private bool _idleRaised;

    private Action<TaskBase, TaskResult> _currentCompletedHandler;

    public string QueueName { get; }

    public bool IsActive { get; set; } = true;

    public bool AutoStartNext { get; set; } = true;

    public int HistoryCapacity { get; set; } = 32;

    public TaskQueueFailurePolicy FailurePolicy { get; set; } = TaskQueueFailurePolicy.ByTaskResult;

    public TaskBase Current { get; private set; }

    public TaskServiceState State { get; private set; } = TaskServiceState.Idle;

    public bool HasTasks => Current != null || _pending.Count > 0;

    public int PendingCount => _pending.Count;

    public int HistoryCount => _history.Count;

    public event Action<TaskQueue, TaskBase> TaskStarted;
    public event Action<TaskQueue, TaskBase, TaskResult> TaskCompleted;
    public event Action<TaskQueue, TaskBase, TaskResult> TaskSucceeded;
    public event Action<TaskQueue, TaskBase, TaskResult> TaskFailed;
    public event Action<TaskQueue, TaskBase, TaskResult> TaskCanceled;
    public event Action<TaskQueue> QueueChanged;
    public event Action<TaskQueue> QueueBecameIdle;
    public event Action<TaskQueue, TaskBase, TaskResult> ExecutionStoppedByFailure;

    public TaskQueue(string queueName)
    {
        QueueName = string.IsNullOrWhiteSpace(queueName) ? "TaskQueue" : queueName;
    }

    public bool Enqueue(TaskBase task)
    {
        return EnqueueInternal(task, front: false);
    }

    public bool EnqueueFront(TaskBase task)
    {
        return EnqueueInternal(task, front: true);
    }

    public bool Preempt(TaskBase preemptTask, bool clearPending = true)
    {
        if (preemptTask == null)
            return false;

        if (!preemptTask.SetPending())
            return false;

        if (Current != null)
        {
            Current.Cancel("Preempted by queue", TaskCancelReason.Preempted);
            // Current 会在 Completed 回调里被清掉
        }

        if (clearPending && _pending.Count > 0)
        {
            _pending.Clear();
            QueueChanged?.Invoke(this);
        }

        _pending.AddFirst(preemptTask);
        QueueChanged?.Invoke(this);

        if (AutoStartNext && Current == null && State != TaskServiceState.Paused && State != TaskServiceState.Stopped)
        {
            TryStartNext();
        }

        return true;
    }

    public void Tick(float dt)
    {
        if (!IsActive)
            return;

        if (State == TaskServiceState.Paused || State == TaskServiceState.Stopped)
            return;

        _isTicking = true;

        try
        {
            if (Current == null)
            {
                if (AutoStartNext)
                    TryStartNext();
            }

            if (Current == null)
            {
                EnterIdleIfNeeded();
                return;
            }

            if (!Current.IsDone)
            {
                State = TaskServiceState.Running;
                Current.Tick(dt);
            }

            // 如果 Current 在 Tick 中完成，它会通过 Completed 事件回调收尾。
            // 这里不额外兜底伪造结果，避免和 TaskBase 的 Transition 语义冲突。
            EnterIdleIfNeeded();
        }
        finally
        {
            _isTicking = false;
        }
    }

    public void Pause()
    {
        if (State == TaskServiceState.Stopped)
            return;

        State = TaskServiceState.Paused;
    }

    public void Resume()
    {
        if (State != TaskServiceState.Paused)
            return;

        State = HasTasks ? TaskServiceState.Running : TaskServiceState.Idle;

        if (AutoStartNext && Current == null)
            TryStartNext();
    }

    public void Stop(bool cancelCurrent = true, bool clearPending = true)
    {
        State = TaskServiceState.Stopped;

        if (cancelCurrent && Current != null)
        {
            Current.Cancel("Queue stopped", TaskCancelReason.ServiceStopped);
        }

        if (clearPending && _pending.Count > 0)
        {
            _pending.Clear();
            QueueChanged?.Invoke(this);
        }
    }

    public void ResetQueue()
    {
        UnbindCurrent();

        Current = null;
        _pending.Clear();
        _history.Clear();

        _isTicking = false;
        _idleRaised = false;
        State = TaskServiceState.Idle;
    }

    public void CancelCurrent(
        string cancelMessage = null,
        TaskCancelReason cancelReason = TaskCancelReason.Manual)
    {
        if (Current == null)
            return;

        Current.Cancel(cancelMessage, cancelReason);
    }

    public void ClearPending()
    {
        if (_pending.Count == 0)
            return;

        _pending.Clear();
        QueueChanged?.Invoke(this);

        EnterIdleIfNeeded();
    }

    public IReadOnlyCollection<TaskBase> GetPendingSnapshot()
    {
        return new List<TaskBase>(_pending);
    }

    public IReadOnlyCollection<TaskQueueExecutionRecord> GetHistorySnapshot()
    {
        return _history.ToArray();
    }

    private bool EnqueueInternal(TaskBase task, bool front)
    {
        if (task == null)
            return false;

        if (State == TaskServiceState.Stopped)
            return false;

        // 对齐你最新 TaskBase：只允许 None -> Pending
        if (!task.SetPending())
            return false;

        if (front)
            _pending.AddFirst(task);
        else
            _pending.AddLast(task);

        _idleRaised = false;
        QueueChanged?.Invoke(this);

        if (!_isTicking && AutoStartNext && Current == null && State != TaskServiceState.Paused)
            TryStartNext();

        return true;
    }

    private void TryStartNext()
    {
        if (State == TaskServiceState.Paused || State == TaskServiceState.Stopped)
            return;

        if (Current != null && !Current.IsDone)
            return;

        if (_pending.Count == 0)
        {
            Current = null;
            EnterIdleIfNeeded();
            return;
        }

        var node = _pending.First;
        _pending.RemoveFirst();

        Current = node.Value;
        BindCurrent(Current);

        _idleRaised = false;
        State = TaskServiceState.Running;
        QueueChanged?.Invoke(this);
        TaskStarted?.Invoke(this, Current);

        Current.StartTask();
    }

    private void BindCurrent(TaskBase task)
    {
        UnbindCurrent();

        _currentCompletedHandler = OnCurrentCompleted;
        task.Completed += _currentCompletedHandler;
    }

    private void UnbindCurrent()
    {
        if (Current != null && _currentCompletedHandler != null)
        {
            Current.Completed -= _currentCompletedHandler;
        }

        _currentCompletedHandler = null;
    }

    private void OnCurrentCompleted(TaskBase task, TaskResult result)
    {
        if (task == null || result == null)
            return;

        if (Current != task)
            return;

        RecordHistory(task, result);
        PublishCompletionEvents(task, result);

        bool shouldContinue = EvaluateContinuation(result);

        UnbindCurrent();
        Current = null;

        if (!shouldContinue)
        {
            if (FailurePolicy == TaskQueueFailurePolicy.ClearQueue ||
                (FailurePolicy == TaskQueueFailurePolicy.ByTaskResult && result.IsFailed && result.IsFatal))
            {
                if (_pending.Count > 0)
                {
                    _pending.Clear();
                    QueueChanged?.Invoke(this);
                }
            }

            State = _pending.Count > 0 ? TaskServiceState.Running : TaskServiceState.Idle;
            ExecutionStoppedByFailure?.Invoke(this, task, result);
            EnterIdleIfNeeded();
            return;
        }

        if (AutoStartNext && State != TaskServiceState.Paused && State != TaskServiceState.Stopped)
            TryStartNext();
        else
            EnterIdleIfNeeded();
    }

    private bool EvaluateContinuation(TaskResult result)
    {
        if (result == null)
            return true;

        if (result.IsSuccess || result.IsCanceled)
            return true;

        switch (FailurePolicy)
        {
            case TaskQueueFailurePolicy.Continue:
                return true;

            case TaskQueueFailurePolicy.StopQueue:
                return false;

            case TaskQueueFailurePolicy.ClearQueue:
                return false;

            case TaskQueueFailurePolicy.ByTaskResult:
                return !result.IsFatal;

            default:
                return true;
        }
    }

    private void RecordHistory(TaskBase task, TaskResult result)
    {
        var record = new TaskQueueExecutionRecord
        {
            QueueName = QueueName,
            TaskName = result.TaskName ?? task?.Name ?? task?.GetType().Name,
            FinalState = task != null ? task.State : TaskState.None,
            Outcome = result.Outcome,
            Message = result.Message,
            FailReason = result.FailReason,
            CancelReason = result.CancelReason,
            IsFatal = result.IsFatal,
            Duration = result.Duration,
            CompletedAt = Time.time,
            NodeId = result.NodeId,
            DependencyKey = result.DependencyKey,
            ErrorCode = result.ErrorCode
        };

        _history.Enqueue(record);

        while (_history.Count > HistoryCapacity)
            _history.Dequeue();
    }

    private void PublishCompletionEvents(TaskBase task, TaskResult result)
    {
        TaskCompleted?.Invoke(this, task, result);

        if (result.IsSuccess)
            TaskSucceeded?.Invoke(this, task, result);
        else if (result.IsFailed)
            TaskFailed?.Invoke(this, task, result);
        else if (result.IsCanceled)
            TaskCanceled?.Invoke(this, task, result);
    }

    private void EnterIdleIfNeeded()
    {
        if (HasTasks)
        {
            if (State != TaskServiceState.Paused && State != TaskServiceState.Stopped)
                State = TaskServiceState.Running;
            return;
        }

        State = TaskServiceState.Idle;

        if (_idleRaised)
            return;

        _idleRaised = true;
        QueueBecameIdle?.Invoke(this);
    }
}