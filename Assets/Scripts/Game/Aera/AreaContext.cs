using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AreaContext : MonoSingleton<AreaContext>
{
    [Header("全局唯一真相源")]
    [SerializeField] private GridAsset worldGrid;

    [Header("场景中预先存在的 Area（可手工拖拽，也可自动收集）")]
    public List<Area> Areas = new();

    private readonly Dictionary<byte, Area> _areaById = new();
    private readonly Dictionary<byte, AreaRuntimeGridCache> _runtimeCacheByAreaId = new();

    [Serializable]
    public sealed class AreaRuntimeGridCache
    {
        public List<int> CellIndices = new();
        public List<int> BoundaryIndices = new();
        public int CellCount => CellIndices?.Count ?? 0;
    }

    public GridAsset WorldGrid => worldGrid;
    public IReadOnlyDictionary<byte, Area> AreaById => _areaById;
    public IReadOnlyDictionary<byte, AreaRuntimeGridCache> RuntimeCacheByAreaId => _runtimeCacheByAreaId;

    protected override void Awake()
    {
        base.Awake();
        TryAutoBindWorldGrid();
        CollectExistingAreas();
        RebuildFromWorldGrid();
    }

    [ContextMenu("Collect Existing Areas")]
    public void CollectExistingAreas()
    {
        Areas = GetComponentsInChildren<Area>(true)
            .Where(a => a != null)
            .Distinct()
            .ToList();
    }

    [ContextMenu("Rebuild From World Grid")]
    public void RebuildFromWorldGrid()
    {
        _areaById.Clear();
        _runtimeCacheByAreaId.Clear();

        if (worldGrid == null)
        {
            Debug.LogWarning("[AreaContext] WorldGrid 为空，无法构建 Area 缓存。");
            return;
        }

        CollectExistingAreas();

        // 1. 建 AreaId -> Area 映射（只绑定已有 Area，不创建）
        foreach (var area in Areas)
        {
            if (area == null) continue;

            if (area.AreaId == 0)
            {
                Debug.LogWarning($"[AreaContext] Area {area.name} 的 AreaId=0，已跳过。", area);
                continue;
            }

            if (_areaById.ContainsKey(area.AreaId))
            {
                Debug.LogError($"[AreaContext] 检测到重复 AreaId={area.AreaId}，对象：{_areaById[area.AreaId].name} 和 {area.name}");
                continue;
            }

            _areaById.Add(area.AreaId, area);
            _runtimeCacheByAreaId.Add(area.AreaId, new AreaRuntimeGridCache());
        }

        int cellCount = Mathf.Max(worldGrid.Width * worldGrid.Height, 0);
        if (worldGrid.ownerAreaId == null || worldGrid.ownerAreaId.Length != cellCount)
        {
            Debug.LogError($"[AreaContext] ownerAreaId[] 未初始化或长度不匹配。期待={cellCount}, 实际={(worldGrid.ownerAreaId == null ? -1 : worldGrid.ownerAreaId.Length)}");
            return;
        }

        // 2. 扫 grid 真相，填充每个 Area 的 CellIndices
        for (int index = 0; index < cellCount; index++)
        {
            byte areaId = worldGrid.ownerAreaId[index];
            if (areaId == 0) continue;

            if (!_runtimeCacheByAreaId.TryGetValue(areaId, out var cache))
            {
                Debug.LogWarning($"[AreaContext] Grid 中存在 AreaId={areaId}，但场景里没有对应的 Area 对象。index={index}");
                continue;
            }

            cache.CellIndices.Add(index);
        }

        // 3. 计算边界
        foreach (var pair in _runtimeCacheByAreaId)
        {
            byte areaId = pair.Key;
            var cache = pair.Value;

            foreach (int index in cache.CellIndices)
            {
                if (IsBoundaryCell(index, areaId))
                    cache.BoundaryIndices.Add(index);
            }
        }

        // 4. 回填到已有 Area
        foreach (var pair in _areaById)
        {
            byte areaId = pair.Key;
            var area = pair.Value;
            var cache = _runtimeCacheByAreaId[areaId];

            area.BindRuntimeData(
                areaId,
                worldGrid,
                cache.CellIndices,
                cache.BoundaryIndices);
        }
    }

    public bool TryGetAreaById(byte areaId, out Area area)
    {
        return _areaById.TryGetValue(areaId, out area);
    }

    public Area FindAreaById(byte areaId)
    {
        _areaById.TryGetValue(areaId, out var area);
        return area;
    }

    public bool TryGetAreaIdByVector3(Vector3 pos, out byte areaId)
    {
        areaId = 0;
        if (worldGrid == null) return false;

        int index = worldGrid.WorldToIndex(pos);
        if (index < 0) return false;
        if (worldGrid.ownerAreaId == null || (uint)index >= (uint)worldGrid.ownerAreaId.Length) return false;

        areaId = worldGrid.ownerAreaId[index];
        return areaId != 0;
    }

    public bool TryGetAreaByVector3(Vector3 pos, out Area area)
    {
        area = null;
        if (!TryGetAreaIdByVector3(pos, out var areaId)) return false;
        return _areaById.TryGetValue(areaId, out area);
    }

    public Area FindAreaByVector3(Vector3 pos)
    {
        return TryGetAreaByVector3(pos, out var area) ? area : null;
    }

    public GridAsset FindGridAssetByVector3(Vector3 pos)
    {
        return TryGetAreaIdByVector3(pos, out _) ? worldGrid : null;
    }

    public bool RegisterResident(Resident r)
    {
        if (r == null) return false;
        if (!TryGetAreaByVector3(r.transform.position, out var area)) return false;
        return area.RegisterResident(r);
    }

    public bool RegisterNewBuilding(BuildAsset asset, GameObject build)
    {
        if (build == null) return false;
        if (!TryGetAreaByVector3(build.transform.position, out var area)) return false;
        return area.RegisterNewBuilding(asset, build);
    }

    private bool IsBoundaryCell(int index, byte areaId)
    {
        int x = index % worldGrid.Width;
        int z = index / worldGrid.Width;

        return !HasSameOwner(x - 1, z, areaId)
            || !HasSameOwner(x + 1, z, areaId)
            || !HasSameOwner(x, z - 1, areaId)
            || !HasSameOwner(x, z + 1, areaId);
    }

    private bool HasSameOwner(int x, int z, byte areaId)
    {
        if ((uint)x >= (uint)worldGrid.Width || (uint)z >= (uint)worldGrid.Height)
            return false;

        int index = worldGrid.ToIndex(x, z);
        return worldGrid.ownerAreaId != null
            && (uint)index < (uint)worldGrid.ownerAreaId.Length
            && worldGrid.ownerAreaId[index] == areaId;
    }

    private void TryAutoBindWorldGrid()
    {
        if (worldGrid != null) return;
        if (GridContext.Grids == null) return;

        GridAsset found = null;
        int count = 0;

        for (int i = 1; i < GridContext.Grids.Count; i++)
        {
            var grid = GridContext.Grids[i];
            if (grid == null) continue;

            count++;
            if (found == null) found = grid;
        }

        if (count == 1)
            worldGrid = found;
        else if (count > 1)
            Debug.LogWarning("[AreaContext] 检测到多个 GridAsset。新架构只支持 1 张全局 GridAsset，请手动指定 WorldGrid。");
    }

    private byte _idcount=0;
    public void RegisterArea(Area area)
    {
        _areaById.Add(_idcount,area);
        _idcount++;
    }
    public Area GetDebugArea()
    {
        return _areaById[0];
    }
}