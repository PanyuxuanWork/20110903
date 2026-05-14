/***************************************************************************
// File       : B_First_Widget.cs
// Author     : Panyuxuan
// Created    : 2025/11/01
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using ChenUI;
using UnityEngine;

public class B_First_Widget : MonoBehaviour
{
    public B_First_Widget_Type type;
    [SerializeField] public CButton cbtn;

}

public enum B_First_Widget_Type
{
    None,
    Building,

}