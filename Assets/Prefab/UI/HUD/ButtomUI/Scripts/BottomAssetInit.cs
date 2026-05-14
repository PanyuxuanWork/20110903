/***************************************************************************
// File       : BottomAssetInit.cs
// Author     : Panyuxuan
// Created    : 2025/11/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;
using UnityEngine;


public class BottomAssetInit : MonoBehaviour
{
    [SerializeField] private List<BuildAsset> Buildings;
    [HideInInspector] public List<BuildAsset> Building_Copy=new();
    [SerializeField] private Transform SecondPanel;

    private void Start()
    {
        foreach (var v in Buildings)
        {
           Building_Copy.Add(Instantiate(v));
        }
        Init();
    }

    public void Init()
    {
        for (int i = 0; i < SecondPanel.childCount; i++)
        {
            var child = SecondPanel.GetChild(i);
            if (child.TryGetComponent(out B_Second_Container_View containerView))
            {
                InitSecondContainer(containerView.type, containerView);
            }
        }
    }

    private void InitSecondContainer(B_First_Widget_Type type, B_Second_Container_View view)
    {
        switch (type)
        {
            case B_First_Widget_Type.Building:
                {
                    foreach (var v in Building_Copy)
                    {
                        var widget = view.CreateOneWidget(v.bname, v.icon, () =>
                        {
                            BuildingService.Instance.StartBuilding(v);
                        });
                        ModelManager.hudModel.BuildingSecondWidgetDic.TryAdd(
                            v.bname, widget);
                        view.WidgetList.Add(widget);
                        widget.unlock = v.unlocked;
                        view.OnShowAction += () =>
                        {
                            
                        };
                    }
                    break;
                }
        }
    }

}
