/***************************************************************************
// File       : ReserveResourceTask.cs
// Author     : Panyuxuan
// Created    : 2025/10/26
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using Sim.Resources;
using Unity.VisualScripting;
using UnityEngine;

public class ReserveResourceTask : TaskBase
{
    public int ResidentTicket;
    public int StorageTicket;
    public Storage TargetStorage;
    public Resident resident;
    public ResourceId id;
    public int amount;


    protected override void OnStart()
    {
        ResidentEconomyService reco = resident.GetComponent<ResidentEconomyService>();
        if (!reco.backpack.TryReserveCapacity(id, amount, out ResidentTicket))
        {
            
        }

        if (!TargetStorage.TryReserveResource(id, amount, out StorageTicket))
        {
            
        }

    }

    protected override bool OnUpdate(float dt)
    {
        return true;
    }

    public static ReserveResourceTask Create
    (Resident resident,
        Storage targetStorage,
        ResourceId id,
        int amount)
    {
        ReserveResourceTask t = ObPool<ReserveResourceTask>.Get();
        t.resident = resident;
        t.TargetStorage = targetStorage;
        t.amount = amount;
        t.id = id;
        return t;
    }

}
