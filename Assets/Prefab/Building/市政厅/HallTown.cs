/***************************************************************************
// File       : HallTown.cs
// Author     : Panyuxuan
// Created    : 2025/12/26
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System;
using System.Collections;
using Sirenix.OdinInspector;
using UnityEngine;

public class HallTown : BuildingBase, IStepListener
{
    [Header("HallTown")]
    public Transform SpawnPosition;        // 生成居民的位置
    public Resident ResidentPrefab;        // Resident的Prefab，用来实例化新的居民
    public float SpawnSpeed = 1.0f;        // 生成速率（秒）
    public Transform ResidentRoot;
    [ShowInInspector] private bool stopSpawn = false;        // 暂停生成的开关
    [ReadOnly, ShowInInspector] private float spawnTime = 0f;          // 用于计时生成居民的定时器
    [ReadOnly, ShowInInspector] private float spawnTimerMax = 10f;      // 生成间隔的最大时间
    private int index = 0;

    protected override void Awake()
    {
        base.Awake();
        Area.hallTown = this;
    }

    #region tick
    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;

    // 每次tick调用
    public void OnTick(in TickContext ctx)
    {
        if (!IsActive)
            return;

        if (CanSpawn(ctx))
        {
            SpawnResident();
        }
    }

    private bool CanSpawn(TickContext ctx)
    {
        if (Area == null) return false;
        if (Area.buildingContext.GetOneVacantHouse(out Build_House house, out var reason)) return false;
        if (stopSpawn)
        {
            return false; // 如果暂停生成，直接返回不生成
        }

        // 更新时间
        spawnTime += ctx.DeltaTime;

        if (spawnTime >= spawnTimerMax)
        {
            spawnTime = 0f;  // 重置计时器
            return true;     // 达到生成时间，允许生成
        }

        return false;
    }

    public void SetSpawnSpeed(float speed)
    {
        SpawnSpeed = speed;
        spawnTimerMax = speed;  // 动态更新生成速率
    }

    public void StopSpawn()
    {
        stopSpawn = true;
        spawnTime = 0f;  // 停止生成时重置计时器
    }

    public void StartSpawn()
    {
        stopSpawn = false;
    }

    // 生成一个居民
    private void SpawnResident()
    {
        if (ResidentPrefab != null && SpawnPosition != null)
        {
            // 在指定位置实例化居民
            var resident = Instantiate(ResidentPrefab, SpawnPosition.position, Quaternion.identity);
            resident.transform.SetParent(ResidentRoot);
            resident.name = $"resident_{index++}";
            TLog.Log("Resident spawned at: " + SpawnPosition.position);
            resident.professionComp.SetProfession(ProfessionType.失业);
        }
        else
        {
            TLog.Warning("ResidentPrefab or SpawnPosition is not set!");
        }
    }

    // 启用时添加监听
    private void OnEnable() => GlobalStep.Instance.AddListener(this);

    // 禁用时移除监听
    private void OnDisable() => GlobalStep.Instance.RemoveListener(this);
    #endregion


}

