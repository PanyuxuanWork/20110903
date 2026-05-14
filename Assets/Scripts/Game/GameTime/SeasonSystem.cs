/***************************************************************************
// File       : SeasonSystem.cs
// Author     : Panyuxuan
// Created    : 2026/03/02
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using UnityEngine;

public enum SeasonId
{
    Spring,
    Summer,
    Autumn,
    Winter
}

[Serializable]
public struct SeasonDef
{
    public SeasonId id;

    [Min(1)]
    public int days;

    public string displayName;
}

public sealed class SeasonSystem : MonoBehaviour
{
    [Header("Refs")]
    public TimeSystem timeSystem;

    [Header("Calendar")]
    public SeasonDef[] seasons;

    [Tooltip("如果你的 TimeSystem DayIndex=0 不代表第一年的第一天，可以在这里加偏移")]
    public int dayIndexOffset = 0;

    [Header("Runtime (ReadOnly)")]
    [SerializeField] private int yearIndex;
    [SerializeField] private SeasonId currentSeason;
    [SerializeField] private int dayOfYear;     // 0..daysInYear-1
    [SerializeField] private int dayOfSeason;   // 0..seasonDays-1
    [SerializeField] private int daysInYear;

    public int YearIndex => yearIndex;
    public SeasonId CurrentSeason => currentSeason;
    public int DayOfYear => dayOfYear;
    public int DayOfSeason => dayOfSeason;
    public int DaysInYear => daysInYear;
    public int CurrentSeasonDays => GetSeasonDays(currentSeason);

    public float Year01 => daysInYear <= 0 ? 0f : (dayOfYear / (float)daysInYear);                 // 0..1
    public float Season01 => CurrentSeasonDays <= 0 ? 0f : (dayOfSeason / (float)CurrentSeasonDays); // 0..1

    // 事件：建造/天气/作物/音效/美术系统都可以订阅
    public event Action<int> OnYearChanged;                               // newYearIndex
    public event Action<SeasonId, int> OnSeasonChanged;                   // (newSeason, yearIndex)
    public event Action<SeasonId, int, int> OnSeasonDayChanged;           // (season, yearIndex, dayOfSeason)
    

    void Reset()
    {
        // 给一个默认四季配置
        seasons = new[]
        {
            new SeasonDef{ id = SeasonId.Spring, days = 30, displayName = "Spring" },
            new SeasonDef{ id = SeasonId.Summer, days = 30, displayName = "Summer" },
            new SeasonDef{ id = SeasonId.Autumn, days = 30, displayName = "Autumn" },
            new SeasonDef{ id = SeasonId.Winter, days = 30, displayName = "Winter" },
        };
    }

    void OnEnable()
    {
        if (timeSystem != null)
            timeSystem.OnDayChanged += HandleDayChanged;
    }

    void OnDisable()
    {
        if (timeSystem != null)
            timeSystem.OnDayChanged -= HandleDayChanged;
    }

    void Start()
    {
        timeSystem ??= GetComponent<TimeSystem>();

        RebuildDaysInYear();
        RecalculateFromDayIndex(timeSystem.DayIndex + dayIndexOffset, fireEvents: false);

        // 可选：开局发一次“当天”
        OnSeasonDayChanged?.Invoke(currentSeason, yearIndex, dayOfSeason);
    }

    void HandleDayChanged(int newDayIndex)
    {
        RebuildDaysInYear();
        RecalculateFromDayIndex(newDayIndex + dayIndexOffset, fireEvents: true);
    }

    void RebuildDaysInYear()
    {
        daysInYear = 0;
        if (seasons == null || seasons.Length == 0) return;

        for (int i = 0; i < seasons.Length; i++)
            daysInYear += Mathf.Max(1, seasons[i].days);
    }

    void RecalculateFromDayIndex(int absoluteDayIndex, bool fireEvents)
    {
        // 统一在方法开头记录旧值（避免局部变量重名）
        int oldYearIndex = yearIndex;
        SeasonId oldSeasonId = currentSeason;
        int oldDayOfSeason = dayOfSeason;

        if (daysInYear <= 0)
        {
            // 没配 seasons 时退化：永远 Spring
            yearIndex = 0;
            currentSeason = SeasonId.Spring;
            dayOfYear = 0;
            dayOfSeason = 0;

            if (fireEvents)
            {
                if (oldYearIndex != yearIndex) OnYearChanged?.Invoke(yearIndex);
                if (oldSeasonId != currentSeason) OnSeasonChanged?.Invoke(currentSeason, yearIndex);
                if (oldDayOfSeason != dayOfSeason) OnSeasonDayChanged?.Invoke(currentSeason, yearIndex, dayOfSeason);
            }
            return;
        }

        absoluteDayIndex = Mathf.Max(0, absoluteDayIndex);

        yearIndex = absoluteDayIndex / daysInYear;
        dayOfYear = absoluteDayIndex % daysInYear;

        // 找到属于哪一季
        int cursor = 0;
        SeasonId foundSeason = seasons[0].id;
        int foundSeasonDays = Mathf.Max(1, seasons[0].days);
        int foundDayOfSeason = 0;

        for (int i = 0; i < seasons.Length; i++)
        {
            int sd = Mathf.Max(1, seasons[i].days);
            if (dayOfYear < cursor + sd)
            {
                foundSeason = seasons[i].id;
                foundSeasonDays = sd;
                foundDayOfSeason = dayOfYear - cursor;
                break;
            }
            cursor += sd;
        }

        currentSeason = foundSeason;
        dayOfSeason = Mathf.Clamp(foundDayOfSeason, 0, foundSeasonDays - 1);

        if (!fireEvents) return;

        if (yearIndex != oldYearIndex)
            OnYearChanged?.Invoke(yearIndex);

        if (currentSeason != oldSeasonId)
            OnSeasonChanged?.Invoke(currentSeason, yearIndex);

        if (dayOfSeason != oldDayOfSeason || currentSeason != oldSeasonId || yearIndex != oldYearIndex)
            OnSeasonDayChanged?.Invoke(currentSeason, yearIndex, dayOfSeason);
    }

    int GetSeasonDays(SeasonId id)
    {
        if (seasons == null) return 0;
        for (int i = 0; i < seasons.Length; i++)
            if (seasons[i].id == id) return Mathf.Max(1, seasons[i].days);
        return 0;
    }

    // 方便 UI 显示
    public string GetDateString()
        => $"Year {yearIndex + 1} / {currentSeason} / Day {dayOfSeason + 1}";
}