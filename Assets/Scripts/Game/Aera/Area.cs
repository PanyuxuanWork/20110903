using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

public class Area : MonoBehaviour
{
    public GridAsset grid;
    public BuildingContext buildingContext;
    public List<Resident> residents = new();
    public Dictionary<ushort, HashSet<Resident>> residentsDict = new();
    public ProducerContext producerContext;

    public bool isLock { get; private set; }

    private void Awake()
    {
        if (buildingContext == null)
        {
            GameObject go = new GameObject("BuildingContext");
            buildingContext = go.AddComponent<BuildingContext>();
            buildingContext.ParentArea = this;
            go.transform.SetParent(this.transform);
        }

        if (producerContext == null)
        {
            GameObject prod = new GameObject("ProducerContext");
            producerContext = prod.AddComponent<ProducerContext>();
            producerContext.ParentArea = this;
            producerContext.AcceptCode = 4096;
            prod.transform.SetParent(this.transform);
        }
    }

    public bool IsContainVector3(Vector3 world)
    {
        return grid.ContainsWorld(in world);
    }

    public bool RegisterResident(Resident r)
    {
        if (!r.TryGetComponent<ResidentProfession>(out var comp))
        {
            TLog.Log("Resident loss ResidentProfession Script");
            return false;
        }
        if (IsContainVector3(r.transform.position))
        {
            var v = GetResidentsByProfession(comp.Code);
            if (v == null)
            {
                v = new HashSet<Resident>();
                residentsDict.TryAdd(comp.Code, v);
            }
            if (!residents.Contains(r))
            {
                residents.Add(r);
                v.Add(r);
                r.ParentArea = this;
                if (!producerContext.residents.Contains(r.economyService))
                {
                    producerContext.residents.Add(r.economyService);
                }
                return true;
            }


        }
        return false;
    }

    public bool RegisterNewBuilding(BuildAsset asset, GameObject build)
    {
        if (IsContainVector3(build.transform.position))
        {
            buildingContext.RegisterBuilding(asset, build);
            TLog.Log(this, $"×¢²á³É¹¦:{build.name}");
            return true;
        }
        return false;
    }

    public HashSet<Resident> GetResidentsByProfession(ushort code)
    {
        return residentsDict.GetValueOrDefault(code);
    }

    public void Locked() => isLock = true;
    public void Unlock() => isLock = false;
}
