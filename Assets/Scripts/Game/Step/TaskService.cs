/***************************************************************************
// File       : TaskService.cs
// Author     : Panyuxuan
// Created    : 2026/03/15
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;
[AutoAttached]
public class TaskService : MonoSingleton<TaskService>, IStepListener
{
    private readonly List<TaskQueue> _queues = new();
    private readonly List<TaskQueue> _pendingAdd = new();
    private readonly List<TaskQueue> _pendingRemove = new();

    private bool _isTicking;
    private bool _useGlobal;
    private bool _serviceIdleRaised;

    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;

    [field: SerializeField]
    public TaskServiceState ServiceState { get; private set; } = TaskServiceState.Idle;

    public int QueueCount => _queues.Count;

    public event Action<TaskQueue> QueueAdded;
    public event Action<TaskQueue> QueueRemoved;
    public event Action<TaskQueue> QueueBecameIdle;
    public event Action ServiceBecameIdle;

    public void OnTick(in TickContext ctx) => TickAll(ctx.DeltaTime);

    private void OnEnable()
    {
        _useGlobal = GlobalStep.Instance != null;
        if (_useGlobal)
            GlobalStep.Instance.AddListener(this);

        enabled = true;
    }

    private void OnDisable()
    {
        if (_useGlobal && GlobalStep.Instance != null)
            GlobalStep.Instance.RemoveListener(this);
    }

    private void Update()
    {
        if (!_useGlobal)
            TickAll(Time.deltaTime);
    }

    public bool AddQueue(TaskQueue queue)
    {
        if (queue == null)
            return false;

        if (ContainsQueue(queue))
            return false;

        if (_isTicking)
        {
            if (!_pendingAdd.Contains(queue))
                _pendingAdd.Add(queue);

            _pendingRemove.Remove(queue);
            return true;
        }

        _queues.Add(queue);
        _serviceIdleRaised = false;
        QueueAdded?.Invoke(queue);
        RefreshServiceState();
        return true;
    }

    public bool RemoveQueue(TaskQueue queue, bool stopQueue = false, bool clearPending = false)
    {
        if (queue == null)
            return false;

        if (_isTicking)
        {
            if (!_pendingRemove.Contains(queue))
                _pendingRemove.Add(queue);

            _pendingAdd.Remove(queue);
            return true;
        }

        if (!_queues.Remove(queue))
            return false;

        if (stopQueue)
            queue.Stop(cancelCurrent: true, clearPending: clearPending);
        else if (clearPending)
            queue.ClearPending();

        QueueRemoved?.Invoke(queue);
        RefreshServiceState();
        RaiseIdleIfNeeded();
        return true;
    }

    public bool ContainsQueue(TaskQueue queue)
    {
        if (queue == null)
            return false;

        return _queues.Contains(queue) || _pendingAdd.Contains(queue);
    }

    public IReadOnlyList<TaskQueue> GetQueuesSnapshot()
    {
        return new List<TaskQueue>(_queues);
    }

    public void TickAll(float dt)
    {
        if (!IsActive)
            return;

        if (ServiceState == TaskServiceState.Paused || ServiceState == TaskServiceState.Stopped)
            return;

        _isTicking = true;

        try
        {
            for (int i = 0; i < _queues.Count; i++)
            {
                TaskQueue queue = _queues[i];
                if (queue == null)
                    continue;

                if (!queue.IsActive)
                    continue;

                TaskServiceState beforeState = queue.State;
                bool hadTasksBefore = queue.HasTasks;

                queue.Tick(dt);

                if (hadTasksBefore &&
                    !queue.HasTasks &&
                    queue.State == TaskServiceState.Idle &&
                    beforeState != TaskServiceState.Idle)
                {
                    QueueBecameIdle?.Invoke(queue);
                }
            }
        }
        finally
        {
            _isTicking = false;
            FlushPendingOperations();
        }

        RefreshServiceState();
        RaiseIdleIfNeeded();
    }

    public void Pause()
    {
        if (ServiceState == TaskServiceState.Stopped)
            return;

        for (int i = 0; i < _queues.Count; i++)
        {
            _queues[i]?.Pause();
        }

        ServiceState = TaskServiceState.Paused;
    }

    public void Resume()
    {
        if (ServiceState != TaskServiceState.Paused)
            return;

        for (int i = 0; i < _queues.Count; i++)
        {
            _queues[i]?.Resume();
        }

        RefreshServiceState();
    }

    public void Stop(bool cancelCurrent = true, bool clearPending = true)
    {
        for (int i = 0; i < _queues.Count; i++)
        {
            _queues[i]?.Stop(cancelCurrent, clearPending);
        }

        ServiceState = TaskServiceState.Stopped;
    }

    public void ResetService(bool clearQueues = true)
    {
        if (clearQueues)
        {
            for (int i = 0; i < _queues.Count; i++)
            {
                _queues[i]?.ResetQueue();
            }

            _queues.Clear();
        }

        _pendingAdd.Clear();
        _pendingRemove.Clear();
        _isTicking = false;
        _serviceIdleRaised = false;
        ServiceState = TaskServiceState.Idle;
    }

    public void CancelAllCurrent(
        string cancelMessage = null,
        TaskCancelReason cancelReason = TaskCancelReason.Manual)
    {
        for (int i = 0; i < _queues.Count; i++)
        {
            _queues[i]?.CancelCurrent(cancelMessage, cancelReason);
        }

        RefreshServiceState();
    }

    public void ClearAllPending()
    {
        for (int i = 0; i < _queues.Count; i++)
        {
            _queues[i]?.ClearPending();
        }

        RefreshServiceState();
        RaiseIdleIfNeeded();
    }

    private void FlushPendingOperations()
    {
        if (_pendingRemove.Count > 0)
        {
            for (int i = 0; i < _pendingRemove.Count; i++)
            {
                TaskQueue queue = _pendingRemove[i];
                if (queue == null)
                    continue;

                if (_queues.Remove(queue))
                {
                    QueueRemoved?.Invoke(queue);
                }
            }

            _pendingRemove.Clear();
        }

        if (_pendingAdd.Count > 0)
        {
            for (int i = 0; i < _pendingAdd.Count; i++)
            {
                TaskQueue queue = _pendingAdd[i];
                if (queue == null)
                    continue;

                if (_queues.Contains(queue))
                    continue;

                _queues.Add(queue);
                _serviceIdleRaised = false;
                QueueAdded?.Invoke(queue);
            }

            _pendingAdd.Clear();
        }
    }

    private void RefreshServiceState()
    {
        if (ServiceState == TaskServiceState.Stopped)
            return;

        bool anyPaused = false;
        bool anyRunning = false;
        bool anyPreempting = false;

        for (int i = 0; i < _queues.Count; i++)
        {
            TaskQueue queue = _queues[i];
            if (queue == null || !queue.IsActive)
                continue;

            if (queue.State == TaskServiceState.Preempting)
                anyPreempting = true;

            if (queue.State == TaskServiceState.Paused)
                anyPaused = true;

            if (queue.State == TaskServiceState.Running || queue.HasTasks)
                anyRunning = true;
        }

        if (anyPreempting)
        {
            ServiceState = TaskServiceState.Preempting;
            return;
        }

        if (anyPaused)
        {
            ServiceState = TaskServiceState.Paused;
            return;
        }

        ServiceState = anyRunning ? TaskServiceState.Running : TaskServiceState.Idle;
    }

    private void RaiseIdleIfNeeded()
    {
        if (ServiceState != TaskServiceState.Idle)
        {
            _serviceIdleRaised = false;
            return;
        }

        if (HasAnyTasks())
        {
            _serviceIdleRaised = false;
            return;
        }

        if (_serviceIdleRaised)
            return;

        _serviceIdleRaised = true;
        ServiceBecameIdle?.Invoke();
    }

    private bool HasAnyTasks()
    {
        for (int i = 0; i < _queues.Count; i++)
        {
            TaskQueue queue = _queues[i];
            if (queue != null && queue.HasTasks)
                return true;
        }

        return false;
    }
}