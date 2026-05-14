using System;
using System.Collections.Generic;
using UnityEngine;

public enum SequenceFailurePolicy
{
    Continue,       // 子任务失败也继续后续
    StopSequence,   // 子任务失败则整个序列失败
    ByTaskResult    // 按子任务 result.IsFatal 决定
}

public sealed class TaskSequence : TaskBase
{
    private readonly List<TaskBase> _steps = new List<TaskBase>(8);

    private int _cursor;
    private TaskBase _current;
    private TaskResult _lastChildResult;

    // —— 循环控制 —— //
    public bool keepLooping = true;
    private bool _stopRequested = false;
    private bool _looping = false;
    private Func<bool> _shouldContinue;
    private Func<IEnumerable<TaskBase>> _cycleFactory;

    public bool IsExclusiveLooping { get; private set; } = false;

    // —— 行为策略 —— //
    public SequenceFailurePolicy FailurePolicy { get; private set; } = SequenceFailurePolicy.StopSequence;
    public bool PropagateCancelToChild { get; private set; } = true;

    public TaskSequence()
    {
        Name = "TaskSequence";
    }

    /// <summary>
    /// 兼容旧逻辑：
    /// stopOnFail = true  -> StopSequence
    /// stopOnFail = false -> Continue
    /// </summary>
    public TaskSequence Configure(bool stopOnFail = true, bool propagateCancelToChild = true)
    {
        FailurePolicy = stopOnFail
            ? SequenceFailurePolicy.StopSequence
            : SequenceFailurePolicy.Continue;

        PropagateCancelToChild = propagateCancelToChild;
        return this;
    }

    public TaskSequence Configure(
        SequenceFailurePolicy failurePolicy,
        bool propagateCancelToChild = true)
    {
        FailurePolicy = failurePolicy;
        PropagateCancelToChild = propagateCancelToChild;
        return this;
    }

    public TaskSequence EnableLooping(
        bool exclusiveLooping,
        Func<bool> shouldContinue = null,
        Func<IEnumerable<TaskBase>> cycleFactory = null)
    {
        _looping = true;
        _stopRequested = false;
        keepLooping = true;
        IsExclusiveLooping = exclusiveLooping;

        _shouldContinue = shouldContinue;
        _cycleFactory = cycleFactory;
        return this;
    }

    /// <summary>
    /// 请求在当前轮结束后停止
    /// </summary>
    public void RequestStop()
    {
        _stopRequested = true;
    }

    public TaskSequence Add(TaskBase step)
    {
        if (step != null)
            _steps.Add(step);

        return this;
    }

    public TaskSequence AddRange(IEnumerable<TaskBase> steps)
    {
        if (steps == null) return this;

        foreach (var s in steps)
        {
            if (s != null)
                _steps.Add(s);
        }

        return this;
    }

    /// <summary>
    /// 插入到当前任务之后，适用于当前任务结束后立即执行
    /// </summary>
    public void InsertFront(IEnumerable<TaskBase> newHead)
    {
        if (newHead == null) return;

        int insertIndex = Mathf.Clamp(_cursor + (_current != null ? 1 : 0), 0, _steps.Count);
        _steps.InsertRange(insertIndex, newHead is List<TaskBase> list ? list : new List<TaskBase>(newHead));
    }

    /// <summary>
    /// 打断当前序列，把后续任务替换成新任务
    /// </summary>
    public void InterruptAndPrepend(IEnumerable<TaskBase> newHead)
    {
        if (newHead == null) return;

        if (_current != null && !_current.IsDone && PropagateCancelToChild)
        {
            try
            {
                _current.Cancel(
                    "Interrupted by TaskSequence",
                    TaskCancelReason.GraphAborted);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        int removeStart = Mathf.Max(_cursor + 1, 0);
        if (removeStart < _steps.Count)
            _steps.RemoveRange(removeStart, _steps.Count - removeStart);

        InsertFront(newHead);
    }

    protected override void OnStart()
    {
        if (!PrepareCycle())
        {
            Succeed("TaskSequence has no executable steps");
            return;
        }

        StartCurrent();
    }

    protected override bool OnUpdate(float dt)
    {
        if (_current == null)
            return false;

        if (!_current.IsDone)
            _current.Tick(dt);

        return false;
    }

    protected override void OnCancel()
    {
        if (PropagateCancelToChild && _current != null && !_current.IsDone)
        {
            try
            {
                _current.Cancel(
                    "Canceled by TaskSequence",
                    TaskCancelReason.GraphAborted);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    protected override void OnCompletedInternal(TaskResult result)
    {
        if (_current != null)
            _current.Completed -= OnChildDone;

        _current = null;
        _lastChildResult = null;
        _steps.Clear();
    }

    protected override void ResetCore()
    {
        base.ResetCore();

        _steps.Clear();
        _cursor = 0;
        _current = null;
        _lastChildResult = null;

        keepLooping = true;
        _stopRequested = false;
        _looping = false;
        IsExclusiveLooping = false;

        _shouldContinue = null;
        _cycleFactory = null;

        FailurePolicy = SequenceFailurePolicy.StopSequence;
        PropagateCancelToChild = true;

        Name = "TaskSequence";
    }

    private bool PrepareCycle()
    {
        // 每轮开始前先检查是否继续
        if (!keepLooping || _stopRequested)
            return false;

        try
        {
            if (_shouldContinue != null && !_shouldContinue())
                return false;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            Fail("TaskSequence shouldContinue threw exception", TaskFailReason.Exception, ex, true);
            return false;
        }

        // 每轮重建步骤（推荐）
        if (_cycleFactory != null)
        {
            _steps.Clear();

            try
            {
                var fresh = _cycleFactory.Invoke();
                if (fresh != null)
                {
                    foreach (var s in fresh)
                    {
                        if (s != null)
                            _steps.Add(s);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Fail("TaskSequence cycleFactory threw exception", TaskFailReason.Exception, ex, true);
                return false;
            }
        }

        _cursor = 0;
        return _steps.Count > 0;
    }

    private void StartCurrent()
    {
        if (IsDone) return;

        if (_cursor >= _steps.Count)
        {
            OnCycleFinished();
            return;
        }

        _current = _steps[_cursor];

        if (_current == null)
        {
            _cursor++;
            StartCurrent();
            return;
        }

        _lastChildResult = null;
        _current.Completed += OnChildDone;

        try
        {
            bool enteredPending = _current.SetPending();
            if (!enteredPending && _current.State == TaskState.None)
            {
                Fail("Child task failed to enter pending state", TaskFailReason.InvalidState, null, true);
                return;
            }

            _current.StartTask();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Fail("TaskSequence child start threw exception", TaskFailReason.Exception, e, true);
        }
    }

    private void OnChildDone(TaskBase child, TaskResult result)
    {
        child.Completed -= OnChildDone;
        _lastChildResult = result;

        bool shouldContinue = ShouldContinueAfterChild(result);

        if (!shouldContinue)
        {
            FailFromChild(child, result);
            return;
        }

        _cursor++;
        _current = null;
        StartCurrent();
    }

    private bool ShouldContinueAfterChild(TaskResult result)
    {
        if (result == null)
            return true;

        if (result.IsSuccess)
            return true;

        if (result.IsCanceled)
            return false;

        switch (FailurePolicy)
        {
            case SequenceFailurePolicy.Continue:
                return true;

            case SequenceFailurePolicy.StopSequence:
                return false;

            case SequenceFailurePolicy.ByTaskResult:
                return !result.IsFatal;

            default:
                return false;
        }
    }

    private void FailFromChild(TaskBase child, TaskResult result)
    {
        if (result == null)
        {
            Fail("TaskSequence child completed without result", TaskFailReason.Unknown, null, true);
            return;
        }

        if (result.IsCanceled)
        {
            Cancel(
                $"TaskSequence canceled by child: {result.Message}",
                result.CancelReason,
                result.ErrorCode);
            return;
        }

        string childName = child?.Name ?? child?.GetType().Name ?? "UnknownChild";
        string msg = $"TaskSequence child failed: {childName} | {result.Message}";

        Fail(
            msg,
            result.FailReason,
            result.Exception,
            result.IsFatal);
    }

    private void OnCycleFinished()
    {
        if (_looping && PrepareCycle())
        {
            StartCurrent();
        }
        else
        {
            if (_lastChildResult != null && _lastChildResult.IsCanceled)
            {
                Cancel(
                    "TaskSequence ended by child cancel",
                    _lastChildResult.CancelReason,
                    _lastChildResult.ErrorCode);
            }
            else
            {
                Succeed("TaskSequence completed");
            }
        }
    }
}