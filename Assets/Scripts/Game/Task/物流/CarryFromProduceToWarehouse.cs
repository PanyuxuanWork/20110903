using Sim.Resources;
using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public sealed class CarryFromProduceToWarehouse : TaskBase
{
    public Resident resident;
    public ProducerUnit unit;          // 来源
    public Storage sinkStorage;        // 目标
    public ResourceId id;
    public int amount;
    public int sinkTicket;             // 仓库容量票据（已在派单阶段拿到）
    public int unitGoodsTicket;        // 生产出库票据（已在派单阶段拿到）

    private int backpackCapTicket;     // 背包容量票据
    private int backpackGoodsTicket;   // 背包出库票据
    private int _moved;                // 实际装包量

    [ShowInInspector]
    private readonly List<MiniStep> _steps = new List<MiniStep>();

    private int _cursor = -1;
    private bool _rollbackDone;

    public static CarryFromProduceToWarehouse Create(
        Resident resident,
        ProducerUnit producerUnit,
        Storage sinkStorage,
        ResourceId resourceId,
        int qty,
        int sinkCapTicket,
        int unitGoodsTicket)
    {
        var task = new CarryFromProduceToWarehouse();
        task.resident = resident;
        task.unit = producerUnit;
        task.sinkStorage = sinkStorage;
        task.id = resourceId;
        task.amount = qty;
        task.sinkTicket = sinkCapTicket;
        task.unitGoodsTicket = unitGoodsTicket;
        return task;
    }

    protected override void OnStart()
    {
        if (resident?.economyService == null ||
            resident.economyService.backpack == null ||
            unit?.outputStorage == null ||
            sinkStorage == null ||
            amount <= 0 ||
            id == ResourceId.None ||
            sinkTicket == 0 ||
            unitGoodsTicket == 0)
        {
            Fail("CarryFromProduceToWarehouse: invalid task parameters");
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
            "前往生产设施",
            resident,
            unit.transform.position,
            pass));

        _steps.Add(new TransferFromSourceToBackpackStep(
            "装载到背包",
            resident,
            unit.outputStorage as Storage,
            id,
            amount,
            () => unitGoodsTicket,
            () => backpackCapTicket,
            moved => _moved = moved));

        _steps.Add(new ReserveBackpackGoodsStep(
            "预约背包出库",
            resident,
            id,
            () => _moved,
            ticket => backpackGoodsTicket = ticket));

        _steps.Add(MoveToStep.Create(
            "前往仓库",
            resident,
            sinkStorage.transform.position,
            pass));

        _steps.Add(new TransferFromBackpackToSinkStep(
            "卸货到仓库",
            resident,
            sinkStorage,
            id,
            () => _moved,
            () => backpackGoodsTicket,
            () => sinkTicket));

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
            hub?.NotifyOutputCompleted(unit, id, amount);

            TLog.Log($"[CarryFromProduceToWarehouse] Completed: {amount} x {id}");
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
                unit?.outputStorage is Storage src)
            {
                int taken = bp.RemoveFromAnySlot(id, _moved);
                if (taken > 0)
                    src.AddToAnySlot(id, taken);
            }
        }
        catch { }

        try
        {
            if (unit?.outputStorage is IStorage s1 && unitGoodsTicket != 0)
                s1.CancelGoodsReserve(unitGoodsTicket);
        }
        catch { }

        try
        {
            if (sinkStorage is IStorage s2 && sinkTicket != 0)
                s2.CancelCapacityReserve(sinkTicket);
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
            hub?.NotifyOutputFailed(unit, id, amount);
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
        public override void OnComplete(Result result) { }
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
            if (bp == null)
            {
                Fail("backpack is null");
                return;
            }

            if (_source == null)
            {
                Fail("source is null");
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
        public override void OnComplete(Result result) { }
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
            if (moved <= 0)
            {
                Fail("moved <= 0");
                return;
            }

            var bp = _resident?.economyService?.backpack as IStorage;
            if (bp == null)
            {
                Fail("backpack is null");
                return;
            }

            if (bp.TryReserveResource(_id, moved, out int ticket, 10f))
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
        public override void OnComplete(Result result) { }
    }

    private sealed class TransferFromBackpackToSinkStep : MiniStep
    {
        public override string StepName { get; set; }

        private readonly Resident _resident;
        private readonly Storage _sink;
        private readonly ResourceId _id;
        private readonly Func<int> _movedGetter;
        private readonly Func<int> _goodsTicketGetter;
        private readonly Func<int> _sinkCapTicketGetter;

        public TransferFromBackpackToSinkStep(
            string name,
            Resident resident,
            Storage sink,
            ResourceId id,
            Func<int> movedGetter,
            Func<int> goodsTicketGetter,
            Func<int> sinkCapTicketGetter)
        {
            StepName = name;
            _resident = resident;
            _sink = sink;
            _id = id;
            _movedGetter = movedGetter;
            _goodsTicketGetter = goodsTicketGetter;
            _sinkCapTicketGetter = sinkCapTicketGetter;
        }

        public override void OnStart()
        {
            int moved = _movedGetter();
            if (moved <= 0)
            {
                Fail("moved <= 0");
                return;
            }

            var bp = _resident?.economyService?.backpack as Storage;
            if (bp == null)
            {
                Fail("backpack is null");
                return;
            }

            if (_sink == null)
            {
                Fail("sink is null");
                return;
            }

            int goodsTicket = _goodsTicketGetter();
            if (goodsTicket == 0)
            {
                Fail("backpack goods ticket is invalid");
                return;
            }

            int off = bp.OfferResource(goodsTicket, moved);
            if (off <= 0)
            {
                Fail("backpack.OfferResource <= 0");
                return;
            }

            int sinkCapTicket = _sinkCapTicketGetter();
            if (sinkCapTicket == 0)
            {
                bp.AddToAnySlot(_id, off);
                Fail("sink capacity ticket is invalid");
                return;
            }

            int put = _sink.GetResource(sinkCapTicket, off);
            if (put < off)
            {
                int leftover = off - put;
                if (leftover > 0)
                    bp.AddToAnySlot(_id, leftover);

                Fail("sink.GetResource partial");
                return;
            }

            _resident?.economyService?.ReleaseEmptyBackpackSlotsToNone();
            Succeed();
        }

        public override bool OnUpdate(float dt) => false;
        public override void OnComplete(Result result) { }
    }
}
public sealed class MoveToStep : MiniStep
{
    public override string StepName { get; set; }

    // --- 可调参数（可按需在 Create 时覆盖） ---
    private float _speed = 3.5f;
    private bool _rotate = true;
    private float _rotLerp = 10f;

    // 只在“真的卡住”时重算路径
    private bool _repathOnBlocked = true;

    // 连续无进展多久视为卡住（秒）/ 判定区间最小位移（米）/ 采样间隔（秒）
    private float _stuckTimeout = 1.5f;
    private float _minProgress = 0.25f;
    private float _progressCheckInterval = 0.5f;

    // 重算后冷却（秒）
    private float _repathCooldown = 1.0f;

    // --- 与网格尺度相关 ---
    private float _edgeLength = 1.0f;       // 外部传入
    private float _arriveSqrEps = 1.0f;            // = 0.5 * edge^2
    private float _nodeReachSqr;            // 过路点阈值，建议 edge*0.25

    // --- 运行时状态 ---
    private Resident resident;
    private GridAsset _grid;
    private byte[] _passMask;
    private Vector3 _destination;

    private int[] _path;
    private int _cursor;
    private bool _awaitingPath;

    private Vector3 _lastProgressPos;
    private float _progressTimer;
    private float _stuckAccum;
    private float _cooldownTimer;

    // FinalApproach：路点吃光但离终点还有一点点时，短时间直线靠近
    private bool _finalApproach;
    private float _finalTimer;
    private float _finalTimeout = 1.5f;      // 直靠超时则限次重寻

    // 路点吃光后的限次重寻，避免死循环
    private int _repathAfterConsumedCnt;
    private int _repathAfterConsumedMax = 2;

    // ==================== 核心流程 ====================

    private void RequestPath()
    {
        _awaitingPath = true;
        _cooldownTimer = _repathCooldown;

        int startIdx = _grid.WorldToIndex(resident.transform.position);
        int goalIdx = _grid.WorldToIndex(_destination);
        if (startIdx < 0 || goalIdx < 0) { Fail("startIdx||goalIdx<0"); return; }

        var pair = new PathPair { StartIndex = startIdx, GoalIndex = goalIdx };
        FindPathService.RequestFindPath(resident.gameObject.GetInstanceID(), _grid, pair, _passMask, OnPathReady);
    }

    private void OnPathReady(int[] path)
    {
        if (IsDone) return;
        _awaitingPath = false;
        _path = path;
        _cursor = 0;

        if (_path == null || _path.Length == 0) { Fail("path == null || _path.Length == 0"); return; }

        // 重置进展统计 & 直靠状态
        _lastProgressPos = resident.transform.position;
        _progressTimer = 0f;
        _stuckAccum = 0f;

        _finalApproach = false;
        _finalTimer = 0f;
    }

    public override void OnStart()
    {
        if (resident == null) { Fail("resident == null"); return; }
        _grid = resident.ParentArea?.grid;
        if (_grid == null) { Fail("_grid ==null"); return; }

        // 用传入的 edgeLength 计算阈值
        _edgeLength = Mathf.Max(0.001f, _edgeLength);
        _arriveSqrEps = 0.5f * _edgeLength * _edgeLength;                 // (√2 * edge / 2)^2 = 0.5 * edge^2
        _nodeReachSqr = Mathf.Max(0.01f, (_edgeLength * 0.25f) * (_edgeLength * 0.25f)); // 四分之一个格

        _lastProgressPos = resident.transform.position;
        _progressTimer = 0f;
        _stuckAccum = 0f;
        _cooldownTimer = 0f;

        _finalApproach = false;
        _finalTimer = 0f;
        _repathAfterConsumedCnt = 0;

        RequestPath();
    }

    public override bool OnUpdate(float dt)
    {
        if (_awaitingPath) return false;
        if (_path == null || _path.Length == 0) { Fail("path==null||pathLength==0"); return false; }

        // 1) 到终点判定（先整体判，避免路径细节误差）
        Vector3 toGoal = _destination - resident.transform.position; toGoal.y = 0f;
        if (toGoal.sqrMagnitude <= _arriveSqrEps)
        {
            Succeed();
            return true;
        }

        // 2) 路点边界
        if (_cursor < 0) { Fail("cursor<0"); return false; }

        if (_cursor >= _path.Length)
        {
            // 路点已吃光：若已接近终点就成功，否则进入 FinalApproach 直靠
            if (toGoal.sqrMagnitude <= _arriveSqrEps)
            {
                Succeed();
                return true;
            }

            if (!_finalApproach)
            {
                _finalApproach = true;
                _finalTimer = 0f;
            }

            // FinalApproach：朝终点匀速直靠
            _finalTimer += dt;
            if (toGoal.sqrMagnitude > 1e-6f)
            {
                resident.transform.position += toGoal.normalized * Mathf.Max(0.01f, _speed) * dt;
                if (_rotate)
                {
                    var want = Quaternion.LookRotation(new Vector3(toGoal.x, 0f, toGoal.z));
                    resident.transform.rotation = Quaternion.Slerp(resident.transform.rotation, want, Mathf.Clamp01(_rotLerp * dt));
                }
            }

            // 直靠中再次判到达
            if ((_destination - resident.transform.position).sqrMagnitude <= _arriveSqrEps)
            {
                Succeed();
                return true;
            }

            // 直靠超时：限次 Repath；仍不行则放宽阈值兜底
            if (_finalTimer >= _finalTimeout)
            {
                if (_repathOnBlocked && !_awaitingPath && _cooldownTimer <= 0f && _repathAfterConsumedCnt < _repathAfterConsumedMax)
                {
                    _repathAfterConsumedCnt++;
                    _finalTimer = 0f;
                    RequestPath();
                    return false;
                }

                // 放宽阈值一次（避免毫米误差卡死）
                float relaxed = _arriveSqrEps * 0.74f; // ~1.2 倍半径
                if ((_destination - resident.transform.position).sqrMagnitude <= relaxed)
                {
                    Succeed();
                    return true;
                }

                // 仍不行 -> 明确失败（可根据项目改成等待地图更新后再 Repath）
                Fail("可根据项目改成等待地图更新后再 Repath");
                return false;
            }

            return false;
        }

        // 3) 跟随当前路点
        Vector3 wp = _grid.IndexToWorldCenter(_path[_cursor]);
        Vector3 to = wp - resident.transform.position; to.y = 0f;

        if (to.sqrMagnitude <= _nodeReachSqr)
        {
            _cursor++;
            if (_cursor >= _path.Length)
            {
                // 刚好吃光，走“路点吃光分支”由直靠/到达判定处理
                return false;
            }
            wp = _grid.IndexToWorldCenter(_path[_cursor]);
            to = wp - resident.transform.position; to.y = 0f;
        }

        // 推进移动
        resident.transform.position += to.normalized * Mathf.Max(0.01f, _speed) * dt;

        if (_rotate && to.sqrMagnitude > 1e-6f)
        {
            var want = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z));
            resident.transform.rotation = Quaternion.Slerp(resident.transform.rotation, want, Mathf.Clamp01(_rotLerp * dt));
        }

        // 4) 无进展检测 + 仅在卡住时 Repath（带冷却）
        _progressTimer += dt;
        if (_cooldownTimer > 0f) _cooldownTimer -= dt;

        if (_progressTimer >= _progressCheckInterval)
        {
            float moved = (resident.transform.position - _lastProgressPos).magnitude;
            _lastProgressPos = resident.transform.position;
            _progressTimer = 0f;

            if (moved < _minProgress)
            {
                _stuckAccum += _progressCheckInterval;
                if (_repathOnBlocked && _stuckAccum >= _stuckTimeout && _cooldownTimer <= 0f && !_awaitingPath)
                {
                    _stuckAccum = 0f;
                    RequestPath();    // 只有“真的卡住”才重算
                    return false;
                }
            }
            else
            {
                _stuckAccum = 0f;   // 有进展就清零
            }
        }

        return false;
    }

    public static MoveToStep Create(string name, Resident r, Vector3 destination, byte[] passMask, float edgeLength)
    {
        var s = ObPool<MoveToStep>.Get();
        s.Reset();
        s.StepName = name;
        s.resident = r;
        s._passMask = passMask ?? new byte[] { 1 };
        s._destination = destination;
        s._edgeLength = edgeLength > 0f ? edgeLength : 1f;
        return s;
    }

    // 兼容旧用法：未显式传 edgeLength 时，默认 1f
    public static MoveToStep Create(string name, Resident r, Vector3 destination, byte[] passMask)
        => Create(name, r, destination, passMask, 1f);

    public override void Reset()
    {
        base.Reset();
        resident = null; _grid = null; _passMask = null;
        _path = null; _cursor = 0; _awaitingPath = false;
        _progressTimer = 0f; _stuckAccum = 0f; _cooldownTimer = 0f;
        _lastProgressPos = Vector3.zero; _destination = Vector3.zero;

        _finalApproach = false; _finalTimer = 0f;
        _repathAfterConsumedCnt = 0;

        // 保留 _edgeLength（通常一步结束后对象池会复用，会在 Create 重新设值）
    }
}