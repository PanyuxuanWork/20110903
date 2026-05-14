using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Tick 上下文：提供 Tick 序号、累计模拟时间、当前 Tick 的 dt。</summary>
public readonly struct TickContext
{
    public readonly int TickIndex;
    public readonly double SimTime;
    public readonly float DeltaTime;
    public TickContext(int idx, double simTime, float dt)
    { TickIndex = idx; SimTime = simTime; DeltaTime = dt; }
}

/// <summary>
/// 接口式步进监听者：实现并在 OnEnable 里注册到 GlobalStep。
/// Priority 越小越先执行；IsActive=false 时会被清理。
/// </summary>
public interface IStepListener
{
    int Priority { get; set; }   // 0为默认；负数更早，正数更晚
    bool IsActive { get; set; }   // 返回false则自动移除
    void OnTick(in TickContext ctx);
}


[DefaultExecutionOrder(-1000)]
public class GlobalStep : MonoSingleton<GlobalStep>
{
    [Header("Tick Settings")] [Tooltip("单个 Tick 的模拟时长（秒）。例如 0.02 ≈ 50 Tick/g")]
    public float tickSeconds = 0.02f;

    [Tooltip("播放速率：1=正常，0=暂停，2=两倍速")] [Range(0f, 4f)]
    public float timeScale = 1f;

    [Tooltip("每帧最多推进的 Tick 数，防止卡顿雪崩")] public int maxTicksPerFrame = 8;

    [Tooltip("卡顿时是否丢弃多余积压 Tick（true=丢弃保持实时性；false=尽可能补齐）")]
    public bool dropExcessTicks = true;

    // —— 接口式监听 —— //
    private readonly List<IStepListener> _listeners = new List<IStepListener>(256);

    // —— 跨线程投递：后台→主线程（在下一次 Tick 开头执行）—— //
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

    // Tick 累积
    private float _acc;
    private int _tickIndex;
    private double _simTime;

    // 可选：对异步Post的取消
    private CancellationTokenSource _cts;

    protected override void Awake()
    {
        base.Awake(); // 保证单例注册
        _cts = new CancellationTokenSource();
        TLog.Log(this, "全局步进系统初始化完成...");
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }


    private void Update()
    {
        if (tickSeconds <= 1e-6f || timeScale <= 0f) return;

        _acc += Time.deltaTime * timeScale;
        int ticks = 0;

        while (_acc >= tickSeconds && ticks < maxTicksPerFrame)
        {
            _acc -= tickSeconds;
            RunOneTick(tickSeconds * timeScale);
            ticks++;
        }

        if (dropExcessTicks && ticks >= maxTicksPerFrame)
        {
            // 丢弃积压，保持系统“实时”响应（对模拟建造类通常更友好）
            _acc = 0f;
        }
    }

    private void RunOneTick(float dt)
    {
        var ctx = new TickContext(_tickIndex, _simTime, dt);

        // 1) 处理跨线程投递（保证这些逻辑在主线程 & Tick 边界执行）
        DrainMainThreadQueue();

        // 2) 按优先级调用接口式监听者
        if (_listeners.Count > 1)
            _listeners.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        for (int i = 0; i < _listeners.Count; i++)
        {
            var l = _listeners[i];
            if (l == null || !l.IsActive) continue;
            try
            {
                l.OnTick(in ctx);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        _tickIndex++;
        _simTime += dt;

        // 3) 清理失活/空引用
        _listeners.RemoveAll(l => l == null || !l.IsActive);
    }

    private void DrainMainThreadQueue()
    {
        while (_mainThreadQueue.TryDequeue(out var a))
        {
            try
            {
                a?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    // =================== 对外 API ===================

    /// <summary>注册一个步进监听者（接口式）。</summary>
    public void AddListener(IStepListener l)
    {
        if (l != null && !_listeners.Contains(l)) _listeners.Add(l);
    }

    /// <summary>解除注册。</summary>
    public void RemoveListener(IStepListener l) => _listeners.Remove(l);

    /// <summary>
    /// 从任意线程投递一个回调，保证在“下一次 Tick 开头（主线程）”执行。
    /// 适用于：寻路 Job/Task 完成后，把结果、后续状态变更统一投递回主线程。
    /// </summary>
    public void PostFromAnyThread(Action action)
    {
        if (action != null) _mainThreadQueue.Enqueue(action);
    }

    /// <summary>
    /// 在后台线程执行一个异步任务（Task），完成后将“结果处理回调”投递到下一次 Tick。
    /// </summary>
    public async void PostAsync(Func<Task> asyncWork, Action onDone = null, Action<Exception> onError = null)
    {
        try
        {
            await Task.Run(async () =>
            {
                if (asyncWork != null) await asyncWork();
            });
            PostFromAnyThread(onDone);
        }
        catch (Exception e)
        {
            if (onError != null) PostFromAnyThread(() => onError(e));
            else Debug.LogException(e);
        }
    }

    /// <summary>
    /// 支持取消令牌的异步投递：外部可调用 CancelAllAsync() 取消未完成任务。
    /// </summary>
    public async void PostAsync(Func<CancellationToken, Task> asyncWork, Action onDone = null,
        Action<Exception> onError = null)
    {
        try
        {
            var token = _cts?.Token ?? CancellationToken.None;
            await Task.Run(async () =>
            {
                if (asyncWork != null) await asyncWork(token);
            }, token);
            PostFromAnyThread(onDone);
        }
        catch (OperationCanceledException)
        {
            // 可选：忽略或上报取消
        }
        catch (Exception e)
        {
            if (onError != null) PostFromAnyThread(() => onError(e));
            else Debug.LogException(e);
        }
    }

    /// <summary>取消所有通过 PostAsync 提交且尚未完成的后台任务。</summary>
    public void CancelAllAsync()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }
        finally
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
        }
    }

    // 运行时调参
    public void SetTickSeconds(float seconds) => tickSeconds = Mathf.Max(1e-5f, seconds);
    public void SetTimeScale(float scale) => timeScale = Mathf.Max(0f, scale);
    public void Pause(bool pause) => timeScale = pause ? 0f : 1f;
}


