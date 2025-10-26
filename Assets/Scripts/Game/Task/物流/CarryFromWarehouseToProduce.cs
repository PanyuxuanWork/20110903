using System;
using Sim.Resources;
using UnityEngine;

public class CarryFromWarehouseToProduce : TaskBase
{
    public Resident resident;
    public Storage sourceStorage;      // 仓库（来源）
    public ProducerUnit unit;          // 目标生产单元
    public ResourceId id;
    public int amount;

    // 外部传入的票据（由外层先预约好）
    public int storageTicket;   // 来源仓库的 goods ticket（出库）
    public int residentTicket;  // 目标生产单元 input 的 capacity ticket（入库）

    // 内部状态
    private MoveToTask _moveToStorage;
    private MoveToTask _moveToProducer;

    private bool _hasTakenFromStorage = false;
    private int _takenAmount = 0;

    public static CarryFromWarehouseToProduce Create(
        Resident resident,
        Storage sourceStorage,
        ProducerUnit unit,
        ResourceId id,
        int amount,
        int storageTicket,
        int residentTicket)
    {
        var t = new CarryFromWarehouseToProduce();
        t.resident = resident;
        t.sourceStorage = sourceStorage;
        t.unit = unit;
        t.id = id;
        t.amount = amount;
        t.storageTicket = storageTicket;
        t.residentTicket = residentTicket;
        return t;
    }

    protected override void OnStart()
    {
        if (resident == null || sourceStorage == null || unit == null || id == ResourceId.None || amount <= 0)
        {
            Fail();
            return;
        }

        try
        {
            var passMask = resident.economyService?.passMask ?? new byte[] { 1 };
            _moveToStorage = MoveToTask.Create(resident, sourceStorage.transform, passMask);
            _moveToStorage.Completed += OnArrivedStorage;
            resident.taskService.Enqueue(_moveToStorage);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            Fail();
        }
    }

    private void OnArrivedStorage(TaskBase t, TaskResult result)
    {
        try { t.Completed -= OnArrivedStorage; } catch { }

        if (result != TaskResult.Succeeded)
        {
            TryRollbackAndFail("Move to storage failed or cancelled");
            return;
        }

        try
        {
            var storage = sourceStorage as IStorage;
            if (storage == null)
            {
                TryRollbackAndFail("Source storage is invalid");
                return;
            }

            // 从仓库出库（使用外部提供的 storageTicket）
            int taken = storage.OfferResource(storageTicket, amount);
            if (taken <= 0)
            {
                TryRollbackAndFail($"OfferResource returned 0 (ticket={storageTicket}, amount={amount})");
                return;
            }

            // 放入 resident 背包（外层曾为背包预约 capacity 或者背包已预留；此处仍然假定有 capacity）
            var backpack = resident.backpack as IStorage;
            if (backpack == null)
            {
                // 尝试把货放回仓库
                TryReturnToStorage(storage, id, taken);
                TryRollbackAndFail("Resident backpack invalid");
                return;
            }

            int added = backpack.AddToAnySlot(id, taken);
            _hasTakenFromStorage = true;
            _takenAmount = added;

            if (added != taken)
            {
                // 未能全部放入背包 -> 尝试把剩余放回仓库并回滚
                int leftover = taken - added;
                TryReturnToStorage(storage, id, leftover);
                TryRollbackAndFail($"Backpack accepted only {added}/{taken}, rolled back");
                return;
            }

            // 成功拿货并放入背包，开始移动到生产单元
            var passMask = resident.economyService?.passMask ?? new byte[] { 1 };
            _moveToProducer = MoveToTask.Create(resident, unit.transform, passMask);
            _moveToProducer.Completed += OnArrivedProducer;
            resident.taskService.Enqueue(_moveToProducer);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            TryRollbackAndFail("Exception while taking resource from storage: " + ex.Message);
        }
    }

    private void OnArrivedProducer(TaskBase t, TaskResult result)
    {
        try { t.Completed -= OnArrivedProducer; } catch { }

        if (result != TaskResult.Succeeded)
        {
            TryRollbackAndFail("Move to producer failed or cancelled");
            return;
        }

        try
        {
            var backpack = resident.backpack as IStorage;
            var input = unit.inputStorage as IStorage;
            if (backpack == null || input == null)
            {
                TryRollbackAndFail("Backpack or unit inputStorage invalid");
                return;
            }

            int wantToPut = Math.Min(_takenAmount > 0 ? _takenAmount : amount, amount);
            if (wantToPut <= 0)
            {
                TryRollbackAndFail("No cargo to put to producer");
                return;
            }

            int removed = backpack.RemoveFromAnySlot(id, wantToPut);
            if (removed <= 0)
            {
                TryRollbackAndFail("Failed to remove items from backpack");
                return;
            }

            // 把货放入生产单元的 inputStorage (使用 residentTicket 作为 capacity ticket)
            int accepted = input.GetResource(residentTicket, removed);
            if (accepted < removed)
            {
                // 部分被接受 -> 将剩余尝试放回背包（best-effort）
                int leftover = removed - accepted;
                int ret = backpack.AddToAnySlot(id, leftover);
                if (ret < leftover)
                {
                    Debug.LogWarning($"[CarryToProduce] Failed to return {leftover - ret} items back to backpack after partial put.");
                }
            }

            if (accepted > 0)
            {
                resident.ParentArea.producerContext.NotifyInputCompleted(unit, resident.economyService, id, accepted);
            }
            else
            {
                resident.ParentArea.producerContext.NotifyInputFailed(unit, resident.economyService, id, removed);
            }


            Succeed();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            TryRollbackAndFail("Exception while putting resource to producer: " + ex.Message);
        }
    }

    private void TryReturnToStorage(IStorage storage, ResourceId resId, int amountToReturn)
    {
        try
        {
            if (storage != null && amountToReturn > 0)
            {
                storage.AddToAnySlot(resId, amountToReturn);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CarryToProduce] Failed to return {amountToReturn}x{resId} back to storage: {ex.Message}");
        }
    }

    private void TryRollbackAndFail(string reason)
    {
        Debug.LogWarning("[CarryToProduce] Rollback and fail: " + reason);

        // 回滚预约（best-effort）
        try
        {
            if (sourceStorage != null && storageTicket != 0)
            {
                try { (sourceStorage as IStorage)?.CancelGoodsReserve(storageTicket); } catch { }
            }
        }
        catch { }

        try
        {
            if (unit?.inputStorage != null && residentTicket != 0)
            {
                try { (unit.inputStorage as IStorage)?.CancelCapacityReserve(residentTicket); } catch { }
            }
        }
        catch { }

        // 如果已经从仓库拿走了一些货物但尚未成功放入生产单元，尽量把货放回仓库（best-effort）
        try
        {
            if (_hasTakenFromStorage && _takenAmount > 0 && sourceStorage != null)
            {
                TryReturnToStorage(sourceStorage as IStorage, id, _takenAmount);
            }
        }
        catch { }

        Fail();
    }

    protected override bool OnUpdate(float dt)
    {
        // 主逻辑通过 MoveToTask 的回调完成，OnUpdate 无需额外工作
        return false;
    }

    protected override void OnCancel()
    {
        try
        {
            if (_moveToStorage != null && !_moveToStorage.IsDone)
            {
                try { _moveToStorage.Cancel(); } catch { }
                try { _moveToStorage.Completed -= OnArrivedStorage; } catch { }
            }
        }
        catch { }

        try
        {
            if (_moveToProducer != null && !_moveToProducer.IsDone)
            {
                try { _moveToProducer.Cancel(); } catch { }
                try { _moveToProducer.Completed -= OnArrivedProducer; } catch { }
            }
        }
        catch { }

        TryRollbackAndFail("Canceled");
    }

    protected override void Reset()
    {
        base.Reset();
        resident = null;
        sourceStorage = null;
        unit = null;
        id = ResourceId.None;
        amount = 0;
        storageTicket = 0;
        residentTicket = 0;
        _moveToStorage = null;
        _moveToProducer = null;
        _hasTakenFromStorage = false;
        _takenAmount = 0;
    }
}
