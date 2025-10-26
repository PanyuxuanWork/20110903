using System;

// ============================================================================
// 32 位建筑编码方案：
// [31..24] = Major(8)       大类类型（保持原先分组风格）
// [23..16] = Minor(8)       小类类型（不再混入大类位，纯 0~255）
// [15..8]  = AssetId(8)     BuildingAsset 中的 ID（0~255）
// [7..0]   = InstanceId(8)  全局分配的实例 ID（0~255）
// ============================================================================

/// <summary>
/// 建筑大类（8 位）。沿用你原本“分组风格”，默认使用 0x10、0x20... 这种视觉分组，便于延续旧注释与文档。
/// 也可以改为 0x01,0x02... 连续编号；数值本身不影响编码，只要 0~255 即可。
/// </summary>
public enum BuildMajor : byte
{
    None = 0x00,
    Residential = 0x10, // 住宅类
    Industrial = 0x20, // 工业类
    Commerce = 0x30, // 商业类
    Military = 0x40, // 军事类
    Agriculture = 0x50, // 农业类
    Civic = 0x60, // 公共建筑
    Decoration = 0x70, // 装饰类
    Special = 0x80, // 特殊类
    // 可继续扩展到 0xF0 范围内的分段（或任意 0~255）
}

/// <summary>
/// 建筑小类（8 位）。与旧版不同：现在“小类”不再把“大类信息”混入自身数值，纯粹表示当前大类下的细分编号。
/// 建议每个大类的小类从 0x01 起编号，0x00 预留为 None。
/// </summary>
public enum BuildMinor : byte
{
    None = 0x00,

    // Residential 小类（示例）
    House = 0x01,
    Apartment = 0x02,
    Mansion = 0x03,

    // Industrial 小类（示例）
    Mine = 0x01 + 0x20,   // 只是示例值；你也可以把所有小类集中从 0x01 连续编号
    Factory = 0x02 + 0x20,
    Workshop = 0x03 + 0x20,

    // Agriculture 小类（示例）
    Farm = 0x01 + 0x50,
    Barn = 0x02 + 0x50,

    // 按你的项目进一步补齐/重排。注意：数值范围 0~255，自由度很大。
}

/// <summary>
/// [TODO] 建筑编码工具类（32 位版）：提供打包、解码、单字段读写等功能。
/// 兼容性说明：本工具为全新 32 位格式；若需从旧 16 位迁移，可在下方添加转换辅助方法。
/// </summary>
public static class BuildCode32
{
    // ----------------------------
    // 常量掩码/位移
    // ----------------------------
    private const int MAJOR_SHIFT = 24;
    private const int MINOR_SHIFT = 16;
    private const int ASSETID_SHIFT = 8;
    private const uint BYTE_MASK = 0xFFu;

    // ----------------------------
    // 打包（Encode）
    // ----------------------------

    /// <summary>
    /// 将 大类/小类/AssetID/InstanceID 打包为 32 位编码。
    /// </summary>
    public static uint Encode(BuildMajor major, BuildMinor minor, byte assetId, byte instanceId)
    {
        return ((uint)major << MAJOR_SHIFT) |
               ((uint)minor << MINOR_SHIFT) |
               ((uint)assetId << ASSETID_SHIFT) |
               (uint)instanceId;
    }

    /// <summary>
    /// 重载：直接用裸 byte 打包。
    /// </summary>
    public static uint Encode(byte major, byte minor, byte assetId, byte instanceId)
    {
        return ((uint)major << MAJOR_SHIFT) |
               ((uint)minor << MINOR_SHIFT) |
               ((uint)assetId << ASSETID_SHIFT) |
               (uint)instanceId;
    }

    // ----------------------------
    // 解包（Decode）
    // ----------------------------

    /// <summary>
    /// 从 32 位编码解码为 (major, minor, assetId, instanceId) —— 枚举版本。
    /// </summary>
    public static (BuildMajor major, BuildMinor minor, byte assetId, byte instanceId) Decode(uint code)
    {
        byte major = (byte)((code >> MAJOR_SHIFT) & BYTE_MASK);
        byte minor = (byte)((code >> MINOR_SHIFT) & BYTE_MASK);
        byte assetId = (byte)((code >> ASSETID_SHIFT) & BYTE_MASK);
        byte instance = (byte)(code & BYTE_MASK);

        return ((BuildMajor)major, (BuildMinor)minor, assetId, instance);
    }

    /// <summary>
    /// 从 32 位编码解码为裸 byte。
    /// </summary>
    public static (byte major, byte minor, byte assetId, byte instanceId) DecodeBytes(uint code)
    {
        byte major = (byte)((code >> MAJOR_SHIFT) & BYTE_MASK);
        byte minor = (byte)((code >> MINOR_SHIFT) & BYTE_MASK);
        byte assetId = (byte)((code >> ASSETID_SHIFT) & BYTE_MASK);
        byte instance = (byte)(code & BYTE_MASK);
        return (major, minor, assetId, instance);
    }

    // ----------------------------
    // 单字段读取（Extractors）
    // ----------------------------

    public static byte GetMajorByte(uint code) => (byte)((code >> MAJOR_SHIFT) & BYTE_MASK);
    public static byte GetMinorByte(uint code) => (byte)((code >> MINOR_SHIFT) & BYTE_MASK);
    public static byte GetAssetId(uint code) => (byte)((code >> ASSETID_SHIFT) & BYTE_MASK);
    public static byte GetInstanceId(uint code) => (byte)(code & BYTE_MASK);

    public static BuildMajor GetMajor(uint code) => (BuildMajor)GetMajorByte(code);
    public static BuildMinor GetMinor(uint code) => (BuildMinor)GetMinorByte(code);

    // ----------------------------
    // 单字段写入（Setters，保持其他位不变）
    // ----------------------------

    public static uint SetMajor(uint code, byte major)
    {
        code &= ~(BYTE_MASK << MAJOR_SHIFT);
        code |= ((uint)major << MAJOR_SHIFT);
        return code;
    }

    public static uint SetMinor(uint code, byte minor)
    {
        code &= ~(BYTE_MASK << MINOR_SHIFT);
        code |= ((uint)minor << MINOR_SHIFT);
        return code;
    }

    public static uint SetAssetId(uint code, byte assetId)
    {
        code &= ~(BYTE_MASK << ASSETID_SHIFT);
        code |= ((uint)assetId << ASSETID_SHIFT);
        return code;
    }

    public static uint SetInstanceId(uint code, byte instanceId)
    {
        code &= ~BYTE_MASK;
        code |= instanceId;
        return code;
    }

    // ----------------------------
    // 便捷方法
    // ----------------------------

    /// <summary>
    /// 检查编码是否为“空”（四段皆为 0）。
    /// </summary>
    public static bool IsZero(uint code) => code == 0u;

    /// <summary>
    /// 人类可读的字符串，用于调试。
    /// </summary>
    public static string ToDebugString(uint code)
    {
        var (M, m, A, I) = DecodeBytes(code);
        return $"BuildCode32[Major=0x{M:X2}, Minor=0x{m:X2}, AssetId=0x{A:X2}, InstanceId=0x{I:X2}]";
    }

    // ----------------------------
    // （可选）从旧 16 位编码迁移的辅助方法
    // 旧格式： [15..8]=Type(8) ; [7..0]=Id(8)
    // 你可以按需把旧 Type 拆成 Major/Minor（如果旧 Type 的高4位=大类，低4位=小类），并填入新的 AssetId/InstanceId。
    // ----------------------------
    public static uint FromLegacy16(ushort legacyCode, byte assetId, byte instanceId)
    {
        byte type8 = (byte)(legacyCode >> 8);
        byte id8 = (byte)(legacyCode & 0xFF);

        // 若旧版规则为：type8 高4位=大类，低4位=小类
        byte majorNibble = (byte)((type8 >> 4) & 0x0F);
        byte minorNibble = (byte)(type8 & 0x0F);

        byte major = (byte)(majorNibble << 4); // 仍按 0x10,0x20... 的视觉分组
        byte minor = (minorNibble == 0) ? (byte)0 : (byte)minorNibble; // 旧 None -> 0，新小类用 1..15

        // 这里你也可以选择把旧 id8 填到新 AssetId，而把参数 assetId 作为覆盖。
        // 当前实现：新 AssetId 优先生效；旧 id8 如需保留可自行扩展。
        return Encode(major, minor, assetId, instanceId);
    }
}
