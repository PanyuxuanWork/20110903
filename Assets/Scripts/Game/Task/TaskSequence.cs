using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TaskSequence : TaskBase
{
    // —— 池化 —— //
    public static TaskSequence Acquire() => ObPool<TaskSequence>.Get().ResetForUse();
    public static void Release(TaskSequence t) => ObPool<TaskSequence>.Release(t);

    private TaskSequence ResetForUse()
    {
        // TaskBase 自己的 Reset 由外部/池管理；这里重置本类字段
        _steps.Clear();
        _cursor = 0;
        _current = null;

        keepLooping = true;
        _stopRequested = false;
        _looping = false;
        IsExclusiveLooping = false;

        _shouldContinue = null;
        _cycleFactory = null;
        StopOnFail = true;
        PropagateCancelToChild = true;
        Name = "TaskSequence";
        return this;
    }

    // —— 固定/基础步骤（非工厂模式时使用）——
    private readonly List<TaskBase> _steps = new List<TaskBase>(8);
    private int _cursor;
    private TaskBase _current;

    // —— 循环控制 —— //
    public bool keepLooping = true;        // 外部可随时置 false；每轮开始前会检查
    private bool _stopRequested = false;   // 内部请求停止（可用于外部调用 RequestStop()）
    private bool _looping = false;         // 是否处于循环模式
    private Func<bool> _shouldContinue;    // 每轮结束后，下一轮开始前的条件检查
    private Func<IEnumerable<TaskBase>> _cycleFactory; // 每轮生成步骤

    public bool IsExclusiveLooping { get; private set; } = false;

    // —— 行为策略 —— //
    public bool StopOnFail { get; private set; } = true;
    public bool PropagateCancelToChild { get; private set; } = true;

    // ========== 配置 API ==========
    /// <summary>
    /// 配置失败策略和取消策略
    /// </summary>
    /// <param StepName="stopOnFail">子任务失败会立即中止整个任务</param>
    /// <param StepName="propagateCancelToChild">序列取消，子任务也取消</param>
    /// <returns></returns>
    public TaskSequence Configure(bool stopOnFail = true, bool propagateCancelToChild = true)
    {
        StopOnFail = stopOnFail;
        PropagateCancelToChild = propagateCancelToChild;
        return this;
    }

    /// <summary>
    /// 开启循环模式
    /// </summary>
    /// <param StepName="exclusiveLooping">开启后普通任务无法抢占</param>
    /// <param StepName="shouldContinue">每一轮开始前检查是否继续</param>
    /// <param StepName="cycleFactory">每一轮生成一组新的子任务</param>
    ///eg:
    ///seq.EnableLooping(true, 
    ///shouldContinue: () => !Depot.IsFull, 
    ///cycleFactory: () => BuildFarmerCycle(resident));
    /// <returns></returns>
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

    /// <summary>请求在当前轮结束后停止。</summary>
    public void RequestStop() => _stopRequested = true;

    // ========== 组装基础步骤（非工厂模式）==========
    /// <summary>
    /// 外部组装
    /// </summary>
    /// <param StepName="step">子任务</param>
    /// <returns></returns>
    public TaskSequence Add(TaskBase step) { if (step != null) _steps.Add(step); return this; }
    /// <summary>
    /// 批量外部组装
    /// </summary>
    /// <param StepName="steps">多个子任务 </param>
    /// <returns></returns>
    public TaskSequence AddRange(IEnumerable<TaskBase> steps)
    {
        if (steps != null) foreach (var s in steps) if (s != null) _steps.Add(s);
        return this;
    }
    /// <summary>
    /// 插入到下一个任务，适用于当前任务结束后，立即执行下一个任务
    /// </summary>
    /// <param StepName="newHead"></param>
    public void InsertFront(IEnumerable<TaskBase> newHead)
    {
        if (newHead == null) return;
        int insertIndex = Mathf.Clamp(_cursor + (_current != null ? 1 : 0), 0, _steps.Count);
        _steps.InsertRange(insertIndex, newHead);
    }
    /// <summary>
    /// 打断当前序列，把后续任务替换成新任务
    /// </summary>
    /// <param StepName="newHead">新任务序列</param>
    public void InterruptAndPrepend(IEnumerable<TaskBase> newHead)
    {
        if (_current != null && PropagateCancelToChild && !_current.IsDone)
        {
            try { _current.Cancel(); } catch (Exception e) { Debug.LogException(e); }
        }
        int removeStart = Mathf.Max(_cursor + 1, 0);
        if (removeStart < _steps.Count) _steps.RemoveRange(removeStart, _steps.Count - removeStart);
        InsertFront(newHead);
    }

    // ========== 生命周期（TaskBase）==========
    protected override void OnStart()
    {
        if (!PrepareCycle()) { Succeed(); return; }
        StartCurrent();
    }

    protected override bool OnUpdate(float dt) => false;

    protected override void OnCancel()
    {
        if (PropagateCancelToChild && _current != null && !_current.IsDone)
        {
            try { _current.Cancel(); } catch (Exception e) { Debug.LogException(e); }
        }
    }

    protected override void OnComplete(TaskResult result)
    {
        if (_current != null) _current.Completed -= OnChildDone;
        _current = null;
        // 清理未运行到的 steps（若有池化任务，这里可以考虑回收）
        _steps.Clear();

        // 归还到池
        Release(this);
    }

    // ========== 内部 ==========
    private bool PrepareCycle()
    {
        // 每轮开始前的“是否继续”检查（先看开关，再看条件）
        if (!keepLooping || _stopRequested) return false;
        if (_shouldContinue != null && !_shouldContinue()) return false;

        // 如有工厂：每轮重建步骤（推荐——每轮新的 Task 实例）
        if (_cycleFactory != null)
        {
            _steps.Clear();
            var fresh = _cycleFactory.Invoke();
            if (fresh != null) foreach (var s in fresh) if (s != null) _steps.Add(s);
        }

        _cursor = 0;
        return _steps.Count > 0;
    }

    private void StartCurrent()
    {
        if (_cursor >= _steps.Count) { OnCycleFinished(); return; }

        _current = _steps[_cursor];
        if (_current == null) { _cursor++; StartCurrent(); return; }

        _current.Completed += OnChildDone;
        try { _current.StartTask(); }
        catch (Exception e) { Debug.LogException(e); Advance(false); }
    }

    private void OnChildDone(TaskBase t, TaskResult r)
    {
        t.Completed -= OnChildDone;
        Advance(r == TaskResult.Succeeded);
    }

    private void Advance(bool ok)
    {
        if (!ok && StopOnFail) { Fail(); return; }
        _cursor++; _current = null;
        StartCurrent();
    }

    private void OnCycleFinished()
    {
        if (_looping && PrepareCycle())
        {
            StartCurrent();
        }
        else
        {
            Succeed(); // 循环结束
        }
    }
}
