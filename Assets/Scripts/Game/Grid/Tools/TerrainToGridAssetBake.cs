/***************************************************************************
// File       : TerrainToGridAssetBake.cs
// Author     : Panyuxuan
// Created    : 2025/12/21
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO]/// Editor工具：从Terrain生成/覆盖一个GridAsset
            /// - CellWidth = 1
   /// - passableType 全部 = 1
   /// - heightY = 对应Terrain高度（采样cell中心点）
// ***************************************************************************/
using System;
using System.IO;
using Sirenix.OdinInspector;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Editor工具：从Terrain生成/覆盖一个GridAsset
/// - CellWidth = 1
/// - passableType 全部 = 1
/// - heightY = 对应Terrain高度（采样cell中心点）
/// </summary>
public class TerrainToGridAssetBaker : MonoBehaviour
{
    [Title("输入")]
    [Required] public Terrain terrain;

    [Title("输出")]
    [InfoBox("若指定 TargetGridAsset，则直接覆盖它；否则会在 SaveFolder 下创建新资产。", InfoMessageType.None)]
    public GridAsset targetGridAsset;

    [FolderPath(AbsolutePath = false)]
    public string saveFolder = "Assets/GridAssets";

    public string assetName = "GridAsset_FromTerrain";

    [Title("采样设置")]
    [ReadOnly] public float cellWidth = 1f;

    [Tooltip("true=采样每格中心点；false=采样每格左下角")]
    public bool sampleAtCellCenter = true;

#if UNITY_EDITOR
    [Button("从 Terrain 生成 / 覆盖 GridAsset", ButtonSizes.Large)]
    public void Bake()
    {
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("[TerrainToGridAssetBaker] Terrain 或 TerrainData 为空。");
            return;
        }

        TerrainData td = terrain.terrainData;
        Vector3 tPos = terrain.transform.position;
        Vector3 tSize = td.size;

        // 你的 GridAsset 语义：OriginXZ 是“世界左下角（对齐到 CellWidth 的整数倍）”
        // 这里做 floor 对齐；再用 (terrainPos+size - origin) 来计算格数，确保覆盖整个Terrain范围
        float originX = Mathf.Floor(tPos.x / cellWidth) * cellWidth;
        float originZ = Mathf.Floor(tPos.z / cellWidth) * cellWidth;

        int width = Mathf.CeilToInt((tPos.x + tSize.x - originX) / cellWidth);
        int height = Mathf.CeilToInt((tPos.z + tSize.z - originZ) / cellWidth);

        long nLong = (long)width * (long)height;
        if (width <= 0 || height <= 0 || nLong <= 0 || nLong > int.MaxValue)
        {
            Debug.LogError($"[TerrainToGridAssetBaker] 生成尺寸非法：Width={width}, Height={height}, Total={nLong}");
            return;
        }
        int n = (int)nLong;

        // 创建或复用 GridAsset
        GridAsset grid = targetGridAsset;
        bool createdNew = false;

        if (grid == null)
        {
            EnsureFolder(saveFolder);

            string path = AssetDatabase.GenerateUniqueAssetPath($"{saveFolder.TrimEnd('/')}/{assetName}.asset");
            grid = ScriptableObject.CreateInstance<GridAsset>();
            AssetDatabase.CreateAsset(grid, path);
            createdNew = true;

            targetGridAsset = grid;
        }
        else
        {
            Undo.RecordObject(grid, "Bake GridAsset From Terrain");
        }

        // 写入基础参数
        grid.CellWidth = 1f;
        grid.OriginXZ = new Vector2(originX, originZ);
        grid.Width = width;
        grid.Height = height;

        // 重建数组并填充
        grid.passableType = new byte[n];
        grid.heightY = new float[n];

        // passableType 全部设为 1
        for (int i = 0; i < n; i++) grid.passableType[i] = 1;

        // 高度采样
        try
        {
            for (int z = 0; z < height; z++)
            {
                float wz = originZ + (sampleAtCellCenter ? (z + 0.5f) : z) * cellWidth;

                // 进度条（可取消）
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Baking GridAsset",
                        $"Sampling heights... {z + 1}/{height}",
                        (float)(z + 1) / height))
                {
                    Debug.LogWarning("[TerrainToGridAssetBaker] 用户取消了烘焙。");
                    return;
                }

                for (int x = 0; x < width; x++)
                {
                    float wx = originX + (sampleAtCellCenter ? (x + 0.5f) : x) * cellWidth;

                    int idx = x + width * z;
                    grid.heightY[idx] = SampleTerrainHeightWorld(td, tPos, tSize, wx, wz);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EditorUtility.SetDirty(grid);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(createdNew
            ? $"[TerrainToGridAssetBaker] 已创建并生成 GridAsset：{AssetDatabase.GetAssetPath(grid)} (W={width}, H={height})"
            : $"[TerrainToGridAssetBaker] 已覆盖生成 GridAsset：{AssetDatabase.GetAssetPath(grid)} (W={width}, H={height})");
    }

    private static float SampleTerrainHeightWorld(TerrainData td, Vector3 terrainPos, Vector3 terrainSize, float worldX, float worldZ)
    {
        // 转换到Terrain归一化坐标并夹紧（防止 origin floor 对齐后出现少量越界）
        float u = (worldX - terrainPos.x) / Mathf.Max(terrainSize.x, 1e-6f);
        float v = (worldZ - terrainPos.z) / Mathf.Max(terrainSize.z, 1e-6f);
        u = Mathf.Clamp01(u);
        v = Mathf.Clamp01(v);

        // GetInterpolatedHeight 返回相对Terrain本地(0..size.y)的高度，需加上terrainPos.y得到世界高度
        return terrainPos.y + td.GetInterpolatedHeight(u, v);
    }

    private static void EnsureFolder(string assetFolder)
    {
        if (string.IsNullOrWhiteSpace(assetFolder)) return;
        assetFolder = assetFolder.Replace("\\", "/").TrimEnd('/');

        if (AssetDatabase.IsValidFolder(assetFolder)) return;

        // 物理创建目录（更稳妥），然后 Refresh
        if (assetFolder.StartsWith("Assets"))
        {
            string rel = assetFolder.Substring("Assets".Length).TrimStart('/');
            string full = Path.Combine(Application.dataPath, rel);
            Directory.CreateDirectory(full);
            AssetDatabase.Refresh();
        }
    }
#endif
}
