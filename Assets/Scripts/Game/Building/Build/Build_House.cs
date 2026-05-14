using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Build_House : BuildingBase
{
    /// <summary>
    /// ÈË¿Ú
    /// </summary>
    public int ResidentAmount { get; private set; } = 4;

    public List<Resident> Residents { get; private set; } = new List<Resident>();

    protected override void Awake()
    {
        base.Awake();
    }
}
