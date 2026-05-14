/***************************************************************************
// File       : ItemID.cs
// Author     : Panyuxuan
// Created    : 2026/03/12
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using Sim.Resources;
using UnityEngine;

/// <summary>
/// 物品编码定义。
/// 32位结构：
/// [高8位][第二组8位][第三组8位][低8位]
/// 本类只负责定义常量，不包含任何方法。
/// </summary>
public static class ItemID
{
    // =========================
    // 示例：高8位 ObjectType
    // =========================
    public const int FirstType_1 = 0x01;
    public const int FirstType_2 = 0x02;
    public const int FirstType_3 = 0x03;

    // =========================
    // 示例：第二组8位 MajorType
    // =========================
    public const int SecondType_Resource = 0x01;
    public const int SecondType_Building = 0x02;
    public const int MajorType_Food = 0x03;
    public const int MajorType_Industrial = 0x04;

    // =========================
    // 示例：第三组8位 MinorType
    // 这里只放少量示例，你后面自己补
    // =========================
    public const int MinorType_Wood = 0x01;
    public const int MinorType_Stone = 0x02;
    public const int MinorType_Iron = 0x03;
    public const int MinorType_Food = 0x04;

    // =========================
    // 低8位扩展位
    // =========================
    public const int Extension_Default = 0x00;

    // =========================
    // 示例物品编码
    // 命名方式：<ItemName>ItemCode
    // =========================
    public const int WoodItemCode =
        (FirstType_1 << 24) |
        (SecondType_Resource << 16) |
        (MinorType_Wood << 8) |
        Extension_Default;

    public const int StoneItemCode =
        (FirstType_1 << 24) |
        (SecondType_Resource << 16) |
        (MinorType_Stone << 8) |
        Extension_Default;

    public const int IronItemCode =
        (FirstType_1 << 24) |
        (SecondType_Building << 16) |
        (MinorType_Iron << 8) |
        Extension_Default;

    public const int FoodItemCode =
        (FirstType_1 << 24) |
        (MajorType_Food << 16) |
        (MinorType_Food << 8) |
        Extension_Default;

}
