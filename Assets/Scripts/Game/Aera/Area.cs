using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(AreaManager))]
public class Area : MonoBehaviour
{
    #region 区域特殊字段

    public HallTown hallTown;

    #endregion

    #region 区域标识 / 真相引用 / 派生缓存

    [Header("区域标识")]
    [Tooltip("0=无效；1..50=合法 AreaId")]
    public byte AreaId;

    [Header("全局唯一真相源")]
    public GridAsset grid;

    // Area 对全局 Grid 的局部缓存 / 工作集（不是唯一真相）
    [NonSerialized] private readonly List<int> _cellIndices = new();
    [NonSerialized] private readonly List<int> _boundaryIndices = new();

    public IReadOnlyList<int> CellIndices => _cellIndices;
    public IReadOnlyList<int> BoundaryIndices => _boundaryIndices;
    public int CellCount => _cellIndices.Count;

    #endregion

    #region 区域上下文

    public BuildingContext buildingContext;
    public ResidentContext residentContext;
    public ProductionFlowHub productionFlowHub;
    public TransferDispatchCenter transferDispatchCenter;
    public CityContext cityContext;
    public ResourceContext resourceContext;

    #endregion

    public bool isLock { get; private set; }

    private void Awake()
    {
        AreaContext.Instance.RegisterArea(this);
        if (buildingContext == null)
        {
            GameObject go = new GameObject("BuildingContext");
            buildingContext = go.AddComponent<BuildingContext>();
            buildingContext.ParentArea = this;
            go.transform.SetParent(transform);
        }

        if (productionFlowHub == null)
        {
            GameObject prod = new GameObject("ProducerContext");
            productionFlowHub = prod.AddComponent<ProductionFlowHub>();
            productionFlowHub.ParentArea = this;
            productionFlowHub.AcceptCode = 4096;
            prod.transform.SetParent(transform);

            transferDispatchCenter = prod.AddComponent<TransferDispatchCenter>();
            transferDispatchCenter.ParentArea = this;
            transferDispatchCenter.FlowHub = productionFlowHub;
        }

        if (residentContext == null)
        {
            GameObject prod = new GameObject("ResidentContext");
            residentContext = prod.AddComponent<ResidentContext>();
            residentContext.ParentArea = this;
            prod.transform.SetParent(transform);
        }

        if (cityContext == null)
        {
            GameObject prod = new GameObject("CityContext");
            cityContext = prod.AddComponent<CityContext>();
            cityContext.ParentArea = this;
            prod.transform.SetParent(transform);
        }

        if (resourceContext == null)
        {
            GameObject prod = new GameObject("ResourceContext");
            resourceContext = prod.AddComponent<ResourceContext>();
            resourceContext.ParentArea = this;
            prod.transform.SetParent(transform);
        }
    }

    #region Area API

    /// <summary>
    /// 由 AreaContext 在重建缓存时调用。
    /// </summary>
    public void BindRuntimeData(
        byte areaId,
        GridAsset worldGrid,
        IEnumerable<int> cellIndices = null,
        IEnumerable<int> boundaryIndices = null)
    {
        AreaId = areaId;
        grid = worldGrid;

        _cellIndices.Clear();
        _boundaryIndices.Clear();

        if (cellIndices != null) _cellIndices.AddRange(cellIndices);
        if (boundaryIndices != null) _boundaryIndices.AddRange(boundaryIndices);
    }

    public void SetRuntimeGrid(GridAsset worldGrid)
    {
        grid = worldGrid;
    }

    public void SetAreaId(byte areaId)
    {
        AreaId = areaId;
    }

    public bool IsContainVector3(Vector3 world)
    {
        if (grid == null) return false;
        if (AreaId == 0) return false;
        if (grid.ownerAreaId == null) return false;

        int index = grid.WorldToIndex(world);
        if (index < 0) return false;
        if ((uint)index >= (uint)grid.ownerAreaId.Length) return false;

        return grid.ownerAreaId[index] == AreaId;
    }

    public bool ContainsIndex(int index)
    {
        if (grid == null) return false;
        if (AreaId == 0) return false;
        if (grid.ownerAreaId == null) return false;
        if ((uint)index >= (uint)grid.ownerAreaId.Length) return false;

        return grid.ownerAreaId[index] == AreaId;
    }

    public bool RegisterResident(Resident r)
    {
        if (r == null) return false;

        if (!r.TryGetComponent(out ResidentEconomyService comp))
        {
            TLog.Log("Resident loss ResidentProfession Script");
            return false;
        }

        if (!IsContainVector3(r.transform.position))
            return false;

        if (!residentContext.RegisterResident(r))
            return false;

        r.ParentArea = this;
        AutoDispatchHouse(r);
        return true;
    }

    public bool RegisterNewBuilding(BuildAsset asset, GameObject build)
    {
        if (build == null) return false;
        if (!IsContainVector3(build.transform.position)) return false;

        buildingContext.RegisterBuilding(asset, build);
        TLog.Log(this, $"注册成功:{build.name}");
        return true;
    }

    public bool TryGetUnemployedResident(out Resident resident)
    {
        resident = residentContext?.residents?
            .FirstOrDefault(v => v.professionComp != null
                                 && v.professionComp.professionType == ProfessionType.失业);

        return resident != null;
    }

    public void Locked() => isLock = true;
    public void Unlock() => isLock = false;

    #endregion

    #region Area Manager

    public void AutoDispatchHouse(Resident resident)
    {
        if (buildingContext.GetOneVacantHouse(out Build_House house, out string reason))
        {
            resident.SetRelayHouse(house);
        }
        else
        {
            TLog.Error(this, $"自动分配居民小屋发生错误，{reason}");
        }
    }

    #endregion
}