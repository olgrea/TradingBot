using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TradingBot.Broker;

namespace TradingBot.Utils
{
    public static class Extensions
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
                ClientId = order.ClientId, 
                Action = order.Action.ToString(),
                TotalQuantity = order.TotalQuantity,
                OcaGroup = order.OcaGroup, 
                OcaType = (int)order.OcaType,
                Transmit = order.Transmit,
                OutsideRth = true,
                Tif = "DAY"
            };

            switch(order.OrderType)
            {
                case "MKT": break;
                case "LMT":
                    ibo.LmtPrice = Convert.ToDouble((order as Limit).LmtPrice);
                    break;

                case "STP": 
                    ibo.AuxPrice = Convert.ToDouble((order as Stop).StopPrice);
                    break;

                case "TRAIL": 
                    ibo.AuxPrice = Convert.ToDouble((order as TrailingStop).StopPrice);
                    ibo.TrailingPercent = (order as TrailingStop).TrailingPercent;
                    break;

                case "MIT": 
                    ibo.AuxPrice = Convert.ToDouble((order as MarketIfTouched).TouchPrice);
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
                    tbo = new Market();
                    break;

                case "LMT":
                    tbo = new Limit() { LmtPrice = Convert.ToDecimal(ibo.LmtPrice) };
                    break;

                case "STP":
                    tbo = new Stop() { StopPrice = Convert.ToDecimal(ibo.AuxPrice) };
                    break;

                case "TRAIL":
                    tbo = new TrailingStop() 
                    {
                        StopPrice = Convert.ToDecimal(ibo.AuxPrice) ,
                        TrailingPercent = ibo.TrailingPercent,
                    };
                    break;

                case "MIT":
                    tbo = new MarketIfTouched() { TouchPrice = Convert.ToDecimal(ibo.AuxPrice) };
                    break;

                default:
                    throw new NotImplementedException($"{ibo.OrderType}");
            }

            tbo.ClientId = ibo.ClientId;
            tbo.Action = Enum.Parse<OrderAction>(ibo.Action);
            tbo.TotalQuantity = ibo.TotalQuantity;
            tbo.OcaGroup = ibo.OcaGroup;
            tbo.OcaType = (OcaType)ibo.OcaType;
            tbo.Transmit = ibo.Transmit;

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
