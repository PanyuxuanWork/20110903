using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StepEg : MonoBehaviour, IStepListener
{
    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    private void OnEnable() => GlobalStep.Instance.AddListener(this);
    private void OnDisable() => GlobalStep.Instance.RemoveListener(this);
    public void OnTick(in TickContext ctx)
    {
        //TODO
    }

    /// <summary>
    /// 完成后把后续逻辑放在 下一次 Tick 开头执行。
    /// </summary>
    void OnReady()
    {
        //GlobalStep.Instance.PostFromAnyThread(new Action());
    }
 /*   /// <summary>
    /// 提交一个后台异步工作，再回到 Tick
    /// </summary>
    void AsyncEg()
    {
        GlobalStep.Instance.PostAsync(async () =>
            {
                //await SomeFunc();
            },
            onDone: () =>
            {
                //CompletedFunction
            });
    }*/
}
