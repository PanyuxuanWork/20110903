using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public struct PathPair
{
    public int StartIndex;
    public int GoalIndex;
}

public class GridBatchPathfindingRunner:MonoSingleton<GridBatchPathfindingRunner>
{

    public enum CallbackMode
    {
        WhenAllDone,
        EachAfterBatch
    }

    /// <summary>
    /// 更明确的寻路结果状态
    /// </summary>
    public enum PathQueryStatus
    {
        Success = 0,

        // 前置校验阶段可明确判断的失败
        InvalidGrid = 1,
        EmptyPairs = 2,
        InvalidStartIndex = 3,
        InvalidGoalIndex = 4,
        StartBlocked = 5,
        GoalBlocked = 6,

        // 前置合法，但旧接口最终没有返回路径
        NoPath = 7,

        // 特殊情况
        StartEqualsGoal = 8,
        Unknown = 9
    }

    /// <summary>
    /// 新的寻路结果
    /// </summary>
    [Serializable]
    public struct PathQueryResult
    {
        public int RequestIndex;
        public int StartIndex;
        public int GoalIndex;
        public PathQueryStatus Status;
        public int[] Path;

        public bool Success => Status == PathQueryStatus.Success;
        public bool Failed => !Success;

        public string Message
        {
            get
            {
                switch (Status)
                {
                    case PathQueryStatus.Success: return "寻路成功";
                    case PathQueryStatus.InvalidGrid: return "网格无效";
                    case PathQueryStatus.EmptyPairs: return "请求对列为空";
                    case PathQueryStatus.InvalidStartIndex: return "起点索引越界";
                    case PathQueryStatus.InvalidGoalIndex: return "终点索引越界";
                    case PathQueryStatus.StartBlocked: return "起点不可通行";
                    case PathQueryStatus.GoalBlocked: return "终点不可通行";
                    case PathQueryStatus.NoPath: return "无可达路径";
                    case PathQueryStatus.StartEqualsGoal: return "起点终点相同";
                    default: return "未知状态";
                }
            }
        }

        public override string ToString()
        {
            int len = Path == null ? 0 : Path.Length;
            return $"[PathQueryResult] RequestIndex={RequestIndex} Start={StartIndex} Goal={GoalIndex} Status={Status} PathLen={len}";
        }
    }

    private struct ValidPairMapping
    {
        public int OriginalIndex;
        public PathPair Pair;
    }

    /// <summary>
    /// 新增：健壮版批量寻路（异步/延时）
    /// 不改动旧接口，内部仍复用原来的 RequestBatch。
    /// </summary>
    public Coroutine RequestBatchSafe(
        GridAsset grid,
        IReadOnlyList<PathPair> pairs,
        byte[] passableTypes,
        bool allowDiagonal,
        bool blockCornerCut,
        CallbackMode mode,
        Action<PathQueryResult[]> onAllDone = null,
        Action<PathQueryResult> onEach = null,
        int delayFrames = 1,
        float delaySeconds = 0f)
    {
        return StartCoroutine(CoRequestBatchSafe(
            grid,
            pairs,
            passableTypes,
            allowDiagonal,
            blockCornerCut,
            mode,
            onAllDone,
            onEach,
            delayFrames,
            delaySeconds));
    }

    private IEnumerator CoRequestBatchSafe(
        GridAsset grid,
        IReadOnlyList<PathPair> pairs,
        byte[] passableTypes,
        bool allowDiagonal,
        bool blockCornerCut,
        CallbackMode mode,
        Action<PathQueryResult[]> onAllDone,
        Action<PathQueryResult> onEach,
        int delayFrames,
        float delaySeconds)
    {
        // 1) 延时支持
        if (delayFrames > 0)
        {
            for (int i = 0; i < delayFrames; i++)
                yield return null;
        }

        if (delaySeconds > 0f)
            yield return new WaitForSeconds(delaySeconds);

        // 2) 空请求直接返回
        if (pairs == null || pairs.Count == 0)
        {
            TLog.Warning("安全寻路失败：请求对列为空");
            onAllDone?.Invoke(Array.Empty<PathQueryResult>());
            yield break;
        }

        // 3) 先准备结果数组，占位
        PathQueryResult[] finalResults = new PathQueryResult[pairs.Count];
        var validMappings = new List<ValidPairMapping>(pairs.Count);

        // 4) 网格校验
        if (grid)
        {
            for (int i = 0; i < pairs.Count; i++)
            {
                finalResults[i] = CreateResult(
                    requestIndex: i,
                    startIndex: pairs[i].StartIndex,
                    goalIndex: pairs[i].GoalIndex,
                    status: PathQueryStatus.InvalidGrid,
                    path: Array.Empty<int>());
            }

            DispatchSafeResults(finalResults, mode, onAllDone, onEach);
            yield break;
        }

        // 5) 逐个 pair 做前置校验
        for (int i = 0; i < pairs.Count; i++)
        {
            int start = pairs[i].StartIndex;
            int goal = pairs[i].GoalIndex;

            PathQueryStatus precheckStatus = PrecheckPair(
                grid,
                start,
                goal,
                passableTypes);

            if (precheckStatus == PathQueryStatus.Success)
            {
                validMappings.Add(new ValidPairMapping
                {
                    OriginalIndex = i,
                    Pair = pairs[i]
                });
            }
            else
            {
                int[] fallbackPath = precheckStatus == PathQueryStatus.StartEqualsGoal
                    ? new[] { start }
                    : Array.Empty<int>();

                finalResults[i] = CreateResult(
                    requestIndex: i,
                    startIndex: start,
                    goalIndex: goal,
                    status: precheckStatus,
                    path: fallbackPath);
            }
        }

        // 6) 如果没有合法 pair，就直接回调
        if (validMappings.Count == 0)
        {
            DispatchSafeResults(finalResults, mode, onAllDone, onEach);
            yield break;
        }

        // 7) 把合法 pair 抽出来，继续走旧接口
        List<PathPair> validPairs = new List<PathPair>(validMappings.Count);
        for (int i = 0; i < validMappings.Count; i++)
            validPairs.Add(validMappings[i].Pair);

        bool finished = false;
        int[][] legacyPaths = null;

        RequestBatch(
            grid,
            validPairs,
            passableTypes,
            allowDiagonal,
            blockCornerCut,
            CallbackMode.WhenAllDone, // 固定整批完成后处理，便于映射结果
            onAllDone: paths =>
            {
                legacyPaths = paths;
                finished = true;
            },
            onEach: null);

        while (!finished)
            yield return null;

        // 8) 合并旧结果 -> 新结果
        for (int i = 0; i < validMappings.Count; i++)
        {
            int originalIndex = validMappings[i].OriginalIndex;
            PathPair pair = validMappings[i].Pair;

            int[] path = null;
            if (legacyPaths != null && i < legacyPaths.Length)
                path = legacyPaths[i];

            bool ok = path != null && path.Length > 0;

            finalResults[originalIndex] = CreateResult(
                requestIndex: originalIndex,
                startIndex: pair.StartIndex,
                goalIndex: pair.GoalIndex,
                status: ok ? PathQueryStatus.Success : PathQueryStatus.NoPath,
                path: ok ? path : Array.Empty<int>());
        }

        // 9) 回调分发
        DispatchSafeResults(finalResults, mode, onAllDone, onEach);
    }

    private PathQueryResult CreateResult(
        int requestIndex,
        int startIndex,
        int goalIndex,
        PathQueryStatus status,
        int[] path)
    {
        return new PathQueryResult
        {
            RequestIndex = requestIndex,
            StartIndex = startIndex,
            GoalIndex = goalIndex,
            Status = status,
            Path = path ?? Array.Empty<int>()
        };
    }

    private void DispatchSafeResults(
        PathQueryResult[] results,
        CallbackMode mode,
        Action<PathQueryResult[]> onAllDone,
        Action<PathQueryResult> onEach)
    {
        if (results == null)
            results = Array.Empty<PathQueryResult>();

        for (int i = 0; i < results.Length; i++)
        {
            var r = results[i];

            if (r.Success)
                TLog.Log($"安全寻路结果：索引={r.RequestIndex} 起点={r.StartIndex} 终点={r.GoalIndex} 状态={r.Status} 路径长度={(r.Path == null ? 0 : r.Path.Length)}");
            else
                TLog.Warning($"安全寻路结果：索引={r.RequestIndex} 起点={r.StartIndex} 终点={r.GoalIndex} 状态={r.Status} 原因={r.Message}");

            if (mode == CallbackMode.EachAfterBatch)
            {
                try { onEach?.Invoke(r); }
                catch (Exception e) { TLog.Error($"安全寻路单条回调异常：{e}"); Debug.LogException(e); }
            }
        }

        try { onAllDone?.Invoke(results); }
        catch (Exception e) { TLog.Error($"安全寻路批量回调异常：{e}"); Debug.LogException(e); }
    }

    private PathQueryStatus PrecheckPair(
        GridAsset grid,
        int startIndex,
        int goalIndex,
        byte[] passableTypes)
    {
        if (!IsIndexValid(grid, startIndex))
            return PathQueryStatus.InvalidStartIndex;

        if (!IsIndexValid(grid, goalIndex))
            return PathQueryStatus.InvalidGoalIndex;

        if (startIndex == goalIndex)
            return PathQueryStatus.StartEqualsGoal;

        if (!IsIndexPassable(grid, startIndex, passableTypes))
            return PathQueryStatus.StartBlocked;

        if (!IsIndexPassable(grid, goalIndex, passableTypes))
            return PathQueryStatus.GoalBlocked;

        return PathQueryStatus.Success;
    }

    private bool IsIndexValid(GridAsset grid, int index)
    {
        if (grid == null || grid.passableType == null)
            return false;

        return index >= 0 && index < grid.passableType.Length;
    }

    private bool IsIndexPassable(GridAsset grid, int index, byte[] passableTypes)
    {
        if (!IsIndexValid(grid, index))
            return false;

        byte tileType = grid.passableType[index];

        // 若没传 passableTypes，则沿用旧逻辑语义：视为“没有额外限制”
        if (passableTypes == null || passableTypes.Length == 0)
            return true;

        for (int i = 0; i < passableTypes.Length; i++)
        {
            if (passableTypes[i] == tileType)
                return true;
        }

        return false;
    }


    /// <summary>
    /// 发起批量寻路
    /// </summary>
    public void RequestBatch(
        GridAsset grid,
        IReadOnlyList<PathPair> pairs,
        byte[] passableTypes,
        bool allowDiagonal,
        bool blockCornerCut,
        CallbackMode mode,
        Action<int[][]> onAllDone = null,
        Action<int[]> onEach = null)
    {
        if (!ValidateGrid(grid))
        {
            TLog.Warning("寻路失败：网格无效或尺寸/数据不匹配（可能为 null、宽高<=0 或 passableType 长度不等于宽*高）");
            onAllDone?.Invoke(Array.Empty<int[]>());
            return;
        }
        if (pairs == null || pairs.Count == 0)
        {
            TLog.Warning("寻路失败：请求对列为空（无起点/终点对）");
            onAllDone?.Invoke(Array.Empty<int[]>());
            return;
        }

        // 输入打包
        var starts = new NativeArray<int>(pairs.Count, Allocator.TempJob);
        var goals = new NativeArray<int>(pairs.Count, Allocator.TempJob);
        for (int i = 0; i < pairs.Count; i++)
        {
            starts[i] = pairs[i].StartIndex;
            goals[i] = pairs[i].GoalIndex;
        }

        // 允许通行掩码
        var passMask = new NativeArray<byte>(256, Allocator.TempJob);
        if (passableTypes != null)
            for (int i = 0; i < passableTypes.Length; i++) passMask[passableTypes[i]] = 1;

        // passableType 拷贝
        var buildable = new NativeArray<byte>(grid.passableType.Length, Allocator.TempJob);
        for (int i = 0; i < grid.passableType.Length; i++) buildable[i] = grid.passableType[i];

        // 输出流
        var stream = new NativeStream(pairs.Count, Allocator.TempJob);
        var writer = stream.AsWriter();

        var job = new GridAStarBatchJob
        {
            Width = grid.Width,
            Height = grid.Height,
            Buildable = buildable,
            PassMask256 = passMask,
            AllowDiagonal = (byte)(allowDiagonal ? 1 : 0),
            BlockCornerCut = (byte)(blockCornerCut ? 1 : 0),
            Starts = starts,
            Goals = goals,
            Writer = writer
        };

        var handle = job.Schedule(pairs.Count, 1);
        JobHandle.ScheduleBatchedJobs();

        float startTime = Time.realtimeSinceStartup;
        StartCoroutine(CoCollect(stream, starts, goals, buildable, passMask, handle, mode, onAllDone, onEach, startTime));

    }

    private System.Collections.IEnumerator CoCollect(
        NativeStream stream,
        NativeArray<int> starts,
        NativeArray<int> goals,
        NativeArray<byte> buildable,
        NativeArray<byte> passMask,
        JobHandle handle,
        CallbackMode mode,
        Action<int[][]> onAllDone,
        Action<int[]> onEach,
        float startTime)
    {
        while (!handle.IsCompleted) yield return null;
        handle.Complete();

        var results = new List<int[]>(starts.Length);
        var reader = stream.AsReader();

        for (int i = 0; i < starts.Length; i++)
        {
            reader.BeginForEachIndex(i);
            int len = reader.Read<int>();

            if (len <= 0)
            {
                // 失败（runner 只能得出“无路径/非法”的笼统原因）
                TLog.Warning($"寻路失败：起点Index={starts[i]} 终点Index={goals[i]} 原因=无可达路径或起点/终点非法/被阻挡");
                results.Add(Array.Empty<int>());
                reader.EndForEachIndex();
                continue;
            }

            int[] path = new int[len];
            for (int k = 0; k < len; k++) path[k] = reader.Read<int>();
            results.Add(path);
            reader.EndForEachIndex();

            // 成功（长度）
            TLog.Log($"寻路成功：起点Index={starts[i]} 终点Index={goals[i]} 路径长度={len}");

            if (mode == CallbackMode.EachAfterBatch)
            {
                try { onEach?.Invoke(path); }
                catch (Exception e) { TLog.Error($"单条回调异常：{e}"); Debug.LogException(e); }
            }
        }

        // 资源释放
        stream.Dispose();
        starts.Dispose();
        goals.Dispose();
        buildable.Dispose();
        passMask.Dispose();

        if (mode == CallbackMode.WhenAllDone)
        {
            try { onAllDone?.Invoke(results.ToArray()); }
            catch (Exception e) { TLog.Error($"批量回调异常：{e}"); Debug.LogException(e); }
        }
        else
        {
            onAllDone?.Invoke(results.ToArray());
        }

        // （批量概览放在 ControlCenter 统计，这里不重复）
    }

    private bool ValidateGrid(GridAsset g)
    {
        bool ok = g != null && g.passableType != null && g.Width > 0 && g.Height > 0 &&
                  g.passableType.Length == g.Width * g.Height;
        if (!ok)
        {
            TLog.Warning("寻路失败：网格校验未通过（null / 宽高无效 / passableType 长度与尺寸不匹配）");
        }
        return ok;
    }
}