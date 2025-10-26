using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class ResidentTaskService : MonoBehaviour, IStepListener
{
    [SerializeField] public Resident Model;
    [ShowInInspector]
    private readonly Queue<TaskBase> _queue = new Queue<TaskBase>(8);

    [ShowInInspector]
    public TaskBase Current { get; private set; }
    public int QueueCount => _queue.Count;

    private bool _useGlobal;

    public int Priority { get; set; } = 0; // 可调优先级，确保与Pathfinding顺序一致
    public bool IsActive { get; set; } = true; // 可按需加更多条件
    public void OnTick(in TickContext ctx) => TickLocal(ctx.DeltaTime);

    private void Awake()
    {
        Model = GetComponent<Resident>();
    }


    void OnEnable()
    {
        _useGlobal = GlobalStep.Instance != null;
        if (_useGlobal) GlobalStep.Instance.AddListener(this);
        enabled = true; // 确保本地Update可用以做回退
    }

    void OnDisable()
    {
        if (_useGlobal && GlobalStep.Instance != null)
            GlobalStep.Instance.RemoveListener(this);
    }

    void Update()
    {
        if (!_useGlobal) TickLocal(Time.deltaTime);
    }



    private void TickLocal(float dt)
    {
        if (Current != null && !Current.IsDone)
            Current.Tick(dt);                 
    }


    public bool Enqueue(TaskBase task, bool front = false)
    {
        if (task == null || Model == null || !Model.CanAccept) return false;

        // —— 独占中的拒绝（除非 front:true）——
        if (!front && Current is TaskSequence { IsExclusiveLooping: true })
            return false;

        if (front && _queue.Count > 0)
        {
            var tmp = new List<TaskBase>(_queue.Count + 1) { task };
            tmp.AddRange(_queue);
            _queue.Clear();
            foreach (var t in tmp) _queue.Enqueue(t);
        }
        else
        {
            _queue.Enqueue(task);
        }

        TryStartNext();
        return true;
    }

    public void CancelCurrent(bool clearQueue = false)
    {
        if (Current != null)
        {
            Current.Completed -= OnTaskCompleted;
            Current.Cancel();
            Current = null;
        }
        if (clearQueue) _queue.Clear();
    }

    private void TryStartNext()
    {
        if (Current != null && !Current.IsDone) return;
        if (_queue.Count == 0) { Current = null; return; }

        var next = _queue.Dequeue();
        Current = next;
        next.Completed += OnTaskCompleted;

        Debug.Log($"[ResidentTaskService] Start task: {next.Name ?? next.GetType().Name}");
        next.StartTask();
    }

    private void OnTaskCompleted(TaskBase task, TaskResult result)
    {
        task.Completed -= OnTaskCompleted;
        Current = null;
        TryStartNext();
    }

    // ========= “仅下一帧”抢占 =========
    private Coroutine _preemptCoro;

    /// <summary>
    /// 在“下一帧”进行抢占：清空当前与队列，优先执行 preempt；若提供 after，则在 preempt 之后继续执行 after。
    /// 注意：若当前是独占循环序列，仍然能被此 API 抢占（设计上允许“硬抢占”）。
    /// </summary>
    public void PreemptNextFrame(TaskBase preempt, TaskBase after = null)
    {
        if (preempt == null) return;
        if (_preemptCoro != null) StopCoroutine(_preemptCoro);
        _preemptCoro = StartCoroutine(CoPreemptNextFrame(preempt, after));
    }

    private IEnumerator CoPreemptNextFrame(TaskBase preempt, TaskBase after)
    {
        yield return null; // 等下一帧

        // 抢占：取消当前 + 清空队列
        CancelCurrent(clearQueue: true);

        // 执行抢占任务
        Enqueue(preempt, front: true);

        // 抢占任务完成后，自动继续 'after'（若提供）
        if (after != null) Enqueue(after, front: false);

        _preemptCoro = null;
    }
}
