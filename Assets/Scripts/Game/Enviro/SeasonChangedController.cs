/***************************************************************************
// File       : SeasonChangedController.cs
// Author     : Panyuxuan
// Created    : 2026/03/06
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(TreeGrassSeasonChangedController))]
[RequireComponent(typeof(LightVolumeChangedController))]
[RequireComponent(typeof(TerrainSeaonChangedController))]
public class SeasonChangedController : MonoSingleton<SeasonChangedController>, IStepListener
{
    [Header("State")]
    [SerializeField]
    private SeasonId _currentSeason = SeasonId.Summer;
    public SeasonId CurrentSeason => _currentSeason;

    [Header("Transition")]
    public AnimationCurve ProgressCurve = AnimationCurve.Linear(0, 0, 1, 1);

    // 你要求的两个 Action
    public Action<SeasonId, float> OnSeasonContinuousChanged;
    public Action<SeasonId> OnSeasonChanged;

    // 内部
    private bool _isTransitioning;
    private SeasonId _fromSeason;
    private SeasonId _toSeason;
    private float _duration;
    private float _elapsed;

    private TreeGrassSeasonChangedController tgsCtrl;
    private LightVolumeChangedController lvcCtrl;
    private TerrainSeaonChangedController tscCtrl;

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    protected override void Awake()
    {
        base.Awake();
        tgsCtrl ??= GetComponent<TreeGrassSeasonChangedController>();
        lvcCtrl ??= GetComponent<LightVolumeChangedController>();
        tscCtrl ??= GetComponent<TerrainSeaonChangedController>();
    }


    private void Start()
    {
        OnSeasonContinuousChanged += tgsCtrl.OnSeasonChanged;
        OnSeasonContinuousChanged += lvcCtrl.OnSeasonChanged;
        OnSeasonContinuousChanged += tscCtrl.TransitionAToB;
    }


    private void OnEnable()
    {
        // 自动注册到你的 GlobalStep（如果你是这种架构）
        // 如果你不想自动注册，就删掉这段，改为外部统一注册
        var step = GlobalStep.Instance;
        if (step != null) step.AddListener(this);
    }

    private void OnDisable()
    {
        var step = GlobalStep.Instance;
        if (step != null) step.RemoveListener(this);
    }

    /// <summary>
    /// 发起一次换季。若正在换季，会从“当前进度下的目标态”直接打断并重新开始。
    /// </summary>
    [Button]
    public void ChangeSeason(SeasonId target, float duration)
    {
        // 相同季节且不在过渡中：直接触发完成事件（可按你需求选择是否触发）
        if (!_isTransitioning && target == _currentSeason)
        {
            OnSeasonContinuousChanged?.Invoke(target, 1f);
            OnSeasonChanged?.Invoke(target);
            return;
        }

        _isTransitioning = true;
        _fromSeason = _currentSeason; // 逻辑上的起点季节
        _toSeason = target;
        _duration = Mathf.Max(0.0001f, duration);
        _elapsed = 0f;

        // 第一次先推送 0，方便订阅者初始化
        OnSeasonContinuousChanged?.Invoke(_toSeason, 0f);
    }

    public void OnTick(in TickContext ctx)
    {
        if (!IsActive) return;
        if (!_isTransitioning) return;

        _elapsed += Mathf.Max(0f, ctx.DeltaTime);

        float raw = Mathf.Clamp01(_elapsed / _duration);
        float t01 = ProgressCurve != null ? Mathf.Clamp01(ProgressCurve.Evaluate(raw)) : raw;

        // 连续广播
        OnSeasonContinuousChanged?.Invoke(_toSeason, t01);

        if (raw >= 1f)
        {
            _isTransitioning = false;
            _currentSeason = _toSeason;

            // 收敛一次，避免曲线或浮点误差
            OnSeasonContinuousChanged?.Invoke(_toSeason, 1f);

            // 完成广播
            OnSeasonChanged?.Invoke(_toSeason);
        }
    }

}
