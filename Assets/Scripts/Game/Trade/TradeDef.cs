/***************************************************************************
// File       : TradeResourceStack.cs
// Author     : Panyuxuan
// Created    : 2026/03/12
// Copyright  : © 2025 SkyWander Games. All rights reserved.
// Description: [TODO] Reset script summary here
// ***************************************************************************/

using System.Collections.Generic;

using System;
using System.Collections.Generic;
using Game.Items;
using Mono.Cecil;
using Sim.Resources;

namespace Game.Trade
{
    /// <summary>
    /// 贸易槽核心数据 Model。
    /// 沿用你原来的定义。
    /// </summary>
    [Serializable]
    public struct TradeResourceStack
    {
        public int ItemCode;
        public int Amount;

        public TradeResourceStack(int itemCode, int amount)
        {
            ItemCode = itemCode;
            Amount = amount;
        }

        public bool IsValid()
        {
            return ItemCode != 0 && Amount > 0;
        }

        public static TradeResourceStack Empty()
        {
            return new TradeResourceStack(0, 0);
        }

        public override string ToString()
        {
            return $"ItemCode: {ItemCode}, Amount: {Amount}";
        }

        public bool TryTransferToResourceAmount(out TransferResourceAmount tra)
        {
            tra = new TransferResourceAmount();
            if (!ItemIDUtility.TryGetResourceId(ItemCode, out var id))
                return false;
            
            tra.ResourceId = id;
            tra.Amount = Amount;
            return true;
        }
    }

    /// <summary>
    /// 贸易运输方式
    /// </summary>
    public enum TradeTransportMode
    {
        None = 0,
        Human = 1,
        Cart = 2,
        Sea = 3
    }

    /// <summary>
    /// 贸易点类型
    /// </summary>
    public enum TradeEndpointType
    {
        None = 0,
        City = 1,
        Port = 2,
        Warehouse = 3,
        Market = 4
    }

    /// <summary>
    /// 贸易点定义
    /// 对应你原来的 TradeUnitDefinition，但语义更准确。
    /// </summary>
    [Serializable]
    public class TradeEndpointDefinition
    {
        /// <summary>
        /// 贸易点唯一ID
        /// </summary>
        public int EndpointId;

        /// <summary>
        /// 贸易点名称
        /// </summary>
        public string EndpointName;

        /// <summary>
        /// 贸易点类型（城市、港口、仓库等）
        /// </summary>
        public TradeEndpointType EndpointType = TradeEndpointType.City;

        /// <summary>
        /// 该贸易点绑定的存储
        /// </summary>
        public Storage Storage;

        /// <summary>
        /// 是否允许作为出口点
        /// </summary>
        public bool CanExport = true;

        /// <summary>
        /// 是否允许作为进口点
        /// </summary>
        public bool CanImport = true;

        /// <summary>
        /// 该贸易点支持的运输方式
        /// </summary>
        public bool AllowHuman = true;
        public bool AllowCart = true;
        public bool AllowSea = false;

        public TradeEndpointDefinition()
        {
        }

        public TradeEndpointDefinition(
            int endpointId,
            string endpointName,
            Storage storage,
            TradeEndpointType endpointType = TradeEndpointType.City,
            bool canExport = true,
            bool canImport = true,
            bool allowHuman = true,
            bool allowCart = true,
            bool allowSea = false)
        {
           Init(endpointId, endpointName, storage, endpointType, canExport, canImport, allowHuman, allowCart, allowSea);
        }

        public void Init(int endpointId,
            string endpointName,
            Storage storage,
            TradeEndpointType endpointType = TradeEndpointType.City,
            bool canExport = true,
            bool canImport = true,
            bool allowHuman = true,
            bool allowCart = true,
            bool allowSea = false)
        {
            EndpointId = endpointId;
            EndpointName = endpointName;
            Storage = storage;
            EndpointType = endpointType;
            CanExport = canExport;
            CanImport = canImport;
            AllowHuman = allowHuman;
            AllowCart = allowCart;
            AllowSea = allowSea;
        }

        public bool SupportsTransportMode(TradeTransportMode mode)
        {
            switch (mode)
            {
                case TradeTransportMode.Human:
                    return AllowHuman;
                case TradeTransportMode.Cart:
                    return AllowCart;
                case TradeTransportMode.Sea:
                    return AllowSea;
                default:
                    return false;
            }
        }

        public bool IsValid()
        {
            return EndpointId > 0
                && !string.IsNullOrEmpty(EndpointName)
                && Storage != null;
        }

        public override string ToString()
        {
            return $"EndpointId: {EndpointId}, Name: {EndpointName}, Type: {EndpointType}";
        }
    }

    /// <summary>
    /// 贸易路线定义
    /// 只描述“哪两个点之间有一条什么样的路线”
    /// </summary>
    [Serializable]
    public class TradeRouteDefinition
    {
        /// <summary>
        /// 路线唯一ID
        /// </summary>
        public int RouteId;

        /// <summary>
        /// 路线名称
        /// </summary>
        public string RouteName;

        /// <summary>
        /// 起点贸易点ID
        /// </summary>
        public int FromEndpointId;

        /// <summary>
        /// 终点贸易点ID
        /// </summary>
        public int ToEndpointId;

        /// <summary>
        /// 是否双向可用
        /// </summary>
        public bool IsBidirectional = false;

        /// <summary>
        /// 是否允许人力运输
        /// </summary>
        public bool AllowHuman = true;

        /// <summary>
        /// 是否允许马车运输
        /// </summary>
        public bool AllowCart = true;

        /// <summary>
        /// 是否允许海运
        /// </summary>
        public bool AllowSea = false;

        public TradeRouteDefinition()
        {
        }

        public TradeRouteDefinition(
            int routeId,
            string routeName,
            int fromEndpointId,
            int toEndpointId,
            bool isBidirectional = false,
            bool allowHuman = true,
            bool allowCart = true,
            bool allowSea = false)
        {
            Init(routeId, routeName, fromEndpointId, toEndpointId, isBidirectional, allowHuman, allowCart, allowSea);
        }

        public void Init(int routeId,
            string routeName,
            int fromEndpointId,
            int toEndpointId,
            bool isBidirectional = false,
            bool allowHuman = true,
            bool allowCart = true,
            bool allowSea = false)
        {
            RouteId = routeId;
            RouteName = routeName;
            FromEndpointId = fromEndpointId;
            ToEndpointId = toEndpointId;
            IsBidirectional = isBidirectional;
            AllowHuman = allowHuman;
            AllowCart = allowCart;
            AllowSea = allowSea;
        }

        public bool SupportsTransportMode(TradeTransportMode mode)
        {
            switch (mode)
            {
                case TradeTransportMode.Human:
                    return AllowHuman;
                case TradeTransportMode.Cart:
                    return AllowCart;
                case TradeTransportMode.Sea:
                    return AllowSea;
                default:
                    return false;
            }
        }

        public bool MatchesEndpoints(int fromEndpointId, int toEndpointId)
        {
            if (FromEndpointId == fromEndpointId && ToEndpointId == toEndpointId)
            {
                return true;
            }

            if (IsBidirectional
                && FromEndpointId == toEndpointId
                && ToEndpointId == fromEndpointId)
            {
                return true;
            }

            return false;
        }

        public bool IsValid()
        {
            return RouteId > 0
                && !string.IsNullOrEmpty(RouteName)
                && FromEndpointId > 0
                && ToEndpointId > 0
                && FromEndpointId != ToEndpointId;
        }

        public override string ToString()
        {
            return $"RouteId: {RouteId}, Name: {RouteName}, FromEndpointId: {FromEndpointId}, ToEndpointId: {ToEndpointId}";
        }
    }

    /// <summary>
    /// 一条贸易提案 / 交易配方定义
    /// 只描述“拿什么换什么”
    /// 不再绑定路线、运输方式、起终点。
    /// </summary>
    [Serializable]
    public class TradeOfferDefinition
    {
        /// <summary>
        /// 贸易提案唯一ID
        /// </summary>
        public int OfferId;

        /// <summary>
        /// 贸易提案名称
        /// </summary>
        public string OfferName;

        /// <summary>
        /// 付出的资源池
        /// </summary>
        public List<TradeResourceStack> CostResources = new List<TradeResourceStack>();

        /// <summary>
        /// 获得的资源池
        /// </summary>
        public List<TradeResourceStack> RewardResources = new List<TradeResourceStack>();

        public TradeOfferDefinition()
        {
        }

        public TradeOfferDefinition(
            int offerId,
            string offerName,
            List<TradeResourceStack> costResources,
            List<TradeResourceStack> rewardResources)
        {
            Init(offerId, offerName, costResources, rewardResources);
        }

        public void Init(int offerId,
            string offerName,
            List<TradeResourceStack> costResources,
            List<TradeResourceStack> rewardResources)
        {
            OfferId = offerId;
            OfferName = offerName;
            CostResources = costResources ?? new List<TradeResourceStack>();
            RewardResources = rewardResources ?? new List<TradeResourceStack>();
        }

        public bool IsValid()
        {
            return OfferId > 0
                && !string.IsNullOrEmpty(OfferName)
                && HasValidResourceList(CostResources)
                && HasValidResourceList(RewardResources);
        }

        public bool HasCost()
        {
            return HasValidResourceList(CostResources);
        }

        public bool HasReward()
        {
            return HasValidResourceList(RewardResources);
        }

        private bool HasValidResourceList(List<TradeResourceStack> list)
        {
            if (list == null || list.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsValid())
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString()
        {
            return $"OfferId: {OfferId}, Name: {OfferName}";
        }
    }

    /// <summary>
    /// 一次贸易执行的数据上下文
    /// 注意：这里只存数据，不写执行逻辑。
    /// 真正执行由你现有的 TradeTask 脚本负责。
    /// </summary>
    [Serializable]
    public class TradeDataContext
    {
        /// <summary>
        /// 本次上下文ID（可选）
        /// </summary>
        public int ContextId;

        /// <summary>
        /// 本次执行使用的贸易配方
        /// </summary>
        public TradeOfferDefinition OfferDef;

        /// <summary>
        /// 本次执行使用的路线
        /// </summary>
        public TradeRouteDefinition RouteDef;

        /// <summary>
        /// 本次执行的起点
        /// </summary>
        public TradeEndpointDefinition FromEndpoint;

        /// <summary>
        /// 本次执行的终点
        /// </summary>
        public TradeEndpointDefinition ToEndpoint;

        /// <summary>
        /// 本次执行使用的运输方式
        /// </summary>
        public TradeTransportMode TransportMode = TradeTransportMode.None;

        /// <summary>
        /// 实际移动 / 寻路单位
        /// 沿用你现有的运行时类
        /// </summary>
        public TradeMoveUnit MoveUnit;



        public TradeDataContext(
            int contextId,
            TradeOfferDefinition offerDef,
            TradeRouteDefinition routeDef,
            TradeEndpointDefinition fromEndpoint,
            TradeEndpointDefinition toEndpoint,
            TradeTransportMode transportMode,
            TradeMoveUnit moveUnit)
        {
            ContextId = contextId;
            OfferDef = offerDef;
            RouteDef = routeDef;
            FromEndpoint = fromEndpoint;
            ToEndpoint = toEndpoint;
            TransportMode = transportMode;
            MoveUnit = moveUnit;
        }

        public bool IsValid()
        {
            if (OfferDef == null || !OfferDef.IsValid())
            {
                return false;
            }

            if (RouteDef == null || !RouteDef.IsValid())
            {
                return false;
            }

            if (FromEndpoint == null || !FromEndpoint.IsValid())
            {
                return false;
            }

            if (ToEndpoint == null || !ToEndpoint.IsValid())
            {
                return false;
            }

            if (TransportMode == TradeTransportMode.None)
            {
                return false;
            }

            if (!RouteDef.MatchesEndpoints(FromEndpoint.EndpointId, ToEndpoint.EndpointId))
            {
                return false;
            }

            if (!RouteDef.SupportsTransportMode(TransportMode))
            {
                return false;
            }

            if (!FromEndpoint.SupportsTransportMode(TransportMode))
            {
                return false;
            }

            if (!ToEndpoint.SupportsTransportMode(TransportMode))
            {
                return false;
            }

            if (!FromEndpoint.CanExport)
            {
                return false;
            }

            if (!ToEndpoint.CanImport)
            {
                return false;
            }

            if (MoveUnit == null)
            {
                return false;
            }

            return true;
        }

        public override string ToString()
        {
            return $"ContextId: {ContextId}, Offer: {OfferDef?.OfferName}, Route: {RouteDef?.RouteName}, From: {FromEndpoint?.EndpointName}, To: {ToEndpoint?.EndpointName}, Mode: {TransportMode}";
        }
    }
}