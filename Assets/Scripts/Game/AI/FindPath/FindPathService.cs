using System;
using System.Collections;
using System.Collections.Generic;


public static class FindPathService
{
    /// <summary>
    /// 一个单位请求一次寻路
    /// </summary>
    /// <param StepName="grid"></param>
    /// <param StepName="pair"></param>
    /// <param StepName="passableTypes"></param>
    /// <param StepName="onEach"></param>
    public static void RequestOneFindPathImmediately(
        GridAsset grid,
        PathPair pair,
        byte[] passableTypes,
        Action<int[]> onEach = null)
    {
        var list = new List<PathPair>();
        GridBatchPathfindingRunner.Instance.RequestBatch(
            grid,
            list,
            passableTypes,
            true,
            true,
            GridBatchPathfindingRunner.CallbackMode.EachAfterBatch,
            null,
            onEach);

    }

    /// <summary>
    /// 请求一个寻路批次
    /// </summary>
    /// <param StepName="grid"></param>
    /// <param StepName="pairs"></param>
    /// <param StepName="passableTypes"></param>
    /// <param StepName="onEach"></param>
    public static void RequestBatchFindPathImmediately(
        GridAsset grid,
        IReadOnlyList<PathPair> pairs,
        byte[] passableTypes,
        Action<int[]> onEach = null)
    {
        GridBatchPathfindingRunner.Instance.RequestBatch(
            grid,
            pairs,
            passableTypes,
            true,
            true,
            GridBatchPathfindingRunner.CallbackMode.EachAfterBatch,
            null,
            onEach);
    }

    /// <summary>
    /// 申请一次寻路（延迟寻路）
    /// </summary>
    /// <param StepName="grid"></param>
    /// <param StepName="pair"></param>
    /// <param StepName="passableTypes"></param>
    /// <param StepName="onDone"></param>
    public static void RequestFindPath(
        int requesterID,
        GridAsset grid,
        PathPair pair,
        byte[] passableTypes,
        Action<int[]> onDone=null)
    {
        PathfindingControlCenter.Instance.RequestPath(requesterID,grid,pair.StartIndex,pair.GoalIndex,passableTypes,onDone);
    }

}
