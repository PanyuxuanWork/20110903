/***************************************************************************
// File       : DestroyInPackmode.cs
// Author     : Panyuxuan
// Created    : 2025/11/30
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

[DisallowMultipleComponent]
public class DestroyInPackmode : MonoBehaviour
{
    private void Awake()
    {
        if (!Application.isEditor && !Application.isPlaying)
        {
            // 销毁自身及所有子物体
            Destroy(gameObject);
        }
    }
}
