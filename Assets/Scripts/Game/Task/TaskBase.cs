using System;
using System.Collections.Generic;
using UnityEngine;

public enum TaskState
{
    None = 0,
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled
}

/// <summary>
/// 最终结果类型
/// </summary>
public enum TaskOutcome
{
    None = 0,
    Succeeded,
    Failed,
    Canceled
}

public enum TaskFailReason
{
    None = 0,

    Exception,          // 代码异常
    ValidationFailed,   // 前置条件不满足
    Timeout,            // 超时
    DependencyFailed,   // 依赖失败
    Interrupted,        // 被外部打断
    ManualFail,         // 主动调用 Fail
    InvalidState,       //无效的状态
    Unknown
}

public enum TaskCancelReason
{
    None = 0,
    Manual,
    Preempted,
    DependencyInvalidated,
    ServiceStopped,
    QueueCleared,
    GraphAborted
}

public sealed class TaskResult
{
    public TaskOutcome Outcome { get; private set; }
    public TaskFailReason FailReason { get; private set; }
    public TaskCancelReason CancelReason { get; private set; }

    public string Message { get; private set; }
    public Exception Exception { get; private set; }

    public string TaskName { get; private set; }
    public float Duration { get; private set; }

    /// <summary>是否建议上层中断后续链条</summary>
    public bool IsFatal { get; private set; }

    /// <summary>节点图定位</summary>
    public string NodeId { get; private set; }

    /// <summary>依赖标识，例如资源、工序、前置任务 key</summary>
    public string DependencyKey { get; private set; }

    /// <summary>便于统计/筛选/失败分流</summary>
    public string ErrorCode { get; private set; }

    /// <summary>扩展上下文</summary>
    public Dictionary<string, object> Metadata { get; } = new();

    public List<object> Payloads = new();

    public bool IsCompleted => Outcome != TaskOutcome.None;
    public bool IsSuccess => Outcome == TaskOutcome.Succeeded;
    public bool IsFailed => Outcome == TaskOutcome.Failed;
    public bool IsCanceled => Outcome == TaskOutcome.Canceled;

    private TaskResult() { }

    public static TaskResult Success(
        string taskName,
        float duration = 0f,
        string message = null,
        string nodeId = null,
        string dependencyKey = null,
        string errorCode = null)
    {
        return new TaskResult
        {
            Outcome = TaskOutcome.Succeeded,
            FailReason = TaskFailReason.None,
            CancelReason = TaskCancelReason.None,
            TaskName = taskName,
            Duration = duration,
            Message = message,
            IsFatal = false,
            NodeId = nodeId,
            DependencyKey = dependencyKey,
            ErrorCode = errorCode
        };
    }

    public static TaskResult Fail(
        string taskName,
        TaskFailReason reason,
        string message = null,
        Exception exception = null,
        bool isFatal = false,
        float duration = 0f,
        string nodeId = null,
        string dependencyKey = null,
        string errorCode = null)
    {
        return new TaskResult
        {
            Outcome = TaskOutcome.Failed,
            FailReason = reason,
            CancelReason = TaskCancelReason.None,
            Message = message,
            Exception = exception,
            TaskName = taskName,
            Duration = duration,
            IsFatal = isFatal,
            NodeId = nodeId,
            DependencyKey = dependencyKey,
            ErrorCode = errorCode
        };
    }

    public static TaskResult Cancel(
        string taskName,
        string message = null,
        float duration = 0f,
        TaskCancelReason cancelReason = TaskCancelReason.None,
        string nodeId = null,
        string dependencyKey = null,
        string errorCode = null)
    {
        return new TaskResult
        {
            Outcome = TaskOutcome.Canceled,
            FailReason = TaskFailReason.None,
            CancelReason = cancelReason,
            Message = message,
            TaskName = taskName,
            Duration = duration,
            IsFatal = false,
            NodeId = nodeId,
            DependencyKey = dependencyKey,
            ErrorCode = errorCode
        };
    }

    public override string ToString()
    {
        if (IsSuccess) return $"[{TaskName}] Success: {Message}";
        if (IsCanceled) return $"[{TaskName}] Canceled ({CancelReason}): {Message}";
        return $"[{TaskName}] Failed ({FailReason}) Fatal={IsFatal}: {Message}";
    }
}

public abstract class TaskBase
{
    public TaskState State { get; private set; } = TaskState.None;
    public TaskResult Result { get; private set; }

    public string Name { get; protected set; }

    /// <summary>
    /// 可选：节点图里的唯一标识
    /// </summary>
    public string NodeId { get; protected set; }

    /// <summary>
    /// 可选：主要依赖 key，例如资源、设备、前置工序
    /// </summary>
    public string DependencyKey { get; protected set; }

    public bool IsDone =>
        State == TaskState.Succeeded ||
        State == TaskState.Failed ||
        State == TaskState.Canceled;

    public bool EnableTickEvent = false;

    public event Action<TaskBase> Started;
    public event Action<TaskBase, float> Ticked;
    public event Action<TaskBase, TaskResult> Succeeded;
    public event Action<TaskBase, TaskResult> Failed;
    public event Action<TaskBase, TaskResult> Canceled;
    public event Action<TaskBase, TaskResult> Completed;

    /// <summary>
    /// 可选：在状态切换前后给调试面板/图系统监听
    /// </summary>
    public event Action<TaskBase, TaskState, TaskState, TaskResult> BeforeTransition;
    public event Action<TaskBase, TaskState, TaskState, TaskResult> AfterTransition;

    private float _startTime;
    private bool _isTransitioning;


    public void Init(string TaskName, string id, string TaskDependencyKey)
    {
        ResetCore();
        Name = TaskName;
        NodeId = id;
        DependencyKey = TaskDependencyKey;
    }

    /// <summary>
    /// 仅允许 None -> Pending
    /// </summary>
    public bool SetPending()
    {
        if (State != TaskState.None) return false;
        State = TaskState.Pending;
        return true;
    }

    public void StartTask()
    {
        if (_isTransitioning) return;
        if (State != TaskState.None && State != TaskState.Pending) return;

        State = TaskState.Running;
        _startTime = Time.time;
        Result = null;

        try
        {
            Started?.Invoke(this);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        try
        {
            OnStart();
        }
        catch (Exception ex)
        {
            OnException(ex);
            Fail(ex.Message, TaskFailReason.Exception, ex, true);
        }
    }

    public void Tick(float dt)
    {
        if (_isTransitioning) return;
        if (State != TaskState.Running) return;

        if (EnableTickEvent)
        {
            try
            {
                Ticked?.Invoke(this, dt);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        try
        {
            bool finished = OnUpdate(dt);
            if (finished)
                Succeed();
        }
        catch (Exception ex)
        {
            OnException(ex);
            Fail(ex.Message, TaskFailReason.Exception, ex, true);
        }
    }

    public void Cancel(
        string message = null,
        TaskCancelReason cancelReason = TaskCancelReason.Manual,
        string errorCode = null)
    {
        if (_isTransitioning) return;
        if (IsDone) return;
        if (State == TaskState.None) return;

        try
        {
            OnCancel();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        var result = TaskResult.Cancel(
            Name ?? GetType().Name,
            message,
            Elapsed(),
            cancelReason,
            NodeId,
            DependencyKey,
            errorCode);

        Transition(TaskState.Canceled, result);
    }

    protected void Succeed(
        string message = null,
        string errorCode = null)
    {
        if (_isTransitioning) return;
        if (IsDone) return;
        if (State != TaskState.Running) return;

        var result = TaskResult.Success(
            Name ?? GetType().Name,
            Elapsed(),
            message,
            NodeId,
            DependencyKey,
            errorCode);

        Transition(TaskState.Succeeded, result);
    }

    protected void Fail(
        string message,
        TaskFailReason reason = TaskFailReason.None,
        Exception exception = null,
        bool isFatal = false)
    {
        if (_isTransitioning) return;
        if (IsDone) return;
        if (State == TaskState.None) return;

        var result = TaskResult.Fail(
            Name ?? GetType().Name,
            reason,
            message,
            exception,
            isFatal,
            Elapsed(),
            NodeId,
            DependencyKey);

        Transition(TaskState.Failed, result);
    }

    private void Transition(TaskState toState, TaskResult result)
    {
        if (_isTransitioning) return;
        if (result == null) return;
        if (IsDone) return;

        var fromState = State;
        _isTransitioning = true;

        try
        {
            try
            {
                BeforeTransition?.Invoke(this, fromState, toState, result);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            State = toState;
            Result = result;

            try
            {
                switch (result.Outcome)
                {
                    case TaskOutcome.Succeeded:
                        OnSucceededInternal(result);
                        Succeeded?.Invoke(this, result);
                        break;

                    case TaskOutcome.Failed:
                        OnFailedInternal(result);
                        Failed?.Invoke(this, result);
                        break;

                    case TaskOutcome.Canceled:
                        OnCanceledInternal(result);
                        Canceled?.Invoke(this, result);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            try
            {
                OnCompletedInternal(result);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            try
            {
                Completed?.Invoke(this, result);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

            try
            {
                AfterTransition?.Invoke(this, fromState, toState, result);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
        finally
        {
            _isTransitioning = false;
            ClearCallbacks();
        }
    }

    protected virtual void ClearCallbacks()
    {
        Started = null;
        Ticked = null;
        Succeeded = null;
        Failed = null;
        Canceled = null;
        Completed = null;
        BeforeTransition = null;
        AfterTransition = null;
        EnableTickEvent = false;
    }

    protected float Elapsed()
    {
        return _startTime <= 0f ? 0f : Time.time - _startTime;
    }

    public void PrepareForReuse()
    {
        if (_isTransitioning)
            Debug.LogWarning($"[{GetType().Name}] PrepareForReuse called while transitioning.");

        ResetCore();
    }

    protected virtual void ResetCore()
    {
        State = TaskState.None;
        Result = null;
        Name = null;
        NodeId = null;
        DependencyKey = null;
        _startTime = 0f;
        _isTransitioning = false;
        ClearCallbacks();
    }

    protected abstract void OnStart();
    protected abstract bool OnUpdate(float dt);

    protected virtual void OnCancel() { }

    protected virtual void OnSucceededInternal(TaskResult result) { }
    protected virtual void OnFailedInternal(TaskResult result) { }
    protected virtual void OnCanceledInternal(TaskResult result) { }
    protected virtual void OnCompletedInternal(TaskResult result) { }

    protected virtual void OnException(Exception ex) { }
}