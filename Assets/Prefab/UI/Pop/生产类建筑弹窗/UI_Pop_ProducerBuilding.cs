/***************************************************************************
// File       : UI_Pop_ProducerBuilding.cs
// Author     : Panyuxuan
// Created    : 2026/01/26
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

public class UI_Pop_ProducerBuilding : SmartPopup
{
    [Header("-------------------------")]
    [Header("Top")]
    [SerializeField] private CheckBox openOffBtn;

    [Header("Mid")]
    [SerializeField] private Image icon;
    [SerializeField] private TMPro.TMP_Text nameText;
    [SerializeField] private TMPro.TMP_Text typeText;
    [SerializeField] private TMPro.TMP_Text employeeText;
    [SerializeField] private Button addEmployeeBtn;
    [SerializeField] private Button removeEmployeeBtn;
    [SerializeField] private UI_Slot_Widget InputWidget;
    [SerializeField] private UI_Slot_Widget OutputWidget;
    [SerializeField] private Transform sloTransform;
    [ShowInInspector] private HashSet<UI_Slot_Widget> inputSlots = new();
    [ShowInInspector] private HashSet<UI_Slot_Widget> outputSlots = new();
    [HideInInspector] public ProductBuilding produceBuilding;

    #region BtnAction

    private void Close()
    {

    }

    private void FocusOn()
    {

    }

    private void OpenOff()
    {

    }



    private void RemoveOneEmployee()
    {
        if (produceBuilding.producerUnit.EmployeeLists.Count > 0)
        {
            produceBuilding.producerUnit.EmployeeLists[^1].RemoveRelayOnGo();
            produceBuilding.producerUnit.EmployeeLists.RemoveAt(produceBuilding.producerUnit.EmployeeLists.Count - 1);
        }
    }

    #endregion

    #region PublicAction

    private void Init(Sprite s, string n, string t, string e)
    {
        icon.sprite = s;
        nameText.text = n;
        typeText.text = t;
        employeeText.text = e;
    }

    private void Init(Sprite s, string n, string t, int e, bool b)
    {
        Init(s, n, t, e.ToString());
    }

    public void Init(Model_Pop_ProducerBuilding model, ProductBuilding u)
    {
        Init(model.asset.icon,
            model.asset.bname,
            model.asset.buildMinor.ToString(),
            model.unit.EmployeeLists.Count,
            model.isOpen);
        produceBuilding = u;

        foreach (var v in u.producerUnit.Inputs)
        {
            if (GetOneSlot(true, out var slot))
            {
                slot.Init(v);
            }
            else
            {
                var widget = Instantiate(InputWidget, sloTransform);
                inputSlots.Add(widget);
                widget.Init(v);
            }
        }

        foreach (var v in u.producerUnit.Outputs)
        {
            if (GetOneSlot(false, out var slot))
            {
                slot.Init(v);
            }
            else
            {
                var widget = Instantiate(OutputWidget, sloTransform);
                outputSlots.Add(widget);
                widget.Init(v);
            }
        }

        openOffBtn.SetValue(model.isOpen);

        Init();
    }


    #endregion

    private void OnDisable()
    {
        foreach (var v in inputSlots)
        {
            v.gameObject.SetActive(false);
        }
        foreach (var v in outputSlots)
        {
            v.gameObject.SetActive(false);
        }

    }

    private void Init()
    {
        addEmployeeBtn.onClick.AddListener(produceBuilding.AddEmployee);
        removeEmployeeBtn.onClick.AddListener(RemoveOneEmployee);
    }

    private bool GetOneSlot(bool isInput, out UI_Slot_Widget slot)
    {
        slot = null;
        var v = isInput ? inputSlots : outputSlots;
        foreach (var vv in v)
        {
            if (!vv.gameObject.activeSelf)
            {
                slot = vv;
                vv.gameObject.SetActive(true);
                return true;
            }
        }

        return false;
    }


    private void Update()
    {
        if (produceBuilding)
        {
            employeeText.text = produceBuilding.producerUnit.EmployeeLists.Count.ToString();
            for (int i = 0; i < inputSlots.ToList().Count; i++)
            {
                inputSlots.ToList()[i].UpdateNum(produceBuilding.producerUnit.inputStorage.Slots[i].Amount);
            }

            for (int i = 0; i < outputSlots.ToList().Count; i++)
            {
                outputSlots.ToList()[i].UpdateNum(produceBuilding.producerUnit.outputStorage.Slots[i].Amount);
            }
        }
    }

}
