using System.Diagnostics;

namespace TradingBotV2.Broker.Orders
{
    public enum OrderAction
    {
        BUY, SELL
    }

    public enum OcaType
    {
        NONE = 0,
        CANCEL_WITH_BLOCK = 1,
        REDUCE_WITH_BLOCK = 2,
        REDUCE_NON_BLOCK = 3,
    }

    internal class RequestInfo
    {
        public int OrderId { get; set; } = -1;
        public int ClientId { get; set; }
        public int ParentId { get; set; }
        public int PermId { get; set; }
        public bool Transmit { get; set; } = true; // if false, order will be created but not transmitted
        public string OcaGroup { get; set; }
        public OcaType OcaType { get; set; }
    }

    // TODO : to investigate/implement : 
    // Order conditioning
    // order algos
    // "One cancels all" groups
    public abstract class Order
    {
        internal RequestInfo Info { get; set; } = new RequestInfo();
        internal int Id
        {
            get => Info.OrderId;
            set => Info.OrderId = value;
        }

        public readonly string OrderType;
        protected Order(string orderType) => OrderType = orderType;
        public OrderAction Action { get; set; }
        public double TotalQuantity { get; set; }

        public override bool Equals(object obj)
        {
            Debug.Assert(Id > 0);
            var other = obj as Order;
            return other != null && other.Id == Id;
        }

        public override int GetHashCode()
        {
            Debug.Assert(Id > 0);
            return Id.GetHashCode();
        }

        public override string ToString()
        {
            return $"[{Id}] : {Action} {TotalQuantity} {OrderType}";
        }

        public static explicit operator Order(IBApi.Order ibo)
        {
            switch (ibo.OrderType)
            {
                case "MKT":
                    return (MarketOrder)ibo;

                case "LMT":
                    return (LimitOrder)ibo;

                case "STP":
                    return (StopOrder)ibo;

                case "TRAIL":
                    return (TrailingStopOrder)ibo;

                case "MIT":
                    return (MarketIfTouchedOrder)ibo;

                default:
                    throw new NotImplementedException($"{ibo.OrderType}");
            }
        }

        public static explicit operator IBApi.Order(Order order)
        {
            if (order is MarketOrder mo)
                return (IBApi.Order)mo;
            else if (order is MarketIfTouchedOrder mit)
                return (IBApi.Order)mit;
            else if (order is LimitOrder lo)
                return (IBApi.Order)lo;
            else if (order is StopOrder so)
                return (IBApi.Order)so;
            else if (order is TrailingStopOrder tso)
                return (IBApi.Order)tso;
            else
                throw new NotImplementedException($"{order.OrderType}");
        }
    }

    public class MarketOrder : Order
    {
        public MarketOrder() : base("MKT") { }

        public static explicit operator IBApi.Order(MarketOrder order)
        {
            return new IBApi.Order()
            {
                OrderType = order.OrderType,
                Action = order.Action.ToString(),
                TotalQuantity = order.TotalQuantity,

                OrderId = order.Id,
                ClientId = order.Info.ClientId,
                ParentId = order.Info.ParentId,
                PermId = order.Info.PermId,
                Transmit = order.Info.Transmit,
                OcaGroup = order.Info.OcaGroup,
                OcaType = (int)order.Info.OcaType,

                OutsideRth = false,
                Tif = "DAY"
            };
        }

        public static explicit operator MarketOrder(IBApi.Order ibo)
        {
            return new MarketOrder()
            {
                Action = Enum.Parse<OrderAction>(ibo.Action),
                TotalQuantity = ibo.TotalQuantity,
                Id = ibo.OrderId,
                Info = new RequestInfo()
                {
                    ClientId = ibo.ClientId,
                    Transmit = ibo.Transmit,
                    ParentId = ibo.ParentId,
                    PermId = ibo.PermId,
                    OcaGroup = ibo.OcaGroup,
                    OcaType = (OcaType)ibo.OcaType,
                },
            };
        }
    }

    public class MarketIfTouchedOrder : Order
    {
        public MarketIfTouchedOrder() : base("MIT") { }
        public double TouchPrice { get; set; }

        public override string ToString()
        {
            return $"{base.ToString()} Touch price : {TouchPrice:c}";
        }

        public static explicit operator IBApi.Order(MarketIfTouchedOrder order)
        {
            return new IBApi.Order()
            {
                OrderType = order.OrderType,
                Action = order.Action.ToString(),
                TotalQuantity = order.TotalQuantity,

                OrderId = order.Id,
                ClientId = order.Info.ClientId,
                ParentId = order.Info.ParentId,
                PermId = order.Info.PermId,
                Transmit = order.Info.Transmit,
                OcaGroup = order.Info.OcaGroup,
                OcaType = (int)order.Info.OcaType,

                OutsideRth = false,
                Tif = "DAY",

                AuxPrice = order.TouchPrice,
            };
        }

        public static explicit operator MarketIfTouchedOrder(IBApi.Order ibo)
        {
            return new MarketIfTouchedOrder()
            {
                Action = Enum.Parse<OrderAction>(ibo.Action),
                TotalQuantity = ibo.TotalQuantity,
                Id = ibo.OrderId,
                Info = new RequestInfo()
                {
                    ClientId = ibo.ClientId,
                    Transmit = ibo.Transmit,
                    ParentId = ibo.ParentId,
                    PermId = ibo.PermId,
                    OcaGroup = ibo.OcaGroup,
                    OcaType = (OcaType)ibo.OcaType,
                },

                TouchPrice = ibo.AuxPrice,
            };
        }
    }

    public class LimitOrder : Order
    {
        public LimitOrder() : base("LMT") { }
        public double LmtPrice { get; set; }

        public override string ToString()
        {
            return $"{base.ToString()} Limit price : {LmtPrice:c}";
        }

        public static explicit operator IBApi.Order(LimitOrder order)
        {
            return new IBApi.Order()
            {
                OrderType = order.OrderType,
                Action = order.Action.ToString(),
                TotalQuantity = order.TotalQuantity,

                OrderId = order.Id,
                ClientId = order.Info.ClientId,
                ParentId = order.Info.ParentId,
                PermId = order.Info.PermId,
                Transmit = order.Info.Transmit,
                OcaGroup = order.Info.OcaGroup,
                OcaType = (int)order.Info.OcaType,

                OutsideRth = false,
                Tif = "DAY",

                LmtPrice = order.LmtPrice,
            };
        }

        public static explicit operator LimitOrder(IBApi.Order ibo)
        {
            return new LimitOrder()
            {
                Action = Enum.Parse<OrderAction>(ibo.Action),
                TotalQuantity = ibo.TotalQuantity,
                Id = ibo.OrderId,
                Info = new RequestInfo()
                {
                    ClientId = ibo.ClientId,
                    Transmit = ibo.Transmit,
                    ParentId = ibo.ParentId,
                    PermId = ibo.PermId,
                    OcaGroup = ibo.OcaGroup,
                    OcaType = (OcaType)ibo.OcaType,
                },

                LmtPrice = ibo.LmtPrice,
            };
        }
    }

    public class StopOrder : Order
    {
        public StopOrder() : base("STP") { }
        public double StopPrice { get; set; }

        public override string ToString()
        {
            return $"{base.ToString()} Stop price : {StopPrice:c}";
        }

        public static explicit operator IBApi.Order(StopOrder order)
        {
            return new IBApi.Order()
            {
                OrderType = order.OrderType,
                Action = order.Action.ToString(),
                TotalQuantity = order.TotalQuantity,

                OrderId = order.Id,
                ClientId = order.Info.ClientId,
                ParentId = order.Info.ParentId,
                PermId = order.Info.PermId,
                Transmit = order.Info.Transmit,
                OcaGroup = order.Info.OcaGroup,
                OcaType = (int)order.Info.OcaType,

                OutsideRth = false,
                Tif = "DAY",

                AuxPrice = order.StopPrice,
            };
        }

        public static explicit operator StopOrder(IBApi.Order ibo)
        {
            return new StopOrder()
            {
                Action = Enum.Parse<OrderAction>(ibo.Action),
                TotalQuantity = ibo.TotalQuantity,
                Id = ibo.OrderId,
                Info = new RequestInfo()
                {
                    ClientId = ibo.ClientId,
                    Transmit = ibo.Transmit,
                    ParentId = ibo.ParentId,
                    PermId = ibo.PermId,
                    OcaGroup = ibo.OcaGroup,
                    OcaType = (OcaType)ibo.OcaType,
                },

                StopPrice = ibo.AuxPrice,
        };
        }
    }

    public class TrailingStopOrder : Order
    {
        public TrailingStopOrder() : base("TRAIL") { }
        public double StopPrice { get; set; } = double.MaxValue;

        public double TrailingAmount { get; set; } = double.MaxValue;
        // Takes priority over TrailingAmount if set
        public double TrailingPercent { get; set; } = double.MaxValue;

        public override string ToString()
        {
            if (TrailingPercent != double.MaxValue)
                return $"{base.ToString()} TrailingPercent : {TrailingPercent}";
            else if (TrailingAmount != double.MaxValue)
                return $"{base.ToString()} TrailingAmount : {TrailingAmount:c}";
            else
                return base.ToString();
        }

        public static explicit operator IBApi.Order(TrailingStopOrder order)
        {
            return new IBApi.Order()
            {
                OrderType = order.OrderType,
                Action = order.Action.ToString(),
                TotalQuantity = order.TotalQuantity,

                OrderId = order.Id,
                ClientId = order.Info.ClientId,
                ParentId = order.Info.ParentId,
                PermId = order.Info.PermId,
                Transmit = order.Info.Transmit,
                OcaGroup = order.Info.OcaGroup,
                OcaType = (int)order.Info.OcaType,

                OutsideRth = false,
                Tif = "DAY",

                AuxPrice = order.StopPrice,
                TrailingPercent = order.TrailingPercent,
            };
        }

        public static explicit operator TrailingStopOrder(IBApi.Order ibo)
        {
            return new TrailingStopOrder()
            {
                Action = Enum.Parse<OrderAction>(ibo.Action),
                TotalQuantity = ibo.TotalQuantity,
                Id = ibo.OrderId,
                Info = new RequestInfo()
                {
                    ClientId = ibo.ClientId,
                    Transmit = ibo.Transmit,
                    ParentId = ibo.ParentId,
                    PermId = ibo.PermId,
                    OcaGroup = ibo.OcaGroup,
                    OcaType = (OcaType)ibo.OcaType,
                },

                StopPrice = ibo.AuxPrice,
                TrailingPercent = ibo.TrailingPercent,
            };
        }
    }
}
