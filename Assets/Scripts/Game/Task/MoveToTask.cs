using System;
using UnityEngine;

public sealed class MoveToTask : TaskBase
{
    [Serializable]
    public struct MovePoint
    {
        public GameObject go;

        public Transform Transform => go != null ? go.transform : null;
        public Vector3 Position => Transform != null ? Transform.position : Vector3.zero;
        public bool IsValid => go != null;

        public static MovePoint From(GameObject go)
        {
            return new MovePoint { go = go };
        }

        public static MovePoint From(Transform tf)
        {
            return new MovePoint { go = tf != null ? tf.gameObject : null };
        }

        public override string ToString()
        {
            return go != null ? go.name : "null";
        }
    }

    public static MoveToTask Create(
        Resident resident,
        Transform target,
        byte[] passMask,
        float speed = 2.5f,
        float arriveEps = 0.05f,
        bool rotate = true,
        float rotLerp = 12f,
        bool repathOnBlocked = false,
        float repathInterval = 1.0f,
        float stuckTimeout = 2.0f,
        float pathRequestTimeout = 2.0f)
    {
        return Create(
            resident != null ? resident.transform : null,
            resident != null ? resident.ParentArea.grid : null,
            resident != null ? MovePoint.From(resident.gameObject) : default,
            MovePoint.From(target),
            passMask,
            speed,
            arriveEps,
            rotate,
            rotLerp,
            repathOnBlocked,
            repathInterval,
            stuckTimeout,
            pathRequestTimeout);
    }

    public static MoveToTask Create(
        Transform actor,
        GridAsset grid,
        MovePoint start,
        MovePoint goal,
        byte[] passMask,
        float speed = 2.5f,
        float arriveEps = 0.05f,
        bool rotate = true,
        float rotLerp = 12f,
        bool repathOnBlocked = false,
        float repathInterval = 1.0f,
        float stuckTimeout = 2.0f,
        float pathRequestTimeout = 2.0f)
    {
        var t = new MoveToTask();
        t.ResetForUse();
        return t.Init(
            actor,
            grid,
            start,
            goal,
            passMask,
            speed,
            arriveEps,
            rotate,
            rotLerp,
            repathOnBlocked,
            repathInterval,
            stuckTimeout,
            pathRequestTimeout);
    }

    public static MoveToTask Create(
        GridAsset grid,
        MovePoint start,
        MovePoint goal,
        byte[] passMask)
    {
        var t =  Create(
            start.Transform,
            grid,
            start,
            goal,
            passMask,
            speed: 2.5f,
            arriveEps: 0.05f,
            rotate: true,
            rotLerp: 12f,
            repathOnBlocked: false,
            repathInterval: 1.0f,
            stuckTimeout: 2.0f,
            pathRequestTimeout: 2.0f);
        t._start = start;
        t._goal = goal;
        return t;
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
        float stuckTimeout = 2.0f,
        float pathRequestTimeout = 2.0f)
    {
        return Create(
            actor,
            grid,
            MovePoint.From(startTf),
            MovePoint.From(goalTf),
            passMask,
            speed,
            arriveEps,
            rotate,
            rotLerp,
            repathOnBlocked,
            repathInterval,
            stuckTimeout,
            pathRequestTimeout);
    }

    private Transform _actor;
    private GridAsset _grid;
    private MovePoint _start;
    private MovePoint _goal;
    private byte[] _passMask;

    private float _speed;
    private float _arriveSqrEps;
    private bool _rotate;
    private float _rotLerp;

    private bool _repathOnBlocked;
    private float _repathInterval;
    private float _repathTimer;
    private float _stuckTimeout;
    private float _stuckTimer;
    private Vector3 _lastPos;

    private int[] _path;
    private int _cursor;
    private bool _awaitingPath;

    private float _pathRequestTimeout;
    private float _pathRequestTimer;

    private int _pathRequestVersion;

    private MoveToTask ResetForUse()
    {
        _actor = null;
        _grid = null;
        _start = default;
        _goal = default;
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

        _pathRequestTimeout = 2.0f;
        _pathRequestTimer = 0f;
        _pathRequestVersion = 0;

        Name = "MoveTo(GameObject)";
        return this;
    }

    private MoveToTask Init(
        Transform actor,
        GridAsset grid,
        MovePoint start,
        MovePoint goal,
        byte[] passMask,
        float speed,
        float arriveEps,
        bool rotate,
        float rotLerp,
        bool repathOnBlocked,
        float repathInterval,
        float stuckTimeout,
        float pathRequestTimeout)
    {
        _actor = actor;
        _grid = grid;
        _start = start;
        _goal = goal;
        _passMask = passMask;

        _speed = Mathf.Max(0f, speed);
        _arriveSqrEps = Mathf.Max(1e-6f, arriveEps * arriveEps);
        _rotate = rotate;
        _rotLerp = rotLerp;

        _repathOnBlocked = repathOnBlocked;
        _repathInterval = Mathf.Max(0.1f, repathInterval);
        _stuckTimeout = Mathf.Max(0.1f, stuckTimeout);
        _pathRequestTimeout = Mathf.Max(0.1f, pathRequestTimeout);

        return this;
    }

    protected override void OnStart()
    {
        if (_actor == null || _grid == null || !_start.IsValid || !_goal.IsValid)
        {
            Fail($"[MoveToTask] Missing refs. actor={_actor}, grid={_grid}, start={_start}, goal={_goal}");
            return;
        }

        _awaitingPath = true;
        Debug.Log($"[MoveToTask] Starting move from {_start.Position} to {_goal.Position}");
        RequestPath();
    }

    protected override bool OnUpdate(float dt)
    {
        if (_awaitingPath)
        {
            _pathRequestTimer += dt;
            if (_pathRequestTimer >= _pathRequestTimeout)
            {
                _awaitingPath = false;
                Fail($"[MoveToTask] Path request timeout. timeout={_pathRequestTimeout}");
            }
            return false;
        }

        if (_path == null || _path.Length == 0)
        {
            Fail("[MoveToTask] Path not found");
            return false;
        }

        if ((uint)_cursor >= (uint)_path.Length)
        {
            Succeed();
            return true;
        }

        Vector3 target = IndexToWorldCenter(_grid, _path[_cursor]);
        Vector3 pos = _actor.position;
        Vector3 to = target - pos;

        if (to.sqrMagnitude <= _arriveSqrEps)
        {
            _cursor++;
            if (_cursor >= _path.Length)
            {
                Succeed();
                return true;
            }
            return false;
        }

        if (_rotate && to.sqrMagnitude > 1e-6f)
        {
            Quaternion want = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z));
            _actor.rotation = Quaternion.Slerp(_actor.rotation, want, Mathf.Clamp01(_rotLerp * dt));
        }

        Vector3 step = to.normalized * (_speed * dt);
        if (step.sqrMagnitude > to.sqrMagnitude) step = to;
        _actor.position = pos + step;

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
                RequestPath();
            }
        }

        return false;
    }

    protected override void OnCancel()
    {
        // 当前版本先不接取消寻路
    }

    protected override void OnCompletedInternal(TaskResult result)
    {
        _pathRequestVersion++;

        _actor = null;
        _grid = null;
        _start = default;
        _goal = default;
        _passMask = null;
        _path = null;

        ObPool<MoveToTask>.Release(this);
    }

    private void RequestPath()
    {
        _lastPos = _actor.position;
        _stuckTimer = 0f;
        _repathTimer = 0f;
        _pathRequestTimer = 0f;

        if (_actor == null || _grid == null || !_goal.IsValid)
        {
            Fail( $"[MoveToTask] RequestPath invalid refs. actor={_actor}, grid={_grid}, goal={_goal}");
            return;
        }

        // 起点实际仍然用 actor 当前点，这是对的
        int startIdx = _grid.WorldToIndex(_actor.position);
        int goalIdx = _grid.WorldToIndex(_goal.Position);

        if (startIdx < 0 || goalIdx < 0)
        {
            Fail(
                $"[MoveToTask] Out of grid. startIdx={startIdx} pos={_actor.position}, goalIdx={goalIdx} pos={_goal.Position}");
            return;
        }

        var pair = new PathPair
        {
            StartIndex = startIdx,
            GoalIndex = goalIdx
        };

        int requestVersion = ++_pathRequestVersion;

        // requesterId 用寻路者（actor 所属 GameObject）
        int requesterId = _actor.gameObject.GetInstanceID();

        FindPathService.RequestFindPath(
            requesterId,
            _grid,
            pair,
            _passMask,
            path => { OnPathReady(requestVersion, path); });
    }

    private void OnPathReady(int requestVersion, int[] path)
    {
        if (requestVersion != _pathRequestVersion) return;
        if (IsDone) return;

        _awaitingPath = false;
        _pathRequestTimer = 0f;

        if (path == null || path.Length == 0)
        {
            Fail("[MoveToTask] Path not found");
            return;
        }

        _path = path;
        _cursor = 0;
    }

    private static Vector3 IndexToWorldCenter(GridAsset grid, int idx)
    {
        return grid.IndexToWorldCenter(idx);
    }


}