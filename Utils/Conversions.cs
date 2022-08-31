using System;
using System.Globalization;
using TradingBot.Broker;
using TradingBot.Broker.Orders;

namespace TradingBot.Utils
{
    public static class Conversions
    {
        public static Contract ToTBContract(this IBApi.Contract ibc)
        {
            Contract contract = null;

            switch (ibc.SecType)
            {
                case "STK":
                    contract = new Stock()
                    {
                        LastTradeDate = ibc.LastTradeDateOrContractMonth
                    };
                    break;

                case "OPT":
                    contract = new Option()
                    {
                        ContractMonth = ibc.LastTradeDateOrContractMonth,
                        Strike = Convert.ToDecimal(ibc.Strike),
                        Multiplier = Decimal.Parse(ibc.Multiplier, CultureInfo.InvariantCulture),
                        OptionType = (ibc.Right == "C" || ibc.Right == "CALL") ? OptionType.Call : OptionType.Put,
                    };
                    break;

                case "CASH":
                    contract = new Cash();
                    break;

                default:
                    throw new NotSupportedException($"This type of contract is not supported : {ibc.SecType}");
            }

            contract.Id = ibc.ConId;
            contract.Currency = ibc.Currency;
            contract.Exchange = ibc.Exchange ?? ibc.PrimaryExch;
            contract.Symbol = ibc.Symbol;

            return contract;
        }

        public static IBApi.Contract ToIBApiContract(this Contract contract)
        {
            return new IBApi.Contract()
            {
                ConId = contract.Id,
                Currency = contract.Currency,
                SecType = contract.SecType,
                Symbol = contract.Symbol,
                Exchange = contract.Exchange,
            };
        }

        public static IBApi.Order ToIBApiOrder(this Order order)
        {
            var ibo = new IBApi.Order()
            {
                OrderType = order.OrderType,
                Action = order.Action.ToString(),
                TotalQuantity = order.TotalQuantity,

                OrderId = order.Request.OrderId,
                ClientId = order.Request.ClientId,
                ParentId = order.Request.ParentId,
                PermId = order.Request.PermId,
                Transmit = order.Request.Transmit,
                OcaGroup = order.Request.OcaGroup, 
                OcaType = (int)order.Request.OcaType,

                OutsideRth = true,
                Tif = "DAY"
            };

            switch(order.OrderType)
            {
                case "MKT": break;
                case "LMT":
                    ibo.LmtPrice = Convert.ToDouble((order as LimitOrder).LmtPrice);
                    break;

                case "STP": 
                    ibo.AuxPrice = Convert.ToDouble((order as StopOrder).StopPrice);
                    break;

                case "TRAIL": 
                    ibo.AuxPrice = Convert.ToDouble((order as TrailingStopOrder).StopPrice);
                    ibo.TrailingPercent = (order as TrailingStopOrder).TrailingPercent;
                    break;

                case "MIT": 
                    ibo.AuxPrice = Convert.ToDouble((order as MarketIfTouchedOrder).TouchPrice);
                    break;
                
                default:
                    throw new NotImplementedException($"{order.OrderType}");
            }

            return ibo;
        }
        
        public static Order ToTBOrder(this IBApi.Order ibo)
        {
            Order tbo;

            switch (ibo.OrderType)
            {
                case "MKT": 
                    tbo = new MarketOrder();
                    break;

                case "LMT":
                    tbo = new LimitOrder() { LmtPrice = Convert.ToDecimal(ibo.LmtPrice) };
                    break;

                case "STP":
                    tbo = new StopOrder() { StopPrice = Convert.ToDecimal(ibo.AuxPrice) };
                    break;

                case "TRAIL":
                    tbo = new TrailingStopOrder() 
                    {
                        StopPrice = Convert.ToDecimal(ibo.AuxPrice) ,
                        TrailingPercent = ibo.TrailingPercent,
                    };
                    break;

                case "MIT":
                    tbo = new MarketIfTouchedOrder() { TouchPrice = Convert.ToDecimal(ibo.AuxPrice) };
                    break;

                default:
                    throw new NotImplementedException($"{ibo.OrderType}");
            }

            tbo.Action = Enum.Parse<OrderAction>(ibo.Action);
            tbo.TotalQuantity = ibo.TotalQuantity;

            tbo.Request.OrderId = ibo.OrderId;
            tbo.Request.ClientId = ibo.ClientId;
            tbo.Request.Transmit = ibo.Transmit;
            tbo.Request.ParentId = ibo.ParentId;
            tbo.Request.PermId = ibo.PermId;
            tbo.Request.OcaGroup = ibo.OcaGroup;
            tbo.Request.OcaType = (OcaType)ibo.OcaType;

            return tbo;
        }

        public static OrderState ToTBOrderState(this IBApi.OrderState ibo)
        {
            return new OrderState()
            {
                Status = Enum.Parse<Status>(ibo.Status),
                WarningText = ibo.WarningText,
                CompletedStatus = ibo.CompletedStatus,
                CompletedTime = ibo.CompletedTime != null ? DateTime.Parse(ibo.CompletedTime) : DateTime.MinValue,
            };
        }
    }
}
