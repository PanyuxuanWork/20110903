/***************************************************************************
// File       : AreaBase.cs
// Author     : Panyuxuan
// Created    : 2025/10/21
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using UnityEngine;

public abstract class AreaBase : MonoBehaviour
{
    public Area ParentArea { get; set; }
    public abstract void Init();
}
