/***************************************************************************
// File       : GameManager.cs
// Author     : Panyuxuan
// Created    : 2026/02/22
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using UnityEngine;

[AutoAttached]
public class GameManager : MonoSingleton<GameManager>
{
    protected override void Awake()
    {
        base.Awake();
        ModelManager.Init();
    }
}
