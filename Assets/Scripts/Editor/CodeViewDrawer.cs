/***************************************************************************
// File       : CodeViewDrawerDemo.cs
// Author     : Panyuxuan
// Created    : 2025/10/21
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Reset script summary here
// ***************************************************************************/

using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

public class CodeViewDrawerDemo
{
    [LabelText("professionType")]
    [CustomValueDrawer(nameof(DrawUShortHexBin))]
    public ushort code;

#if UNITY_EDITOR
    // 简易：并排两个输入框（Hex / Bin），改任一都会写回到 professionType。
    private ushort DrawUShortHexBin(ushort value, GUIContent label)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(EditorGUIUtility.labelWidth - 4));

        // HEX
        var hex = GUILayout.TextField($"0x{value:X4}", GUILayout.MinWidth(80));
        if (hex.StartsWith("0x") || hex.StartsWith("0X")) hex = hex.Substring(2);
        if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
            value = hexVal;

        // BIN
        var binOld = "0b" + System.Convert.ToString(value, 2).PadLeft(16, '0');
        var binInp = GUILayout.TextField(binOld, GUILayout.MinWidth(140));
        var s = binInp.StartsWith("0b") ? binInp.Substring(2) : binInp;
        s = s.Replace("_", "").Replace(" ", "");
        bool ok = s.Length > 0 && s.Length <= 16;
        for (int i = 0; ok && i < s.Length; i++) ok &= (s[i] == '0' || s[i] == '1');
        if (ok) value = System.Convert.ToUInt16(s, 2);

        GUILayout.EndHorizontal();
        return value;
    }
#endif
}

