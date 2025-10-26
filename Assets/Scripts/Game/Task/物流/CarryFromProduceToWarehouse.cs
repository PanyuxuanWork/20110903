using Sim.Resources;
using System;
using UnityEditor;
using UnityEngine;

public class CarryFromProduceToWarehouse : TaskBase
{
    // --- public fields (for Create convenience / inspector if needed)
    public Resident resident;
    public ProducerUnit unit;
    public Storage targetStorage;
    public ResourceId id;
    public int amount;

    // 外部传入的票据（由外层先预约好）
    public int residentTicket; // 背包的 capacity ticket
    public int storageTicket;  // 生产单元的 goods ticket

    // 内部状态
    private MoveToTask _moveTaskToProducer;
    private MoveToTask _moveTaskToStorage;

    private bool _hasTakenFromProducer = false; // 已经从 producer 取走的标记
    private int _takenAmount = 0;               // 实际从 producer 取到并放入背包的数量

    /// <summary>
    /// 最小 Create 工厂方法（外部负责预约并传入两张票据）
    /// </summary>
    public static CarryFromProduceToWarehouse Create(
        Resident resident,
        ProducerUnit unit,
        Storage targetStorage,
        ResourceId id,
        int amount,
        int residentTicket,
        int storageTicket)
    {
        var t = ObPool<CarryFromProduceToWarehouse>.Get();
        t.resident = resident;
        t.unit = unit;
        t.targetStorage = targetStorage;
        t.id = id;
        t.amount = amount;
        t.residentTicket = residentTicket;
        t.storageTicket = storageTicket;
        return t;
    }

    protected override void OnStart()
    {
        // 基本校验
        if (resident == null || unit == null || targetStorage == null || id == ResourceId.None || amount <= 0)
        {
            Fail();
            return;
        }

        // Enqueue move-to-producer task (使用 resident 的 economyService.passMask)
        try
        {
            var passMask = resident.economyService?.passMask ?? new byte[] { 1 };
            _moveTaskToProducer = MoveToTask.Create(resident, unit.transform, passMask);
            _moveTaskToProducer.Completed += OnArrivedProducer;
            resident.taskService.Enqueue(_moveTaskToProducer);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            Fail();
        }
    }

    // Called when arrived at ProducerUnit (MoveToTask completed)
    private void OnArrivedProducer(TaskBase t, TaskResult result)
    {
        // Unsubscribe
        try { t.Completed -= OnArrivedProducer; } catch { }

        if (result != TaskResult.Succeeded)
        {
            // Move failed / cancelled
            TryRollbackAndFail("Move to producer failed or cancelled");
            return;
        }

        // Try to take goods from producer using the provided storageTicket
        try
        {
            // 1) 从 producer 的 output storage 上申请/提交取货（OfferResource）
            var outputStorage = unit.outputStorage as IStorage;
            if (outputStorage == null)
            {
                TryRollbackAndFail("Producer's outputStorage is null or not IStorage");
                return;
            }

            int taken = outputStorage.OfferResource(storageTicket, amount);
            if (taken <= 0)
            {
                // nothing taken -> treat as failure
                TryRollbackAndFail($"OfferResource returned 0 (ticket={storageTicket}, amount={amount})");
                return;
            }

            // 2) 把取到的货放入 resident 背包（已由外层预约 capacity）
            var backpack = resident.backpack as IStorage;
            if (backpack == null)
            {
                // 将物品尽量放回 producer（best-effort）
                TryReturnToProducer(outputStorage, id, taken);
                TryRollbackAndFail("Resident backpack is null or not IStorage");
                return;
            }

            int added = backpack.AddToAnySlot(id, taken); // 期望 added == taken 因为外层已预约
            _hasTakenFromProducer = true;
            _takenAmount = added;

            if (added != taken)
            {
                // 如果因为某些原因背包没能接受全量，试着把剩下放回 producer（best-effort）
                int leftover = taken - added;
                TryReturnToProducer(outputStorage, id, leftover);
                // 视为部分成功 — 但为了简单起见把剩余也当作失败并回滚（你可根据需要改成继续流）
                TryRollbackAndFail($"Backpack accepted only {added}/{taken}, rolled back");
                return;
            }

            // 到这里：成功将物资从 producer 转入 resident 背包
            // 阶段切换：移动到目标仓库
            var passMask = resident.economyService?.passMask ?? new byte[] { 1 };
            _moveTaskToStorage = MoveToTask.Create(resident, targetStorage.transform, passMask);
            _moveTaskToStorage.Completed += OnArrivedStorage;
            resident.taskService.Enqueue(_moveTaskToStorage);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            TryRollbackAndFail("Exception while taking resource from producer: " + ex.Message);
        }
    }

    // Called when arrived at target Storage
    private void OnArrivedStorage(TaskBase t, TaskResult result)
    {
        try { t.Completed -= OnArrivedStorage; } catch { }

        if (result != TaskResult.Succeeded)
        {
            // Move failed
            TryRollbackAndFail("Move to storage failed or cancelled");
            return;
        }

        // Try to deposit from backpack into target storage using residentTicket (capacity ticket)
        try
        {
            var backpack = resident.backpack as IStorage;
            var dst = targetStorage as IStorage;
            if (backpack == null || dst == null)
            {
                TryRollbackAndFail("Backpack or target storage invalid");
                return;
            }

            // 从背包取出要放的量（取出上一步实际拿到的 _takenAmount 或者 amount，取两者的最小）
            int wantToPut = Math.Min(_takenAmount > 0 ? _takenAmount : amount, amount);
            if (wantToPut <= 0)
            {
                TryRollbackAndFail("No cargo to put");
                return;
            }

            int removed = backpack.RemoveFromAnySlot(id, wantToPut);
            if (removed <= 0)
            {
                TryRollbackAndFail("Failed to remove items from backpack");
                return;
            }

            int accepted = dst.GetResource(residentTicket, removed); // 往仓库写入
            if (accepted < removed)
            {
                // 部分被接受 — 将剩余放回背包（best-effort）
                int leftover = removed - accepted;
                int ret = backpack.AddToAnySlot(id, leftover);
                if (ret < leftover)
                {
                    // 丢失风险 — 记录日志（努力挽回）
                    Debug.LogWarning($"[CarryTask] Failed to return {leftover - ret} items back to backpack after partial put.");
                }
            }

            if (accepted > 0)
            {
              resident.ParentArea.producerContext.NotifyOutputCompleted(unit, resident.economyService, id, accepted);
            }
            else
            {
              resident.ParentArea.producerContext.NotifyOutputFailed(unit, resident.economyService, id, removed);
            }

            Succeed();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            TryRollbackAndFail("Exception while putting resource to target: " + ex.Message);
        }
    }

    // Best-effort: try to return items back to producer storage
    private void TryReturnToProducer(IStorage producerStorage, ResourceId resId, int amountToReturn)
    {
        try
        {
            if (producerStorage != null && amountToReturn > 0)
            {
                producerStorage.AddToAnySlot(resId, amountToReturn);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CarryTask] Failed to return {amountToReturn}x{resId} back to producer: {ex.Message}");
        }
    }

    private void TryRollbackAndFail(string reason)
    {
        Debug.LogWarning("[CarryTask] Rollback and fail: " + reason);

        // 回滚预约（best-effort）
        try
        {
            if (unit != null && storageTicket != 0)
            {
                try { (unit.outputStorage as IStorage)?.CancelGoodsReserve(storageTicket); } catch { }
            }
        }
        catch { }

        try
        {
            if (resident?.backpack != null && residentTicket != 0)
            {
                try { (resident.backpack as IStorage)?.CancelCapacityReserve(residentTicket); } catch { }
            }
        }
        catch { }

        // 如果已经从 producer 拿走了一些货物但尚未成功入库，尽量把货放回 producer（best-effort）
        try
        {
            if (_hasTakenFromProducer && _takenAmount > 0 && unit?.outputStorage != null)
            {
                TryReturnToProducer(unit.outputStorage as IStorage, id, _takenAmount);
            }
        }
        catch { }

        Fail();
    }

    protected override bool OnUpdate(float dt)
    {
        // 这个 Task 的主要工作都在 MoveToTask 的回调中完成，
        // 因此在本 OnUpdate 中通常不需要做额外的工作 —— 维持 Task 处于 Running 即可。
        // 返回 false 表示本任务尚未完成；当 MoveTo 的回调调用 Succeed()/Fail() 时任务会结束。
        return false;
    }

    protected override void OnCancel()
    {
        // 取消/抢占：尝试取消当前两个子 MoveToTask（若已入队）
        try
        {
            if (_moveTaskToProducer != null && !_moveTaskToProducer.IsDone)
            {
                try { _moveTaskToProducer.Cancel(); } catch { }
                try { _moveTaskToProducer.Completed -= OnArrivedProducer; } catch { }
            }
        }
        catch { }

        try
        {
            if (_moveTaskToStorage != null && !_moveTaskToStorage.IsDone)
            {
                try { _moveTaskToStorage.Cancel(); } catch { }
                try { _moveTaskToStorage.Completed -= OnArrivedStorage; } catch { }
            }
        }
        catch { }

        // 尝试回滚已做动作
        TryRollbackAndFail("Canceled");
    }

    protected override void Reset()
    {
        base.Reset();
        resident = null;
        unit = null;
        targetStorage = null;
        id = ResourceId.None;
        amount = 0;
        residentTicket = 0;
        storageTicket = 0;
        _moveTaskToProducer = null;
        _moveTaskToStorage = null;
        _hasTakenFromProducer = false;
        _takenAmount = 0;
    }
}
