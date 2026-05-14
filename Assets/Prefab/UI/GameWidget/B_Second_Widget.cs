/***************************************************************************
// File       : B_Second_Widget.cs
// Author     : Panyuxuan
// Created    : 2025/11/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using System.Net.Mime;
using ChenUI;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class B_Second_Widget : MonoBehaviour
{
    public CButton btn;
    public TMP_Text text;
    public bool unlock = false;

    public B_Second_Widget Create(string name,Sprite image,UnityAction action)
    {
        btn.image.sprite = image;
        btn.onClick.AddListener(action);
        text.text = name;
        gameObject.SetActive(unlock);
        return this;
    }

    public void Unlock()
    {
        unlock=true;
    }

}
