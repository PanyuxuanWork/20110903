
using UnityEngine;
using Sim.Resources;
using System.Collections.Generic;

public class ResidentEconomyService : MonoBehaviour
{
    [Header("预约默认TTL（秒）")]
    public float reservationTtlSec = 30f;

    [Header("导航（供 MoveToTask 使用）")]
    public GridAsset navigationGrid;
    public byte[] passMask;

    [Header("搬运背包")]
    public Storage backpack;

    [Header("搬运能力")]
    [Min(1)]
    public int defaultCarryAmount = 100;

    private Resident _resident;

    // 仅用于“背包容量预约时，把 None 槽临时绑定为具体资源”
    private sealed class SlotBindingRecord
    {
        public ResourceId ResourceId;
        public readonly List<int> SlotIndices = new();
    }

    private readonly Dictionary<int, SlotBindingRecord> _capTicketBindings = new();

    private void Awake()
    {
        passMask ??= new byte[] { 1 };
        _resident = GetComponent<Resident>();
        ResolveRefs();
    }

    private void Start()
    {
        ResolveRefs();
    }

    private void ResolveRefs()
    {
        if (_resident == null)
            _resident = GetComponent<Resident>();

        if (backpack == null && _resident != null)
            backpack = _resident.backpack;

        if (navigationGrid == null && _resident != null && _resident.ParentArea != null)
            navigationGrid = _resident.ParentArea.grid;
    }

    /// <summary>
    /// 纯查询：当前服务是否具备搬运指定资源的能力。
    /// 不修改任何运行时状态。
    /// </summary>
    public bool CanCarry(ResourceId id, int amount = 1)
    {
        if (!isActiveAndEnabled)
            return false;

        if (id == ResourceId.None)
            return false;

        if (amount <= 0)
            return false;

        ResolveRefs();
        if (backpack == null)
            return false;

        return GetCarryCapacity(id) >= amount;
    }

    /// <summary>
    /// 纯查询：当前背包对该资源的可搬运容量。
    /// 规则：
    /// 1. slot.Id == id 的槽，按剩余容量计入。
    /// 2. slot.Id == None 且 Amount == 0 的空槽，可视为“可绑定任意资源槽”，按 Capacity 计入。
    /// </summary>
    public int GetCarryCapacity(ResourceId id)
    {
        ResolveRefs();
        if (backpack == null)
            return 0;

        if (id == ResourceId.None)
            return 0;

        var slots = backpack.Slots;
        if (slots == null || slots.Count == 0)
            return 0;

        int total = 0;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;

            if (slot.Id == id)
            {
                total += Mathf.Max(0, slot.Capacity - slot.Amount);
                continue;
            }

            if (slot.Id == ResourceId.None && slot.Amount == 0)
            {
                total += Mathf.Max(0, slot.Capacity);
            }
        }

        return total;
    }

    /// <summary>
    /// 仅用于“背包”容量预约。
    /// 与通用 Storage.TryReserveCapacity 不同：
    /// - 允许 None 空槽作为可绑定槽；
    /// - 在预约成功前，把需要用到的 None 槽先绑定成目标资源；
    /// - 该逻辑只存在于 ResidentEconomyService，仓库不走这套规则。
    /// </summary>
    public bool TryReserveBackpackCapacity(ResourceId id, int amount, out int capTicket, float ttlSec = -1f)
    {
        capTicket = 0;
        ResolveRefs();

        if (backpack == null)
            return false;
        if (id == ResourceId.None || amount <= 0)
            return false;

        var slots = backpack._slots;
        if (slots == null || slots.Count == 0)
            return false;

        if (GetCarryCapacity(id) < amount)
            return false;

        int remain = amount;
        var newlyBound = new List<int>();

        // 先消耗已绑定为该资源的槽的剩余容量
        for (int i = 0; i < slots.Count && remain > 0; i++)
        {
            var slot = slots[i];
            if (slot == null || slot.Id != id)
                continue;

            remain -= Mathf.Max(0, slot.Capacity - slot.Amount);
        }

        // 不够时，再把 None 空槽绑定成该资源
        for (int i = 0; i < slots.Count && remain > 0; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;
            if (slot.Id != ResourceId.None || slot.Amount != 0)
                continue;

            slot.Id = id;
            newlyBound.Add(i);
            remain -= Mathf.Max(0, slot.Capacity);
        }

        if (remain > 0)
        {
            RevertSlotBindings(newlyBound);
            return false;
        }

        if (!backpack.TryReserveCapacity(id, amount, out capTicket, ttlSec))
        {
            RevertSlotBindings(newlyBound);
            capTicket = 0;
            return false;
        }

        if (newlyBound.Count > 0)
        {
            _capTicketBindings[capTicket] = new SlotBindingRecord
            {
                ResourceId = id,
                SlotIndices = { }
            };
            _capTicketBindings[capTicket].SlotIndices.AddRange(newlyBound);
        }

        return true;
    }

    /// <summary>
    /// 当“背包容量预约”因任务失败/取消而回滚时调用。
    /// 会取消 Storage 票据，并把本次为了预约而新绑定、且当前仍为空的槽恢复为 None。
    /// </summary>
    public void CancelBackpackCapacityReserve(int capTicket)
    {
        ResolveRefs();
        if (backpack != null && capTicket != 0)
            backpack.CancelCapacityReserve(capTicket);

        if (_capTicketBindings.TryGetValue(capTicket, out var record))
        {
            RestoreEmptyBoundSlots(record);
            _capTicketBindings.Remove(capTicket);
        }
    }

    /// <summary>
    /// 当背包已经真正装到货后调用。
    /// 这时不再需要保留“这张容量票据绑定了哪些 None 槽”的临时映射。
    /// 槽位仍保持具体资源类型，直到后续清空再恢复为 None。
    /// </summary>
    public void CommitBackpackCapacityReserve(int capTicket)
    {
        if (capTicket == 0)
            return;

        _capTicketBindings.Remove(capTicket);
    }

    /// <summary>
    /// 把当前背包中“空的具体资源槽”恢复为 None。
    /// 仅用于居民背包，不影响城市仓库。
    /// </summary>
    public void ReleaseEmptyBackpackSlotsToNone(ResourceId onlyId = ResourceId.None)
    {
        ResolveRefs();
        if (backpack == null)
            return;

        var slots = backpack._slots;
        if (slots == null)
            return;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;
            if (slot.Amount != 0)
                continue;
            if (slot.Id == ResourceId.None)
                continue;
            if (onlyId != ResourceId.None && slot.Id != onlyId)
                continue;

            slot.Id = ResourceId.None;
        }
    }

    /// <summary>
    /// 纯查询：当前是否是空闲搬运工（只看背包中的实际装载）。
    /// 这里只看实际货物，不看任务系统 busy 标记。
    /// </summary>
    public bool IsIdleCarrier()
    {
        if (!isActiveAndEnabled)
            return false;

        ResolveRefs();
        if (backpack == null)
            return false;

        var slots = backpack.Slots;
        if (slots == null || slots.Count == 0)
            return false;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;

            if (slot.Amount > 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// 是否拥有至少一个可用于搬运的槽。
    /// 在背包语义下，None 槽和具体资源槽都算合法槽。
    /// </summary>
    public bool HasAnyCarrySlot()
    {
        ResolveRefs();
        if (backpack == null)
            return false;

        var slots = backpack.Slots;
        if (slots == null || slots.Count == 0)
            return false;

        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot == null)
                continue;

            if (slot.Capacity > 0)
                return true;
        }

        return false;
    }

    private void RevertSlotBindings(List<int> indices)
    {
        if (indices == null || indices.Count == 0 || backpack == null)
            return;

        var slots = backpack._slots;
        for (int i = 0; i < indices.Count; i++)
        {
            int idx = indices[i];
            if (idx < 0 || idx >= slots.Count)
                continue;

            var slot = slots[idx];
            if (slot == null)
                continue;
            if (slot.Amount != 0)
                continue;

            slot.Id = ResourceId.None;
        }
    }

    private void RestoreEmptyBoundSlots(SlotBindingRecord record)
    {
        if (record == null || backpack == null)
            return;

        var slots = backpack._slots;
        for (int i = 0; i < record.SlotIndices.Count; i++)
        {
            int idx = record.SlotIndices[i];
            if (idx < 0 || idx >= slots.Count)
                continue;

            var slot = slots[idx];
            if (slot == null)
                continue;
            if (slot.Id != record.ResourceId)
                continue;
            if (slot.Amount != 0)
                continue;

            slot.Id = ResourceId.None;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (defaultCarryAmount < 1)
            defaultCarryAmount = 1;

        passMask ??= new byte[] { 1 };
    }
#endif
}
