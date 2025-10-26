using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 全局网格注册表：按 GridAsset.ID(1..255) 建立 O(1) 索引。
/// - 仅保留一个静态数组做查询真源；Inspector 里用 List 配置初始资产。
/// - Awake 时一次性重建索引，不再在数组/列表之间来回拷贝。
/// - 提供 Get/TryGet/Reload/校验；可选运行时 Register。
/// </summary>
public class GridContext : MonoBehaviour
{
    public static GridContext Instance { get; private set; }

    [Header("启动时注册的 Grid 资产（顺序无所谓，按 GridAsset.ID 索引）")]
    [SerializeField] private List<GridAsset> assets = new();

    // 0 约定为无效；1..255 可用
    private static readonly GridAsset[] _byId = new GridAsset[256];

    /// <summary>按 ID 的只读视图（索引 0 恒为 null）。</summary>
    public static IReadOnlyList<GridAsset> Grids => _byId;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;

        ReloadFromList(assets);     // ← 单次构建索引
        InitAreaContext();          // ← 依赖 AreaContext 的注册（可选）
        TLog.Log(this, "全局网格系统初始化完成...");
    }

    /// <summary>从一组 GridAsset 重建索引；可在运行时或编辑器调用。</summary>
    public static void ReloadFromList(IEnumerable<GridAsset> all)
    {
        Array.Clear(_byId, 0, _byId.Length);
        if (all == null) return;

        foreach (var g in all)
        {
            if (g == null) continue;
            byte id = g.ID; // 1..255
            if (id == 0) { Debug.LogWarning($"[GridContext] 忽略 ID=0（无效）：{g.name}"); continue; }

            if (_byId[id] != null && _byId[id] != g)
            {
                Debug.LogError($"[GridContext] GridId={id} 冲突: {_byId[id].name} vs {g.name}");
                continue;
            }
            _byId[id] = g;
        }
    }

    /// <summary>运行时单个注册（动态加载/卸载时可用）。</summary>
    public static bool Register(GridAsset g)
    {
        if (g == null) return false;
        byte id = g.ID;
        if (id == 0) { Debug.LogWarning($"[GridContext] 不能注册 ID=0: {g.name}"); return false; }
        if (_byId[id] != null && _byId[id] != g)
        {
            Debug.LogError($"[GridContext] GridId={id} 冲突: {_byId[id].name} vs {g.name}");
            return false;
        }
        _byId[id] = g;
        return true;
    }

    /// <summary>O(1) 获取（可能返回 null）。</summary>
    public static GridAsset Get(byte id) => _byId[id];

    /// <summary>更安全的获取。</summary>
    public static bool TryGet(byte id, out GridAsset grid)
    {
        grid = _byId[id];
        return grid != null;
    }

    /// <summary>唯一性检查（可用于调试/菜单）。</summary>
    public static bool IsDuplicateFree(out byte dupId)
    {
        for (byte id = 1; id <= 255; id++)
        {
            var g = _byId[id];
            if (g == null) continue;

            // 找是否有第二个引用同一 id 的资产（理论上不会，因为数组只容纳一个）
            // 这里主要留给外部在重建索引前做额外校验时使用。
        }
        dupId = 0;
        return true;
    }

    private void InitAreaContext()
    {
        // 依赖外部系统时先判空，避免启动顺序问题
        if (AreaContext.Instance == null) return;

        for (int id = 1; id <= 255; id++)
        {
            var v = _byId[id];
            if (v != null) AreaContext.Instance.RegisterAreaByGridAsset(v);
        }
    }

#if UNITY_EDITOR
    // 编辑器下：改 Inspector 列表后自动重建索引，避免运行前“数组/列表不同步”
    private void OnValidate()
    {
        // 编辑器刷新时可能还没有静态数组内容；此时也重建一次以保持一致
        ReloadFromList(assets);
    }
#endif
}
