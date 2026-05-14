/***************************************************************************
// File       : TransferRequest.cs
// Author     : Panyuxuan
// Created    : 2026/03/17
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sim.Resources;
using System;
using System.Collections.Generic;
using UnityEngine;

public enum TransferDirection
{
    InputToProducer,
    OutputFromProducer
}

public enum TransferRequestState
{
    None,
    Pending,
    Dispatching,
    InProgress,
    Completed,
    Failed,
    Canceled
}

[Serializable]
public sealed class TransferRequest
{
    public string RequestId;
    public Storage SourceStorage;
    public Storage TargetStorage;
    public ResourceId ResourceId;
    public int Amount;
    public TransferRequestState State;

    public TransferRequest(
        string requestId,
        Storage sourceStorage,
        Storage targetStorage,
        ResourceId resourceId,
        int amount)
    {
        RequestId = requestId;
        SourceStorage = sourceStorage;
        TargetStorage = targetStorage;
        ResourceId = resourceId;
        Amount = amount;
        State = TransferRequestState.None;
    }

    public static TransferRequest Create(
        Storage sourceStorage,
        Storage targetStorage,
        ResourceId resourceId,
        int amount)
    {
        string requestId = Guid.NewGuid().ToString();
        return new TransferRequest(requestId, sourceStorage, targetStorage, resourceId, amount);
    }
}

[Serializable]
public struct TransferResourceAmount
{
    public ResourceId ResourceId;
    public int Amount;

    public TransferResourceAmount(ResourceId resourceId, int amount)
    {
        ResourceId = resourceId;
        Amount = amount;
    }
}

[Serializable]
public sealed class TransferRequestMixed
{
    public string RequestId;
    public Storage SourceStorage;
    public Storage TargetStorage;
    public List<TransferResourceAmount> ResourceAmounts;
    public TransferRequestState State;

    public TransferRequestMixed(
        string requestId,
        Storage sourceStorage,
        Storage targetStorage,
        List<TransferResourceAmount> resourceAmounts)
    {
        RequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString() : requestId;
        SourceStorage = sourceStorage;
        TargetStorage = targetStorage;
        ResourceAmounts = resourceAmounts != null
            ? new List<TransferResourceAmount>(resourceAmounts)
            : new List<TransferResourceAmount>();
        State = TransferRequestState.None;
    }

    public static TransferRequestMixed Create(
        Storage sourceStorage,
        Storage targetStorage,
        List<TransferResourceAmount> resourceAmounts)
    {
        return new TransferRequestMixed(Guid.NewGuid().ToString(), sourceStorage, targetStorage, resourceAmounts);
    }

    public List<TransferRequest> BuildChildRequests()
    {
        var result = new List<TransferRequest>();

        if (ResourceAmounts == null || ResourceAmounts.Count == 0)
            return result;

        for (int i = 0; i < ResourceAmounts.Count; i++)
        {
            var item = ResourceAmounts[i];
            string childRequestId = $"{RequestId}:{i}:{item.ResourceId}";
            result.Add(new TransferRequest(
                childRequestId,
                SourceStorage,
                TargetStorage,
                item.ResourceId,
                item.Amount));
        }

        return result;
    }
}

[Serializable]
public sealed class TransferRequestMixedContext
{
    [SerializeField] private string _requestId;

    private readonly List<TransferRequestContext> _contexts = new();
    private readonly List<TransferRequestContext> _succeededContexts = new();
    private readonly List<TransferRequestContext> _failedContexts = new();

    public string RequestId => _requestId;
    public TransferRequestMixed Request { get; private set; }

    public IReadOnlyList<TransferRequestContext> Contexts => _contexts;
    public IReadOnlyList<TransferRequestContext> SucceededContexts => _succeededContexts;
    public IReadOnlyList<TransferRequestContext> FailedContexts => _failedContexts;

    public int TotalCount => _contexts.Count;
    public int SuccessCount => _succeededContexts.Count;
    public int FailedCount => _failedContexts.Count;

    public bool HasAnySuccess => SuccessCount > 0;
    public bool IsAllSucceeded => TotalCount > 0 && FailedCount == 0;

    public string Message { get; internal set; }

    internal void Reset(TransferRequestMixed request)
    {
        Request = request;
        _requestId = request != null ? request.RequestId : null;
        Message = null;

        _contexts.Clear();
        _succeededContexts.Clear();
        _failedContexts.Clear();
    }

    internal void AddResult(TransferRequestContext context, bool enqueueSucceeded)
    {
        if (context == null)
            return;

        _contexts.Add(context);

        if (enqueueSucceeded)
            _succeededContexts.Add(context);
        else
            _failedContexts.Add(context);
    }
}
