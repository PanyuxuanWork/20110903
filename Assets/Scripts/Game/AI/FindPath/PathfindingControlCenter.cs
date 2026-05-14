using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局寻路控制中心：时间片调度、LRU缓存、批量调用 Runner（带失败退避 / 熔断）
/// </summary>
[RequireComponent(typeof(GridBatchPathfindingRunner))]
[DefaultExecutionOrder(-800)]
public class PathfindingControlCenter : MonoSingleton<PathfindingControlCenter>, IStepListener
{
    [Header("调度")]
    public float intervalSeconds = 0.02f;
    public int maxPerSlice = 512;
    public bool allowDiagonal = true;
    public bool blockCornerCut = true;

    [Header("缓存")]
    public int cacheCapacity = 4096;
    public bool sampleValidate = false;
    [Range(1, 16)] public int samplePoints = 4;

    [Header("依赖")]
    public GridBatchPathfindingRunner runner;

    [Header("失败退避")]
    public bool enableFailBackoff = true;
    public float baseRetryDelay = 0.5f;
    public float retryStepDelay = 0.5f;
    public float maxRetryDelay = 3f;
    public int maxFailCountBeforeCircuit = 6;
    public float circuitCooldown = 5f;
    [Range(0f, 0.5f)] public float retryJitter = 0.15f;

    [Header("日志开关")]
    public bool enablePathfindingLog = true;

    // 统一封装，受开关控制
    private void LogInfo(string msg) { if (enablePathfindingLog) TLog.Log(this, msg); }
    private void LogWarn(string msg) { if (enablePathfindingLog) TLog.Warning(this, msg); }
    private void LogError(string msg) { if (enablePathfindingLog) TLog.Error(this, msg); }

    /// <summary>
    /// 某个 requester 连续失败过多，进入熔断时触发
    /// 参数：requesterId, startIndex, goalIndex, failCount
    /// </summary>
    public event Action<int, int, int, int> OnPathRequestCircuitOpened;

    public int Priority { get; set; } = -100;
    public bool IsActive { get; set; } = true;

    private readonly Queue<PathRequest> _queue = new Queue<PathRequest>(2048);
    private readonly Dictionary<Key, Entry> _cache = new Dictionary<Key, Entry>(4096);
    private readonly LinkedList<Key> _lru = new LinkedList<Key>();
    private readonly Dictionary<int, FailState> _failStates = new Dictionary<int, FailState>(1024);

    private float _acc;
    private readonly List<PathRequest> _batchReqs = new List<PathRequest>(1024);
    private readonly List<PathPair> _batchPairs = new List<PathPair>(1024);
    private readonly PassMaskCache _maskCache = new PassMaskCache();

    protected override void Awake()
    {
        base.Awake();
        runner = GetComponent<GridBatchPathfindingRunner>();
        TLog.Log(this, "寻路系统初始化完成.");
    }

    private void OnEnable() { GlobalStep.Instance?.AddListener(this); }
    private void OnDisable() { GlobalStep.Instance?.RemoveListener(this); }

    // ================== 对外API ==================

    /// <summary>
    /// requesterId 推荐传单位主体 gameObject.GetInstanceID()
    /// </summary>
    public void RequestPath(
        int requesterId,
        GridAsset grid,
        int startIndex,
        int goalIndex,
        byte[] passableTypes,
        Action<int[]> onDone)
    {
        if (grid == null || startIndex < 0 || goalIndex < 0)
        {
            LogWarn($"寻路失败：参数无效 requester={requesterId}, grid={grid}, start={startIndex}, goal={goalIndex}");
            onDone?.Invoke(Array.Empty<int>());
            return;
        }

        var arr = grid.passableType;
        Vector3 startPos = grid.IndexToWorldCenter(startIndex);
        Vector3 goalPos = grid.IndexToWorldCenter(goalIndex);
        byte startType = arr != null && startIndex < arr.Length ? arr[startIndex] : (byte)255;
        byte goalType = arr != null && goalIndex < arr.Length ? arr[goalIndex] : (byte)255;
        string allowed = passableTypes != null ? string.Join(",", passableTypes) : "ALL";

        LogInfo($"[调试] requester={requesterId} 入参快照：起点Index={startIndex} 终点Index={goalIndex} 起点={startPos} 终点={goalPos} 起点类型={startType} 终点类型={goalType} 允许类型集合={allowed}");

        // 如果上下文变化（起点/终点变了），清掉旧失败状态
        ResetFailStateIfContextChanged(requesterId, startIndex, goalIndex);

        // 退避 / 熔断拦截
        if (TryRejectByFailBackoff(requesterId, startIndex, goalIndex, out string rejectReason))
        {
            LogInfo($"请求被退避拦截：requester={requesterId}, start={startIndex}, goal={goalIndex}, reason={rejectReason}");
            onDone?.Invoke(Array.Empty<int>());
            return;
        }

        var key = Key.From(grid, startIndex, goalIndex, passableTypes, allowDiagonal, blockCornerCut);

        if (TryGetFromCache(key, out var cachedPath))
        {
            if (IsPathStillPassable(grid, cachedPath, passableTypes))
            {
                LogInfo($"LRU缓存命中：requester={requesterId} 起点={startPos} 终点={goalPos} 路径长度={(cachedPath?.Length ?? 0)}");
                onDone?.Invoke(cachedPath);
                return;
            }
            RemoveFromCache(key);
        }
        else
        {
            LogInfo($"LRU缓存未命中：requester={requesterId} 起点={startPos} 终点={goalPos}");
        }

        _queue.Enqueue(new PathRequest
        {
            requesterId = requesterId,
            grid = grid,
            key = key,
            passMask = passableTypes,
            onDone = onDone
        });
    }

    // ================== Tick调度 ==================

    public void OnTick(in TickContext ctx)
    {
        _acc += ctx.DeltaTime;
        if (_acc + 1e-6f < Mathf.Max(1e-3f, intervalSeconds)) return;
        _acc = 0f;

        if (_queue.Count == 0 || runner == null) return;

        _batchReqs.Clear();
        _batchPairs.Clear();

        int take = Mathf.Min(maxPerSlice, _queue.Count);
        for (int i = 0; i < take; i++)
        {
            var req = _queue.Dequeue();

            if (TryGetFromCache(req.key, out var cached))
            {
                if (IsPathStillPassable(req.grid, cached, req.passMask))
                {
                    LogInfo($"LRU缓存命中：requester={req.requesterId} 起点Index={req.key.start} 终点Index={req.key.goal} 路径长度={(cached?.Length ?? 0)}");
                    req.onDone?.Invoke(cached);
                    continue;
                }

                LogWarn($"LRU缓存过期：requester={req.requesterId} 起点Index={req.key.start} 终点Index={req.key.goal}");
                RemoveFromCache(req.key);
            }

            _batchReqs.Add(req);
            _batchPairs.Add(new PathPair { StartIndex = req.key.start, GoalIndex = req.key.goal });
        }

        if (_batchReqs.Count == 0) return;

        var firstGrid = _batchReqs[0].grid;
        var firstMask = _batchReqs[0].passMask;
        float dispatchT = Time.realtimeSinceStartup;

        runner.RequestBatch(
            firstGrid,
            _batchPairs,
            passableTypes: firstMask,
            allowDiagonal: allowDiagonal,
            blockCornerCut: blockCornerCut,
            mode: GridBatchPathfindingRunner.CallbackMode.WhenAllDone,
            onAllDone: results =>
            {
                float cbMs = (Time.realtimeSinceStartup - dispatchT) * 1000f;
                int success = 0, fail = 0;

                GlobalStep.Instance.PostFromAnyThread(() =>
                {
                    for (int i = 0; i < _batchReqs.Count; i++)
                    {
                        var req = _batchReqs[i];
                        var path = (results != null && i < results.Length) ? (results[i] ?? Array.Empty<int>()) : Array.Empty<int>();

                        Vector3 startPos = req.grid.IndexToWorldCenter(req.key.start);
                        Vector3 goalPos = req.grid.IndexToWorldCenter(req.key.goal);

                        if (path.Length == 0)
                        {
                            fail++;
                            RecordFailure(req.requesterId, req.key.start, req.key.goal);

                            string reason = "未知原因";
                            bool startValid = req.key.start >= 0 && req.key.start < req.grid.passableType.Length;
                            bool goalValid = req.key.goal >= 0 && req.key.goal < req.grid.passableType.Length;

                            if (!startValid && !goalValid) reason = "起点与终点均越界或无效";
                            else if (!startValid) reason = "起点越界或无效";
                            else if (!goalValid) reason = "终点越界或无效";
                            else
                            {
                                bool[] allow = _maskCache.Get(req.passMask);
                                byte startType = req.grid.passableType[req.key.start];
                                byte goalType = req.grid.passableType[req.key.goal];
                                bool startAllowed = allow[startType];
                                bool goalAllowed = allow[goalType];

                                if (!startAllowed && !goalAllowed) reason = "起点与终点均被当前掩码禁止";
                                else if (!startAllowed) reason = "起点被当前掩码禁止";
                                else if (!goalAllowed) reason = "终点被当前掩码禁止";
                                else reason = "无可达路径（两点被允许，但算法未找到路径）";
                            }

                            LogWarn($"寻路失败：requester={req.requesterId} 起点={startPos} 终点={goalPos} 原因={reason}");
                        }
                        else
                        {
                            success++;
                            ClearFailState(req.requesterId, req.key.start, req.key.goal);

                            // 只缓存成功路径
                            PutIntoCache(req.key, path);
                            if (path.Length > 1)
                            {
                                var revKey = req.key.Reversed();
                                var rev = new int[path.Length];
                                for (int a = 0, b = path.Length - 1; a < path.Length; a++, b--)
                                    rev[a] = path[b];
                                PutIntoCache(revKey, rev);
                            }

                            LogInfo($"寻路成功：requester={req.requesterId} 起点={startPos} 终点={goalPos} 路径长度={path.Length}");
                        }

                        req.onDone?.Invoke(path);
                    }

                    LogInfo($"本次批量寻路完成：成功 {success} 条，失败 {fail} 条，用时 {cbMs:F2} ms");
                });
            });
    }

    // ================== 失败退避 / 熔断 ==================

    private bool TryRejectByFailBackoff(
        int requesterId,
        int startIndex,
        int goalIndex,
        out string reason)
    {
        reason = null;

        if (!enableFailBackoff)
            return false;

        if (!_failStates.TryGetValue(requesterId, out var state))
            return false;

        bool sameContext = state.startIndex == startIndex && state.goalIndex == goalIndex;
        if (!sameContext)
            return false;

        float now = Time.time;

        if (state.circuitOpen)
        {
            if (now < state.nextRetryTime)
            {
                reason = $"熔断中，剩余 {(state.nextRetryTime - now):F2}s";
                return true;
            }

            // 熔断冷却结束，允许再试一次，但保留 failCount 继续观察
            state.circuitOpen = false;
            _failStates[requesterId] = state;
            return false;
        }

        if (now < state.nextRetryTime)
        {
            reason = $"退避冷却中，剩余 {(state.nextRetryTime - now):F2}s";
            return true;
        }

        return false;
    }

    private void RecordFailure(int requesterId, int startIndex, int goalIndex)
    {
        if (!enableFailBackoff)
            return;

        float now = Time.time;

        if (!_failStates.TryGetValue(requesterId, out var state) ||
            state.startIndex != startIndex ||
            state.goalIndex != goalIndex)
        {
            state = new FailState
            {
                requesterId = requesterId,
                startIndex = startIndex,
                goalIndex = goalIndex,
                failCount = 0,
                nextRetryTime = 0f,
                circuitOpen = false,
                circuitOpenTime = 0f
            };
        }

        state.failCount++;

        float delay = baseRetryDelay + retryStepDelay * (state.failCount - 1);
        delay = Mathf.Min(delay, maxRetryDelay);

        if (retryJitter > 0f)
        {
            float factor = UnityEngine.Random.Range(1f - retryJitter, 1f + retryJitter);
            delay *= factor;
        }

        state.nextRetryTime = now + delay;

        if (state.failCount >= maxFailCountBeforeCircuit)
        {
            state.circuitOpen = true;
            state.circuitOpenTime = now;
            state.nextRetryTime = now + circuitCooldown;

            LogWarn($"寻路熔断：requester={requesterId}, start={startIndex}, goal={goalIndex}, failCount={state.failCount}");
            OnPathRequestCircuitOpened?.Invoke(requesterId, startIndex, goalIndex, state.failCount);
        }

        _failStates[requesterId] = state;
    }

    private void ClearFailState(int requesterId, int startIndex, int goalIndex)
    {
        if (!_failStates.TryGetValue(requesterId, out var state))
            return;

        bool sameContext = state.startIndex == startIndex && state.goalIndex == goalIndex;
        if (!sameContext)
            return;

        _failStates.Remove(requesterId);
    }

    private void ResetFailStateIfContextChanged(int requesterId, int startIndex, int goalIndex)
    {
        if (!_failStates.TryGetValue(requesterId, out var state))
            return;

        if (state.startIndex != startIndex || state.goalIndex != goalIndex)
            _failStates.Remove(requesterId);
    }

    /// <summary>
    /// 给业务层主动清理某个 requester 的失败状态
    /// 例如任务切换、单位重置、目标失效后
    /// </summary>
    public void ResetRequesterFailState(int requesterId)
    {
        _failStates.Remove(requesterId);
    }

    // ================== 路径校验 ==================

    private bool IsPathStillPassable(GridAsset grid, int[] path, byte[] passMask)
    {
        if (path == null || path.Length == 0) return false;
        if (grid == null || grid.passableType == null) return false;

        bool[] allow = _maskCache.Get(passMask);
        foreach (int idx in path)
        {
            if ((uint)idx >= (uint)grid.passableType.Length) return false;
            if (!allow[grid.passableType[idx]]) return false;
        }
        return true;
    }

    // ================== 缓存(LRU) ==================

    private bool TryGetFromCache(Key key, out int[] path)
    {
        if (_cache.TryGetValue(key, out var e))
        {
            _lru.Remove(e.node);
            e.node = _lru.AddFirst(key);
            _cache[key] = e;
            path = e.path;
            return true;
        }
        path = null;
        return false;
    }

    private void RemoveFromCache(Key key)
    {
        if (_cache.TryGetValue(key, out var e))
        {
            _lru.Remove(e.node);
            _cache.Remove(key);
        }
    }

    private void PutIntoCache(Key key, int[] path)
    {
        if (_cache.TryGetValue(key, out var old))
        {
            old.path = path ?? Array.Empty<int>();
            _lru.Remove(old.node);
            old.node = _lru.AddFirst(key);
            _cache[key] = old;
        }
        else
        {
            var node = _lru.AddFirst(key);
            _cache[key] = new Entry { path = path ?? Array.Empty<int>(), node = node };
        }

        if (_cache.Count > cacheCapacity)
        {
            var tail = _lru.Last;
            if (tail != null)
            {
                _cache.Remove(tail.Value);
                _lru.RemoveLast();
            }
        }
    }

    private struct Entry
    {
        public int[] path;
        public LinkedListNode<Key> node;
    }

    private struct FailState
    {
        public int requesterId;
        public int startIndex;
        public int goalIndex;
        public int failCount;
        public float nextRetryTime;
        public bool circuitOpen;
        public float circuitOpenTime;
    }

    private struct Key : IEquatable<Key>
    {
        public readonly int gridId;
        public readonly int start, goal;
        public readonly ulong passMaskHash;
        public readonly byte flags;

        public Key(int gid, int s, int g, ulong maskHash, byte flg)
        {
            gridId = gid;
            start = s;
            goal = g;
            passMaskHash = maskHash;
            flags = flg;
        }

        public static Key From(GridAsset grid, int s, int g, byte[] mask, bool allowDiag, bool blockCorner)
        {
            return new Key(
                grid ? grid.GetInstanceID() : 0,
                s, g,
                MaskHash(mask),
                (byte)((allowDiag ? 1 : 0) | (blockCorner ? 2 : 0))
            );
        }

        public Key Reversed() => new Key(gridId, goal, start, passMaskHash, flags);

        public bool Equals(Key other)
        {
            return gridId == other.gridId
                   && start == other.start
                   && goal == other.goal
                   && passMaskHash == other.passMaskHash
                   && flags == other.flags;
        }

        public override bool Equals(object obj) => obj is Key k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + gridId;
                h = h * 31 + start;
                h = h * 31 + goal;
                h = h * 31 + flags;
                h = h * 31 + (int)(passMaskHash & 0xFFFFFFFFu);
                h = h * 31 + (int)((passMaskHash >> 32) & 0xFFFFFFFFu);
                return h;
            }
        }

        internal static ulong MaskHash(byte[] mask)
        {
            if (mask == null || mask.Length == 0) return 0UL;

            unchecked
            {
                ulong h = 1469598103934665603UL;
                for (int i = 0; i < mask.Length; i++)
                {
                    h ^= mask[i];
                    h *= 1099511628211UL;
                }
                return h;
            }
        }
    }

    private sealed class PassMaskCache
    {
        private readonly Dictionary<ulong, bool[]> _map = new Dictionary<ulong, bool[]>(32);

        public bool[] Get(byte[] mask)
        {
            ulong key = Key.MaskHash(mask);
            if (_map.TryGetValue(key, out var allow)) return allow;

            allow = new bool[256];
            if (mask != null)
            {
                for (int i = 0; i < mask.Length; i++)
                    allow[mask[i]] = true;
            }

            _map[key] = allow;
            return allow;
        }
    }

    private struct PathRequest
    {
        public int requesterId;
        public GridAsset grid;
        public Key key;
        public byte[] passMask;
        public Action<int[]> onDone;
    }
}