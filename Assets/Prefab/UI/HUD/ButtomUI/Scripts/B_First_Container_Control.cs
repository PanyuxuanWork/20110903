/***************************************************************************
// File       : B_First_Container_Control.cs
// Author     : Panyuxuan
// Created    : 2025/11/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/


using System.Collections.Generic;
using Unity.VisualScripting;

public class B_First_Container_Control
{
    public B_First_Container_View view;
    public B_First_Container_Control(B_First_Container_View view)
    {
        this.view = view;
    }
    public void OnFirstWidgetClicked(B_First_Widget_Type widgetType)
    {
        if (!view.SecondContainerDic.TryGetValue(widgetType, out var secondContainer))
        {
            TLog.Error(view, "未找到对应的二级面板");
            return;
        }
        if (!secondContainer.gameObject.activeSelf)
            secondContainer.OnShow();
    }
}
