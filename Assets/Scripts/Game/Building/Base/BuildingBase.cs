using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BuildingBase : MonoBehaviour
{
    public BuildAsset buildAsset;
    [HideInInspector]public Area Area;
    public Transform VisitPosition;
    protected virtual void Awake()
    {
        if (Area == null)
        {
            Area = AreaContext.Instance.FindAreaByVector3(this.transform.position);
            Area.RegisterNewBuilding(buildAsset, gameObject);
        }
    }

}

