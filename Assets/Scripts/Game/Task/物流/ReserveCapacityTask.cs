/***************************************************************************
// File       : ReserveCapacityTask.cs
// Author     : Panyuxuan
// Created    : 2025/10/26
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using System;
using Sim.Resources;
using UnityEngine;

public class ReserveCapacityTask : TaskBase
{
    public Storage storage;
    public Resident resident;
    public int residentTicket;
    public int storageTicket;
    public int amount;
    public ResourceId id;
    private ResidentEconomyService reco;
    public Action<int, int> OnReserved; // (residentTicket, storageTicket)

    protected override void OnStart()
    {
        var storageSuccess = storage.TryReserveCapacity(id, amount, out storageTicket);
        var residentSuccess = reco.backpack.TryReserveResource(id, amount, out residentTicket);

        if (storageSuccess && residentSuccess)
        {
            OnReserved?.Invoke(residentTicket, storageTicket);
        }
        if (!storageSuccess)
        {
            TLog.Error("仓库容量已满");
        }
        if (!residentSuccess)
        {
            TLog.Error("居民背包容量已空");
        }
        Fail("[ReserveCapacityTask] Path not found");
    }

    protected override bool OnUpdate(float dt)
    {
        return true;
    }

    public static ReserveCapacityTask Create(Resident resident, Storage storage,
        ResourceId id, int amount, Action<int, int> outAction)
    {
        var t = ObPool<ReserveCapacityTask>.Get();
        t.resident = resident;
        t.storage = storage;
        t.reco = resident.GetComponent<ResidentEconomyService>();
        t.id = id;
        t.amount = amount;
        t.residentTicket = -1;
        t.storageTicket = -1;
        t.OnReserved = outAction;
        return t;
    }

}
