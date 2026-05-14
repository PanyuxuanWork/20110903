/***************************************************************************
// File       : UI_Pop_Resident.cs
// Author     : Panyuxuan
// Created    : 2026/02/20
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;
using UnityEngine.UI;

public class UI_Pop_Resident : SmartPopup
{
    [Header("---------------Resident UI--------------")]
    [SerializeField] private Image icon;

    [SerializeField] private TMPro.TMP_Text nameText;
    [SerializeField] private TMPro.TMP_Text typeText;
    [SerializeField] private TMPro.TMP_Text relayHouseTxt;
    [SerializeField] private TMPro.TMP_Text relayWorkTxt;
    [SerializeField] private TMPro.TMP_Text curResidentState;
    [SerializeField] private TMPro.TMP_Text resType;
    [SerializeField] private TMPro.TMP_Text resCount;
    [SerializeField] private Button relayWorkBtn;
    [SerializeField] private Button relayHouseBtn;
    [SerializeField] private Button closeBtn;
    [SerializeField] private Button openCloseBtn;

    private Resident resident;

    public void Init(Resident resident)
    {
        this.resident = resident;
        nameText.text = resident.rName;
        curResidentState.text = resident.curState.ToString(); 
        typeText.text = resident.professionComp.professionType.ToString();
        relayHouseTxt.text = resident.workHouse!=null? resident.workHouse.name:"无固定居所";
        relayWorkTxt.text = resident.workHousePair.Key != null ? resident.workHousePair.Key.name : "失业中";
        closeBtn.onClick.AddListener (() => { gameObject.SetActive(false);}) ;
    }

    private void OnDisable()
    {
        relayWorkBtn.onClick.RemoveAllListeners();
        relayHouseBtn.onClick.RemoveAllListeners();
        closeBtn.onClick.RemoveAllListeners();
        openCloseBtn.onClick.RemoveAllListeners();
    }

    private void LateUpdate()
    {
        if (resident.TryGetComponent(out ResidentEconomyService res))
        {
            var s = res.backpack;
            resType.text = s.Slots[0].Id.ToString();
            resCount.text = s.Slots[0].Amount.ToString();
        }
        else
        {
            resType.text = "";
            resCount.text = "";
        }
    }
}
