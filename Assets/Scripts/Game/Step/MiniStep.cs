/***************************************************************************
// File       : MiniStep.cs
// Author     : Panyuxuan
// Created    : 2025/10/28
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Add script summary here
// ***************************************************************************/
using System;

public abstract class MiniStep
{
    public enum State
    {
        None = 0,
        Pending,
        Running,
        Succeeded,
        Failed,
        Canceled
    }

    public enum Result
    {
        Succeeded,
        Failed,
        Canceled
    }

    public State CurrentState { get; private set; } = State.None;
    public bool IsDone => CurrentState == State.Succeeded || CurrentState == State.Failed || CurrentState == State.Canceled;

    // Events (optional)
    public event Action<MiniStep> Started;
    public event Action<MiniStep, float> Ticked; // dt
    public event Action<MiniStep> Succeeded;
    public event Action<MiniStep> Failed;
    public event Action<MiniStep> Canceled;
    public event Action<MiniStep, Result> Completed;
    public event Func<MiniStep, bool> ExtraCompletionCheck;

    public void Start()
    {
        if (CurrentState != State.None && CurrentState != State.Pending) return;
        CurrentState = State.Running;
        try { Started?.Invoke(this); } catch (Exception ex) { OnException(ex); }
        try { OnStart(); } catch (Exception ex) { OnException(ex); Fail(); }
    }
    public bool Tick(float dt)
    {
        if (CurrentState != State.Running) return IsDone;

        try
        {
            // optional tick event
            try { Ticked?.Invoke(this, dt); } catch (Exception ex) { OnException(ex); }

            // OnUpdate should return true when this step wants to complete successfully.
            bool finished = OnUpdate(dt);
            if (finished && (ExtraCompletionCheck == null || ExtraCompletionCheck.Invoke(this)))
            {
                Succeed();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            OnException(ex);
            Fail();
            return true;
        }
    }

    public void Cancel()
    {
        if (IsDone) return;
        try { OnCancel(); } catch { /* ignore */ }
        Transition(State.Canceled, Result.Canceled);
    }

    protected void Succeed()
    {
        if (IsDone) return;
        Transition(State.Succeeded, Result.Succeeded);
    }

    /// <summary>
    /// Mark failure.
    /// </summary>
    protected void Fail()
    {
        if (IsDone) return;
        Transition(State.Failed, Result.Failed);
    }

    private void Transition(State to, Result result)
    {
        CurrentState = to;

        try
        {
            switch (result)
            {
                case Result.Succeeded: OnSuccess(); try { Succeeded?.Invoke(this); } catch { } break;
                case Result.Failed: OnFail(); try { Failed?.Invoke(this); } catch { } break;
                case Result.Canceled: OnCancelEnd(); try { Canceled?.Invoke(this); } catch { } break;
            }
        }
        catch { /* ignore exceptions during callbacks */ }

        try { OnComplete(result); } catch { /* ignore */ }

        try { Completed?.Invoke(this, result); } catch { /* ignore */ }

        // Unsubscribe all to avoid leaks when reused
        Started = null;
        Ticked = null;
        Succeeded = null;
        Failed = null;
        Canceled = null;
        Completed = null;
        ExtraCompletionCheck = null;
    }

    #region Reset / Pooling support

    /// <summary>
    /// Reset to initial state. Subclasses should override and call base.Reset() when pooled.
    /// </summary>
    public virtual void Reset()
    {
        CurrentState = State.None;
        // ensure events cleared
        Started = null;
        Ticked = null;
        Succeeded = null;
        Failed = null;
        Canceled = null;
        Completed = null;
        ExtraCompletionCheck = null;
    }

    #endregion

    #region Hooks for subclasses

    /// <summary>Called when Start() invoked (wrap in try/catch at caller).</summary>
    public virtual void OnStart() { }

    /// <summary>
    /// Called each Tick when running. Return true to indicate success/completion.
    /// If exception thrown, Tick will call Fail().
    /// </summary>
    public virtual bool OnUpdate(float dt)
    {
        return true;
    }

    /// <summary>Called when Cancel() invoked (before transition to Canceled).</summary>
    public virtual void OnCancel() { }

    /// <summary>Called once when transitioning to Succeeded (before OnComplete).</summary>
    public virtual void OnSuccess() { }

    /// <summary>Called once when transitioning to Failed (before OnComplete).</summary>
    public virtual void OnFail() { }

    /// <summary>Called once when transitioning to Canceled (before OnComplete).</summary>
    public virtual void OnCancelEnd() { }

    /// <summary>Called in any terminal transition (Succeeded/Failed/Canceled).</summary>
    public virtual void OnComplete(Result result) { }

    /// <summary>Exception handler for OnStart/OnUpdate.</summary>
    public virtual void OnException(Exception ex)
    {
        // default: log to console in Unity context if available
#if UNITY_ENGINE
        try { UnityEngine.Debug.LogException(ex); } catch { }
#endif
    }

    #endregion

    #region Convenience factory (delegate-based step)

    /// <summary>
    /// Create a quick, anonymous step from delegates.
    /// Note: using closures here will allocate. For hot paths prefer subclassing with methods and method-group subscription.
    /// </summary>
    public static MiniStep FromCallbacks(
        Action onStart = null,
        Func<float, bool> onUpdate = null,
        Action onCancel = null,
        Action onSuccess = null,
        Action onFail = null,
        Action<MiniStep> onCompleted = null)
    {
        return new CallbackMiniStep(onStart, onUpdate, onCancel, onSuccess, onFail, onCompleted);
    }

    private sealed class CallbackMiniStep : MiniStep
    {
        private readonly Action _onStart;
        private readonly Func<float, bool> _onUpdate;
        private readonly Action _onCancel;
        private readonly Action _onSuccess;
        private readonly Action _onFail;
        private readonly Action<MiniStep> _onCompleted;

        public CallbackMiniStep(Action onStart, Func<float, bool> onUpdate, Action onCancel, Action onSuccess, Action onFail, Action<MiniStep> onCompleted)
        {
            _onStart = onStart;
            _onUpdate = onUpdate ?? (_ => true); // default: succeed immediately
            _onCancel = onCancel;
            _onSuccess = onSuccess;
            _onFail = onFail;
            _onCompleted = onCompleted;
        }

        public override void OnStart() => _onStart?.Invoke();
        public override bool OnUpdate(float dt) => _onUpdate(dt);
        public override void OnCancel() => _onCancel?.Invoke();
        public override void OnSuccess() => _onSuccess?.Invoke();
        public override void OnFail() => _onFail?.Invoke();
        public override void OnComplete(Result result) => _onCompleted?.Invoke(this);
        public override void Reset()
        {
            base.Reset();
            // nothing to clear because delegates are readonly; if you pool instances, don't reuse CallbackMiniStep instances with different delegates
        }
    }

    #endregion
}
