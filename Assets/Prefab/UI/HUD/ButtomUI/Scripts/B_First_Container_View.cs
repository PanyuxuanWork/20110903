/***************************************************************************
// File       : B_First_Container_View.cs
// Author     : Panyuxuan
// Created    : 2025/11/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;


[Serializable]
public struct B_FISRT_CONTAINER_CONFIG
{
    public B_First_Widget_Type Type;
    public Sprite UnSelectedSprite;
    public Sprite SelectedSprite;
}

public class B_First_Container_View : MonoBehaviour
{
    public BottomUIView bottomUIView;
    public B_Second_Container_View secondContainerView;
    public Transform SecondRoot;
    private B_First_Container_Control controller;
    public Dictionary<B_First_Widget_Type, B_Second_Container_View> SecondContainerDic = new();


    [Header("Config")]
    public List<B_FISRT_CONTAINER_CONFIG> firstContainerConfigs;
    private List<B_First_Widget> firstWidgets = new();
    [SerializeField] private B_First_Widget firstWidgetPrefab;
    private void Awake()
    {
        controller = new B_First_Container_Control(this);
        foreach (var v in firstContainerConfigs)
        {
            var widget = Instantiate(firstWidgetPrefab, transform);

            #region 按钮
            widget.cbtn.onClick.AddListener(() => controller.OnFirstWidgetClicked(v.Type));
            widget.cbtn.transition = Selectable.Transition.SpriteSwap;
            widget.cbtn.GetComponentInChildren<Image>().sprite = v.UnSelectedSprite;

            var state = widget.cbtn.spriteState;
            state.selectedSprite = v.SelectedSprite;
            state.pressedSprite = v.SelectedSprite;
            state.highlightedSprite = v.SelectedSprite;

            widget.cbtn.spriteState = state;

            #endregion

            firstWidgets.Add(widget);
        }

        // 初始化第二个容器视图字典
        for (int i = 0; i < SecondRoot.childCount; i++)
        {
            var child = SecondRoot.GetChild(i).GetComponent<B_Second_Container_View>();
            if (child == null)
            {
                TLog.Error(this, "未找到B_Second_Container_View组件");
                return;
            }

            if (!SecondContainerDic.TryAdd(child.type, child))
            {
                TLog.Error("有重复类型的二级面板");
                return;
            }
        }
    }

}
