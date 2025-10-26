using System;
using System.Collections;
using UnityEngine;
using Sim.Resources;
using Unity.VisualScripting;

/// <summary>
/// 居民经济服务：把“瞬间搬运”改为“居民走过去搬运”。
/// 流程：预约 → MoveTo(src) → 取货(OfferResource) → MoveTo(dst) → 投放(GetResource) → 清理/回滚。
/// </summary>
public class ResidentEconomyService : MonoBehaviour
{
    [Header("预约默认TTL（秒）")]
    public float reservationTtlSec = 30f;

    [Header("导航（供 MoveToTask 使用）")]
    public GridAsset navigationGrid;      // 请在 Inspector/运行时赋值
    public byte[] passMask;               // 可选：通行掩码；为空则用空数组
    public Storage backpack;

    private void Awake()
    {
        passMask ??= new byte[]{1};
    }
    

    private void Start()
    {
        backpack ??= GetComponent<Resident>().backpack;
        navigationGrid ??= GetComponent<Resident>().ParentArea.grid;
    }

}
