/***************************************************************************
// File       : PopmuskLogic.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

public class PopmuskLogic : MonoBehaviour
{
    private void LateUpdate()
    {
        foreach (var v in PopController.Instance.allPops)
        {
            if (v.gameObject.activeSelf)
                return;
        }
        gameObject.SetActive(false);
    }
}
