/***************************************************************************
// File       : StepManager.cs
// Author     : Panyuxuan
// Created    : 2025/10/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Add script summary here
// ***************************************************************************/

using System;
using System.Collections.Generic;
public class StepManager
{
    private readonly List<MiniStep> _steps = new List<MiniStep>(64);
    private readonly List<MiniStep> _toAdd = new List<MiniStep>(16);
    private readonly List<MiniStep> _toRemove = new List<MiniStep>(16);

    public Action<MiniStep> ReleaseToPool;

    public event Action<MiniStep> StepCompleted;

    public bool AutoReturnToPool { get; set; } = false;

    public void Add(MiniStep step, bool autoStart = true)
    {
        if (step == null) return;
        // if currently ticking, queue add; else add immediately
        _toAdd.Add(step);
        if (autoStart)
        {

        }
    }
    
    public void Remove(MiniStep step)
    {
        if (step == null) return;
        _toRemove.Add(step);
    }

   
    public void CancelAll()
    {
        // cancel active
        foreach (var s in _steps)
        {
            try { if (!s.IsDone) s.Cancel(); } catch { }
        }
        // also pending
        foreach (var s in _toAdd) { try { if (!s.IsDone) s.Cancel(); } catch { } }
        // schedule all for removal
        _toRemove.AddRange(_steps);
        _toRemove.AddRange(_toAdd);
    }


    public void Tick(float dt)
    {
        // 1) merge pending adds
        if (_toAdd.Count > 0)
        {
            for (int i = 0; i < _toAdd.Count; i++)
            {
                var s = _toAdd[i];
                if (s == null) continue;
                // start immediately if not running
                if (s.CurrentState == MiniStep.State.None || s.CurrentState == MiniStep.State.Pending)
                {
                    try { s.Start(); } catch (Exception ex) { try { s.OnException(ex); } catch { } }
                }

                if (s.CurrentState == MiniStep.State.Running)
                {
                    TLog.Log($"{s} is Running");
                }
                _steps.Add(s);
            }
            _toAdd.Clear();
        }

        // 2) tick active steps
        for (int i = 0; i < _steps.Count; i++)
        {
            var s = _steps[i];
            if (s == null) { _toRemove.Add(s); continue; }

            // If step already done, schedule removal
            if (s.IsDone)
            {
                _toRemove.Add(s);
                continue;
            }

            bool finishedThisTick = false;
            try
            {
                finishedThisTick = s.Tick(dt);
            }
            catch (Exception ex)
            {
                // s.Tick should catch and fail internally, but be defensive
                try { s.OnException(ex); } catch { }
                try { s.Cancel(); } catch { }
                finishedThisTick = true;
            }

            if (finishedThisTick || s.IsDone)
            {
                _toRemove.Add(s);
            }
        }

        // 3) apply removals (call completed event & optionally return to pool)
        if (_toRemove.Count > 0)
        {
            for (int i = 0; i < _toRemove.Count; i++)
            {
                var s = _toRemove[i];
                if (s == null) continue;
                // remove from active list (fast removal by swap-last or linear remove - lists are small)
                int idx = _steps.IndexOf(s);
                if (idx >= 0)
                {
                    int last = _steps.Count - 1;
                    _steps[idx] = _steps[last];
                    _steps.RemoveAt(last);
                }

                // notify
                try { StepCompleted?.Invoke(s); } catch { }

                // optionally return to pool
                if (AutoReturnToPool && ReleaseToPool != null)
                {
                    try
                    {
                        // allow step to Reset itself before pooling
                        try { s.Reset(); } catch { }
                        ReleaseToPool(s);
                    }
                    catch (Exception) { /* swallow */ }
                }
            }
            _toRemove.Clear();
        }
    }


    public int Count => _steps.Count + _toAdd.Count;

    public void Reset()
    {
        _steps.Clear();
        _toAdd.Clear();
        _toRemove.Clear();
        ReleaseToPool = null;
        StepCompleted = null;

    }

    public MiniStep Find(Predicate<MiniStep> predicate)
    {
        if (predicate == null) return null;
        for (int i = 0; i < _steps.Count; i++) if (predicate(_steps[i])) return _steps[i];
        for (int i = 0; i < _toAdd.Count; i++) if (predicate(_toAdd[i])) return _toAdd[i];
        return null;
    }

    public MiniStep[] Snapshot()
    {
        var outArr = new MiniStep[_steps.Count];
        _steps.CopyTo(outArr);
        return outArr;
    }
}
