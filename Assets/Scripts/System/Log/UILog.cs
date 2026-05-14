/***************************************************************************
// File       : UILog.cs
// Author     : Panyuxuan
// Created    : 2026/02/08
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using UnityEngine;

[AutoAttached]
public class UILog :MonoSingleton<UILog>
{
    public void Show(string str, bool b = false)
    {
        Debug.Log(str);
    }

    public void ShowError(string str)
    {
        TLog.Error(str);
    }

    public void ShowError(MonoBehaviour mono, string str)
    {
        TLog.Error(mono,str);
    }
}
