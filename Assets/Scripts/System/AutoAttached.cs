/***************************************************************************
// File       : AutoAttached.cs
// Author     : Panyuxuan
// Created    : 2026/02/08
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class AutoAttached : Attribute
{
    public AutoAttached()
    {
      
    }

    public AutoAttached(string go)
    {

    }
}
