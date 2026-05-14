/***************************************************************************
// File       : WorkTask.cs
// Author     : Panyuxuan
// Created    : 2026/01/06
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using UnityEngine;

public class WorkTask : TaskBase
{

    public static WorkTask Create()
    {
        var t = ObPool<WorkTask>.Get();
        return t;
    }

    protected override void OnStart()
    {
        
    }

    protected override bool OnUpdate(float dt)
    {
        return false;
    }
}
