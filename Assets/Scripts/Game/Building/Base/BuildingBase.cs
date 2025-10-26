using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BuildingBase : MonoBehaviour
{
    public BuildAsset Building;
    public Area Area;
    public byte Type;

    protected virtual void Awake()
    {
        if (Area == null)
        {
            Area = AreaContext.Instance.FindAreaByVector3(this.transform.position);
            Area.RegisterNewBuilding(Building, gameObject);
        }
    }

}

