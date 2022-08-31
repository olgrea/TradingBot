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
                OcaGroup = order.OcaGroup, 
                OcaType = (int)order.OcaType,
                OutsideRth = true,
                Tif = "DAY"
            };

            if(order.OrderRequest != null)
            {
                ibo.OrderId = order.OrderRequest.OrderId;
                ibo.ClientId = order.OrderRequest.ClientId;
                ibo.ParentId = order.OrderRequest.ParentId;
                ibo.PermId = order.OrderRequest.PermId;
                ibo.Transmit = order.OrderRequest.Transmit;
            }

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

            tbo.OrderRequest = new OrderRequest()
            {
                OrderId = ibo.OrderId,
                ClientId = ibo.ClientId,
                Transmit = ibo.Transmit,
                ParentId = ibo.ParentId,
                PermId = ibo.PermId,
            };
            
            tbo.Action = Enum.Parse<OrderAction>(ibo.Action);
            tbo.TotalQuantity = ibo.TotalQuantity;
            tbo.OcaGroup = ibo.OcaGroup;
            tbo.OcaType = (OcaType)ibo.OcaType;

            return tbo;
        }

        public static OrderState ToTBOrderStatus(this IBApi.OrderState ibo)
        {
            return new OrderState()
            {
                Status = Enum.Parse<OrderStatus>(ibo.Status),
                Commission = Convert.ToDecimal(ibo.Commission),
                MinCommission = Convert.ToDecimal(ibo.MinCommission),
                MaxCommission = Convert.ToDecimal(ibo.MaxCommission),
                CommissionCurrency = ibo.CommissionCurrency,
                WarningText = ibo.WarningText,
                CompletedStatus = ibo.CompletedStatus,
                CompletedTime = DateTime.Parse(ibo.CompletedTime),
            };
        }
    }
}
