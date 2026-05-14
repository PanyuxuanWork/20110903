/***************************************************************************
// File       : CButton.cs
// Author     : Panyuxuan
// Created    : 2025/11/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;


namespace ChenUI
{
    public class CButton : Button
    {
        public CText buttonText;

        protected override void Awake()
        {
            base.Awake();
            if (buttonText == null)
            {
                buttonText = GetComponentInChildren<CText>();
            }
        }
    }


}