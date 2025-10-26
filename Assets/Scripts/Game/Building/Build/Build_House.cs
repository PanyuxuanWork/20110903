using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Build_House : BuildingBase
{
    /// <summary>
    /// ÈË¿Ú
    /// </summary>
    public int ResidentAmount { get; private set; }

    public List<Resident> Residents { get; private set; } = new List<Resident>();


}
