using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 泛型任务池化基类，所有任务继承它可以自动复用实例。
/// </summary>
public abstract class PooledTask<T> : TaskBase where T : PooledTask<T>, new()
{
    private static readonly Stack<T> _pool = new Stack<T>(64);

    /// <summary>从池子里获取一个任务实例。</summary>
    public static T Acquire()
    {
        if (_pool.Count > 0)
        {
            T task = _pool.Pop();
            task.State = TaskState.None;
            task.Reset();
            return task;
        }
        return new T();
    }

    /// <summary>归还任务实例到池子。</summary>
    public static void Release(T task)
    {
        if (task == null) return;
        task.Cleanup();
        _pool.Push((T)task);
    }

    /// <summary>
    /// 子类可重写以清理临时引用，避免下次复用时残留。
    /// 注意不要在这里做 State 重置，Get 会处理。
    /// </summary>
    protected virtual void Cleanup() { }

    /// <summary>
    /// 子类可重写以初始化/重置自身字段。
    /// </summary>
    protected override void Reset() { base.Reset();}
}