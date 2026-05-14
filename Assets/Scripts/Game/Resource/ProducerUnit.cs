/***************************************************************************
// File       : ProductBuilding.cs
// Author     : Panyuxuan
// Created    : 2025/10/30
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
using Sim.Resources;
using Sirenix.OdinInspector;

/// <summary>
/// 生产者（挂在生产建筑上）：
/// - 典型生产者/消费者模型；支持“无原料生产”的建筑（如水井）
/// - 缺料时向 ProducerContext 请求一批原料；产物有存量时向外提供给 Context 回收
/// - 输入/输出槽可在 Inspector 调整；支持配方
/// </summary>
public class ProducerUnit : MonoBehaviour, IStepListener
{
    public enum State
    {
        Idle,
        WaitingInput,
        Working,
        OutputBlocked,
        WaitingEmployee
    }

    public List<Resident> EmployeeLists = new();

    [Header("关联")]
    public ProductionFlowHub context;
    public Storage inputStorage;
    public Storage outputStorage;

    [Header("配方（一次工序）")]
    public Ingredient[] Inputs;
    public Ingredient[] Outputs;
    [Min(0.01f)] public float WorkSeconds = 3f;

    [Header("上下限/批量")]
    [Tooltip("缺料时向 Context 请求的批量上限（每次调度）")]
    public int InputRequestBatch = 64;

    [Header("搬运/补货阈值（<=0 表示禁用阈值，改为按配方项直接判断）")]
    [Tooltip("当任一输入资源的库存 < 该值时，ProducerUnit 会通知 Context 缺料（默认 1）。")]
    [SerializeField] public int InputRequestThreshold = 1;

    [Tooltip("当任一输出资源的库存 >= 该值时，ProducerUnit 会通知 Context 有产物可搬（默认 1）。")]
    [SerializeField] public int OutputOfferThreshold = 1;

    [Header("通知节流")]
    [Tooltip("避免 WaitingInput / OutputBlocked 状态下每帧重复通知。")]
    [SerializeField] private float notifyCooldown = 0.5f;

    [Header("日志")]
    public bool EnableLogs = true;

    public bool isOpen = true;

    [ShowInInspector]
    public State Current { get; private set; } = State.Idle;

    public IStorage InputStorage => inputStorage;
    public IStorage OutputStorage => outputStorage;

    private float _timer;
    private float _nextInputNotifyAt;
    private float _nextOutputNotifyAt;

    private void Awake()
    {
        if (context == null)
            context = FindFirstObjectByType<ProductionFlowHub>();

        EnsureStorages();
    }

    private void OnEnable()
    {
        if (GlobalStep.Instance != null)
            GlobalStep.Instance.AddListener(this);

        if (context == null)
            context = FindFirstObjectByType<ProductionFlowHub>();

        context?.RegisterProducer(this);
    }

    private void OnDisable()
    {
        if (GlobalStep.Instance != null)
            GlobalStep.Instance.RemoveListener(this);

        context?.UnregisterProducer(this);
    }

    /// <summary>
    /// 全局步进入口。
    /// 生产状态、缺料通知、产出阻塞恢复都在这里推进。
    /// </summary>
    public void Tick(float dt)
    {
        if (!isOpen)
            return;

        if (!HasEmployee())
        {
            SetState(State.WaitingEmployee);
            return;
        }

        switch (Current)
        {
            case State.WaitingEmployee:
                {
                    if (HasEmployee())
                        SetState(State.Idle);
                    break;
                }

            case State.Idle:
                {
                    if (CanStart())
                    {
                        if (ConsumeInputsAtomically())
                        {
                            _timer = WorkSeconds;
                            SetState(State.Working);
                            LogLog($"开始生产（{WorkSeconds:0.##}秒）");
                        }
                        else
                        {
                            SetState(State.WaitingInput);
                            NotifyMissingInputsIfNeeded(force: true);
                        }
                    }
                    else
                    {
                        SetState(State.WaitingInput);
                        NotifyMissingInputsIfNeeded(force: true);
                    }

                    break;
                }

            case State.WaitingInput:
                {
                    if (CanStart() && ConsumeInputsAtomically())
                    {
                        _timer = WorkSeconds;
                        SetState(State.Working);
                        LogLog("原料到位，进入生产。");
                    }
                    else
                    {
                        NotifyMissingInputsIfNeeded(force: false);
                    }

                    break;
                }

            case State.Working:
                {
                    _timer -= dt;
                    if (_timer > 0f)
                        break;

                    if (TryStoreOutputs())
                    {
                        SetState(State.Idle);
                        LogLog("生产完成并入库。");
                        NotifyAvailableOutputsIfNeeded(force: true);
                    }
                    else
                    {
                        SetState(State.OutputBlocked);
                        LogWarn("产出受阻：输出仓无空间/不接收。");
                        NotifyAvailableOutputsIfNeeded(force: true);
                    }

                    break;
                }

            case State.OutputBlocked:
                {
                    if (TryStoreOutputs())
                    {
                        SetState(State.Idle);
                        LogLog("阻塞解除。");
                        NotifyAvailableOutputsIfNeeded(force: true);
                    }
                    else
                    {
                        NotifyAvailableOutputsIfNeeded(force: false);
                    }

                    break;
                }
        }
    }

    /// <summary>
    /// 是否具备开工条件。
    /// 支持“无输入配方”的建筑。
    /// </summary>
    public bool CanStart()
    {
        if (Outputs == null || Outputs.Length == 0)
            return false;

        if (Inputs == null || Inputs.Length == 0)
            return true;

        if (inputStorage == null)
            return false;

        for (int i = 0; i < Inputs.Length; i++)
        {
            var ing = Inputs[i];
            if (!IsValidIngredient(ing))
                continue;

            if (inputStorage.GetAvailable(ing.Id) < ing.Qty)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 根据当前配方自动配置输入/输出仓位。
    /// </summary>
    [ContextMenu("根据配方自动配置槽位与白名单")]
    public void AutoConfigureSlots()
    {
        EnsureStorages();

        if (inputStorage != null && Inputs != null)
        {
            var defs = new List<(ResourceId, int, int)>();
            foreach (var ing in Inputs)
            {
                if (!IsValidIngredient(ing))
                    continue;

                defs.Add((ing.Id, Mathf.Max(ing.Qty * 3, 1), 0));
            }

            inputStorage.ConfigureSlots(defs.ToArray());
        }

        if (outputStorage != null && Outputs != null)
        {
            var defs = new List<(ResourceId, int, int)>();
            foreach (var p in Outputs)
            {
                if (!IsValidIngredient(p))
                    continue;

                defs.Add((p.Id, Mathf.Max(p.Qty * 3, 1), 0));
            }

            outputStorage.ConfigureSlots(defs.ToArray());
        }

        LogLog("已根据配方自动配置槽位。");
    }

    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    public void OnTick(in TickContext ctx)
    {
        Tick(ctx.DeltaTime);
    }

    private void EnsureStorages()
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

        if (outputStorage != null && Outputs != null)
        {
            foreach (var v in Outputs)
            {
                if (!IsValidIngredient(v))
                    continue;

                TryEnsureSlot(outputStorage, v.Id, Mathf.Max(v.Qty * 3, 1), 77);
            }
        }

        if (inputStorage != null && Inputs != null)
        {
            foreach (var v in Inputs)
            {
                if (!IsValidIngredient(v))
                    continue;

                TryEnsureSlot(inputStorage, v.Id, Mathf.Max(v.Qty * 3, 1), 30);
            }
        }
    }

    private bool HasEmployee()
    {
        return EmployeeLists != null && EmployeeLists.Count > 0;
    }

    private bool ConsumeInputsAtomically()
    {
        if (Inputs == null || Inputs.Length == 0)
            return true;

        if (inputStorage == null)
            return false;

        for (int i = 0; i < Inputs.Length; i++)
        {
            var ing = Inputs[i];
            if (!IsValidIngredient(ing))
                continue;

            int removed = inputStorage.RemoveFromAnySlot(ing.Id, ing.Qty);
            if (removed < ing.Qty)
            {
                for (int j = 0; j < i; j++)
                {
                    var back = Inputs[j];
                    if (!IsValidIngredient(back))
                        continue;

                    inputStorage.AddToAnySlot(back.Id, back.Qty);
                }

                return false;
            }
        }

        return true;
    }

    private bool TryStoreOutputs()
    {
        if (Outputs == null || Outputs.Length == 0 || outputStorage == null)
            return false;

        for (int i = 0; i < Outputs.Length; i++)
        {
            var p = Outputs[i];
            if (!IsValidIngredient(p))
                continue;

            if (!outputStorage.CanAccept(p.Id))
                return false;

            if (outputStorage.GetFreeCapacity(p.Id) < p.Qty)
                return false;
        }

        for (int i = 0; i < Outputs.Length; i++)
        {
            var p = Outputs[i];
            if (!IsValidIngredient(p))
                continue;

            int put = outputStorage.AddToAnySlot(p.Id, p.Qty);
            if (put < p.Qty)
            {
                for (int j = 0; j < i; j++)
                {
                    var back = Outputs[j];
                    if (!IsValidIngredient(back))
                        continue;

                    outputStorage.RemoveFromAnySlot(back.Id, back.Qty);
                }

                return false;
            }
        }

        return true;
    }

    private void NotifyMissingInputsIfNeeded(bool force)
    {
        if (context == null)
            return;

        float now = Time.unscaledTime;
        if (!force && now < _nextInputNotifyAt)
            return;

        var list = CollectMissingInputs();
        try
        {
            if (list.Count == 0)
                return;

            for (int i = 0; i < list.Count; i++)
                TryNotifyNeed(list[i]);

            _nextInputNotifyAt = now + Mathf.Max(0.05f, notifyCooldown);
        }
        finally
        {
            ObPool<List<Ingredient>>.Release(list);
        }
    }

    private void NotifyAvailableOutputsIfNeeded(bool force)
    {
        if (context == null)
            return;

        float now = Time.unscaledTime;
        if (!force && now < _nextOutputNotifyAt)
            return;

        var list = CollectAvailableOutputs();
        try
        {
            if (list.Count == 0)
                return;

            for (int i = 0; i < list.Count; i++)
                TryNotifyOffer(list[i]);

            _nextOutputNotifyAt = now + Mathf.Max(0.05f, notifyCooldown);
        }
        finally
        {
            ObPool<List<Ingredient>>.Release(list);
        }
    }

    private List<Ingredient> CollectMissingInputs()
    {
        var list = ObPool<List<Ingredient>>.Get();
        list.Clear();

        if (Inputs == null || Inputs.Length == 0)
            return list;

        for (int i = 0; i < Inputs.Length; i++)
        {
            var inEntry = Inputs[i];
            if (!IsValidIngredient(inEntry))
                continue;

            int have = inputStorage != null ? inputStorage.GetTotalAmount(inEntry.Id) : 0;

            if (InputRequestThreshold <= 0)
            {
                if (have < inEntry.Qty)
                    list.Add(inEntry);
            }
            else
            {
                if (have < InputRequestThreshold)
                    list.Add(inEntry);
            }
        }

        return list;
    }

    private List<Ingredient> CollectAvailableOutputs()
    {
        var list = ObPool<List<Ingredient>>.Get();
        list.Clear();

        if (Outputs == null || Outputs.Length == 0)
            return list;

        for (int i = 0; i < Outputs.Length; i++)
        {
            var outEntry = Outputs[i];
            if (!IsValidIngredient(outEntry))
                continue;

            int have = outputStorage != null ? outputStorage.GetTotalAmount(outEntry.Id) : 0;

            if (OutputOfferThreshold <= 0)
            {
                if (have > 0)
                    list.Add(outEntry);
            }
            else
            {
                if (have >= OutputOfferThreshold)
                    list.Add(outEntry);
            }
        }

        return list;
    }

    private void TryNotifyNeed(Ingredient v)
    {
        if (context == null || !IsValidIngredient(v))
            return;

        context.AcceptOneInput(this, new[] { v });
        LogDebug($"已通知缺料：{v.Id} x{v.Qty}");
    }

    private void TryNotifyOffer(Ingredient v)
    {
        if (context == null || !IsValidIngredient(v))
            return;

        context.AcceptOneOutput(this, new[] { v });
        LogDebug($"已通知供货：{v.Id} x{v.Qty}");
    }

    private static bool IsValidIngredient(Ingredient ing)
    {
        return ing.Id != ResourceId.None &&  ing.Qty > 0;
    }

    private static void TryEnsureSlot(Storage storage, ResourceId id, int capacity, int order)
    {
        if (storage == null)
            return;

        var slots = storage.Slots;
        if (slots != null)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                    continue;

                if (slot.Id == id)
                    return;
            }
        }

        storage.AddOneSlot(id, capacity, order);
    }

    private void SetState(State s)
    {
        if (s == Current)
            return;

        var prev = Current;
        Current = s;
        LogLog($"状态 {prev} → {Current}");
    }

    private void LogLog(string msg)
    {
        if (EnableLogs)
            TLog.Log(this, msg);
    }

    private void LogWarn(string msg)
    {
        if (EnableLogs)
            TLog.Warning(this, msg);
    }

    private void LogDebug(string msg)
    {
        if (EnableLogs)
            TLog.Log(this, msg);
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
public class NeedResource : IEquatable<NeedResource>
{
    [ShowInInspector] public ResourceId id;
    [ShowInInspector] public int Qty;
    [ShowInInspector] public NeedResourceState Status;


    public NeedResource(ResourceId _id, int _qty, NeedResourceState _state)
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
        if (other is null)
            return false;

        return id == other.id && Qty == other.Qty && Status == other.Status;
    }

    public override bool Equals(object obj)
    {
        return obj is NeedResource other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((int)id, Qty, (int)Status);
    }


}

public enum NeedResourceState
{
    WaitingServe,
    Processing,
    Completed,
    Requested
}
