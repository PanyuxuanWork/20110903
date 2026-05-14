/***************************************************************************
// File       : UI_Pop_Warehouse.cs
// Author     : Panyuxuan
// Created    : 2026/02/25
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UI_Pop_Warehouse : SmartPopup
{
    public Image Icon;
    public Toggle pin;
    public Button closeBtn;
    public Toggle openCloseToggle;
    public Transform SlotsTransform;
    [SerializeField] private UI_Slot_Widget slotWidget;

    private List<UI_Slot_Widget> slots = new List<UI_Slot_Widget>();  // 动态列表
    private Warehouse wareHouse;

    public void Init(Warehouse house)
    {
        // 清理旧的
        foreach (var slot in slots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        slots.Clear();

        wareHouse = house;

        // 创建新的（最多4个）
        var storageSlots = house.storage.Slots;
        int count = Mathf.Min(4, storageSlots.Count);

        for (int i = 0; i < count; i++)
        {
            var uiSlot = Instantiate(slotWidget, SlotsTransform);
            slots.Add(uiSlot);

            var resourceSlot = storageSlots[i];
            uiSlot.Init(resourceSlot.Id.ToString(), resourceSlot.Amount);
        }
    }

    private void Update()
    {
        if (!wareHouse) return;

        var storageSlots = wareHouse.storage.Slots;
        int count = Mathf.Min(slots.Count, storageSlots.Count);

        for (int i = 0; i < count; i++)
        {
            if (slots[i] != null)
            {
                slots[i].UpdateNum(storageSlots[i].Amount);
            }
        }
    }

    private void OnDisable()
    {
        foreach (var slot in slots)
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
        slots.Clear();
        wareHouse = null;
    }
}
