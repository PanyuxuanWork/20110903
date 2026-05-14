/***************************************************************************
// File       : CheckBox.cs
// Author     : Panyuxuan
// Created    : 2026/02/02
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class CheckBox : MonoBehaviour
{
    [HideInInspector]public Button btn;
    public Sprite OnSprite;
    public Sprite OffSprite;
    public Action<bool> OnClick;

    private void Awake()
    {
        btn = GetComponent<Button>();
        btn.onClick.AddListener(()=>isOn=!isOn);
    }

    private bool _isOn;

    public bool isOn
    {
        get=>_isOn;
        private set
        {
            _isOn = value;
            UpdateEffect(_isOn);
        }
    }


    private void UpdateEffect(bool b)=> btn.image.sprite = b?OnSprite:OffSprite;

    public void SetValue(bool b) => isOn = b;

}
