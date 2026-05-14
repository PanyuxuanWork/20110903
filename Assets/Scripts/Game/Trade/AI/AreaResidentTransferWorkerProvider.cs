/***************************************************************************
// File       : AreaResidentTransferWorkerProvider.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sim.Resources;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AreaResidentTransferWorkerProvider : MonoBehaviour, ITransferWorkerProvider
{
    [Header("归属")]
    public Area ParentArea;

    [ShowInInspector]
    private readonly HashSet<ResidentEconomyService> _busyWorkers = new();

    public int BusyCount => _busyWorkers.Count;

    private void Awake()
    {
        if (ParentArea == null)
            ParentArea = GetComponent<Area>();
    }

    public bool TryAcquireWorker(
        ResourceId resourceId,
        Storage source,
        out TransferWorkerHandle handle)
    {
        handle = null;

        if (source == null)
            return false;

        if (ParentArea == null || ParentArea.residentContext == null)
            return false;

        if (!ParentArea.residentContext.TryGetResidentSetByProfession(ProfessionType.仓库搬运工, out var set))
            return false;

        float bestScore = float.PositiveInfinity;
        ResidentEconomyService bestEco = null;
        Resident bestResident = null;

        foreach (var rr in set)
        {
            if (rr == null)
                continue;

            var eco = rr.GetComponent<ResidentEconomyService>();
            if (eco == null || !eco.isActiveAndEnabled)
                continue;

            if (_busyWorkers.Contains(eco))
                continue;

            if (!eco.IsIdleCarrier())
                continue;

            if (!eco.CanCarry(resourceId, 1))
                continue;

            var resident = eco.GetComponent<Resident>();
            if (resident == null)
                continue;

            float score = (rr.transform.position - source.transform.position).sqrMagnitude;
            if (score < bestScore)
            {
                bestScore = score;
                bestEco = eco;
                bestResident = resident;
            }
        }

        if (bestEco == null || bestResident == null)
            return false;

        _busyWorkers.Add(bestEco);
        handle = new TransferWorkerHandle(bestEco, bestResident);
        return true;
    }

    public void ReleaseWorker(TransferWorkerHandle handle)
    {
        if (handle?.WorkerEco == null)
            return;

        _busyWorkers.Remove(handle.WorkerEco);
    }
}