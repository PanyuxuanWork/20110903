/***************************************************************************
// File       : BottomUIView.cs
// Author     : Panyuxuan
// Created    : 2025/10/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using NUnit.Framework;
using UnityEngine;

public class BottomUIView : MonoBehaviour
{
    [SerializeField]private B_First_Container_View firstContainerView;
    [SerializeField]private B_Second_Container_View secondContainerView;
    

    /// <summary>
    /// 设置第一个容器视图显示与否
    /// </summary>
    /// <param bname="isVisible"></param>
    public void 
        SetSecondContainerViewShow(bool isVisible)
    {
        secondContainerView.gameObject.SetActive(isVisible);
    }
}
