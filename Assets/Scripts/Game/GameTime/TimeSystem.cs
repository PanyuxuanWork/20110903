/***************************************************************************
// File       : TimeSystem.cs
// Author     : Panyuxuan
// Created    : 2026/03/02
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using UnityEngine;
using System;

public sealed class TimeSystem : MonoBehaviour, IStepListener
{
    [Header("Config")]
    [Tooltip("现实/模拟秒 推进 1 游戏分钟")]
    public float secondsPerGameMinute = 0.25f;

    [Tooltip("开局时间(分钟)，例如 8:00 = 8*60")]
    [Range(0, 24 * 60 - 1)]
    public int startMinuteOfDay = 8 * 60;

    [Header("Runtime")]
    [SerializeField] private int dayIndex = 0;           // 第几天(从0开始)
    [SerializeField] private int minuteOfDay = 0;        // 0..1439
    [SerializeField] private float carrySeconds = 0f;    // 累积到下一个“游戏分钟”的秒数

    public int DayIndex => dayIndex;
    public int MinuteOfDay => minuteOfDay;

    public int Hour => minuteOfDay / 60;
    public int Minute => minuteOfDay % 60;
    public float Day01 => minuteOfDay / (24f * 60f);     // 0..1

    // 常用事件
    public event Action<int, int> OnMinuteChanged; // (dayIndex, minuteOfDay)
    public event Action<int, int> OnHourChanged;   // (dayIndex, hour)
    public event Action<int> OnDayChanged;         // (newDayIndex)

    void Awake()
    {
        minuteOfDay = Mathf.Clamp(startMinuteOfDay, 0, 24 * 60 - 1);
        carrySeconds = 0f;

        // 触发一次初始事件（可选）
        OnMinuteChanged?.Invoke(dayIndex, minuteOfDay);
        OnHourChanged?.Invoke(dayIndex, Hour);
    }

    /// <summary>
    /// 每次 Tick 调用：推进“模拟秒”
    /// </summary>
    public void Advance(float simDeltaSeconds)
    {
        if (simDeltaSeconds <= 0f || secondsPerGameMinute <= 0f) return;

        carrySeconds += simDeltaSeconds;

        // 这次一共能推进多少“游戏分钟”
        int addMinutes = (int)(carrySeconds / secondsPerGameMinute);
        if (addMinutes <= 0) return;

        carrySeconds -= addMinutes * secondsPerGameMinute;

        // 推进分钟，同时正确触发跨小时/跨天事件
        StepMinutes(addMinutes);
    }

    private void StepMinutes(int addMinutes)
    {
        // 旧值
        int oldHour = Hour;

        // 推进（可能跨天）
        int total = minuteOfDay + addMinutes;
        int daysPassed = total / (24 * 60);
        int newMinuteOfDay = total % (24 * 60);

        // 逐天处理（保证 OnDayChanged 不漏）
        if (daysPassed > 0)
        {
            // 先把当天剩余分钟走完，触发小时/分钟边界（如果你需要非常严格）
            // 建造类通常不需要逐分钟补齐跨天前的所有 minute 事件，下面给两种模式：

            // 模式A：严格逐分钟触发（最精确，但快进巨大时事件很多）
            // for (int i=0;i<addMinutes;i++) StepOneMinute();

            // 模式B：只保证小时/天事件正确，分钟事件只发“最终值”（更适合高倍速）
            // ——这里默认用模式B（推荐）
            for (int d = 0; d < daysPassed; d++)
            {
                dayIndex++;
                minuteOfDay = 0;
                OnDayChanged?.Invoke(dayIndex);
                OnHourChanged?.Invoke(dayIndex, 0);
                OnMinuteChanged?.Invoke(dayIndex, minuteOfDay);
            }
        }

        minuteOfDay = newMinuteOfDay;

        // 小时变化（至少发一次最终小时；如果你需要逐小时，也可以补齐）
        int newHour = Hour;
        if (newHour != oldHour)
            OnHourChanged?.Invoke(dayIndex, newHour);

        // 分钟变化（发最终分钟）
        OnMinuteChanged?.Invoke(dayIndex, minuteOfDay);
    }

    // 保存/读档用
    public (int day, int minute, float carry) Capture() => (dayIndex, minuteOfDay, carrySeconds);
    public void Restore(int day, int minute, float carry)
    {
        dayIndex = Mathf.Max(0, day);
        minuteOfDay = Mathf.Clamp(minute, 0, 24 * 60 - 1);
        carrySeconds = Mathf.Max(0f, carry);

        OnMinuteChanged?.Invoke(dayIndex, minuteOfDay);
        OnHourChanged?.Invoke(dayIndex, Hour);
    }

    #region Tick

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public void OnTick(in TickContext ctx)
    {
        Advance(ctx.DeltaTime);
    }

    #endregion

}
