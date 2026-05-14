/***************************************************************************
// File       : UI_Slot_Widget.cs
// Author     : Panyuxuan
// Created    : 2026/01/24
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sim.Resources;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class UI_Slot_Widget : MonoBehaviour
{
    [SerializeField] private Image image;
    [SerializeField] public TMP_Text uname;
    [SerializeField] private TMP_Text count;
    [SerializeField] private TMP_Text capacity;
    public ResourceId rid;
    public void Init(Sprite s, string n, string c)
    {
        image.sprite = s;
        uname.text = n;
        count.text = c;
    }

    public void Init(Sprite s, string n, int c)
    {
        image.sprite = s;
        uname.text = n;
        count.text = c.ToString();
    }

    public void UpdateNum(int c)
    {
        count.text = c.ToString();
    }

    public void Init(string n, int c)
    {
        uname.text = n;
        count.text = c.ToString();
        if (capacity != null)
            capacity.text = "100";
    }

    public void Init(ResourceId id, int c)
    {
        Init(id.ToString(), c);
        rid = id;
    }
    public void Init(Ingredient d)
    {
        Init(Sim.Resources.EnumToString.ConvertResourceID(d.Id), d.Qty);
    }
}
