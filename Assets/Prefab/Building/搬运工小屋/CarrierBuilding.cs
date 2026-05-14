/***************************************************************************
// File       : CarrierBuilding.cs
// Author     : Panyuxuan
// Created    : 2026/01/06
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ClickedUnit))]
public class CarrierBuilding : BuildingBase
{
    private ClickedUnit clickedUnit;
    public List<Resident> employees = new List<Resident>();
    protected override void Awake()
    {
        base.Awake();
        clickedUnit = GetComponent<ClickedUnit>();
        this.clickedUnit.OnClicked += AddEmployee;
    }

    public void AddEmployee()
    {
        foreach (var v in AreaContext.Instance.GetDebugArea().residentContext.residents)
        {
            if (v.professionComp.professionType.Equals(ProfessionType.失业) )
            {
                v.professionComp.SetProfession(ProfessionType.仓库搬运工);
                employees.Add(v);
                return;
            }
        }
    }
}
