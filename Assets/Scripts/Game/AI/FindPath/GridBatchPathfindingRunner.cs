using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public struct PathPair
{
    public int StartIndex;
    public int GoalIndex;
}

public class GridBatchPathfindingRunner : MonoSingleton<GridBatchPathfindingRunner>
{
    public enum CallbackMode
    {
        WhenAllDone,
        EachAfterBatch
    }

    [Header("日志开关")]
    public bool enablePathfindingLog = true;

    // 统一封装，受开关控制
    private void LogInfo(string msg) { if (enablePathfindingLog) TLog.Log(this, msg); }
    private void LogWarn(string msg) { if (enablePathfindingLog) TLog.Warning(this, msg); }
    private void LogError(string msg) { if (enablePathfindingLog) TLog.Error(this, msg); }

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
            LogWarn("寻路失败：网格无效或尺寸/数据不匹配（可能为 null、宽高<=0 或 passableType 长度不等于宽*高）");
            onAllDone?.Invoke(Array.Empty<int[]>());
            return;
        }
        if (pairs == null || pairs.Count == 0)
        {
            LogWarn("寻路失败：请求对列为空（无起点/终点对）");
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
                LogWarn($"寻路失败：起点Index={starts[i]} 终点Index={goals[i]} 原因=无可达路径或起点/终点非法/被阻挡");
                results.Add(Array.Empty<int>());
                reader.EndForEachIndex();
                continue;
            }

            int[] path = new int[len];
            for (int k = 0; k < len; k++) path[k] = reader.Read<int>();
            results.Add(path);
            reader.EndForEachIndex();

            // 成功（长度）
            LogInfo($"寻路成功：起点Index={starts[i]} 终点Index={goals[i]} 路径长度={len}");

            if (mode == CallbackMode.EachAfterBatch)
            {
                try { onEach?.Invoke(path); }
                catch (Exception e) { LogError($"单条回调异常：{e}"); Debug.LogException(e); }
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
            catch (Exception e) { LogError($"批量回调异常：{e}"); Debug.LogException(e); }
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
            LogWarn("寻路失败：网格校验未通过（null / 宽高无效 / passableType 长度与尺寸不匹配）");
        }
        return ok;
    }
}
