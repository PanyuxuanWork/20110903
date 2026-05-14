
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ResidentProfession : MonoBehaviour
{
    [Header("Profession (Authoring)")]
    [SerializeField]
    private ProfessionType type = ProfessionType.失业;


    /// <summary>16 位职业码（只读查看；需要设置请用 SetProfession）</summary>
    public ProfessionType professionType => type;

    /// <summary>
    /// 通过 16 位 professionType 设置职业（用于读档/网络下发/数据驱动）。
    /// </summary>
    public void SetProfession(ProfessionType profession = ProfessionType.失业)
    {
        Resident r = GetComponent<Resident>();
        if (r == null)
            return;

        if (r.ParentArea == null)
        {
            TLog.Error(this, "Resident 的 ParentArea 为空");
            return;
        }

        ResidentContext context = r.ParentArea.residentContext;
        if (!context)
        {
            TLog.Error(this, "未发现ResidentContext");
            return;
        }

        context.residents.Add(r);

        foreach (var pair in context.residentsDict)
        {
            pair.Value.Remove(r);
        }

        if (!context.residentsDict.TryGetValue(profession, out var newSet))
        {
            newSet = new HashSet<Resident>();
            context.residentsDict.Add(profession, newSet);
        }

        newSet.Add(r);
        type = profession;
    }

}



/// <summary>
/// 职业大类（占 8 位，建议按 0x10、0x20 … 便于看 nibble 边界）
/// </summary>
public enum ProfessionMajor : byte
{
    None = 0x00,
    收集类 = 0x10,
    加工类 = 0x30,
    生产类 = 0x20,
    经济加工类 = 0x40,
    住宅类 = 0x50,
    仓储类 = 0x60,
    功能类 = 0x70,
    军事类 = 0x80,
    市政类 = 0x90,
    文化类 = 0xA0,
    特殊类 = 0xB0,

}

/// <summary>
/// 职业小类（占 8 位）。
/// 约定：高 4 位 = 所属大类编号；低 4 位 = 具体小类编号（1..15）
/// 例如：0x11 表示 Worker 类下的小类 1；0x12 表示 Worker 类下的小类 2。
/// 若无小类，用 0x00（失业）。
/// </summary>
public enum ProfessionType : byte
{
    失业 = 0x00,

    采集员 = 0x11,

    农场员工 = 0x21,
    牧场员工 = 0x22,
    渔民 = 0x23,
    伐木工 = 0x24,
    采石员 = 0x25,
    矿场 = 0x26,

    仓库搬运工 = 0x61,

}


