using System.Diagnostics;
using System.Security.Cryptography;
using Broker.Utils;
using IBApi;

namespace Broker.IBKR.Orders
{
    public enum OrderAction
    {
        BUY, SELL
    }

    public enum OrderType
    {
        MKT,
        LMT,
        MIT,
        STP,
        REL,
        TRAIL,
    }

    public enum TimeInForce
    {
        DAY,
        GTC,
        IOC,
        GTD,
        OPG,
        FOK,
        DTC,
    }

    internal class RequestInfo
    {
        public int OrderId { get; set; } = -1;
        public int ClientId { get; set; }
        public int ParentId { get; set; }
        public int PermId { get; set; }
        public bool Transmit { get; set; } = true; // if false, order will be created but not transmitted
        public TimeInForce TimeInForce { get; set; } = TimeInForce.DAY;
        public bool ExtendedHours { get; set; }
    }

    public enum AdaptiveAlgorithmPriority
    {
        Urgent, Normal, Patient,
    }

    // TODO : to investigate/implement : 
    // Order conditioning
    public abstract class Order
    {
        class IBAlgorithm
        {
            public string? Id { get; set; }
            public string? Strategy { get; set; }
            public List<TagValue> Params { get; set; } = new List<TagValue>();
        }

        protected Order() { }
        protected Order(IBApi.Order ibo)
        {
            Action = Enum.Parse<OrderAction>(ibo.Action);
            TotalQuantity = Convert.ToDouble(ibo.TotalQuantity);
            Info = new RequestInfo()
            {
                OrderId = ibo.OrderId,
                ClientId = ibo.ClientId,
                Transmit = ibo.Transmit,
                ParentId = ibo.ParentId,
                PermId = ibo.PermId,
                TimeInForce = Enum.Parse<TimeInForce>(ibo.Tif),
                ExtendedHours = ibo.OutsideRth,
            };

            Algorithm.Id = ibo.AlgoId;
            Algorithm.Strategy = ibo.AlgoStrategy;
            Algorithm.Params = ibo.AlgoParams;

            OrderConditions = ibo.Conditions;
            ConditionsTriggerOrderCancellation = ibo.ConditionsCancelOrder;

            OrderType type = Enum.Parse<OrderType>(ibo.OrderType.Replace(' ', '_'));
            if (OrderType != type)
                throw new ArgumentException($"IBKR order is of type {type} but needs to be {OrderType}");
        }

        internal RequestInfo Info { get; set; } = new RequestInfo();
        IBAlgorithm Algorithm { get; set; } = new IBAlgorithm();

        // TODO : create wrapper for OrderCondition?
        internal List<OrderCondition> OrderConditions { get; set; } = new List<OrderCondition>();
        public bool ConditionsTriggerOrderCancellation { get; set; } = false;
        internal bool NeedsConditionFulfillmentToBeOpened => OrderConditions.Any() && !ConditionsTriggerOrderCancellation;
        internal bool ConditionsFulfilled { get; set; }

        internal int Id
        {
            get => Info.OrderId;
            set => Info.OrderId = value;
        }

        public abstract OrderType OrderType { get; }
        public OrderAction Action { get; set; }
        public double TotalQuantity { get; set; }

        public override bool Equals(object? obj)
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

        protected void SetAdaptiveAlgoParams(AdaptiveAlgorithmPriority priority)
        {
            Algorithm.Strategy = "Adaptive";
            Algorithm.Params = new List<TagValue>() { new TagValue("adaptivePriority", priority.ToString()) };
        }

        public void AddPriceCondition(bool isMore, double price, bool isConjunction = true)
        {
            var cond = (PriceCondition)OrderCondition.Create(OrderConditionType.Price);
            cond.IsMore = isMore;
            cond.Price = price;
            cond.IsConjunctionConnection = isConjunction;
            AddCondition(cond);
        }

        public void AddPercentCondition(bool isMore, double percent, bool isConjunction = true)
        {
            var cond = (PercentChangeCondition)OrderCondition.Create(OrderConditionType.PercentCange);
            cond.IsMore = isMore;
            cond.ChangePercent = percent;
            cond.IsConjunctionConnection = isConjunction;
            AddCondition(cond);
        }

        public void AddTimeCondition(bool isMore, DateTime time, bool isConjunction = true)
        {
            var cond = (TimeCondition)OrderCondition.Create(OrderConditionType.Time);
            cond.IsMore = isMore;
            cond.Time = time.ToString(MarketDataUtils.TWSTimeFormat);
            cond.IsConjunctionConnection = isConjunction;
            AddCondition(cond);
        }

        void AddCondition(OrderCondition cond)
        {
            throw new NotImplementedException("Feature not working correctly. Disabling it for now.");

            //OrderConditions.Add(cond);

            ////TODO : need to check if I can use this flag when using conditions 
            //if (NeedsConditionFulfillmentToBeOpened)
            //    Info.Transmit = false;
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

                case "REL":
                    return (MarketIfTouchedOrder)ibo;

                default:
                    throw new NotImplementedException($"{ibo.OrderType}");
            }
        }

        protected virtual IBApi.Order ToIBApi()
        {
            return new IBApi.Order()
            {
                OrderType = OrderType.ToString(),
                Action = Action.ToString(),
                TotalQuantity = Convert.ToDecimal(TotalQuantity),

                OrderId = Id,
                ClientId = Info.ClientId,
                ParentId = Info.ParentId,
                PermId = Info.PermId,
                Transmit = Info.Transmit,

                OutsideRth = Info.ExtendedHours,
                Tif = Info.TimeInForce.ToString(),

                AlgoId = Algorithm.Id,
                AlgoStrategy = Algorithm.Strategy,
                AlgoParams = Algorithm.Params,

                Conditions = OrderConditions,
                ConditionsCancelOrder = ConditionsTriggerOrderCancellation,
            };
        }

        public static explicit operator IBApi.Order(Order order)
        {
            return order.ToIBApi();
        }
    }
    public class MarketOnOpen : MarketOrder
    {
        public MarketOnOpen()
        {
            Info.TimeInForce = TimeInForce.OPG;
        }

        public MarketOnOpen(IBApi.Order ibo) : base(ibo)
        {
            Info.TimeInForce = TimeInForce.OPG;
        }
    }

    public class MarketOrder : Order
    {
        public MarketOrder() { }
        public MarketOrder(IBApi.Order ibo) : base(ibo) { }

        public override OrderType OrderType => OrderType.MKT;

        public void AsAdaptiveAlgo(AdaptiveAlgorithmPriority priority)
        {
            SetAdaptiveAlgoParams(priority);
        }

        public static explicit operator IBApi.Order(MarketOrder order) => (IBApi.Order)(order as Order);

        public static explicit operator MarketOrder(IBApi.Order ibo) => new MarketOrder(ibo);
    }

    public class MarketIfTouchedOrder : Order
    {
        public MarketIfTouchedOrder() { }
        public MarketIfTouchedOrder(IBApi.Order ibo) : base(ibo)
        {
            TouchPrice = ibo.AuxPrice;
        }

        public double TouchPrice { get; set; }

        public override OrderType OrderType => OrderType.MIT;

        public override string ToString()
        {
            return $"{base.ToString()} Touch price : {TouchPrice:c}";
        }

        public static explicit operator IBApi.Order(MarketIfTouchedOrder order)
        {
            return order.ToIBApi();
        }

        protected override IBApi.Order ToIBApi()
        {
            var ibo = base.ToIBApi();
            ibo.AuxPrice = TouchPrice;
            return ibo;
        }

        public static explicit operator MarketIfTouchedOrder(IBApi.Order ibo) => new MarketIfTouchedOrder(ibo);
    }

    public class LimitOrder : Order
    {
        public LimitOrder() { }
        public LimitOrder(IBApi.Order ibo) : base(ibo)
        {
            LmtPrice = ibo.LmtPrice;
        }

        public double LmtPrice { get; set; }

        public override OrderType OrderType => OrderType.LMT;

        public void AsAdaptiveAlgo(AdaptiveAlgorithmPriority priority)
        {
            SetAdaptiveAlgoParams(priority);
        }

        public override string ToString()
        {
            return $"{base.ToString()} Limit price : {LmtPrice:c}";
        }

        public static explicit operator IBApi.Order(LimitOrder order)
        {
            return order.ToIBApi();
        }

        protected override IBApi.Order ToIBApi()
        {
            var ibo = base.ToIBApi();
            ibo.LmtPrice = LmtPrice;
            return ibo;
        }

        public static explicit operator LimitOrder(IBApi.Order ibo) => new LimitOrder(ibo);
    }

    public class StopOrder : Order
    {
        public StopOrder() { }
        public StopOrder(IBApi.Order ibo) : base(ibo)
        {
            StopPrice = ibo.AuxPrice;
        }
        public double StopPrice { get; set; }

        public override OrderType OrderType => OrderType.STP;

        public override string ToString()
        {
            return $"{base.ToString()} Stop price : {StopPrice:c}";
        }

        public static explicit operator IBApi.Order(StopOrder order)
        {
            return order.ToIBApi();
        }

        protected override IBApi.Order ToIBApi()
        {
            var ibo = base.ToIBApi();
            ibo.AuxPrice = StopPrice;
            return ibo;
        }

        public static explicit operator StopOrder(IBApi.Order ibo) => new StopOrder(ibo);
    }

    public enum TrailingAmountUnits
    {
        Absolute,
        Percent
    }

    public class TrailingStopOrder : Order
    {
        double? _trailingAmount;
        TrailingAmountUnits? _units;

        public TrailingStopOrder() { }
        public TrailingStopOrder(IBApi.Order ibo) : base(ibo)
        {
            StopPrice = ibo.TrailStopPrice;

            // TrailingPercent takes precedence apparently. See comments of IBApi.Order.TrailingPercent 
            if (ibo.TrailingPercent != double.MaxValue)
            {
                TrailingAmount = ibo.TrailingPercent;
                TrailingAmountUnits = Orders.TrailingAmountUnits.Percent;
            }
            else if (ibo.AuxPrice != double.MaxValue)
            {
                TrailingAmount = ibo.AuxPrice;
                TrailingAmountUnits = Orders.TrailingAmountUnits.Absolute;
            }
            else
                throw new ArgumentException($"Neither Trailing percent or trailing amount is set in IBKR order");
        }

        public double? StopPrice { get; set; }
        public double? TrailingAmount
        {
            get => _trailingAmount;
            set
            {
                if (value is not null && _units == Orders.TrailingAmountUnits.Percent)
                    value = Math.Clamp(value.Value, 0.0, 1.0);

                _trailingAmount = value;
            }
        }

        public TrailingAmountUnits? TrailingAmountUnits
        {
            get => _units;
            set
            {
                if (value is not null && value == Orders.TrailingAmountUnits.Percent && _trailingAmount is not null)
                    _trailingAmount = Math.Clamp(_trailingAmount.Value, 0.0, 1.0);

                _units = value;
            }
        }

        public override OrderType OrderType => OrderType.TRAIL;

        public override string ToString()
        {
            if (_units == Orders.TrailingAmountUnits.Percent)
                return $"{base.ToString()} TrailingAmount : {_trailingAmount * 100} %";
            else if (_units == Orders.TrailingAmountUnits.Absolute)
                return $"{base.ToString()} TrailingAmount : {TrailingAmount:c}";
            else
                return base.ToString();
        }

        public static explicit operator IBApi.Order(TrailingStopOrder order)
        {
            return order.ToIBApi();
        }

        protected override IBApi.Order ToIBApi()
        {
            var ibo = base.ToIBApi();
            ibo.TrailStopPrice = StopPrice is null ? double.MaxValue : StopPrice.Value;

            ArgumentNullException.ThrowIfNull(TrailingAmount, nameof(TrailingAmount));
            ArgumentNullException.ThrowIfNull(TrailingAmountUnits, nameof(TrailingAmountUnits));

            if (TrailingAmountUnits == Orders.TrailingAmountUnits.Absolute)
            {
                if (TrailingAmount.HasValue)
                    ibo.AuxPrice = TrailingAmount.Value;
            }
            else if (TrailingAmountUnits == Orders.TrailingAmountUnits.Percent)
            {
                if (TrailingAmount.HasValue)
                    ibo.TrailingPercent = TrailingAmount.Value;
            }
            else
                throw new NotImplementedException($"{TrailingAmountUnits}");

            return ibo;
        }

        public static explicit operator TrailingStopOrder(IBApi.Order ibo) => new TrailingStopOrder(ibo);
    }

    public class RelativeOrder : Order
    {
        public RelativeOrder() { }

        public RelativeOrder(IBApi.Order ibo) : base(ibo)
        {
            PriceCap = ibo.LmtPrice;
            OffsetAmount = ibo.AuxPrice;
        }

        // Optional if set to zero.
        public double PriceCap { get; set; } = 0.0;
        public double OffsetAmount { get; set; }
        internal double? CurrentPrice { get; set; }

        public override OrderType OrderType => OrderType.REL;

        public static explicit operator IBApi.Order(RelativeOrder order)
        {
            return order.ToIBApi();
        }

        protected override IBApi.Order ToIBApi()
        {
            var ibo = base.ToIBApi();
            ibo.LmtPrice = PriceCap;
            ibo.AuxPrice = OffsetAmount;
            return ibo;
        }

        public static explicit operator RelativeOrder(IBApi.Order ibo) => new RelativeOrder(ibo);
    }
}
