using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using Sirenix.OdinInspector;

public class BuildingContext : MonoBehaviour
{
    public Area ParentArea;
    // K: 唯一32位键(高8=Major, 次8=Minor, 再8=AssetID, 低8=InstanceID)
    [ShowInInspector]
    public Dictionary<uint, GameObject> Buildings = new();
    // K: 基键(低8清零)  V: 下次要使用的实例ID（作为“累计创建次数”）
    private Dictionary<uint, byte> Counts = new();
    public Dictionary<BuildMinor, HashSet<GameObject>> Type_Buildings_Dic = new();

    private void Awake()
    {
        Buildings ??= new Dictionary<uint, GameObject>(2048);
        Counts ??= new Dictionary<uint, byte>(256);
    }

    #region 内部API

    /// <summary>
    /// 你的“累计次数=实例ID”的策略：第一次分配 0，之后递增。
    /// </summary>
    public uint RegisterBuilding(BuildAsset build, GameObject go, bool tryReuseOnOverflow = true)
    {
        var nullList = new List<uint>();
        foreach (var v in Buildings)
        {
            if (v.Value.Equals(null))
                nullList.Add(v.Key);
            if (v.Value.Equals(go))
                return 0000;
        }

        foreach (var v in nullList)
        {
            Buildings.Remove(v);
        }

        go.transform.SetParent(this.transform);

        // 低8位清零，得到 baseKey
        uint baseKey = BuildingTypeManager.Encode(build.buildMajor, build.buildMinor, build.ID, 0x00) & 0xFFFFFF00u;

        // 当前计数（即将要用作 InstanceID 的值），默认 0
        byte nextId = 0;
        if (Counts.TryGetValue(baseKey, out byte current))
        {
            nextId = current; // 直接把“当前累计次数”当作实例ID（第一次就是0）
        }

        uint uniqueKey = SetLow8Bits(baseKey, nextId);

        // 如果键已存在（理论上不该发生；除非你重复注册或回绕了），处理一下
        if (Buildings.ContainsKey(uniqueKey))
        {
            if (tryReuseOnOverflow)
            {
                // 在 0..255 里找一个没被占用的空位（O(256)，代价很小）
                if (!FindFreeInstanceId(baseKey, out nextId))
                {
                    Debug.LogError($"[BuildingContext] 实例ID已满（0..255均被占用），基键=0x{baseKey:X8}。请调整位段或改为可回收方案。");
                    return 0; // 失败
                }
                uniqueKey = SetLow8Bits(baseKey, nextId);
            }
            else
            {
                Debug.LogError($"[BuildingContext] 唯一键已存在：0x{uniqueKey:X8}，可能是计数溢出或重复注册。");
                return 0;
            }
        }


        var vv = Type_Buildings_Dic;
        if (vv.ContainsKey(build.buildMinor))
        {
            var set = vv.GetValueOrDefault(build.buildMinor) ?? new HashSet<GameObject>();
            if (!set.Add(go))
            {
                TLog.Error(this, $"初始化失败，{go.name}建筑已经被注册过");
                return 0;
            }
        }
        
        Buildings[uniqueKey] = go;

        // 计数 +1；到 255 后不再增长（避免 byte 回绕到 0）
        if (Counts.TryGetValue(baseKey, out byte cur))
        {
            if (cur < 255) Counts[baseKey] = (byte)(cur + 1);
            // ==255 时保持 255，不自增；下次会触发上面的冲突处理/回收逻辑
        }
        else
        {
            Counts[baseKey] = 1; // 第一次使用了 0，下一次将用 1
        }

        // Debug.Log($"Register key=0x{uniqueKey:X8} (base=0x{baseKey:X8}, inst={nextId}) for {go.StepName}");
        return uniqueKey;
    }

    public bool Unregister(uint uniqueKey)
    {
        return Buildings.Remove(uniqueKey);
        // 注意：按“累计次数”方案，不回收 Counts。若想回收，请结合 FindFreeInstanceId 的占用判断。
    }

    private static uint SetLow8Bits(uint value, byte b)
    {
        return (value & 0xFFFFFF00u) | b;
    }

    /// <summary>
    /// 在 0..255 中寻找第一个未被占用的实例ID。
    /// </summary>
    private bool FindFreeInstanceId(uint baseKey, out byte freeId)
    {
        for (int i = 0; i < 256; i++)
        {
            uint k = SetLow8Bits(baseKey, (byte)i);
            if (!Buildings.ContainsKey(k))
            {
                freeId = (byte)i;
                return true;
            }
        }
        freeId = 0;
        return false;
    }

    #endregion

    public bool GetOneVacantHouse(out Build_House house, out string reason)
    {
        reason = "";
        house = null;
        var set = Type_Buildings_Dic.GetValueOrDefault(BuildMinor.小型住宅);
        if (set == null)
        {
            reason = "未发现建筑小屋的集合";
            return false;
        }

        foreach (var v in set)
        {
            if (v.TryGetComponent(out Build_House tempHouse))
            {
                if (tempHouse.Residents.Count < tempHouse.ResidentAmount)
                {
                    house = tempHouse;
                    reason = "Success";
                    return true;
                }
            }
        }

        reason = "所有房屋均已住满";
        return false;
    }

}
