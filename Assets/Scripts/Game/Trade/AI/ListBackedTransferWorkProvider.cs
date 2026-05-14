/***************************************************************************
// File       : ListBackedTransferWorkProvider.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sim.Resources;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ListBackedTransferWorkerProvider : MonoBehaviour, ITransferWorkerProvider
{
    [Header("候选搬运工")]
    [SerializeField] private List<Resident> workers = new();

    [ShowInInspector]
    private readonly HashSet<ResidentEconomyService> _busyWorkers = new();

    public int BusyCount => _busyWorkers.Count;
    public List<Resident> Workers => workers;

    public bool TryAcquireWorker(
        ResourceId resourceId,
        Storage source,
        out TransferWorkerHandle handle)
    {
        handle = null;

        if (source == null || workers == null || workers.Count == 0)
            return false;

        float bestScore = float.PositiveInfinity;
        ResidentEconomyService bestEco = null;
        Resident bestResident = null;

        for (int i = 0; i < workers.Count; i++)
        {
            var resident = workers[i];
            if (resident == null || !resident.isActiveAndEnabled)
                continue;

            var eco = resident.GetComponent<ResidentEconomyService>();
            if (eco == null || !eco.isActiveAndEnabled)
                continue;

            if (_busyWorkers.Contains(eco))
                continue;

            if (!eco.IsIdleCarrier())
                continue;

            if (!eco.CanCarry(resourceId, 1))
                continue;

            float score = (resident.transform.position - source.transform.position).sqrMagnitude;
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
