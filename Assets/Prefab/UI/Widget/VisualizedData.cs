/***************************************************************************
// File       : VisualizedData.cs
// Author     : Panyuxuan
// Created    : 2025/12/25
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] 可视化数据类，用于在UI中显示数据变化，耦合了数据和UI文本组件。
// ***************************************************************************/

using System;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class VisualizedData<T> where T : struct
{
    [ShowInInspector] public TMPro.TMP_Text UIText;
    private T _data;

    public T Data
    {
        get => _data;
        private set
        {
            _data = value;
            if (UIText != null)
            {
                UIText.text = _data.ToString();
            }
        }
    }

    public T Get()=> Data;
    public void Set(T data)=> Data = data;

}
