/***************************************************************************
// File       : PopController.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class PopController : MonoSingleton<PopController>
{
    [SerializeField] private RectTransform popTransform;
    [SerializeField] private UI_Pop_ProducerBuilding ProducerBuilding;
    [SerializeField] private UI_Pop_Resident ResidentPop;
    [SerializeField] private UI_Pop_Warehouse WareHousePop;
    public List<SmartPopup> allPops = new();

    #region Config
    public void ShowProducer(ProductBuilding build)
    {

        if (TryGetAPopup<UI_Pop_ProducerBuilding>(out var v))
        {
            if (v is UI_Pop_ProducerBuilding building)
            {
                Model_Pop_ProducerBuilding model = new Model_Pop_ProducerBuilding();
                model.unit = build.producerUnit;
                model.asset = build.buildAsset;
                model.inputList = build.producerUnit.Inputs;
                model.outputList = build.producerUnit.Outputs;
                model.isOpen = build.producerUnit.isOpen;
                building.Init(model, build);
                building.transform.position = GetMousePos();
                building.gameObject.SetActive(true);

            }

        }
        else
        {
            var b = Instantiate(ProducerBuilding, this.transform);
            b.transform.position = GetMousePos();
            Model_Pop_ProducerBuilding model = new Model_Pop_ProducerBuilding();
            model.unit = build.producerUnit;
            model.asset = build.buildAsset;
            model.inputList = build.producerUnit.Inputs;
            model.outputList = build.producerUnit.Outputs;
            model.isOpen = build.producerUnit.isOpen;
            b.Init(model, build);
            b.transform.position = GetMousePos();
            b.gameObject.SetActive(true);
            allPops.Add(b);
        }
    }

    public void ShowResident(Resident resident)
    {
        if (TryGetAResidentPop(out UI_Pop_Resident p))
        {
            p.gameObject.SetActive(true);
            p.Init(resident);
        }
        else
        {
            Instantiate(ResidentPop,transform).Init(resident);
        }
    }

    public void ShowWareHouse(Warehouse house)
    {
        if (TryGetAWareHouse(out UI_Pop_Warehouse warehouse))
        {
            warehouse.gameObject.SetActive(true);
            warehouse.Init(house);
        }
        else
        {
            Instantiate(WareHousePop,transform).Init(house);
        }
    }
    #endregion

    #region 内部调用
    public void CloseAll()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            if (transform.GetChild(i).GetComponent<SmartPopup>().IsPinned)
                continue;
            transform.GetChild(i).gameObject.SetActive(false);
        }
    }

    public void ForceCloseAll()
    {
        for (int i = 0; i < transform.childCount; i++)
        {
            transform.GetChild(i).gameObject.SetActive(false);
        }
    }

    private bool TryGetAPopup<T>(out SmartPopup a) where T : SmartPopup
    {
        a = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            if (transform.GetChild(i).gameObject.activeSelf)
                continue;
            var v = transform.GetChild(i).GetComponent<SmartPopup>();
            if (v != null && v is T)
            {
                a = v;
                return true;
            }
        }

        return false;

    }

    private bool TryGetAResidentPop(out UI_Pop_Resident upr)
    {
        upr = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            var v = transform.GetChild(i).gameObject;
            if (v.TryGetComponent(out upr))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetAWareHouse(out UI_Pop_Warehouse upr)
    {
        upr = null;
        for (int i = 0; i < transform.childCount; i++)
        {
            var v = transform.GetChild(i).gameObject;
            if (v.TryGetComponent(out upr))
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 GetMousePos()
    {
        Vector2 mousePosition = Input.mousePosition;

        Vector3 worldPos;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(popTransform, mousePosition, null, out worldPos))
        {

        }
        else
        {
            Debug.LogError("Failed to convert screen point to world point.");
        }
        return worldPos;
    }


    #endregion

}
