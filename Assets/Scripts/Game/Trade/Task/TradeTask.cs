/***************************************************************************
// File       : TradeTask.cs
// Author     : Panyuxuan
// Created    : 2026/03/15
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;
using Game.Items;
using Sim.Resources;
using UnityEngine;

namespace Game.Trade
{
    public enum TradeTaskType
    {
        WaitingResource,
        Starting,
        Moving,
        Exchanger,
        ComeBack,
        Stored
    }

    public sealed class WaitingResourceTradeTask : TaskBase
    {
        private TradeDataContext _tradeCtx;
        private readonly List<TransferRequestContext> _transferContexts = new();
        private int _pendingCount;
        private bool _isTerminal;
        private bool _isSuccess;
        private string _terminalMessage;

        public static WaitingResourceTradeTask Create(TradeDataContext ctx)
        {
            WaitingResourceTradeTask task = new WaitingResourceTradeTask();
            task._tradeCtx = ctx;
            return task;
        }

        protected override void OnStart()
        {
            _transferContexts.Clear();
            _pendingCount = 0;
            _isTerminal = false;
            _isSuccess = false;
            _terminalMessage = null;

            if (_tradeCtx == null || !_tradeCtx.IsValid())
            {
                Fail("ctx 无效");
                return;
            }

            var ctx = _tradeCtx;
            var sourceStorage = ctx.FromEndpoint?.Storage;
            var caravanStorage = ctx.MoveUnit != null ? ctx.MoveUnit.storage : null;

            if (sourceStorage == null || caravanStorage == null)
            {
                Fail("未发现来源仓库或商队仓库");
                return;
            }

            if (sourceStorage.parentArea == null)
            {
                Fail("来源仓库未挂到区域地块");
                return;
            }

            TransferDispatchCenter center = sourceStorage.parentArea.transferDispatchCenter;
            if (center == null)
            {
                Fail("未发现 TransferDispatchCenter");
                return;
            }

            if (ctx.OfferDef == null || ctx.OfferDef.CostResources == null || ctx.OfferDef.CostResources.Count == 0)
            {
                Fail("CostResources 为空");
                return;
            }

            // 1) 商队仓库必须是空仓，且切换为 CostResources 槽位布局
            var costSlotDefs = BuildSlotDefsFromTradeStacks(ctx.OfferDef.CostResources, out var defsReason);
            if (costSlotDefs == null)
            {
                Fail(defsReason);
                return;
            }

            if (!caravanStorage.RebuildSlotsIfEmpty(costSlotDefs))
            {
                Fail("商队仓库非空，无法切换为 CostResources 槽位布局");
                return;
            }

            // 2) 为每种成本资源创建一条搬运请求：FromEndpoint.Storage -> MoveUnit.storage
            Dictionary<ResourceId, int> costMap = BuildResourceMap(ctx.OfferDef.CostResources, out var mapReason);
            if (costMap == null)
            {
                Fail(mapReason);
                return;
            }

            if (costMap.Count == 0)
            {
                Fail("CostResources 中没有有效资源");
                return;
            }

            foreach (var kv in costMap)
            {
                ResourceId id = kv.Key;
                int amount = kv.Value;

                if (amount <= 0)
                    continue;

                TransferRequest request = TransferRequest.Create(
                    sourceStorage,
                    caravanStorage,
                    id,
                    amount);

                if (!center.TryEnqueueRequest(request, out var requestContext))
                {
                    UnbindAllContexts();
                    Fail(requestContext?.Message ?? $"资源调度失败，resource={id}, amount={amount}");
                    return;
                }

                BindContext(requestContext);
                _transferContexts.Add(requestContext);
                _pendingCount++;
            }

            if (_pendingCount <= 0)
            {
                Fail("没有成功创建任何装货请求");
            }
        }

        protected override bool OnUpdate(float dt)
        {
            if (!_isTerminal)
                return false;

            if (_isSuccess)
                return true;

            Fail(string.IsNullOrEmpty(_terminalMessage) ? "资源调度未成功完成" : _terminalMessage);
            return true;
        }

        protected override void OnCancel()
        {
            UnbindAllContexts();
        }

        private void BindContext(TransferRequestContext context)
        {
            if (context == null)
                return;

            context.Completed += OnContextCompleted;
            context.Failed += OnContextFailed;
            context.Canceled += OnContextCanceled;
        }

        private void UnbindAllContexts()
        {
            for (int i = 0; i < _transferContexts.Count; i++)
            {
                var ctx = _transferContexts[i];
                if (ctx == null)
                    continue;

                ctx.Completed -= OnContextCompleted;
                ctx.Failed -= OnContextFailed;
                ctx.Canceled -= OnContextCanceled;
            }

            _transferContexts.Clear();
        }

        private void OnContextCompleted(TransferRequestContext context)
        {
            if (!_transferContexts.Contains(context))
                return;

            context.Completed -= OnContextCompleted;
            context.Failed -= OnContextFailed;
            context.Canceled -= OnContextCanceled;

            _pendingCount--;

            if (_pendingCount <= 0)
            {
                _isSuccess = true;
                _isTerminal = true;
                _terminalMessage = null;
                _transferContexts.Clear();
            }
        }

        private void OnContextFailed(TransferRequestContext context, string message)
        {
            if (!_transferContexts.Contains(context))
                return;

            _isSuccess = false;
            _isTerminal = true;
            _terminalMessage = string.IsNullOrEmpty(message) ? "资源调度失败" : message;
            UnbindAllContexts();
        }

        private void OnContextCanceled(TransferRequestContext context, string message)
        {
            if (!_transferContexts.Contains(context))
                return;

            _isSuccess = false;
            _isTerminal = true;
            _terminalMessage = string.IsNullOrEmpty(message) ? "资源调度取消" : message;
            UnbindAllContexts();
        }

        private static (ResourceId id, int cap, int initial)[] BuildSlotDefsFromTradeStacks(
            List<TradeResourceStack> stacks,
            out string reason)
        {
            reason = null;

            Dictionary<ResourceId, int> map = BuildResourceMap(stacks, out reason);
            if (map == null)
                return null;

            var defs = new (ResourceId id, int cap, int initial)[map.Count];
            int index = 0;

            foreach (var kv in map)
            {
                defs[index++] = (kv.Key, kv.Value, 0);
            }

            return defs;
        }

        private static Dictionary<ResourceId, int> BuildResourceMap(
            List<TradeResourceStack> stacks,
            out string reason)
        {
            reason = null;

            Dictionary<ResourceId, int> map = new Dictionary<ResourceId, int>();

            if (stacks == null || stacks.Count == 0)
                return map;

            for (int i = 0; i < stacks.Count; i++)
            {
                var stack = stacks[i];
                if (!stack.IsValid())
                    continue;

                if (!ItemIDUtility.TryGetResourceId(stack.ItemCode, out var id) || id == ResourceId.None)
                {
                    reason = $"TradeResourceStack[{i}] itemCode={stack.ItemCode} 无法转换为 ResourceId";
                    return null;
                }

                if (stack.Amount <= 0)
                    continue;

                if (!map.TryGetValue(id, out var oldValue))
                    oldValue = 0;

                map[id] = oldValue + stack.Amount;
            }

            return map;
        }
    }

    public sealed class ExchangeTradeTask : TaskBase
    {
        private TradeDataContext _tradeDataContext;
        private bool _isSuccess;

        public static ExchangeTradeTask Create(TradeDataContext definition)
        {
            var task = new ExchangeTradeTask();
            task._tradeDataContext = definition;
            return task;
        }

        protected override void OnStart()
        {
            _isSuccess = false;

            if (_tradeDataContext == null)
            {
                Fail("TradeDataContext is null.");
                return;
            }

            if (!TradeInstantExchangeUtility.TryApplyInstantExchange(_tradeDataContext, out var reason))
            {
                Fail(reason);
                return;
            }

            _isSuccess = true;
        }

        protected override bool OnUpdate(float dt)
        {
            return _isSuccess;
        }
    }

    /// <summary>
    /// 将商队携带的奖励资源卸回出发地仓库。
    /// </summary>
    public sealed class PutResourcesToStorageTradeTask : TaskBase
    {
        private TradeDataContext _tradeDataContext;
        private readonly List<TransferRequestContext> _requestContexts = new();
        private bool _isTerminal;
        private bool _isSuccess;
        private string _terminalMessage;
        private int _remainingRequests;

        public static PutResourcesToStorageTradeTask Create(TradeDataContext definition)
        {
            var task = new PutResourcesToStorageTradeTask();
            task._tradeDataContext = definition;
            return task;
        }

        protected override void OnStart()
        {
            ResetRuntimeState();

            if (_tradeDataContext == null || !_tradeDataContext.IsValid())
            {
                Fail("TradeDataContext 无效");
                return;
            }

            Storage caravanStorage = _tradeDataContext.MoveUnit != null ? _tradeDataContext.MoveUnit.storage : null;
            Storage targetStorage = _tradeDataContext.FromEndpoint?.Storage;

            if (caravanStorage == null || targetStorage == null)
            {
                Fail("未发现商队仓库或目标入库仓库");
                return;
            }

            TransferDispatchCenter center = ResolveDispatchCenter(caravanStorage, targetStorage);
            if (center == null)
            {
                Fail("未发现可用的 TransferDispatchCenter");
                return;
            }

            if (_tradeDataContext.OfferDef?.RewardResources == null || _tradeDataContext.OfferDef.RewardResources.Count == 0)
            {
                _isSuccess = true;
                _isTerminal = true;
                return;
            }

            int validRequestCount = 0;

            for (int i = 0; i < _tradeDataContext.OfferDef.RewardResources.Count; i++)
            {
                TradeResourceStack rewardStack = _tradeDataContext.OfferDef.RewardResources[i];
                if (!rewardStack.IsValid())
                    continue;

                if (rewardStack.Amount <= 0)
                    continue;

                if (!ItemIDUtility.TryGetResourceId(rewardStack.ItemCode, out var resourceId) || resourceId == ResourceId.None)
                {
                    Fail($"RewardResources[{i}] 无法解析为合法 ResourceId, itemCode={rewardStack.ItemCode}");
                    return;
                }

                int amountInCaravan = caravanStorage.GetAvailable(resourceId);
                if (amountInCaravan <= 0)
                    continue;

                int unloadAmount = Mathf.Min(amountInCaravan, rewardStack.Amount);
                if (unloadAmount <= 0)
                    continue;

                var request = TransferRequest.Create(
                    caravanStorage,
                    targetStorage,
                    resourceId,
                    unloadAmount);

                if (!center.TryEnqueueRequest(request, out var requestContext))
                {
                    Fail(requestContext?.Message ?? $"卸货调度失败, resourceId={resourceId}, amount={unloadAmount}");
                    return;
                }

                validRequestCount++;
                _remainingRequests++;
                BindContext(requestContext);
                _requestContexts.Add(requestContext);
            }

            if (validRequestCount == 0)
            {
                _isSuccess = true;
                _isTerminal = true;
            }
        }

        protected override bool OnUpdate(float dt)
        {
            if (!_isTerminal)
                return false;

            if (_isSuccess)
                return true;

            Fail(string.IsNullOrEmpty(_terminalMessage) ? "卸货未成功完成" : _terminalMessage);
            return true;
        }

        protected override void OnCancel()
        {
            UnbindAllContexts();
        }

        private void BindContext(TransferRequestContext context)
        {
            if (context == null)
                return;

            context.Completed += OnContextCompleted;
            context.Failed += OnContextFailed;
            context.Canceled += OnContextCanceled;
        }

        private void UnbindAllContexts()
        {
            for (int i = 0; i < _requestContexts.Count; i++)
            {
                TransferRequestContext context = _requestContexts[i];
                if (context == null)
                    continue;

                context.Completed -= OnContextCompleted;
                context.Failed -= OnContextFailed;
                context.Canceled -= OnContextCanceled;
            }
        }

        private void OnContextCompleted(TransferRequestContext context)
        {
            if (_isTerminal || !_requestContexts.Contains(context))
                return;

            context.Completed -= OnContextCompleted;
            context.Failed -= OnContextFailed;
            context.Canceled -= OnContextCanceled;

            _remainingRequests--;
            if (_remainingRequests > 0)
                return;

            _isSuccess = true;
            _isTerminal = true;
            _terminalMessage = null;
            UnbindAllContexts();
        }

        private void OnContextFailed(TransferRequestContext context, string message)
        {
            if (_isTerminal || !_requestContexts.Contains(context))
                return;

            _isSuccess = false;
            _isTerminal = true;
            _terminalMessage = string.IsNullOrEmpty(message) ? "卸货调度失败" : message;
            UnbindAllContexts();
        }

        private void OnContextCanceled(TransferRequestContext context, string message)
        {
            if (_isTerminal || !_requestContexts.Contains(context))
                return;

            _isSuccess = false;
            _isTerminal = true;
            _terminalMessage = string.IsNullOrEmpty(message) ? "卸货调度取消" : message;
            UnbindAllContexts();
        }

        private void ResetRuntimeState()
        {
            _isTerminal = false;
            _isSuccess = false;
            _terminalMessage = null;
            _remainingRequests = 0;
            UnbindAllContexts();
            _requestContexts.Clear();
        }

        private static TransferDispatchCenter ResolveDispatchCenter(Storage sourceStorage, Storage targetStorage)
        {
            if (sourceStorage != null && sourceStorage.parentArea != null && sourceStorage.parentArea.transferDispatchCenter != null)
                return sourceStorage.parentArea.transferDispatchCenter;

            if (targetStorage != null && targetStorage.parentArea != null && targetStorage.parentArea.transferDispatchCenter != null)
                return targetStorage.parentArea.transferDispatchCenter;

            return null;
        }
    }
}
