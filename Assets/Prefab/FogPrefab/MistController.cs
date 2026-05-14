/***************************************************************************
// File       : MistController.cs
// Author     : Panyuxuan
// Created    : 2026/03/02
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

public class MistController : MonoBehaviour
{
    private bool exploring;

    private float progress = 0;
    private float speed = 1;

    public void Init(float x,float z,float speed)
    {
        transform.localScale =
            new Vector3(
                transform.localScale.x * x,
                transform.localScale.y,
                transform.localScale.z * z);
    }


    
}
