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

        internal static ContractDetails ToTBContractDetails(this IBApi.ContractDetails details)
        {
            return new ContractDetails()
            {
                Contract = details.Contract.ToTBContract(),
                Industry = details.Industry,
                LongName = details.LongName,
                MarketName = details.MarketName,
                MinTick = details.MinTick,
                OrderTypes = details.OrderTypes,
                StockType = details.StockType,
                TimeZoneId = details.TimeZoneId,
                ValidExchanges = details.ValidExchanges,
                RegularTradingHours = details.LiquidHours,
            };
        }

        internal static IBApi.ContractDetails ToIBApiContractDetails(this ContractDetails details)
        {
            return new IBApi.ContractDetails()
            {
                Contract = details.Contract.ToIBApiContract(),
                Industry = details.Industry,
                LongName = details.LongName,
                MarketName = details.MarketName,
                MinTick = details.MinTick,
                OrderTypes = details.OrderTypes,
                StockType = details.StockType,
                TimeZoneId = details.TimeZoneId,
                ValidExchanges = details.ValidExchanges,
                LiquidHours = details.RegularTradingHours,
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
                ClientId = order.Info.ClientId,
                ParentId = order.Info.ParentId,
                PermId = order.Info.PermId,
                Transmit = order.Info.Transmit,
                OcaGroup = order.Info.OcaGroup,
                OcaType = (int)order.Info.OcaType,

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
                        StopPrice = ibo.AuxPrice,
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
            tbo.Info.ClientId = ibo.ClientId;
            tbo.Info.Transmit = ibo.Transmit;
            tbo.Info.ParentId = ibo.ParentId;
            tbo.Info.PermId = ibo.PermId;
            tbo.Info.OcaGroup = ibo.OcaGroup;
            tbo.Info.OcaType = (OcaType)ibo.OcaType;

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
