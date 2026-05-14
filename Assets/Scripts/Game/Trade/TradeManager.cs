using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Game.Trade
{
    /// <summary>
    /// 双程贸易版 TradeManager
    ///
    /// 负责：
    /// 1. UI 发起贸易入口
    /// 2. 调用 TradeService 登记贸易任务
    /// 3. 派发去程 MoveToTask
    /// 4. 去程到达后执行交换
    /// 5. 派发回程 MoveToTask
    /// 6. 回程到达后入库
    /// 7. 提供调试/查询接口
    /// </summary>
    public enum TradeState
    {
        None,
        WaitingResource,
        MovingTo,
        MovingBack,
        Failed,
        Succeed,
        Cancel
    }

    public class TradeStateMachine
    {
        public int Step { get; private set; }
        public int AllStep { get; private set; }
        public TradeState State { get; private set; }

        public TradeStateMachine(int allStep)
        {
            Step = 0;
            AllStep = allStep;
            State = TradeState.WaitingResource;
        }

        public void EnterState(TradeState newState)
        {
            if (State == TradeState.Succeed)
                return;
            State = newState;
        }

        public void UpdateStep()
        {
            if (++Step >= AllStep)
            {
                EnterState(TradeState.Succeed);
                Step = AllStep;
            }
        }
    }

    public class TradeMoveTask
    {
        /// <summary>
        /// tradeID -> moveTaskIDs
        /// </summary>
        private readonly Dictionary<int, int[]> tradeKeyDictionary = new();

        /// <summary>
        /// moveTaskID -> MoveToTask
        /// </summary>
        private readonly Dictionary<int, MoveToTask> moveKeyDictionary = new();

        /// <summary>
        /// 通过贸易路线 ID 获取对应的移动任务列表
        /// </summary>
        public Result TryGetTaskByTradeID(int tradeID, out MoveToTask[] tasks)
        {
            var result = new Result();
            tasks = null;

            if (!tradeKeyDictionary.TryGetValue(tradeID, out var moveTaskIds))
            {
                result.DebugReason($"未找到贸易路线, tradeID:{tradeID}");
                return result;
            }

            if (moveTaskIds == null || moveTaskIds.Length == 0)
            {
                result.DebugReason($"贸易路线没有配置移动任务, tradeID:{tradeID}");
                return result;
            }

            var taskList = new List<MoveToTask>(moveTaskIds.Length);

            for (int i = 0; i < moveTaskIds.Length; i++)
            {
                int moveTaskId = moveTaskIds[i];

                if (!moveKeyDictionary.TryGetValue(moveTaskId, out var moveTask) || moveTask == null)
                {
                    result.DebugReason(
                        $"贸易路线中的移动任务不存在, tradeID:{tradeID}, moveTaskID:{moveTaskId}");
                    return result;
                }

                taskList.Add(moveTask);
            }

            tasks = taskList.ToArray();
            result.success = true;
            return result;
        }

        /// <summary>
        /// 注册或覆盖一个移动任务
        /// </summary>
        public void SetMoveTask(int moveTaskID, MoveToTask task)
        {
            moveKeyDictionary[moveTaskID] = task;
        }

        /// <summary>
        /// 注册或覆盖一条贸易路线对应的移动任务 ID 列表
        /// </summary>
        public void SetTradeRoute(int tradeID, int[] moveTaskIDs)
        {
            tradeKeyDictionary[tradeID] = moveTaskIDs;
        }

        /// <summary>
        /// 删除一个移动任务
        /// </summary>
        public bool RemoveMoveTask(int moveTaskID)
        {
            return moveKeyDictionary.Remove(moveTaskID);
        }

        /// <summary>
        /// 删除一条贸易路线
        /// </summary>
        public bool RemoveTradeRoute(int tradeID)
        {
            return tradeKeyDictionary.Remove(tradeID);
        }

        /// <summary>
        /// 清空所有数据
        /// </summary>
        public void Clear()
        {
            tradeKeyDictionary.Clear();
            moveKeyDictionary.Clear();
        }
    }

    public class TradeManager : MonoSingleton<TradeManager>
    {
        private TradeRuntimeState runtimeState;
        private TradeService service;
        private TradeStateMachine stateMachine;
        private TradeMoveTask moveTaskContains;
        [SerializeField]private GridAsset debugGridAsset;
        [SerializeField] private TradeMoveUnit testUnit;
        [SerializeField] private Storage testFromStorage;
        [SerializeField] private Storage testToStorage;

        public TradeService Service => service;
        public TradeRuntimeState RuntimeState => runtimeState;

        protected override void Awake()
        {
            base.Awake();
            runtimeState = new TradeRuntimeState();
            service = new TradeService(runtimeState);
            moveTaskContains = new TradeMoveTask();
        }

        [Button("Test")]
        public void TestTrade()
        {
            TradeOfferDefinition OfferDef = new TradeOfferDefinition();
            TradeRouteDefinition RouteDef = new TradeRouteDefinition();
            TradeEndpointDefinition FromEndpoint = new TradeEndpointDefinition();
            TradeEndpointDefinition ToEndpoint = new TradeEndpointDefinition();
            List<TradeResourceStack> offerStacks = new List<TradeResourceStack>()
            {
                new TradeResourceStack(0x01010100, 10),
                new TradeResourceStack(0x01010200, 20)
            };
            List<TradeResourceStack> rewardStacks = new List<TradeResourceStack>()
            {
                new TradeResourceStack(0x01010300, 01),
                new TradeResourceStack(0x01010400, 10)
            };
            OfferDef.Init(01, "testOfferDef", offerStacks, rewardStacks);

            RouteDef.Init(01, "testRouteDef", 01, 02);

            FromEndpoint.Init(1, "测试终点", testFromStorage);

            ToEndpoint.Init(2, "测试终点", testToStorage);

            TradeDataContext ctx = new TradeDataContext(001,
                OfferDef,
                RouteDef,
                FromEndpoint,
                ToEndpoint,
                TradeTransportMode.Human, testUnit
                );
            StartTrade(ctx);
        }

        public Result StartTrade(TradeDataContext ctx)
        {
            TradeExecution execution = new TradeExecution(ctx,
                service,
                debugGridAsset);

            return execution.StartTrade();

        }
    }
}
