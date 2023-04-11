using System.Globalization;
using TradingBotV2.Broker.Contracts;
using TradingBotV2.Broker.Orders;
using OrderState = TradingBotV2.Broker.Orders.OrderState;
using Contract = TradingBotV2.Broker.Contracts.Contract;
using ContractDetails = TradingBotV2.Broker.Contracts.ContractDetails;
using Order = TradingBotV2.Broker.Orders.Order;
using Position = TradingBotV2.Broker.Accounts.Position;
using OrderStatus = TradingBotV2.Broker.Orders.OrderStatus;

namespace TradingBotV2.Broker.MarketData
{
    internal static class IBKRConversions
    {
        //TODO : use explicit conversion operators

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

        internal static Last ToTBLast(this IBApi.Last last)
        {
            return new Last()
            {
                Price = last.Price,
                Size = last.Size,
                Time = DateTimeOffset.FromUnixTimeSeconds(last.Time).DateTime.ToLocalTime(),
            };
        }

        internal static Last ToTBLast(this IBApi.HistoricalTickLast last)
        {
            return new Last()
            {
                Price = last.Price,
                Size = Convert.ToInt32(last.Size),
                Time = DateTimeOffset.FromUnixTimeSeconds(last.Time).DateTime.ToLocalTime(),
            };
        }

        internal static BidAsk ToTBBidAsk(this IBApi.BidAsk ba)
        {
            return new BidAsk()
            {
                Bid = ba.Bid,
                BidSize = ba.BidSize,
                Ask = ba.Ask,
                AskSize = ba.AskSize,
                Time = DateTimeOffset.FromUnixTimeSeconds(ba.Time).DateTime.ToLocalTime(),
            };
        }

        internal static BidAsk ToTBBidAsk(this IBApi.HistoricalTickBidAsk ba)
        {
            return new BidAsk()
            {
                Bid = ba.PriceBid,
                BidSize = Convert.ToInt32(ba.SizeBid),
                Ask = ba.PriceAsk,
                AskSize = Convert.ToInt32(ba.SizeAsk),
                Time = DateTimeOffset.FromUnixTimeSeconds(ba.Time).DateTime.ToLocalTime(),
            };
        }

        internal static Position ToTBPosition(this IBApi.Position position)
        {
            return new Position()
            {
                Contract = position.Contract.ToTBContract(),
                PositionAmount = position.PositionAmount,
                MarketPrice = position.MarketPrice,
                MarketValue = position.MarketValue,
                AverageCost = position.AverageCost,
                UnrealizedPNL = position.UnrealizedPNL,
                RealizedPNL = position.RealizedPNL,
            };
        }

        internal static Bar ToTBBar(this IBApi.FiveSecBar bar)
        {
            return new Bar()
            {
                BarLength = BarLength._5Sec,
                Open = bar.Open,
                Close = bar.Close,
                High = bar.High,
                Low = bar.Low,
                Volume = bar.Volume,
                VWAP = bar.WAP,
                TradeAmount = bar.TradeAmount,
                Time = DateTimeOffset.FromUnixTimeSeconds(bar.Date).DateTime.ToLocalTime(),
            };
        }

        internal static Bar ToTBBar(this IBApi.Bar bar)
        {
            return new Bar()
            {
                Open = bar.Open,
                Close = bar.Close,
                High = bar.High,
                Low = bar.Low,
                Volume = bar.Volume,
                TradeAmount = bar.Count,
                Time = DateTime.SpecifyKind(DateTime.ParseExact(bar.Time, MarketDataUtils.TWSTimeFormat, CultureInfo.InvariantCulture), DateTimeKind.Local)
            };
        }

        internal static OrderStatus ToTBOrderStatus(this IBApi.OrderStatus status)
        {
            return new OrderStatus()
            {
                Info = new RequestInfo()
                {
                    OrderId = status.OrderId,
                    ParentId = status.ParentId,
                    ClientId = status.ClientId,
                    PermId = status.PermId,
                },
                Status = !string.IsNullOrEmpty(status.Status) ? (Status)Enum.Parse(typeof(Status), status.Status) : Status.Unknown,
                Filled = status.Filled,
                Remaining = status.Remaining,
                AvgFillPrice = status.AvgFillPrice,
                LastFillPrice = status.LastFillPrice,
                MktCapPrice = status.MktCapPrice,
            };
        }

        
    }
}
