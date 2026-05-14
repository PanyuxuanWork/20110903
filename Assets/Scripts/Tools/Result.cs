/***************************************************************************
// File       : Result.cs
// Author     : Panyuxuan
// Created    : 2026/03/15
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using UnityEngine;

/// <summary>
/// 对象池，用完一定要清理！
/// </summary>
public class Result
{
    public bool success;
    public string reason { get; private set; }


    public static Result Error(string error)
    {
        Result result = new Result();
        result.DebugReason(error);
        return result;
    }
    public void DebugReason(string _reason)
    {
        success = false;
#if UNITY_EDITOR|| DEVELOPMENT_BUILD
        this.reason = _reason;
        Debug.LogError(reason);
#endif
    }
}
