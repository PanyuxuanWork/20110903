/***************************************************************************
// File       : TradeStartResult.cs
// Author     : Panyuxuan
// Created    : 2026/03/12
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using UnityEngine;

namespace Game.Trade
{
    /// <summary>
    /// 发起贸易结果
    /// </summary>
    public struct TradeStartResult
    {
        public bool Success;
        public int TaskId;
        public string ErrorMessage;

        public static TradeStartResult CreateSuccess(int taskId)
        {
            UILog.Instance.Show($"创建任务成功 {taskId}");
            return new TradeStartResult
            {
                Success = true,
                TaskId = taskId,
                ErrorMessage = string.Empty
            };
        }

        public static TradeStartResult CreateFailed(string errorMessage)
        {
            return new TradeStartResult
            {
                Success = false,
                TaskId = 0,
                ErrorMessage = errorMessage
            };
        }
    }
}