/***************************************************************************
// File       : StepManager.cs
// Author     : Panyuxuan
// Created    : 2025/10/28
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Add script summary here
// ***************************************************************************/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StepManager : IStepListener
{
    private Queue<MiniStep> _stepsQueue = new Queue<MiniStep>(); // 步骤队列
    private MiniStep _currentStep; // 当前执行的步骤
    private bool _isExecuting = false; // 是否正在执行步骤
    public bool ClearQuqueWhenStepFailed = true;

    // 当前步骤的状态（可以被外部监听）
    public MiniStep.State CurrentState => _currentStep?.CurrentState ?? MiniStep.State.None;

    // IStepListener 接口实现：监听每个 Tick 的事件
    public int Priority { get; set; } = 0; // 优先级
    public bool IsActive { get; set; } = true; // 是否激活，默认 true

    // 在 Tick 上每次更新步骤
    public void OnTick(in TickContext ctx)
    {
        if (_currentStep != null && _currentStep.CurrentState == MiniStep.State.Running)
        {
            // 如果当前步骤是运行中的状态，更新它
            _currentStep.OnUpdate(ctx.DeltaTime);
        }
    }

    // 添加步骤到队列
    public void AddStep(params MiniStep[] steps)
    {
        foreach (var step in steps)
        {
            _stepsQueue.Enqueue(step);
            step.Completed += OnStepCompleted; // 注册步骤完成事件
        }
    }

    // 外部手动调用：启动下一个步骤
    public void StartNextStep()
    {
        if (_isExecuting || _stepsQueue.Count == 0)
        {
            TLog.Log($"{_currentStep?.StepName}正在执行或步骤队列为空");
            return; // 如果有任务正在执行或队列为空，则返回
        }

        _currentStep = _stepsQueue.Dequeue(); // 获取队列中的下一个步骤
        _isExecuting = true; // 标记任务开始执行
        _currentStep.Start(); // 启动步骤
    }

    // 步骤完成时的处理
    private void OnStepCompleted(MiniStep step, MiniStep.Result result)
    {
        _isExecuting = false; // 标记任务结束
        Debug.Log($"{step.StepName} 完成，结果：{result}");
        if (result == MiniStep.Result.Succeeded)
            StartNextStep();
        else
        {
            TLog.Log($"{step.StepName} 失败，结果：{result}");
            if (!ClearQuqueWhenStepFailed) _stepsQueue.Clear();
        }
    }
}


