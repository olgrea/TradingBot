using System;
using System.Globalization;
using TradingBot.Broker.Orders;

namespace TradingBot.Broker.Client
{
    internal static class Conversions
    {
        internal static Contract ToTBContract(this IBApi.Contract ibc)
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
                        Strike = ibc.Strike,
                        Multiplier = double.Parse(ibc.Multiplier, CultureInfo.InvariantCulture),
                        OptionType = ibc.Right == "C" || ibc.Right == "CALL" ? OptionType.Call : OptionType.Put,
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

        internal static IBApi.Contract ToIBApiContract(this Contract contract)
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

        internal static IBApi.Order ToIBApiOrder(this Order order)
        {
            var ibo = new IBApi.Order()
            {
                OrderType = order.OrderType,
                Action = order.Action.ToString(),
                TotalQuantity = order.TotalQuantity,

                OrderId = order.Id,
                ClientId = order.RequestInfo.ClientId,
                ParentId = order.RequestInfo.ParentId,
                PermId = order.RequestInfo.PermId,
                Transmit = order.RequestInfo.Transmit,
                OcaGroup = order.RequestInfo.OcaGroup,
                OcaType = (int)order.RequestInfo.OcaType,

                OutsideRth = true,
                Tif = "DAY"
            };

            switch (order.OrderType)
            {
                case "MKT": break;
                case "LMT":
                    ibo.LmtPrice = Convert.ToDouble((order as LimitOrder).LmtPrice);
                    break;

                case "STP":
                    ibo.AuxPrice = Convert.ToDouble((order as StopOrder).StopPrice);
                    break;

                case "TRAIL":
                    // TODO : verify this
                    ibo.AuxPrice = Convert.ToDouble((order as TrailingStopOrder).StopPrice);
                    ibo.TrailStopPrice = Convert.ToDouble((order as TrailingStopOrder).TrailingAmount);
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

        internal static Order ToTBOrder(this IBApi.Order ibo)
        {
            Order tbo;

            switch (ibo.OrderType)
            {
                case "MKT":
                    tbo = new MarketOrder();
                    break;

                case "LMT":
                    tbo = new LimitOrder() { LmtPrice = ibo.LmtPrice };
                    break;

                case "STP":
                    tbo = new StopOrder() { StopPrice = ibo.AuxPrice };
                    break;

                case "TRAIL":
                    tbo = new TrailingStopOrder()
                    {
                        //TODO : verify this
                        StopPrice = ibo.AuxPrice,
                        TrailingAmount = ibo.TrailStopPrice,
                        TrailingPercent = ibo.TrailingPercent,
                    };
                    break;

                case "MIT":
                    tbo = new MarketIfTouchedOrder() { TouchPrice = ibo.AuxPrice };
                    break;

                default:
                    throw new NotImplementedException($"{ibo.OrderType}");
            }

            tbo.Action = Enum.Parse<OrderAction>(ibo.Action);
            tbo.TotalQuantity = ibo.TotalQuantity;

            tbo.Id = ibo.OrderId;
            tbo.RequestInfo.ClientId = ibo.ClientId;
            tbo.RequestInfo.Transmit = ibo.Transmit;
            tbo.RequestInfo.ParentId = ibo.ParentId;
            tbo.RequestInfo.PermId = ibo.PermId;
            tbo.RequestInfo.OcaGroup = ibo.OcaGroup;
            tbo.RequestInfo.OcaType = (OcaType)ibo.OcaType;

            return tbo;
        }

        internal static OrderState ToTBOrderState(this IBApi.OrderState ibo)
        {
            return new OrderState()
            {
                Status = Enum.Parse<Status>(ibo.Status),
                WarningText = ibo.WarningText,
                CompletedStatus = ibo.CompletedStatus,
                CompletedTime = ibo.CompletedTime != null ? DateTime.Parse(ibo.CompletedTime) : DateTime.MinValue,

                Commission = ibo.Commission,
                MinCommission = ibo.MinCommission,
                MaxCommission = ibo.MaxCommission,
                CommissionCurrency = ibo.CommissionCurrency,
            };
        }

        internal static OrderExecution ToTBExecution(this IBApi.Execution exec)
        {
            return new OrderExecution()
            {
                ExecId = exec.ExecId,
                OrderId = exec.OrderId,
                Exchange = exec.Exchange,
                Action = exec.Side == "BOT" ? OrderAction.BUY : OrderAction.SELL,
                Shares = exec.Shares,
                Price = exec.Price,
                AvgPrice = exec.AvgPrice,

                // non-standard date format...
                Time = DateTime.ParseExact(exec.Time, "yyyyMMdd  HH:mm:ss", CultureInfo.InvariantCulture)
            };
        }

        internal static CommissionInfo ToTBCommission(this IBApi.CommissionReport report)
        {
            return new CommissionInfo()
            {
                 Commission = report.Commission,
                 ExecId = report.ExecId,   
                 Currency = report.Currency,
                 RealizedPNL = report.RealizedPNL,
            };
        }
    }
}
