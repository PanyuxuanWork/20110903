
using System;
using UnityEngine;

[RequireComponent(typeof(Storage))]
public class Warehouse : BuildingBase
{
    public Storage storage;

    protected override void Awake()
    {
        base.Awake();
        storage = GetComponent<Storage>();
    }

}
