/***************************************************************************
// File       : GlobalTimeSystem.cs
// Author     : Panyuxuan
// Created    : 2026/03/02
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using Enviro;
using UnityEngine;

[RequireComponent(typeof(SeasonSystem))]
[RequireComponent(typeof(TimeSystem))]
public class GlobalTimeSystem : MonoSingleton<GlobalTimeSystem>
{

    private TimeSystem timeSystem;
    private SeasonSystem seasonSystem;
    private EnviroManager enviro;

    protected override void Awake()
    {
        base.Awake();
        timeSystem = GetComponent<TimeSystem>();
        seasonSystem = GetComponent<SeasonSystem>();
    }

    private void Start()
    {
        enviro = EnviroManager.instance;
    }


}
