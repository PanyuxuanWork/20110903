/***************************************************************************
// File       : TaskToken.cs
// Author     : Panyuxuan
// Created    : 2025/10/22
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: Add script summary here
// ***************************************************************************/
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class TaskToken
{
    public int TokenId { get; private set; }
    public bool IsCompleted { get; private set; }
    private Action _onCompletedCallback;  // 回调，用于任务完成时触发

    public TaskToken(int tokenId)
    {
        TokenId = tokenId;
        IsCompleted = false;
    }

    public void Complete()
    {
        IsCompleted = true;
        _onCompletedCallback?.Invoke();  // 触发任务完成的回调
    }

    // 注册任务完成后的回调
    public void OnCompleted(Action callback)
    {
        _onCompletedCallback = callback;
    }
}

public class TaskTokenManager
{
    private int _currentTokenId = 0; // 用于生成唯一的令牌
    private readonly Dictionary<int, TaskToken> _activeTokens = new Dictionary<int, TaskToken>();

    // 生成一个新的令牌
    public TaskToken GenerateToken()
    {
        _currentTokenId++;
        var token = new TaskToken(_currentTokenId);
        _activeTokens[_currentTokenId] = token;
        return token;
    }

    // 完成一个令牌，标记任务为完成
    public void CompleteToken(int tokenId)
    {
        if (_activeTokens.ContainsKey(tokenId))
        {
            _activeTokens[tokenId].Complete();
            _activeTokens.Remove(tokenId);
        }
        else
        {
            throw new InvalidOperationException($"Token {tokenId} not found.");
        }
    }

    // 检查任务令牌是否完成
    public bool IsTokenCompleted(int tokenId)
    {
        return _activeTokens.ContainsKey(tokenId) && _activeTokens[tokenId].IsCompleted;
    }

    // 等待令牌完成，阻塞当前线程
    public void WaitForCompletion(int tokenId)
    {
        while (!IsTokenCompleted(tokenId))
        {
            Thread.Sleep(100); // 每 100 毫秒检查一次，避免过度占用 CPU
        }
    }

    // 等待令牌完成，异步阻塞
    public async Task WaitForCompletionAsync(int tokenId)
    {
        while (!IsTokenCompleted(tokenId))
        {
            await Task.Delay(100); // 使用异步的等待，避免阻塞线程
        }
    }
}
