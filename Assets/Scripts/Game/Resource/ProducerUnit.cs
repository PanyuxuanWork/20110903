using System;
using System.Collections.Generic;
using UnityEngine;
using Sim.Resources;

/// <summary>
/// 生产者（挂在生产建筑上）：
/// - 典型生产者/消费者模型；支持“无原料生产”的建筑（如水井）
/// - 缺料时向 ProducerContext 请求一批原料；产物有存量时向外提供给 Context 回收
/// - 输入/输出槽可在 Inspector 调整；支持配方
/// </summary>
public class ProducerUnit : MonoBehaviour, IStepListener
{
    public enum State { Idle, WaitingInput, Working, OutputBlocked }

    [Header("关联")]
    public ProducerContext context;         // 可留空，Awake 时自动查找场景里的 ProducerContext
    public Storage inputStorage;       // 原料槽集合（可为空，不需要原料时可不挂/不使用）
    public Storage outputStorage;      // 产物槽集合（必须存在）

    [Header("配方（一次工序）")]
    public Ingredient[] Inputs;             // 可为空数组
    public Ingredient[] Outputs;            // 至少应有一种，否则无意义
    [Min(0.01f)] public float WorkSeconds = 3f;

    [Header("上下限/批量")]
    [Tooltip("缺料时向 Context 请求的批量上限（每次调度）")]
    public int InputRequestBatch = 64;

    [Header("搬运/补货阈值（<=0 表示禁用阈值，保留原行为）")]
    [Tooltip("当任一输入资源的库存 < 该值时，ProducerUnit 会通知 Context 缺料（默认 1）。")]
    [SerializeField] public int InputRequestThreshold = 1;

    [Tooltip("当任一输出资源的库存 >= 该值时，ProducerUnit 会通知 Context 有产物可搬（默认 1）。")]
    [SerializeField] public int OutputOfferThreshold = 1;

    [Header("日志")]
    public bool EnableLogs = true;

    public State Current { get; private set; } = State.Idle;

    // 对外暴露 IStorage 以便 Context 调用
    public IStorage InputStorage => inputStorage;
    public IStorage OutputStorage => outputStorage;

    // 内部
    private float _timer;

    private void Awake()
    {
        if (context == null) context = FindFirstObjectByType<ProducerContext>();

        if (outputStorage == null)
        {
            outputStorage = new GameObject($"{name}_OutputStore").AddComponent<Storage>();
            outputStorage.transform.SetParent(transform, false);
            foreach (var v in Outputs)
            {
                outputStorage.AddOneSlot(v.Id, 100, 20);
            }
        }

        // 若有输入配方但没挂输入仓，则自动创建
        if (inputStorage == null && (Inputs != null && Inputs.Length > 0))
        {
            inputStorage = new GameObject($"{name}_InputStore").AddComponent<Storage>();
            inputStorage.transform.SetParent(transform, false);
            foreach (var v in Inputs)
            {
                inputStorage.AddOneSlot(v.Id, 100, 20);
            }
        }

        // 让 Context 来订阅事件
        if (context != null) context.RegisterProducer(this);
    }

    private void OnEnable()
    {
        if (GlobalStep.Instance != null) // ← 防止域重载/播放状态切换时 NRE
            GlobalStep.Instance.AddListener(this);
    }

    private void OnDisable()
    {
        if (GlobalStep.Instance != null)
            GlobalStep.Instance.RemoveListener(this);
    }

    // 建议放到你的全局步进中统一调用
    public void Tick(float dt)
    {
        switch (Current)
        {
            case State.Idle:
                {
                    if (CanStart())
                    {
                        if (ConsumeInputsAtomically()) // 原料成功消耗
                        {
                            _timer = WorkSeconds;
                            SetState(State.Working);
                            LogLog($"开始生产（{WorkSeconds:0.##}秒）");
                        }
                        else
                        {
                            SetState(State.WaitingInput);
                            if (ShouldNotifyNeeds()) TryNotifyNeeds();  // 没有原料时提醒生产上下文
                        }
                    }
                    else
                    {
                        SetState(State.WaitingInput);
                        TryNotifyNeeds(); TryNotifyNeeds();  // 无原料的情况下进行缺料通知
                    }
                    break;
                }

            case State.WaitingInput:
                {
                    // 等待物流补料。补到位则开工
                    if (CanStart() && ConsumeInputsAtomically())
                    {
                        _timer = WorkSeconds;
                        SetState(State.Working);
                        LogLog("原料到位，进入生产。");
                    }
                    else
                    {
                        TryNotifyNeeds(); TryNotifyNeeds();  // 检查是否缺料
                    }
                    break;
                }

            case State.Working:
                {
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        if (TryStoreOutputs())
                        {
                            SetState(State.Idle);
                            LogLog("生产完成并入库。");
                            if(ShouldNotifyOffers())TryNotifyOffers(); // 检查是否满足提供条件
                        }
                        else
                        {
                            SetState(State.OutputBlocked);
                            LogWarn("产出受阻：输出仓无空间/不接收。");
                            if (ShouldNotifyOffers()) TryNotifyOffers(); // 提醒 Context 清理产出
                        }
                    }
                    break;
                }

            case State.OutputBlocked:
                {
                    // 如果输出仓有空间则恢复生产
                    if (TryStoreOutputs())
                    {
                        SetState(State.Idle);
                        LogLog("阻塞解除。");
                        if (ShouldNotifyOffers()) TryNotifyOffers(); // 入库成功后继续供货
                    }
                    else
                    {
                        if (ShouldNotifyOffers()) TryNotifyOffers();
                    }
                    break;
                }
        }
    }

    private bool ShouldNotifyNeeds()
    {
        // 阈值 <= 0 表示不启用阈值（保持原行为：始终通知）
        if (InputRequestThreshold <= 0) return true;

        // 假定 ProducerUnit 有一个 Inputs 数组（配方输入），每项包含 Id/Qty
        // 并且有某种方式能查询当前库存总量，比如 inputStorage.GetTotalAmount(id)
        // 如果你的项目里把输入分布在多个 storage，请把下面合计逻辑改为对应实现。
        foreach (var inEntry in Inputs) // Inputs 来自你的配方字段
        {
            // 这里用一个假定的 inputStorage：若你有多个 input storages，需要将其合并求和
            // 将下面的 inputStorage.GetTotalAmount 替换为你项目的实际查询方法
            int have = 0;
            if (inputStorage != null) have = inputStorage.GetTotalAmount(inEntry.Id);
            // 如果任一输入项的现有库存 < 阈值，就通知补料
            if (have < InputRequestThreshold) return true;
        }
        return false;
    }

    private bool ShouldNotifyOffers()
    {
        // 阈值 <= 0 表示不启用阈值（保持原行为：始终通知）
        if (OutputOfferThreshold <= 0) return true;

        // 假定 Outputs 数组和 outputStorage 可用（按你的实现替换）
        foreach (var outEntry in Outputs)
        {
            int have = 0;
            if (outputStorage != null) have = outputStorage.GetTotalAmount(outEntry.Id);
            if (have >= OutputOfferThreshold) return true;
        }
        return false;
    }


    // ====== 通知 Context ======

    private void TryNotifyNeeds()
    {
        context.AcceptOneInput(this, Inputs);
    }

    private void TryNotifyOffers()
    {
        context.AcceptOneOutput(this, Outputs);
    }

    

    // ====== 生产内核 ======

    // 是否具备开工条件（允许“无原料配方”）
    private bool CanStart()
    {
        if (Outputs == null || Outputs.Length == 0) return false; // 没有产物就没必要生产
        if (Inputs == null || Inputs.Length == 0) return true;   // 无原料型建筑（如水井）
        if (inputStorage == null) return false;

        for (int i = 0; i < Inputs.Length; i++)
        {
            var ing = Inputs[i];
            if (ing.Id == ResourceId.None || ing.Qty <= 0) continue;
            if (inputStorage.GetAvailable(ing.Id) < ing.Qty) return false;
        }
        return true;
    }

    // 原子消耗所有输入（同一建筑内部，直接用 Add/Remove 原语）
    private bool ConsumeInputsAtomically()
    {
        if (Inputs == null || Inputs.Length == 0) return true; // 无原料直接通过
        if (inputStorage == null) return false;

        for (int i = 0; i < Inputs.Length; i++)
        {
            var ing = Inputs[i];
            if (ing.Id == ResourceId.None || ing.Qty <= 0) continue;

            int removed = inputStorage.RemoveFromAnySlot(ing.Id, ing.Qty);
            if (removed < ing.Qty)
            {
                // 回滚之前已扣的
                for (int j = 0; j < i; j++)
                {
                    var back = Inputs[j];
                    if (back.Id == ResourceId.None || back.Qty <= 0) continue;
                    inputStorage.AddToAnySlot(back.Id, back.Qty);
                }
                return false;
            }
        }
        return true;
    }

    // 将产物写入输出仓；如果一次写不下，失败（等物流清空后重试）
    private bool TryStoreOutputs()
    {
        if (Outputs == null || Outputs.Length == 0 || outputStorage == null) return false;

        // 预检查容量
        for (int i = 0; i < Outputs.Length; i++)
        {
            var p = Outputs[i];
            if (p.Id == ResourceId.None || p.Qty <= 0) continue;
            if (!outputStorage.CanAccept(p.Id)) return false;
            if (outputStorage.GetFreeCapacity(p.Id) < p.Qty) return false;
        }

        // 真正写入
        for (int i = 0; i < Outputs.Length; i++)
        {
            var p = Outputs[i];
            if (p.Id == ResourceId.None || p.Qty <= 0) continue;

            int put = outputStorage.AddToAnySlot(p.Id, p.Qty);
            if (put < p.Qty)
            {
                // 极端并发：回滚已写
                for (int j = 0; j < i; j++)
                {
                    var back = Outputs[j];
                    outputStorage.RemoveFromAnySlot(back.Id, back.Qty);
                }
                return false;
            }
        }
        return true;
    }

    private void SetState(State s)
    {
        if (s == Current) return;
        var prev = Current;
        Current = s;
        LogLog($"状态 {prev} → {Current}");
    }

    // ====== 便捷配置 ======
    [ContextMenu("根据配方自动配置槽位与白名单")]
    public void AutoConfigureSlots()
    {
        if (outputStorage == null)
        {
            outputStorage = new GameObject($"{name}_OutputStore").AddComponent<Storage>();
            outputStorage.transform.SetParent(transform, false);
        }
        if (inputStorage == null && Inputs != null && Inputs.Length > 0)
        {
            inputStorage = new GameObject($"{name}_InputStore").AddComponent<Storage>();
            inputStorage.transform.SetParent(transform, false);
        }

        // 输入槽
        if (inputStorage != null && Inputs != null)
        {
            var defs = new List<(ResourceId, int, int)>();
            var wl = new List<ResourceId>();
            foreach (var ing in Inputs)
            {
                if (ing.Id == ResourceId.None || ing.Qty <= 0) continue;
                defs.Add((ing.Id, Mathf.Max(ing.Qty * 3, 1), 0));
                wl.Add(ing.Id);
            }
            inputStorage.ConfigureSlots(defs.ToArray());
            inputStorage.SetAcceptWhitelist(wl.ToArray());
        }

        // 输出槽
        if (Outputs != null)
        {
            var defs = new List<(ResourceId, int, int)>();
            var wl = new List<ResourceId>();
            foreach (var p in Outputs)
            {
                if (p.Id == ResourceId.None || p.Qty <= 0) continue;
                defs.Add((p.Id, Mathf.Max(p.Qty * 3, 1), 0));
                wl.Add(p.Id);
            }
            outputStorage.ConfigureSlots(defs.ToArray());
            outputStorage.SetAcceptWhitelist(wl.ToArray());
        }

        LogLog("已根据配方自动配置槽位与白名单。");
    }

    // ====== 日志 ======
    private void LogLog(string msg) { if (EnableLogs) TLog.Log(this, msg); }
    private void LogWarn(string msg) { if (EnableLogs) TLog.Warning(this, msg); }
    private void LogDebug(string msg) { if (EnableLogs) TLog.Log(this, msg); } // ← 避免把“调试”当 Error

    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    public void OnTick(in TickContext ctx)
    {
        Tick(ctx.DeltaTime);
    }
}
[Serializable]
public struct Ingredient : IEquatable<Ingredient>
{
    public ResourceId Id;
    public int Qty;

    public bool Equals(Ingredient other)
    {
        return Id == other.Id && Qty == other.Qty;
    }

    public override bool Equals(object obj)
    {
        return obj is Ingredient other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((int)Id, Qty);
    }
}

[Serializable]
public struct NeedResource : IEquatable<NeedResource>
{
    public ResourceId id;
    public int Qty;
    public NeedResourceState Status;

    public NeedResource(ResourceId _id,int _qty,NeedResourceState _state)
    {
        id = _id;
        Qty = _qty;
        Status = _state;
    }

    public NeedResource(Ingredient ingredient)
    {
        id = ingredient.Id;
        Qty = ingredient.Qty;
        Status = NeedResourceState.WaitingServe;
    }


    public bool Equals(NeedResource other)
    {
        return id == other.id && Qty == other.Qty;
    }

    public override bool Equals(object obj)
    {
        return obj is NeedResource other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((int)id, Qty);
    }


}

public enum NeedResourceState
{
    WaitingServe,
    Processing,
    Completed
}
