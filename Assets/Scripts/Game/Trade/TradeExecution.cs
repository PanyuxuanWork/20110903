/***************************************************************************
// File       : TradeExecution.cs
// Author     : Panyuxuan
// Created    : 2025/08/
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/


namespace Game.Trade
{
    /// <summary>
    /// 单笔贸易执行实例。
    ///
    /// 职责：
    /// 1. 持有本次贸易自己的上下文
    /// 2. 向 TradeService 登记，拿到 tradeTaskId
    /// 3. 创建并持有自己的 TaskQueue
    /// 4. 把五个原子任务封装成一条完整贸易流程
    /// 5. 向外提供一个傻瓜式 StartTrade() 入口
    ///
    /// 不负责：
    /// - 全局管理所有贸易
    /// - 记录容器本身（这由 TradeService 负责）
    /// - 具体资源逻辑实现（由各 Task 负责）
    /// </summary>
    public sealed class TradeExecution
    {
        private readonly TradeDataContext _context;
        private readonly TradeService _tradeService;
        private readonly GridAsset _gridAsset;

        private TaskQueue _queue;
        private int _tradeTaskId;
        private bool _started;
        private bool _submitted;

        public TradeDataContext Context => _context;
        public TradeService TradeService => _tradeService;
        public TaskQueue Queue => _queue;
        public int TradeTaskId => _tradeTaskId;
        public bool IsStarted => _started;
        public bool IsSubmitted => _submitted;

        public TradeExecution(
            TradeDataContext context,
            TradeService tradeService,
            GridAsset gridAsset)
        {
            _context = context;
            _tradeService = tradeService;
            _gridAsset = gridAsset;
        }

        /// <summary>
        /// 启动这笔贸易。
        /// 外部只需要调用这一个方法。
        /// </summary>
        public Result StartTrade()
        {
            Result result = new();

            if (_started)
            {
                result.DebugReason("TradeExecution 已经启动过，不能重复 StartTrade。");
                return result;
            }

            _started = true;

            Result validateResult = ValidateBeforeStart();
            if (!validateResult.success)
            {
                _started = false;
                return validateResult;
            }

            TradeStartResult registerResult = _tradeService.StartTrade(_context);
            if (!registerResult.Success)
            {
                result.DebugReason($"TradeService 登记失败，原因：{registerResult.ErrorMessage}");
                _started = false;
                return result;
            }

            _tradeTaskId = registerResult.TaskId;

            Result buildQueueResult = BuildQueue();
            if (!buildQueueResult.success)
            {
                RollbackRecord();
                _started = false;
                return buildQueueResult;
            }

            if (TaskService.Instance == null)
            {
                RollbackRecord();
                _started = false;
                result.DebugReason("TaskService.Instance 为空。");
                return result;
            }

            if (!TaskService.Instance.AddQueue(_queue))
            {
                RollbackRecord();
                _started = false;
                result.DebugReason("任务队列添加失败。");
                return result;
            }

            _submitted = true;
            result.success = true;
            return result;
        }

        /// <summary>
        /// 构建整笔贸易的任务链：
        /// 等待资源 -> 前往目的地 -> 交换 -> 返回 -> 卸货
        /// </summary>
        private Result BuildQueue()
        {
            Result result = new();

            string queueName = BuildQueueName();
            _queue = new TaskQueue(queueName);
            _queue.FailurePolicy = TaskQueueFailurePolicy.ClearQueue;
            WaitingResourceTradeTask waitingResourceTask = CreateWaitingResourceTask();
            MoveToTask moveToTargetTask = CreateMoveToTargetTask();
            ExchangeTradeTask exchangeTradeTask = CreateExchangeTask();
            MoveToTask moveBackTask = CreateMoveBackTask();
            PutResourcesToStorageTradeTask putResourcesToStorageTask = CreatePutResourcesToStorageTask();

            if (waitingResourceTask == null
                || moveToTargetTask == null
                || exchangeTradeTask == null
                || moveBackTask == null
                || putResourcesToStorageTask == null)
            {
                result.DebugReason("TradeExecution 构建任务链失败：存在空任务。");
                return result;
            }
            _queue.Enqueue(waitingResourceTask);
            _queue.Enqueue(moveToTargetTask);
            _queue.Enqueue(exchangeTradeTask);
            _queue.Enqueue(moveBackTask);
            _queue.Enqueue(putResourcesToStorageTask);

            result.success = true;
            return result;
        }

        private WaitingResourceTradeTask CreateWaitingResourceTask()
        {
            return WaitingResourceTradeTask.Create(_context);
        }

        private ExchangeTradeTask CreateExchangeTask()
        {
            return ExchangeTradeTask.Create(_context);
        }

        private PutResourcesToStorageTradeTask CreatePutResourcesToStorageTask()
        {
            return PutResourcesToStorageTradeTask.Create(_context);
        }

        /// <summary>
        /// 去程移动任务
        ///
        /// 这里先沿用你当前 TradeManager 里的占位写法。
        /// 后面你把真实起点 / 终点点位接进来即可。
        /// </summary>
        private MoveToTask CreateMoveToTargetTask()
        {
            if (_context == null || _context.MoveUnit == null)
            {
                return null;
            }

            return MoveToTask.Create(
                _context.MoveUnit.transform,
                _gridAsset,
                MoveToTask.MovePoint.From(_context.MoveUnit.gameObject),
                MoveToTask.MovePoint.From(_context.ToEndpoint.Storage.gameObject),
                new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        }

        /// <summary>
        /// 回程移动任务
        ///
        /// 这里同样先沿用你当前的占位写法。
        /// </summary>
        private MoveToTask CreateMoveBackTask()
        {
            if (_context == null || _context.MoveUnit == null)
            {
                return null;
            }

            return MoveToTask.Create(
                _context.MoveUnit.transform,
                _gridAsset,
                MoveToTask.MovePoint.From(_context.MoveUnit.gameObject),
                MoveToTask.MovePoint.From(_context.FromEndpoint.Storage.gameObject),
                new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        }

        private Result ValidateBeforeStart()
        {
            Result result = new();

            if (_context == null)
            {
                result.DebugReason("TradeDataContext 为空。");
                return result;
            }

            if (!_context.IsValid())
            {
                result.DebugReason("TradeDataContext 无效。");
                return result;
            }

            if (_tradeService == null)
            {
                result.DebugReason("TradeService 为空。");
                return result;
            }

            if (_gridAsset == null)
            {
                result.DebugReason("GridAsset 为空。");
                return result;
            }

            result.success = true;
            return result;
        }

        private string BuildQueueName()
        {
            string offerName = _context?.OfferDef?.OfferName;
            string routeName = _context?.RouteDef?.RouteName;

            if (string.IsNullOrEmpty(offerName) && string.IsNullOrEmpty(routeName))
            {
                return "贸易流程";
            }

            if (string.IsNullOrEmpty(routeName))
            {
                return $"贸易流程：{offerName}";
            }

            if (string.IsNullOrEmpty(offerName))
            {
                return $"贸易流程：{routeName}";
            }

            return $"贸易流程：{offerName} - {routeName}";
        }

        /// <summary>
        /// 任务链还没真正跑起来就失败时，回滚记录层。
        /// </summary>
        private void RollbackRecord()
        {
            if (_tradeTaskId <= 0 || _tradeService == null)
            {
                return;
            }

            _tradeService.CancelTrade(_tradeTaskId);
        }

        // --------------------------------------------------------------------
        // 下面这些方法是给后续“表现层 -> 记录层”正式联动预留的接口。
        // 你后面只需要让原子 Task 在关键节点回调这些方法即可。
        // --------------------------------------------------------------------

        /// <summary>
        /// 等待资源完成，进入运输状态。
        /// </summary>
        public void OnWaitingResourceFinished(float totalPathLength = 0f)
        {
            if (_tradeTaskId <= 0 || _tradeService == null)
            {
                return;
            }

            _tradeService.MarkTravelling(_tradeTaskId, totalPathLength);
        }

        /// <summary>
        /// 路上更新进度。
        /// </summary>
        public void OnMoveProgress(float traveledPathLength, float totalPathLength = 0f)
        {
            if (_tradeTaskId <= 0 || _tradeService == null)
            {
                return;
            }

            _tradeService.UpdateProgress(_tradeTaskId, traveledPathLength, totalPathLength);
        }

        /// <summary>
        /// 外部检测到阻断后，由表现层回写到记录层。
        /// </summary>
        public void OnBlocked(TradeBlockReason reason)
        {
            if (_tradeTaskId <= 0 || _tradeService == null)
            {
                return;
            }

            _tradeService.SetBlocked(_tradeTaskId, reason);
        }

        /// <summary>
        /// 外部检测到阻断解除后，由表现层回写到记录层。
        /// </summary>
        public void OnUnblocked()
        {
            if (_tradeTaskId <= 0 || _tradeService == null)
            {
                return;
            }

            _tradeService.ClearBlocked(_tradeTaskId);
        }

        /// <summary>
        /// 整笔贸易最终完成。
        /// </summary>
        public void OnTradeCompleted()
        {
            if (_tradeTaskId <= 0 || _tradeService == null)
            {
                return;
            }

            _tradeService.CompleteTrade(_tradeTaskId);
        }

        /// <summary>
        /// 这笔贸易流程失败或被取消。
        /// </summary>
        public void OnTradeFailed()
        {
            if (_tradeTaskId <= 0 || _tradeService == null)
            {
                return;
            }

            _tradeService.CancelTrade(_tradeTaskId);
        }
    }
}
