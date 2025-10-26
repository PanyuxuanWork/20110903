using Sirenix.OdinInspector;
using System;
using Sirenix.Serialization;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 职业大类（占 8 位，建议按 0x10、0x20 … 便于看 nibble 边界）
/// </summary>
public enum ProfessionMajor : byte
{
    None = 0x00,
    Worker = 0x10, // 例：与原来的 Worker 语义对应
    // 可继续扩展：Agriculture=0x20, Commerce=0x30, ...
}

/// <summary>
/// 职业小类（占 8 位）。
/// 约定：高 4 位 = 所属大类编号；低 4 位 = 具体小类编号（1..15）
/// 例如：0x11 表示 Worker 类下的小类 1；0x12 表示 Worker 类下的小类 2。
/// 若无小类，用 0x00（None）。
/// </summary>
public enum ProfessionMinor : byte
{
    None = 0x00,

    // --- Worker family 示例 ---
    // 高 4 位=1（属于 Worker），低 4 位=1/2/3...
    Worker_A = 0x11,
    Worker_B = 0x12,
    Worker_C = 0x13,
}

/// <summary>
/// 职业编码工具：
/// Code(ushort) = [15..8] Major | [7..0] Minor
/// </summary>
public static class ProfessionCodeUtil
{
    public static ushort Encode(ProfessionMajor major, ProfessionMinor minor)
    {
        byte major8 = (byte)major;
        byte minor8 = (byte)minor;
        return (ushort)((major8 << 8) | minor8);
    }

    public static (ProfessionMajor major, ProfessionMinor minor) Decode(ushort code)
    {
        byte major8 = (byte)(code >> 8);
        byte minor8 = (byte)(code & 0xFF);
        return ((ProfessionMajor)major8, (ProfessionMinor)minor8);
    }

    /// <summary>
    /// 校验规则：高 8 位不能为 0；若 Minor!=None，则 (Minor>>4) 必须等于 (Major>>4)。
    /// </summary>
    public static bool IsConsistent(ProfessionMajor major, ProfessionMinor minor)
    {
        byte M = (byte)major;
        if (M == 0) return false; // 大类高 8 位不能为 0

        byte m = (byte)minor;
        if (m == 0) return true;  // 无小类允许

        return (byte)(m >> 4) == (byte)(M >> 4); // 小类高 4 位必须等于大类高 4 位
    }
}

/// <summary>
/// Resident 的职业组件（无状态机版本）：
/// - 在 Inspector 里配置 Major/Minor；
/// - 自动计算 Code；
/// - 也可通过 Code 反向设置（用于存档/网络/外部表驱动）。
/// </summary>
[DisallowMultipleComponent]
public class ResidentProfession : MonoBehaviour
{
    [Header("Profession (Authoring)")]
    private ProfessionMajor major = ProfessionMajor.Worker;
    private ProfessionMinor minor = ProfessionMinor.None;

    [SerializeField, Tooltip("自动计算出的 16 位职业码：高8=大类，低8=小类")]
    private ushort code;

    /// <summary>16 位职业码（只读查看；需要设置请用 SetByCode）</summary>
    public ushort Code => code;

    /// <summary>
    /// 通过 16 位 Code 设置职业（用于读档/网络下发/数据驱动）。
    /// </summary>
    public void SetByCode(ushort newCode)
    {
        var (mj, mi) = ProfessionCodeUtil.Decode(newCode);
        if (!ProfessionCodeUtil.IsConsistent(mj, mi))
        {
            Debug.LogError($"[ResidentProfession] Invalid Profession Code: 0x{newCode:X4} (major={mj}, minor={mi})");
            return;
        }

        major = mj;
        minor = mi;
        code = newCode;
        // 如需在切换职业时挂/卸职责服务，可在此处触发（见下方注释）
        // UpdateServicesForProfession();
    }



}
