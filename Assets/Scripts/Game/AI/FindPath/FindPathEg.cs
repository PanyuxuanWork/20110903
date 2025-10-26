using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FindPathEg : MonoBehaviour
{
    public GridAsset grid;

    public int[] sx;
    public int[] sz;
    public int[] gx;
    public int[] gz;
    // Start is called before the first frame update
    void Start()
    {
        // 1) 组装批量请求（示例：10 个单位各自有起终点）
        var pairs = new List<PathPair>(10);
        for (int i = 0; i < 10; i++)
        {
            pairs.Add(new PathPair
            {
                StartIndex = grid.ToIndex(sx[i], sz[i]),
                GoalIndex = grid.ToIndex(gx[i], gz[i]),
            });
        }

        byte[] pass = { 1, 2, 5 }; // 允许通行的 passableType 值
        GridBatchPathfindingRunner.Instance.RequestBatch(
            grid,
            pairs,
            pass,
            allowDiagonal: true,
            blockCornerCut: true,
            mode: GridBatchPathfindingRunner.CallbackMode.WhenAllDone, // 或 EachAfterBatch
            onAllDone: allPaths =>
            {
                // allPaths.Length == pairs.Count
                // allPaths[i] 是第 i 条路径（int[]，不可达则长度为0）
            },
            onEach: onePath =>
            {
                // 如果使用 EachAfterBatch 模式，会在批量结束后对每条依次回调
            });

    }


}
