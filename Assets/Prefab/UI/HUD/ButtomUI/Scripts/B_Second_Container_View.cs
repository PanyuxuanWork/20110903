/***************************************************************************
// File       : B_Second_Container_View.cs
// Author     : Panyuxuan
// Created    : 2025/11/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
public class B_Second_Container_View : MonoBehaviour
{
    public B_First_Widget_Type type;
    [SerializeField] public B_Second_Widget SecondWidget;
    public Action OnShowAction ; 
    public List<B_Second_Widget> WidgetList;
    public List<Object> ObjList;
    public IEnumerator Start()
    {
        yield return null;
        gameObject.SetActive(false);
    }

    public void OnShow()
    {
        gameObject.SetActive(true);
        UpdateUnlockedWidget();
        OnShowAction?.Invoke();
    }

    public B_Second_Widget CreateOneWidget(string name, Sprite image, UnityAction action)
    {
        try
        {
            var widget = Instantiate(SecondWidget, this.transform);
            widget.Create(name, image, action);
            if (type != B_First_Widget_Type.Building)
                widget.Unlock();
            return widget;
        }
        catch (Exception e)
        {
            TLog.Error(e.Message);
            throw;
        }

    }

    private void UpdateUnlockedWidget()
    {
        foreach (var v in WidgetList)
        {
            v.gameObject.SetActive(v.unlock);
        }
    }
}
