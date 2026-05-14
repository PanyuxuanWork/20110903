/***************************************************************************
// File       : ProductUnitContext.cs
// Author     : Panyuxuan
// Created    : 2026/02/23
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] 原版ProducerContext耦合过大，更新版
// ***************************************************************************/

using Sim.Resources;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


#region 内部数据结构体

public enum NeedToHandleDataState
{
    等待处理,
    处理中,
    处理完成,
    处理出错
}


/// <summary>
/// 需要调度的，来自外部生产单位的请求
/// </summary>
public struct NeedToHandleData
{
    public ProducerUnit unit;
    public KeyValuePair<ResourceId, int> resourceData;
    public Resident resident;
    public int Ticket;
    public int TTL;
    public NeedToHandleDataState state;
}


#endregion

public class ProductUnitContext : MonoBehaviour, IStepListener
{
    #region Tick

    public int Priority { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public void OnTick(in TickContext ctx)
    {
        foreach (var v in AllNeedHandleOutputSet)
        {
            HandlerOneOutput(v.Value);
        }

        foreach (var v in AllNeedHandleInputSet)
        {
            HandlerOneInput(v.Value);
        }
    }
    #endregion


    public Area ParentArea;

    /// <summary>
    /// Area中所有的Storages
    /// </summary>
    [ShowInInspector]
    public List<Storage> storages=new();

    /// <summary>
    /// 所有需要输入处理的集合
    /// </summary>
    [ShowInInspector]
    public Dictionary<ProducerUnit, NeedToHandleData> AllNeedHandleInputSet = new();

    /// <summary>
    /// 所有需要输出处理的集合
    /// </summary>
    [ShowInInspector]
    public Dictionary<ProducerUnit, NeedToHandleData> AllNeedHandleOutputSet = new();



    #region 外部API

    /// <summary>
    /// 外部调用，尝试接受一个外部生产单位的缺货请求
    /// </summary>
    /// <param name="unit"></param>
    /// <param name="set"></param>
    /// <returns></returns>
    public bool TryRecordUnitInput(NeedToHandleData data)
    {
        //在集合中
        if (AllNeedHandleInputSet.ContainsKey(data.unit))
        {
            // 这里应该处理更新请求的逻辑
            if (data.Ticket > AllNeedHandleInputSet[data.unit].Ticket)
            {
                // 取消旧任务，更新为新任务
                //TODO CancelOldTask(AllNeedHandleInputSet[data.produceBuilding]);
                AllNeedHandleInputSet[data.unit] = data;
                return true;
            }
            return false;
        }

        else
        {
            AllNeedHandleInputSet.Add(data.unit, data);
            return true;
        }

    }

    public bool TryRecordUnitOutput(NeedToHandleData data)
    {
        if (AllNeedHandleOutputSet.ContainsKey(data.unit))
        {
            return false;
        }
        else
        {
            AllNeedHandleOutputSet.Add(data.unit, data);
            return true;
        }
    }
    #endregion

    #region 内部处理

    /// <summary>
    /// 处理一个缺货请求,Storage->produceBuilding
    /// </summary>
    private void HandlerOneInput(NeedToHandleData data)
    {
        switch (data.state)
        {
            case NeedToHandleDataState.等待处理:
                {
                    if (!ParentArea.residentContext.TryGetResidentSetByProfession(ProfessionType.仓库搬运工, out var set))
                    {
                        UILog.Instance.ShowError("找不到搬运工");
                        return;
                    }

                    // 准备工作
                    Resident resident = null;
                    Storage storage = null;
                    ResourceId rid = data.resourceData.Key;
                    int amount = data.resourceData.Value;
                    int residentTicket = 0;
                    int storageTicket = 0;

                    // Step1 找到合适的Resident
                    if (!TryFindAppropriateResident(set, out resident))
                    {
                        UILog.Instance.ShowError("所有搬运工均繁忙");
                        return;
                    }

                    data.resident = resident;
                    resident.backpack._slots[0].Reset(rid, 100, 0);

                    try
                    {
                        // Step2 预约居民背包
                        if (!resident.backpack.TryReserveCapacity(rid, amount, out residentTicket))
                        {
                            TLog.Error(this, $"{resident}预约背包容量失败");
                            return;
                        }

                        // Step3 找到合适的仓库并预约
                        var list = FindNearestStorage(resident.transform.position, out var l);
                        ObPool<List<Storage> >.Release(l);
                        bool storageFound = false;

                        foreach (var v in list)
                        {
                            if (v.TryReserveResource(rid, amount, out storageTicket))
                            {
                                storage = v;
                                storageFound = true;
                                break;
                            }
                        }

                        if (!storageFound)
                        {
                            // 回滚居民预约
                            resident.backpack.CancelCapacityReserve(residentTicket);
                            TLog.Error(this, "找不到有足够货物的仓库");
                            return;
                        }

                        // Step4 两个预约都成功，下发任务
                        data.state = NeedToHandleDataState.处理中;

                        var carry = CarryFromWarehouseToProduce.Create(
                            resident,
                            storage,
                            data.unit,
                            rid,
                            amount,
                            storageTicket,
                            residentTicket);

                        carry.Succeeded += (a, b) =>
                        {
                            // 任务完成，释放居民（让他可以接新任务）
                            ReleaseResident(resident);

                            if (AllNeedHandleInputSet.ContainsKey(data.unit))
                            {
                                data.state = NeedToHandleDataState.处理完成;
                            }
                            else
                            {
                                TLog.Error(this, $"{data.unit}在任务完成时没有找到对应的参数");
                                data.state = NeedToHandleDataState.处理出错;
                            }
                        };

                        carry.Failed += (a, b) =>
                        {
                            // 任务失败，回滚预约（系统会自动过期，但最好手动清理）
                            try
                            {
                                if (storageTicket != 0) storage?.CancelGoodsReserve(storageTicket);
                                if (residentTicket != 0) resident?.backpack.CancelCapacityReserve(residentTicket);
                            }
                            catch { }

                            ReleaseResident(resident);
                            AllNeedHandleInputSet.Remove(data.unit);
                            data.state = NeedToHandleDataState.处理出错;
                        };
                        resident.taskService.Enqueue(carry);
                    }
                    catch (Exception ex)
                    {
                        // 异常回滚
                        try { if (residentTicket != 0) resident?.backpack.CancelCapacityReserve(residentTicket); } catch { }
                        try { if (storageTicket != 0) storage?.CancelGoodsReserve(storageTicket); } catch { }
                        ReleaseResident(resident);
                        TLog.Error(this, ex.Message);
                        return;
                    }
                    break;
                }

            case NeedToHandleDataState.处理中:
                {
                    // 可以加个超时检查
                    break;
                }

            case NeedToHandleDataState.处理完成:
                {
                    AllNeedHandleInputSet.Remove(data.unit);
                    break;
                }

            case NeedToHandleDataState.处理出错:
                {
                    // 错误处理：记录日志、通知UI等
                    AllNeedHandleInputSet.Remove(data.unit);
                    break;
                }
        }
    }

    /// <summary>
    /// 处理一个货满请求,produceBuilding->storage
    /// </summary>
    /// <param name="data"></param>
    private void HandlerOneOutput(NeedToHandleData data)
    {
        switch (data.state)
        {
            case NeedToHandleDataState.等待处理:
                {
                    if (!ParentArea.residentContext.TryGetResidentSetByProfession(ProfessionType.仓库搬运工, out var set))
                    {
                        UILog.Instance.ShowError("找不到搬运工");
                        data.state = NeedToHandleDataState.处理出错;
                        return;
                    }

                    Resident resident = null;
                    Storage storage = null;
                    ResourceId rid = data.resourceData.Key;
                    int amount = data.resourceData.Value;
                    int residentTicket = 0;
                    int storageTicket = 0;

                    // Step1 找到合适的Resident
                    if (!TryFindAppropriateResident(set, out resident))
                    {
                        UILog.Instance.ShowError("所有搬运工均繁忙");
                        data.state = NeedToHandleDataState.处理出错;
                        return;
                    }

                    data.resident = resident;
                    resident.curState = ResidentState.工作中;  // 设置状态
                    resident.backpack._slots[0].Reset(rid, 100, 0);

                    try
                    {
                        // Step2 预约居民背包容量（用于存放从生产单元取出的货物）
                        if (!resident.backpack.TryReserveCapacity(rid, amount, out residentTicket))
                        {
                            TLog.Error(this, $"{resident}预约背包容量失败");
                            ReleaseResident(resident);
                            data.state = NeedToHandleDataState.处理出错;
                            return;
                        }

                        // Step3 找到合适的仓库并预约容量
                        var list = FindNearestStorage(resident.transform.position,out var l);
                        ObPool<List<Storage>>.Release(l);
                        bool storageFound = false;

                        foreach (var v in list)
                        {
                            if (v.TryReserveCapacity(rid, amount, out storageTicket))
                            {
                                storage = v;
                                storageFound = true;
                                break;
                            }
                        }

                        if (!storageFound)
                        {
                            // 回滚居民预约
                            resident.backpack.CancelCapacityReserve(residentTicket);
                            ReleaseResident(resident);
                            TLog.Error(this, "找不到有足够容量的仓库");
                            data.state = NeedToHandleDataState.处理出错;
                            return;
                        }

                        // Step4 两个预约都成功，下发任务
                        data.state = NeedToHandleDataState.处理中;

                        var carry = CarryFromProduceToWarehouse.Create(
                            resident,
                            data.unit,
                            storage,
                            rid,
                            amount,
                            storageTicket,
                            residentTicket);

                        carry.Succeeded += (a,b) =>
                        {
                            // 任务成功
                            if (AllNeedHandleOutputSet.ContainsKey(data.unit))
                            {
                                data.state = NeedToHandleDataState.处理完成;
                            }
                            else
                            {
                                TLog.Error(this, $"{data.unit}在任务完成时没有找到对应的参数");
                                data.state = NeedToHandleDataState.处理出错;
                            }
                            ReleaseResident(resident);
                        };

                        carry.Failed += (a,b) =>
                        {
                            // 任务失败，回滚预约
                            try
                            {
                                if (storageTicket != 0) storage?.CancelCapacityReserve(storageTicket);
                                if (residentTicket != 0) resident?.backpack.CancelCapacityReserve(residentTicket);
                            }
                            catch { }

                            ReleaseResident(resident);
                            AllNeedHandleInputSet.Remove(data.unit);
                            data.state = NeedToHandleDataState.处理出错;
                        };

                        resident.taskService.Enqueue(carry);
                    }
                    catch (Exception ex)
                    {
                        // 异常回滚
                        try { if (residentTicket != 0) resident?.backpack.CancelCapacityReserve(residentTicket); } catch { }
                        try { if (storageTicket != 0) storage?.CancelCapacityReserve(storageTicket); } catch { }
                        ReleaseResident(resident);
                        TLog.Error(this, ex.Message);
                        data.state = NeedToHandleDataState.处理出错;
                    }
                    break;
                }

            case NeedToHandleDataState.处理中:
                {
                    // 可以加超时检查
                    // if (Time.time - data.startTime > data.TTL) 
                    // {
                    //     data.state = NeedToHandleDataState.处理出错;
                    // }
                    break;
                }

            case NeedToHandleDataState.处理完成:
                {
                    AllNeedHandleOutputSet.Remove(data.unit);
                    break;
                }

            case NeedToHandleDataState.处理出错:
                {
                    // 错误处理：释放资源、记录日志
                    if (data.resident != null)
                    {
                        ReleaseResident(data.resident);
                    }
                    AllNeedHandleOutputSet.Remove(data.unit);
                    break;
                }
        }
    }

    /// <summary>
    /// 找一个合适的Resident
    /// </summary>
    /// <param name="resident"></param>
    /// <returns></returns>
    private bool TryFindAppropriateResident(HashSet<Resident> set, out Resident resident)
    {
        resident = null;
        foreach (var v in set)
        {
            if (v.curState == ResidentState.无所事事)
            {
                resident = v;
                return true;
            }

        }

        return false;
    }

    private List<Storage> FindNearestStorage(Vector3 pos,out List<Storage> tmpList)
    {
        tmpList = ObPool<List<Storage>>.Get();
        tmpList.Clear();
        tmpList.AddRange(storages);
        tmpList.Sort((a, b) =>
        {
            float disA = (pos - a.transform.position).sqrMagnitude;
            float disB = (pos - b.transform.position).sqrMagnitude;
            return disA.CompareTo(disB);
        });

        return tmpList;
    }
    #endregion

    private void ReleaseResident(Resident resident)
    {
        if (resident == null) return;
        resident.curState = ResidentState.无所事事;
        resident.backpack._slots[0].Reset(ResourceId.None, 0, 0);
    }
}



