/***************************************************************************
// File       : HUDController.cs
// Author     : Panyuxuan
// Created    : 2025/11/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using Sim.Resources;
using UnityEngine;

public class HUDController : MonoSingleton<HUDController>, IStepListener
{
    private HUDModel model;
    private HUDView view;
    private Area defaultArea;

    protected override void Awake()
    {
        base.Awake();
        
        view = GetComponent<HUDView>();
        
    }

    private IEnumerator Start()
    {
        yield return null;

        defaultArea = AreaContext.Instance.GetDebugArea();

        model = ModelManager.hudModel;
        model.WorkPeopleAmount.OnValueChanged += OnWorkAmountValueChanged;
        model.PopulationAmount.OnValueChanged += OnPopulationValueChanged;
        model.UnEmployeeAmount.OnValueChanged += OnUnEmployeeValueChanged;
        model.HappinessValue.OnValueChanged += OnHappinessValueChanged;
        model.ReputationValue.OnValueChanged += OnReputationValueChanged;

        model.Gold.OnValueChanged += OnGoldValueChanged;
        model.Food.OnValueChanged += OnFoodValueChanged;
        model.Stone.OnValueChanged += OnStoneValueChanged;
        model.Water.OnValueChanged += OnWaterValueChanged;
        model.TechPoint.OnValueChanged += OnTechPointValueChanged;
        model.Wood.OnValueChanged += OnWoodValueChanged;
    }


    #region HUDModelUpdate

    private void UpdateModelData()
    {
        if (model == null) return;

        var res = defaultArea.residentContext;
        model.PopulationAmount.Data = res.residents.Count;

        if (res.TryGetResidentSetByProfession(ProfessionType.失业, out var set))
        {
            var unemployeeCount = set.Count;
            model.WorkPeopleAmount.Data = res.residents.Count - unemployeeCount;
            model.UnEmployeeAmount.Data = unemployeeCount;
        }

        model.HappinessValue.Data = defaultArea.cityContext.GetCurHappiness();
        model.ReputationValue.Data = defaultArea.cityContext.GetCurReputation();

        model.Food.Data = defaultArea.resourceContext.GetResourceTotalAmountByType(ResourceId.食物);
        model.Stone.Data = defaultArea.resourceContext.GetResourceTotalAmountByType(ResourceId.石头);
        model.Water.Data = defaultArea.resourceContext.GetResourceTotalAmountByType(ResourceId.水);
        model.Wood.Data = defaultArea.resourceContext.GetResourceTotalAmountByType(ResourceId.木头);

    }


    #endregion

    #region Tick

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public void OnTick(in TickContext ctx)
    {
        UpdateModelData();
    }

    #endregion

    #region Action

    private void OnPopulationValueChanged(int old, int n)
    {
        view.topUIWidget.cityPeopleInfo.population.Set(n);
    }

    private void OnWorkAmountValueChanged(int old, int n)
    {
        view.topUIWidget.cityPeopleInfo.worker.Set(n);
    }

    private void OnUnEmployeeValueChanged(int old, int n)
    {
        view.topUIWidget.cityPeopleInfo.unemployment.Set(n);
    }


    private void OnHappinessValueChanged(int old, int n)
    {
        view.topUIWidget.cityPeopleInfo.happiness.Set(n);
    }

    private void OnReputationValueChanged(int old, int n)
    {
        view.topUIWidget.cityPeopleInfo.reputation.Set(n);
    }

    private void OnGoldValueChanged(int old, int n)
    {
        view.topUIWidget.cityResourceInfo.gold.Set(n);
    }

    private void OnWaterValueChanged(int old, int n)
    {
        view.topUIWidget.cityResourceInfo.water.Set(n);
    }

    private void OnWoodValueChanged(int old, int n)
    {
        view.topUIWidget.cityResourceInfo.wood.Set(n);
    }

    private void OnFoodValueChanged(int old, int n)
    {
        view.topUIWidget.cityResourceInfo.food.Set(n);
    }

    private void OnStoneValueChanged(int old, int n)
    {
        view.topUIWidget.cityResourceInfo.brick.Set(n);
    }

    private void OnTechPointValueChanged(int old, int n)
    {
        view.topUIWidget.cityResourceInfo.techPoints.Set(n);
    }

    #endregion

    #region UI解锁

    public void NotifyBuildUnlock(string buildName)
    {
        if (model.BuildingSecondWidgetDic.TryGetValue(buildName, out var widget))
        {
            if (widget.unlock)
            {
                TLog.Warning(this,$"建筑{buildName}已经解锁,重复解锁");
                return;
            }
            widget.unlock = true;
            widget.gameObject.SetActive(true);
        }
    }


    #endregion
}
