using System;
using UnityEngine;

/// <summary>
/// MoveToTask：起点/终点用 Transform 表示；Start 时异步寻路（FindPathService），回调拿到 path 后沿路移动。
/// - 池化：用 ObPool<MoveToTask>，完成/取消时自动回收
/// - 可选：卡住重寻路（以当前 actor/goal 的最新位置重新请求）
/// </summary>
public sealed class MoveToTask : TaskBase
{
    // ========= 工厂（你习惯的 Create） =========

    public static MoveToTask Create(Resident resident, 
        Transform target, 
        byte[] passMask,
        float speed = 2.5f,
        float arriveEps = 0.05f,
        bool rotate = true,
        float rotLerp = 12f,
        bool repathOnBlocked = false,
        float repathInterval = 1.0f,
        float stuckTimeout = 2.0f)
    {
        return Create(resident.transform,
            resident.ParentArea.grid,
            resident.transform,
            target,
            passMask, speed, arriveEps, rotate, rotLerp, repathOnBlocked, repathInterval, stuckTimeout);
    }

    public static MoveToTask Create(
        Transform actor,
        GridAsset grid,
        Transform startTf,
        Transform goalTf,
        byte[] passMask,
        float speed = 2.5f,
        float arriveEps = 0.05f,
        bool rotate = true,
        float rotLerp = 12f,
        bool repathOnBlocked = false,
        float repathInterval = 1.0f,
        float stuckTimeout = 2.0f)
    {
        var t = ObPool<MoveToTask>.Get();
        t.Reset();        // TaskBase: 清状态/事件
        t.ResetForUse();  // 本类私有字段复位
        return t.Init(actor, grid, startTf, goalTf, passMask,
                      speed, arriveEps, rotate, rotLerp,
                      repathOnBlocked, repathInterval, stuckTimeout);
    }

    // ========= 输入对象 =========
    private Transform _actor;   // 执行移动的单位
    private GridAsset _grid;
    private Transform _startTf; // 起点（Transform）
    private Transform _goalTf;  // 终点（Transform）
    private byte[] _passMask;

    // 调参
    private float _speed;
    private float _arriveSqrEps;
    private bool _rotate;
    private float _rotLerp;

    // 健壮性（可选）
    private bool _repathOnBlocked;
    private float _repathInterval;
    private float _repathTimer;
    private float _stuckTimeout;
    private float _stuckTimer;
    private Vector3 _lastPos;

    // 运行态
    private int[] _path;
    private int _cursor;
    private bool _awaitingPath;

    // ========= 复位 & 初始化 =========
    private MoveToTask ResetForUse()
    {
        _actor = null;
        _grid = null;
        _startTf = null;
        _goalTf = null;
        _passMask = null;

        _speed = 2.5f;
        _arriveSqrEps = 0.05f * 0.05f;
        _rotate = true;
        _rotLerp = 12f;

        _repathOnBlocked = false;
        _repathInterval = 1.0f;
        _repathTimer = 0f;

        _stuckTimeout = 2.0f;
        _stuckTimer = 0f;
        _lastPos = Vector3.zero;

        _path = null;
        _cursor = 0;
        _awaitingPath = false;

        Name = "MoveTo(Transform)";
        return this;
    }

    private MoveToTask Init(
        Transform actor,
        GridAsset grid,
        Transform startTf,
        Transform goalTf,
        byte[] passMask,
        float speed,
        float arriveEps,
        bool rotate,
        float rotLerp,
        bool repathOnBlocked,
        float repathInterval,
        float stuckTimeout)
    {
        _actor = actor;
        _grid = grid;
        _startTf = startTf;
        _goalTf = goalTf;
        _passMask = passMask;

        _speed = Mathf.Max(0f, speed);
        _arriveSqrEps = Mathf.Max(1e-6f, arriveEps * arriveEps);
        _rotate = rotate;
        _rotLerp = rotLerp;

        _repathOnBlocked = repathOnBlocked;
        _repathInterval = Mathf.Max(0.1f, repathInterval);
        _stuckTimeout = Mathf.Max(0.1f, stuckTimeout);

        return this;
    }

    // ========= 生命周期（TaskBase）=========
    protected override void OnStart()
    {
        if (_actor == null || _grid == null || _startTf == null || _goalTf == null)
        {
            Debug.LogWarning($"[MoveToTask] Missing refs. actor={_actor}, grid={_grid}, start={_startTf}, goal={_goalTf}");
            Fail(); return;
        }
        _awaitingPath = true;
        Debug.Log($"[MoveToTask] Starting move from {_startTf.position} to {_goalTf.position}");
        RequestPathByTransforms();
    }

    protected override bool OnUpdate(float dt)
    {
        if (_awaitingPath)
        {
            return false;
        }
        if (_path == null || _path.Length == 0)
        {
            Fail(); return false;
        }

        if ((uint)_cursor >= (uint)_path.Length)
        {
            Succeed(); return true;
        }

        Vector3 target = IndexToWorldCenter(_grid, _path[_cursor]); // 用本地计算得到格子中心
        Vector3 pos = _actor.position;
        Vector3 to = target - pos;

        // 到达当前 waypoint
        if (to.sqrMagnitude <= _arriveSqrEps)
        {
            _cursor++;
            if (_cursor >= _path.Length)
            {
                Succeed(); return true;
            }
            return false;
        }

        // 朝向
        if (_rotate && to.sqrMagnitude > 1e-6f)
        {
            Quaternion want = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z));
            _actor.rotation = Quaternion.Slerp(_actor.rotation, want, Mathf.Clamp01(_rotLerp * dt));
        }

        // 移动（XZ）
        Vector3 step = to.normalized * (_speed * dt);
        if (step.sqrMagnitude > to.sqrMagnitude) step = to;
        _actor.position = pos + step;
        // 卡住检测 + 按需重寻
        _stuckTimer += dt;
        _repathTimer += dt;
        if (_stuckTimer >= _stuckTimeout)
        {
            float movedSqr = (_actor.position - _lastPos).sqrMagnitude;
            _lastPos = _actor.position;
            _stuckTimer = 0f;

            if (movedSqr < 1e-6f && _repathOnBlocked && _repathTimer >= _repathInterval)
            {
                _repathTimer = 0f;
                _awaitingPath = true;
                // 以“当前 actor/goal 的 Transform 位置”重新请求
                RequestPathByTransforms();
            }
        }

        return false;
    }

    protected override void OnCancel()
    {
        // 如果你的寻路有“取消”token，可在此撤销；当前实现不需要额外处理
    }

    protected override void OnComplete(TaskResult result)
    {
        // 释放强引用，便于池化复用
        _actor = null;
        _grid = null;
        _startTf = null;
        _goalTf = null;
        _passMask = null;
        _path = null;

        ObPool<MoveToTask>.Release(this);
    }

    // ========= 内部：寻路与坐标换算 =========
    private void RequestPathByTransforms()
    {
        _lastPos = _actor.position;
        _stuckTimer = 0f;
        _repathTimer = 0f;

        int startIdx = _grid.WorldToIndex(_startTf.position);
        int goalIdx = _grid.WorldToIndex(_goalTf.position);
        if (startIdx < 0 || goalIdx < 0)
        {
            Debug.LogWarning($"[MoveToTask] Out of grid. startIdx={startIdx} pos={_startTf.position}, goalIdx={goalIdx} pos={_goalTf.position}");
            Fail(); return;
        }

        var pair = new PathPair { StartIndex = startIdx, GoalIndex = goalIdx };
        FindPathService.RequestFindPath(_grid, pair, _passMask, OnPathReady);
    }

    private void OnPathReady(int[] path)
    {
        if (IsDone) return;  // 可能已被取消/抢占
        _awaitingPath = false;

        if (path == null || path.Length == 0)
        {
            Fail(); return;
        }

        _path = path;
        _cursor = 0;
    }

    private static Vector3 IndexToWorldCenter(GridAsset grid, int idx)
    {
        return grid.IndexToWorldCenter(idx);
    }
}