/***************************************************************************
// File       : GetResourceTask.cs
// Author     : Panyuxuan
// Created    : 2025/10/26
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// **************************************************************************/

using UnityEngine;

public class GetResourceTask : TaskBase
{
    public Resident resident;
    public int residentTicket;
    public int storageTicket;
    public Storage storage;
    private ResidentEconomyService reco;
    public int amount;

    protected override void OnStart()
    {
        bool residentSuccess = amount == reco.backpack.GetResource(residentTicket, amount);
        bool storageSuccess = amount == storage.OfferResource(storageTicket, amount);
        if (!residentSuccess || !storageSuccess)
        {
            Debug.LogError($"[GetResourceTask] 资源搬运失败！residentSuccess={residentSuccess}, storageSuccess={storageSuccess}");
        }
    }

    protected override bool OnUpdate(float dt)
    {
        return true;
    }

    public static GetResourceTask Create(Resident resident, Storage storage, int residentTicket,
        int storageTicket)
    {
        var t = ObPool<GetResourceTask>.Get();
        t.resident = resident;
        t.storage = storage;
        t.reco = resident.GetComponent<ResidentEconomyService>();
        t.residentTicket = residentTicket;
        t.storageTicket = storageTicket;
        t.amount = 0;
        return t;
    }

}

