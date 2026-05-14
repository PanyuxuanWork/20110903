/***************************************************************************
// File       : StepContinuousInvoker.cs
// Author     : Panyuxuan
// Created    : 2026/03/03
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

public static class StepContinuousInvoker
{
    /// <summary>
    /// 返回的句柄：可查询是否完成、可取消。
    /// </summary>
    public sealed class Handle : IStepListener
    {
        public int Priority { get; set; } = 0;
        public bool IsActive { get; set; } = true;

        public bool IsDone { get; private set; }

        private readonly Action<float> _action;
        private readonly Action _onComplete;
        private readonly float _duration;

        private float _elapsed;
        private bool _started;

        internal Handle(Action<float> action, float time, Action onComplete)
        {
            _action = action ?? throw new ArgumentNullException(nameof(action));
            _onComplete = onComplete;
            _duration = Mathf.Max(0f, time);

            // time <= 0：立即完成
            if (_duration <= 0f)
            {
                _action(1f);
                Complete();
            }
        }

        public void Cancel()
        {
            if (!IsActive) return;
            IsActive = false;
            IsDone = true;
        }

        public void OnTick(in TickContext ctx)
        {
            if (!IsActive || IsDone) return;

            // 第一次 Tick：先给 0（你不想要这次的话，把这段删掉即可）
            if (!_started)
            {
                _started = true;
                _elapsed = 0f;
                _action(0f);

                // duration 可能为 0 的边界（虽然上面已处理）
                if (_duration <= 0f)
                {
                    _action(1f);
                    Complete();
                    return;
                }
            }

            _elapsed += Mathf.Max(0f, ctx.DeltaTime);
            float t01 = Mathf.Clamp01(_elapsed / _duration);
            _action(t01);

            if (_elapsed >= _duration)
            {
                // 确保最后收敛到 1（避免浮点误差）
                if (t01 < 1f) _action(1f);
                Complete();
            }
        }

        private void Complete()
        {
            IsDone = true;
            IsActive = false;

            try
            {
                _onComplete?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    /// <summary>
    /// 在 time 秒内，每个 Tick 调用一次 action(progress01)。
    /// 返回句柄，可 Cancel。
    /// </summary>
    public static Handle ContinuousInvoke(
        Action<float> action,
        float time,
        int priority = 0,
        Action onComplete = null)
    {
        var step = GlobalStep.Instance;
        if (step == null)
            throw new InvalidOperationException("GlobalStep.Instance is null. Ensure GlobalStep exists in the scene.");

        var h = new Handle(action, time, onComplete) { Priority = priority };

        // 如果 time<=0 会在 Handle 构造里立刻完成，此时不用注册
        if (h.IsActive && !h.IsDone)
            step.AddListener(h);

        return h;
    }

    public static Handle ContinuousMatInvoke(
        Action<Material, Material, float> action,
        Material a,
        Material b,
        float time,
        int priority = 0,
        Action onComplete = null)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        // 复用你现有的 ContinuousInvoke(Action<float>, ...)
        return ContinuousInvoke(
            t01 => action(a, b, t01),
            time,
            priority,
            onComplete
        );

    }
}