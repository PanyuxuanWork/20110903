using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AreaContext : MonoSingleton<AreaContext>
{
    public List<Area> Areas = new List<Area>();

    protected override void Awake()
    {
        base.Awake();
        foreach (var v in Areas)
        {
            RegisterArea(v);
        }
    }

    public bool RegisterArea(Area area)
    {
        if (!Areas.Contains(area))
        {
            Areas.Add(area);
            return true;
        }
        return false;
    }

    public bool UnregisterArea(Area area)
    {
        if (Areas.Contains(area))
        {
            Areas.Remove(area);
            return true;
        }
        return false;
    }

    public Area FindAreaByVector3(Vector3 pos)
    {
        foreach (var v in Areas)
        {
            if (v.grid.ContainsWorld(pos))
            {
                return v;
            }
        }
        TLog.Error("没有查找到具体区域");
        return null;
    }

    public bool RegisterAreaByGridAsset(GridAsset grid)
    {
        if (!GridContext.Grids.Contains(grid))
            return false;
        GameObject go = new GameObject($"Area_{grid.name}");
        go.transform.parent = transform;
        var a = go.AddComponent<Area>();
        a.grid = grid;
        grid.parent = a;
        RegisterArea(a);
        return true;
    }

    public bool RegisterResident(Resident r)
    {
        foreach (var v in Areas)
        {
            if (v.RegisterResident(r)) return true;
        }
        return false;
    }

    //public bool RegisterBuilding(BuildingBase<MonoBehaviour> build)
}
