/***************************************************************************
// File       : ProductBuilding.cs
// Author     : Panyuxuan
// Created    : 2025/10/21
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using UnityEngine;


[RequireComponent(typeof(ClickedUnit))]
[RequireComponent(typeof(ProducerUnit))]

public class ProductBuilding : BuildingBase
{
    protected ClickedUnit clickedUnit;
    public ProducerUnit producerUnit;
    public ProfessionType residentProfessionType = ProfessionType.失业;
    protected override void Awake()
    {
        base.Awake();

        producerUnit = GetComponent<ProducerUnit>();

        gameObject.TryGetComponent<ClickedUnit>(out var comp);
        if (comp != null)
        {
            clickedUnit = comp;
        }

        else
        {
            clickedUnit = gameObject.AddComponent<ClickedUnit>();
        }

        clickedUnit = GetComponent<ClickedUnit>();
        clickedUnit.ClickedName = buildAsset.bname;
        clickedUnit.OnClicked += OnShow;

        //TODO: 临时调用,20260321
        clickedUnit.OnClicked+= AddEmployee;


    }


    protected virtual void OnShow()
    {
        PopController.Instance.ShowProducer(this);
    }

    public void AddEmployee()
    {

        foreach (var v in AreaContext.Instance.GetDebugArea().residentContext.residents)
        {

            if (v.professionComp.professionType.Equals(ProfessionType.失业))
            {
                v.professionComp.SetProfession(residentProfessionType);
                producerUnit.EmployeeLists.Add(v);

                //TODO: 寻路掩码预设
                MoveToTask move = MoveToTask.Create(v, transform, new byte[] { 1, 0, 2, 3, 4, 5, 6, 7, 8, 9 });
                v.taskService.Enqueue(move);
                return;
            }
        }

    }
}
