using System;
using System.Collections;
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

public enum TaskResult
{
    Succeeded,
    Failed,
    Canceled
}

public abstract class TaskBase
{
    public TaskState State { get; set; } = TaskState.None;
    public bool IsDone => State == TaskState.Succeeded || State == TaskState.Failed || State == TaskState.Canceled;

    public Action<TaskBase> Started;
    public Action<TaskBase, float> Ticked;
    public bool EnableTickEvent = false;
    public Action<TaskBase> OnSucceeded;
    public Action<TaskBase> OnFailed;
    public Action<TaskBase> OnCanceled;
    public Action<TaskBase, TaskResult> Completed;

    public string Name;

    public void StartTask()
    {
        if (State != TaskState.None && State != TaskState.Pending) return;
        State = TaskState.Running;

        // 新增：Start 事件
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
            Fail();
        }
    }

    public void Tick(float dt)
    {
        if (State != TaskState.Running) return;

        // 新增：Tick 事件（可开关）
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
            if (!OnUpdate(dt)) return; // 未完成
            Succeed();
        }
        catch (Exception ex)
        {
            OnException(ex);
            Fail();
        }
    }

    public void Cancel()
    {
        if (IsDone) return;
        try
        {
            OnCancel();
        }
        catch
        {
        }

        Transition(TaskState.Canceled, TaskResult.Canceled);
    }

    public void SetPending() => State = TaskState.Pending;

    protected void Succeed()
    {
        if (IsDone) return;
        Transition(TaskState.Succeeded, TaskResult.Succeeded);
    }

    protected void Fail()
    {
        if (IsDone) return;
        Transition(TaskState.Failed, TaskResult.Failed);
    }

    private void Transition(TaskState toState, TaskResult result)
    {
        State = toState;

        // 先触发特定结果回调
        try
        {
            switch (result)
            {
                case TaskResult.Succeeded:
                    OnSuccess();
                    OnSucceeded?.Invoke(this);
                    break;
                case TaskResult.Failed:
                    OnFail();
                    OnFailed?.Invoke(this);
                    break;
                case TaskResult.Canceled:
                    OnCancelEnd();
                    OnCanceled?.Invoke(this);
                    break;
            }
        }
        catch
        {
            /* ignore */
        }

        // 再做公共收尾（类似 finally）
        try
        {
            OnComplete(result);
        }
        catch
        {
            /* ignore */
        }

        // 最后发汇总 Completed
        try
        {
            Completed?.Invoke(this, result);
        }
        catch
        {
            /* ignore */
        }

        // 解绑所有事件，防止对象池复用产生泄漏
        Started = null;
        Ticked = null;
        OnSucceeded = null;
        OnFailed = null;
        OnCanceled = null;
        Completed = null;

        // 也可在这里将 EnableTickEvent 复位为 false（看你是否希望复用时默认关闭）
        EnableTickEvent = false;
    }

    // ―― 复位（对象池用）――
    protected virtual void Reset()
    {
        State = TaskState.None;
        Name = null;

        // 清理订阅，保持与 Transition 一致
        Started = null;
        Ticked = null;
        OnSucceeded = null;
        OnFailed = null;
        OnCanceled = null;
        Completed = null;

        EnableTickEvent = false;
    }

    // ―― 子类要实现/可选重写的钩子 ―― //
    protected abstract void OnStart();
    protected abstract bool OnUpdate(float dt);

    /// <summary>显式取消时（Transition 前）调用，可用于停止逻辑或撤销操作。</summary>
    protected virtual void OnCancel()
    {
    }

    /// <summary>成功时专属收尾（在 OnComplete 之前）。</summary>
    protected virtual void OnSuccess()
    {
    }

    /// <summary>失败时专属收尾（在 OnComplete 之前）。</summary>
    protected virtual void OnFail()
    {
    }

    /// <summary>取消时专属收尾（在 OnComplete 之前）。</summary>
    protected virtual void OnCancelEnd()
    {
    }

    /// <summary>公共收尾，统一释放资源（成功/失败/取消都会调用）。</summary>
    protected virtual void OnComplete(TaskResult result)
    {
    }

    /// <summary>捕获 OnStart/OnUpdate 内部异常。</summary>
    protected virtual void OnException(Exception ex)
    {
    }
}
