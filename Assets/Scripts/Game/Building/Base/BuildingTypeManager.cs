using System;
using System.Diagnostics;

// ===============================================================
// BuildingTypeManager — 32-bit 编码布局：
// [31..28]=Major4   0..15  （建筑大类）
// [27..24]=Minor4   0..15  （建筑小类）
// [23..16]=Asset8   0..255 （初始化模板/BuildingAsset Id）
// [15..0 ]=Inst16   0..65535（场景运行期实例 Id）
// ===============================================================

#region 枚举（可按项目实际补充；取值必须 < 16）
public enum BuildMajor : byte
{
    None = 0,
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
    超级建筑 = 0xC0,
}

public enum BuildMinor : byte
{
    None = 0,
    采集小屋 = 0x11,

    农场 = 0x21,
    牧场 = 0x22,
    渔坞 = 0x23,
    伐木场 = 0x24,
    采石场 = 0x25,
    矿场 = 0x26,

    粮食加工厂 = 0x31,
    肉类加工厂 = 0x32,
    冶炼厂 = 0x33,
    木材加工厂 = 0x34,

    制造厂 = 0x41,

    小型住宅 = 0x51,
    仓库 = 0x61,

    医院 = 0x71,
    斥候营地=0x72,

    市政厅 = 0x91,
    营地 = 0x92

}
#endregion

public static class BuildingTypeManager
{
    // 位常量
    public const int MAJOR4_SHIFT = 28;
    public const int MINOR4_SHIFT = 24;
    public const int ASSET8_SHIFT = 16;

    public const uint NIBBLE_MASK = 0xFu;       // 4 bit
    public const uint BYTE_MASK = 0xFFu;      // 8 bit
    public const uint WORD_MASK = 0xFFFFu;    // 16 bit

    // ==========================
    // Encode / Decode
    // ==========================

    /// <summary>打包 4–4–8–16（枚举版）</summary>
    public static uint Encode(BuildMajor major4, BuildMinor minor4, byte asset8, ushort inst16)
        => Encode((byte)major4, (byte)minor4, asset8, inst16);

    /// <summary>打包 4–4–8–16（字节版）</summary>
    public static uint Encode(byte major4, byte minor4, byte asset8, ushort inst16)
    {
#if DEBUG
        Debug.Assert(major4 < 16, "major4 必须 < 16");
        Debug.Assert(minor4 < 16, "minor4 必须 < 16");
#endif
        uint code = 0;
        code |= ((uint)major4 & NIBBLE_MASK) << MAJOR4_SHIFT;
        code |= ((uint)minor4 & NIBBLE_MASK) << MINOR4_SHIFT;
        code |= ((uint)asset8 & BYTE_MASK) << ASSET8_SHIFT;
        code |= (uint)inst16 & WORD_MASK;
        return code;
    }

    /// <summary>解包到基础类型</summary>
    public static void Decode(uint code, out byte major4, out byte minor4, out byte asset8, out ushort inst16)
    {
        major4 = (byte)((code >> MAJOR4_SHIFT) & NIBBLE_MASK);
        minor4 = (byte)((code >> MINOR4_SHIFT) & NIBBLE_MASK);
        asset8 = (byte)((code >> ASSET8_SHIFT) & BYTE_MASK);
        inst16 = (ushort)(code & WORD_MASK);
    }

    /// <summary>解包到枚举</summary>
    public static void Decode(uint code, out BuildMajor major4, out BuildMinor minor4, out byte asset8, out ushort inst16)
    {
        Decode(code, out byte maj, out byte min, out asset8, out inst16);
        major4 = (BuildMajor)maj;
        minor4 = (BuildMinor)min;
    }

    // ==========================
    // Getters
    // ==========================
    public static byte GetMajor4(uint code) => (byte)((code >> MAJOR4_SHIFT) & NIBBLE_MASK);
    public static byte GetMinor4(uint code) => (byte)((code >> MINOR4_SHIFT) & NIBBLE_MASK);
    public static byte GetAsset8(uint code) => (byte)((code >> ASSET8_SHIFT) & BYTE_MASK);
    public static ushort GetInstance16(uint code) => (ushort)(code & WORD_MASK);

    public static BuildMajor GetMajor(uint code) => (BuildMajor)GetMajor4(code);
    public static BuildMinor GetMinor(uint code) => (BuildMinor)GetMinor4(code);

    // ==========================
    // Setters（保留其余位）
    // ==========================
    public static uint SetMajor4(uint code, byte major4)
    {
#if DEBUG
        Debug.Assert(major4 < 16);
#endif
        code &= ~(NIBBLE_MASK << MAJOR4_SHIFT);
        code |= ((uint)major4 & NIBBLE_MASK) << MAJOR4_SHIFT;
        return code;
    }

    public static uint SetMinor4(uint code, byte minor4)
    {
#if DEBUG
        Debug.Assert(minor4 < 16);
#endif
        code &= ~(NIBBLE_MASK << MINOR4_SHIFT);
        code |= ((uint)minor4 & NIBBLE_MASK) << MINOR4_SHIFT;
        return code;
    }

    public static uint SetAsset8(uint code, byte asset8)
    {
        code &= ~(BYTE_MASK << ASSET8_SHIFT);
        code |= ((uint)asset8 & BYTE_MASK) << ASSET8_SHIFT;
        return code;
    }

    public static uint SetInstance16(uint code, ushort inst16)
    {
        code &= ~WORD_MASK;
        code |= (uint)inst16 & WORD_MASK;
        return code;
    }

    public static bool Validate(uint code)
    {
        return GetMajor4(code) < 16 && GetMinor4(code) < 16;
    }
}
