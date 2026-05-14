using Sim.Resources;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public sealed class CarryFromWarehouseToProduce : TaskBase
{
    public Resident resident;
    public Storage sourceStorage;          // 仓库（来源）
    public ProducerUnit unit;              // 生产单元（目标）
    public ResourceId id;
    public int amount;

    public int storageGoodsTicket;         // 仓库出库票据（已预约）
    public int unitCapTicket;              // Unit.input 容量票据（已预约）

    private int backpackCapTicket;         // 背包容量票据
    private int backpackGoodsTicket;       // 背包出库票据（背包 -> Unit.input）
    private int _moved;                    // 实际装包/运输量

    [ShowInInspector]
    private readonly List<MiniStep> _steps = new List<MiniStep>();

    private int _cursor = -1;
    private bool _rollbackDone;

    public static CarryFromWarehouseToProduce Create(
        Resident resident,
        Storage sourceStorage,
        ProducerUnit unit,
        ResourceId id,
        int amount,
        int storageGoodsTicket,
        int unitCapTicket)
    {
        var t = new CarryFromWarehouseToProduce();
        t.resident = resident;
        t.sourceStorage = sourceStorage;
        t.unit = unit;
        t.id = id;
        t.amount = Mathf.Max(0, amount);
        t.storageGoodsTicket = storageGoodsTicket;
        t.unitCapTicket = unitCapTicket;
        return t;
    }

    protected override void OnStart()
    {
        if (resident?.economyService == null ||
            resident.economyService.backpack == null ||
            sourceStorage == null ||
            unit?.inputStorage == null ||
            id == ResourceId.None ||
            amount <= 0 ||
            storageGoodsTicket == 0 ||
            unitCapTicket == 0)
        {
            Fail("CarryFromWarehouseToProduce: invalid task parameters");
            return;
        }

        var pass = resident.economyService.passMask ?? new byte[] { 1 };

        _steps.Add(new ReserveBackpackCapacityStep(
            "预约背包容量",
            resident,
            id,
            amount,
            ticket => backpackCapTicket = ticket,
            _ => { }));

        _steps.Add(MoveToStep.Create(
            "前往仓库",
            resident,
            sourceStorage.transform.position,
            pass));

        _steps.Add(new TransferFromSourceToBackpackStep(
            "装载到背包",
            resident,
            sourceStorage,
            id,
            amount,
            () => storageGoodsTicket,
            () => backpackCapTicket,
            moved => _moved = moved));

        _steps.Add(new ReserveBackpackGoodsStep(
            "预约背包出库",
            resident,
            id,
            () => _moved,
            ticket => backpackGoodsTicket = ticket));

        _steps.Add(MoveToStep.Create(
            "前往生产单元",
            resident,
            unit.transform.position,
            pass));

        _steps.Add(new TransferFromBackpackToUnitInputStep(
            "卸货到 Unit.input",
            resident,
            unit,
            id,
            () => _moved,
            () => backpackGoodsTicket,
            () => unitCapTicket));

        foreach (var s in _steps)
            s.Completed += OnStepCompleted;

        StartNext();
    }

    protected override bool OnUpdate(float dt)
    {
        if (_cursor < 0 || _cursor >= _steps.Count)
            return false;

        _steps[_cursor].OnUpdate(dt);
        return false;
    }

    private void OnStepCompleted(MiniStep step, MiniStep.Result result)
    {
        if (result == MiniStep.Result.Succeeded)
        {
            StartNext();
        }
        else
        {
            TryRollback();
            Fail($"{step.StepName} failed");
        }
    }

    private void StartNext()
    {
        _cursor++;

        if (_cursor >= _steps.Count)
        {
            resident?.economyService?.ReleaseEmptyBackpackSlotsToNone();

            var hub = resident?.ParentArea?.productionFlowHub;
            hub?.NotifyInputCompleted(unit, id, amount);

            TLog.Log($"[CarryFromWarehouseToProduce] Completed: {amount} x {id}");
            Succeed();
            return;
        }

        _steps[_cursor].OnStart();
    }

    private void TryRollback()
    {
        if (_rollbackDone)
            return;

        _rollbackDone = true;

        try
        {
            if (_moved > 0 &&
                resident?.economyService?.backpack is Storage bp &&
                sourceStorage != null)
            {
                int taken = bp.RemoveFromAnySlot(id, _moved);
                if (taken > 0)
                    sourceStorage.AddToAnySlot(id, taken);
            }
        }
        catch { }

        try
        {
            if (sourceStorage is IStorage s1 && storageGoodsTicket != 0)
                s1.CancelGoodsReserve(storageGoodsTicket);
        }
        catch { }

        try
        {
            if (unit?.inputStorage is IStorage s2 && unitCapTicket != 0)
                s2.CancelCapacityReserve(unitCapTicket);
        }
        catch { }

        try
        {
            if (resident?.economyService != null && backpackCapTicket != 0)
                resident.economyService.CancelBackpackCapacityReserve(backpackCapTicket);
        }
        catch { }

        try
        {
            if (resident?.economyService?.backpack is IStorage s4 && backpackGoodsTicket != 0)
                s4.CancelGoodsReserve(backpackGoodsTicket);
        }
        catch { }

        try
        {
            resident?.economyService?.ReleaseEmptyBackpackSlotsToNone();
        }
        catch { }

        try
        {
            var hub = resident?.ParentArea?.productionFlowHub;
            hub?.NotifyInputFailed(unit, id, amount);
        }
        catch { }
    }

    protected override void OnCompletedInternal(TaskResult result)
    {
        if (result.IsFailed)
            TryRollback();
        else
            resident?.economyService?.ReleaseEmptyBackpackSlotsToNone();

        foreach (var s in _steps)
            s.Completed -= OnStepCompleted;

        _steps.Clear();
    }

    // ========================== Steps ==========================

    private sealed class ReserveBackpackCapacityStep : MiniStep
    {
        public override string StepName { get; set; }

        private readonly Resident _resident;
        private readonly ResourceId _id;
        private readonly int _amount;
        private readonly Action<int> _onSuccess;
        private readonly Action<string> _onFail;

        public ReserveBackpackCapacityStep(
            string name,
            Resident resident,
            ResourceId id,
            int amount,
            Action<int> onSuccess,
            Action<string> onFail)
        {
            StepName = name;
            _resident = resident;
            _id = id;
            _amount = amount;
            _onSuccess = onSuccess;
            _onFail = onFail;
        }

        public override void OnStart()
        {
            var eco = _resident?.economyService;
            if (eco == null)
            {
                string msg = "ResidentEconomyService is null";
                _onFail?.Invoke(msg);
                Fail(msg);
                return;
            }

            if (eco.TryReserveBackpackCapacity(_id, _amount, out int ticket, eco.reservationTtlSec))
            {
                _onSuccess?.Invoke(ticket);
                Succeed();
            }
            else
            {
                string msg = $"TryReserveBackpackCapacity failed. id={_id}, amount={_amount}";
                _onFail?.Invoke(msg);
                Fail(msg);
            }
        }

        public override bool OnUpdate(float dt) => false;
    }

    private sealed class TransferFromSourceToBackpackStep : MiniStep
    {
        public override string StepName { get; set; }

        private readonly Resident _resident;
        private readonly Storage _source;
        private readonly ResourceId _id;
        private readonly int _want;
        private readonly Func<int> _goodsTicketGetter;
        private readonly Func<int> _bpCapTicketGetter;
        private readonly Action<int> _onMoved;

        public TransferFromSourceToBackpackStep(
            string name,
            Resident resident,
            Storage source,
            ResourceId id,
            int want,
            Func<int> goodsTicketGetter,
            Func<int> bpCapTicketGetter,
            Action<int> onMoved)
        {
            StepName = name;
            _resident = resident;
            _source = source;
            _id = id;
            _want = want;
            _goodsTicketGetter = goodsTicketGetter;
            _bpCapTicketGetter = bpCapTicketGetter;
            _onMoved = onMoved;
        }

        public override void OnStart()
        {
            var bp = _resident?.economyService?.backpack as Storage;
            if (bp == null || _source == null)
            {
                Fail("backpack or source is null");
                return;
            }

            int goodsTicket = _goodsTicketGetter();
            if (goodsTicket == 0)
            {
                Fail("source goods ticket is invalid");
                return;
            }

            int removed = _source.OfferResource(goodsTicket, _want);
            if (removed <= 0)
            {
                Fail("source.OfferResource <= 0");
                return;
            }

            int bpCapTicket = _bpCapTicketGetter();
            if (bpCapTicket == 0)
            {
                _source.AddToAnySlot(_id, removed);
                Fail("backpack capacity ticket is invalid");
                return;
            }

            int accepted = bp.GetResource(bpCapTicket, removed);
            if (accepted < removed)
            {
                int leftover = removed - accepted;
                if (leftover > 0)
                    _source.AddToAnySlot(_id, leftover);
            }

            _onMoved?.Invoke(accepted);

            if (accepted > 0)
                Succeed();
            else
                Fail("accepted <= 0");
        }

        public override bool OnUpdate(float dt) => false;
    }

    private sealed class ReserveBackpackGoodsStep : MiniStep
    {
        public override string StepName { get; set; }

        private readonly Resident _resident;
        private readonly ResourceId _id;
        private readonly Func<int> _movedGetter;
        private readonly Action<int> _onSuccess;

        public ReserveBackpackGoodsStep(
            string name,
            Resident resident,
            ResourceId id,
            Func<int> movedGetter,
            Action<int> onSuccess)
        {
            StepName = name;
            _resident = resident;
            _id = id;
            _movedGetter = movedGetter;
            _onSuccess = onSuccess;
        }

        public override void OnStart()
        {
            int moved = _movedGetter();
            var bp = _resident?.economyService?.backpack as IStorage;

            if (moved > 0 && bp != null &&
                bp.TryReserveResource(_id, moved, out int ticket, 10f))
            {
                _onSuccess?.Invoke(ticket);
                Succeed();
            }
            else
            {
                Fail("TryReserveResource on backpack failed");
            }
        }

        public override bool OnUpdate(float dt) => false;
    }

    private sealed class TransferFromBackpackToUnitInputStep : MiniStep
    {
        public override string StepName { get; set; }

        private readonly Resident _resident;
        private readonly ProducerUnit _unit;
        private readonly ResourceId _id;
        private readonly Func<int> _movedGetter;
        private readonly Func<int> _goodsTicketGetter;
        private readonly Func<int> _unitCapTicketGetter;

        public TransferFromBackpackToUnitInputStep(
            string name,
            Resident resident,
            ProducerUnit unit,
            ResourceId id,
            Func<int> movedGetter,
            Func<int> goodsTicketGetter,
            Func<int> unitCapTicketGetter)
        {
            StepName = name;
            _resident = resident;
            _unit = unit;
            _id = id;
            _movedGetter = movedGetter;
            _goodsTicketGetter = goodsTicketGetter;
            _unitCapTicketGetter = unitCapTicketGetter;
        }

        public override void OnStart()
        {
            int moved = _movedGetter();

            var bp = _resident?.economyService?.backpack as Storage;
            var dst = _unit?.inputStorage as Storage;

            if (bp == null || dst == null || moved <= 0)
            {
                Fail("invalid backpack / dst / moved");
                return;
            }

            int off = bp.OfferResource(_goodsTicketGetter(), moved);
            if (off <= 0)
            {
                Fail("backpack.OfferResource <= 0");
                return;
            }

            int put = dst.GetResource(_unitCapTicketGetter(), off);
            if (put < off)
            {
                int leftover = off - put;
                if (leftover > 0)
                    bp.AddToAnySlot(_id, leftover);

                Fail("dst.GetResource partial");
                return;
            }

            _resident?.economyService?.ReleaseEmptyBackpackSlotsToNone();
            Succeed();
        }

        public override bool OnUpdate(float dt) => false;
    }
}
