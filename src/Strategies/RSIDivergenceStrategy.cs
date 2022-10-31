using System;
using System.Linq;
using System.Collections.Generic;
using TradingBot.Broker.MarketData;
using TradingBot.Broker.Orders;
using TradingBot.Indicators;
using MathNet.Numerics;

namespace TradingBot.Strategies
{
    public class RSIDivergenceStrategy : Strategy
    {
        public RSIDivergenceStrategy(Trader trader) : base(trader)
        {
            AddState<InitState>();
            AddState<MonitoringState>();
            AddState<OversoldState>();
            AddState<BoughtState>();
            AddState<ProfitState>();
            AddState<LetItRideState>();
            
            SetStartState<InitState>();

            AddIndicator(new BollingerBands(BarLength._1Min));
            AddIndicator(new RsiDivergence(BarLength._1Min, 14, 5));
            AddIndicator(new RsiDivergence(BarLength._5Sec, 14, 5));
        }

        internal BollingerBands BollingerBands_1Min => GetIndicator<BollingerBands>(BarLength._1Min);
        internal RsiDivergence RSIDivergence_1Min => GetIndicator<RsiDivergence>(BarLength._1Min);
        internal RsiDivergence RSIDivergence_5Sec => GetIndicator<RsiDivergence>(BarLength._5Sec);

        internal Order BuyOrder { get; set; }
        internal Order MITOrder { get; set; }
        internal Order StopOrder { get; set; }
        internal Dictionary<int, OrderExecution> Executions = new Dictionary<int, OrderExecution>();

        #region States

        class InitState : State<RSIDivergenceStrategy>
        {
            public InitState(RSIDivergenceStrategy strategy) : base(strategy) {}

            public override IState Evaluate()
            {
                if (_strategy.Indicators.All(i => i.IsReady))
                    return GetState<MonitoringState>();
                else
                    return this;
            }
        }

        class MonitoringState : State<RSIDivergenceStrategy>
        {
            public MonitoringState(RSIDivergenceStrategy strategy) : base(strategy) { }

            public override IState Evaluate()
            {
                // We want to find a bar candle that goes below the lower BB
                if (_strategy.LatestBar.Close < _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value
                    && _strategy.RSIDivergence_1Min.LatestResult.RSIDivergence.Value < 0
                    && _strategy.RSIDivergence_1Min.FastRSI.IsOversold)
                {
                    Logger.Info($"Lower band reached. RSIDivergence < 0. Switching to 5 sec resolution.");
                    return GetState<OversoldState>();
                }
                else
                    return this;
            }
        }

        class OversoldState : State<RSIDivergenceStrategy>
        {
            bool _buySignal = false;

            public OversoldState(RSIDivergenceStrategy strategy) : base(strategy) { }

            public override IState Evaluate()
            {
                // At this moment, we switch to a 5 secs resolution.

                if (!_buySignal)
                {
                    if(_strategy.RSIDivergence_5Sec.LatestResult.RSIDivergence > 0 && !_strategy.RSIDivergence_5Sec.FastRSI.IsOversold)
                        _buySignal = true;
                    else
                        return this;
                }


                if (!_isInitialized)
                    InitializeOrders();

                if (!HasBeenRequested(_strategy.BuyOrder))
                {
                    Logger.Info($"RSIDivergence > 0. Placing market BUY order.");
                    PlaceOrder(_strategy.BuyOrder);
                }

                if(EvaluateOrder(_strategy.BuyOrder, null, out OrderExecution orderExecution))
                {
                    if(orderExecution != null)
                    {
                        _strategy.Executions.Add(_strategy.BuyOrder.Id, orderExecution);
                        return GetState<BoughtState>();
                    }
                    else
                        return GetState<MonitoringState>();
                }

                return this;
            }

            protected override void InitializeOrders()
            {
                double funds = _strategy.Trader.GetAvailableFunds();
                var latestBar = _strategy.LatestBar;
                int qty = (int)(funds / latestBar.Close);

                _strategy.BuyOrder = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };

                _isInitialized = true;
            }
        }

        class BoughtState : State<RSIDivergenceStrategy>
        {
            protected Bar _lastBar;

            public BoughtState(RSIDivergenceStrategy strategy) : base(strategy) { }
            public override IState Evaluate()
            {
                return Evaluate<ProfitState, MonitoringState>();
            }

            protected IState Evaluate<TNextState, TResetState>() where TNextState : IState where TResetState : IState
            {
                if (!_isInitialized)
                    InitializeOrders();

                if (!HasBeenRequested(_strategy.StopOrder))
                    PlaceOrder(_strategy.StopOrder);

                if (!HasBeenRequested(_strategy.MITOrder))
                    PlaceOrder(_strategy.MITOrder);

                if (EvaluateOrder(_strategy.StopOrder, _strategy.MITOrder, out _))
                    return GetState<TResetState>();

                if (EvaluateOrder(_strategy.MITOrder, _strategy.StopOrder, out OrderExecution orderExecution))
                {
                    if (orderExecution != null)
                    {
                        _strategy.Executions.Add(_strategy.MITOrder.Id, orderExecution);
                        return GetState<TNextState>();
                    }
                    else
                        return GetState<TResetState>();
                }

                UpdateOrders();

                return this;
            }

            protected override void InitializeOrders()
            {
                _strategy.Executions.TryGetValue(_strategy.BuyOrder.Id, out OrderExecution execution);
                var qty = execution.Shares;

                // TODO : to test
                double halfDiff = (_strategy.BollingerBands_1Min.LatestResult.Sma.Value - _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value) / 2;
                var stop = _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value - halfDiff;

                _strategy.StopOrder = new StopOrder { Action = OrderAction.SELL, TotalQuantity = qty, StopPrice = stop };
                _strategy.MITOrder = new MarketIfTouchedOrder() { Action = OrderAction.SELL, TotalQuantity = qty / 2, TouchPrice = _strategy.BollingerBands_1Min.LatestResult.Sma.Value};

                _isInitialized = true;
            }

            protected virtual void UpdateOrders()
            {
                // TODO : modify orders? There are some fees associated to that apparently... need to be careful
                // https://www.interactivebrokers.ca/en/accounts/fees/cancelModifyExamples.php

                // Examples are not very clear. 0.01¢/order? If yes then it's not that bad...

                if (_lastBar != null && _lastBar != _strategy.LatestBar)
                {
                    StopOrder stopOrder = (_strategy.StopOrder as StopOrder);

                    double halfDiff = (_strategy.BollingerBands_1Min.LatestResult.Sma.Value - _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value) / 2;
                    double lowerHalf = (_strategy.BollingerBands_1Min.LatestResult.LowerBand.Value + _strategy.BollingerBands_1Min.LatestResult.Sma.Value) / 2;
                    double lowerQuarter = (_strategy.BollingerBands_1Min.LatestResult.LowerBand.Value + lowerHalf) / 2;

                    var stop = _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value - halfDiff;
                    if (_strategy.LatestBar.Close > lowerHalf)
                    {
                        stop = lowerHalf;
                    }
                    else if (_strategy.LatestBar.Close > lowerQuarter)
                    {
                        stop = lowerQuarter;
                    }
                    else if (_strategy.LatestBar.Close > _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value)
                    {
                        stop = _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value;
                    }

                    var previousStop = stopOrder.StopPrice;
                    var nextStop = Math.Max(previousStop, stop);
                    if (!nextStop.AlmostEqual(previousStop, 2) && !IsCancelled(stopOrder) && !IsExecuted(stopOrder, out _))
                    {
                        stopOrder.StopPrice = nextStop;
                        ModifyOrder(_strategy.StopOrder);
                    }

                    MarketIfTouchedOrder marketIfTouchedOrder = (_strategy.MITOrder as MarketIfTouchedOrder);
                    var previousTouch = marketIfTouchedOrder.TouchPrice;
                    var nextTouch = _strategy.BollingerBands_1Min.LatestResult.Sma.Value;

                    if (!nextTouch.AlmostEqual(previousTouch, 2))
                    {
                        marketIfTouchedOrder.TouchPrice = nextTouch;
                        ModifyOrder(_strategy.MITOrder);
                    }
                }

                _lastBar = _strategy.LatestBar;
            }
        }

        class ProfitState : BoughtState
        {
            public ProfitState(RSIDivergenceStrategy strategy) : base(strategy) { }
            public override IState Evaluate()
            {
                return Evaluate<LetItRideState, MonitoringState>();
            }

            protected override void InitializeOrders()
            {
                var previousMITOrder = _strategy.MITOrder;
                _strategy.Executions.TryGetValue(previousMITOrder.Id, out OrderExecution execution);
                var qty = execution.Shares;

                var stop = (_strategy.BollingerBands_1Min.LatestResult.Sma.Value + _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value) / 2;

                _strategy.StopOrder = new StopOrder { Action = OrderAction.SELL, TotalQuantity = qty, StopPrice = stop};
                _strategy.MITOrder = new MarketIfTouchedOrder() { Action = OrderAction.SELL, TotalQuantity = qty / 2, TouchPrice = _strategy.BollingerBands_1Min.LatestResult.UpperBand.Value};

                _isInitialized = true;
            }

            protected override void UpdateOrders()
            {
                // TODO : modify orders? There are some fees associated to that apparently... need to be careful
                // https://www.interactivebrokers.ca/en/accounts/fees/cancelModifyExamples.php
                // Examples are not very clear. 0.01¢/order? If yes then it's not that bad...

                if (_lastBar != null && _lastBar != _strategy.LatestBar)
                {
                    StopOrder stopOrder = (_strategy.StopOrder as StopOrder);

                    double stop = (_strategy.BollingerBands_1Min.LatestResult.Sma.Value + _strategy.BollingerBands_1Min.LatestResult.LowerBand.Value) / 2; 
                    if(_strategy.LatestBar.Close > _strategy.BollingerBands_1Min.LatestResult.Sma.Value)
                    {
                        stop = _strategy.BollingerBands_1Min.LatestResult.Sma.Value;
                    }

                    var previousStop = stopOrder.StopPrice;
                    var nextStop = Math.Max(stop, previousStop);
                    if (!nextStop.AlmostEqual(previousStop, 2))
                    {
                        stopOrder.StopPrice = nextStop;
                        ModifyOrder(_strategy.StopOrder);
                    }

                    MarketIfTouchedOrder marketIfTouchedOrder = (_strategy.MITOrder as MarketIfTouchedOrder);
                    var previousTouch = marketIfTouchedOrder.TouchPrice;
                    var nextTouch = _strategy.BollingerBands_1Min.LatestResult.UpperBand.Value;
                    if (!nextTouch.AlmostEqual(previousTouch, 2))
                    {
                        marketIfTouchedOrder.TouchPrice = nextTouch;
                        ModifyOrder(_strategy.MITOrder);
                    }
                }

                _lastBar = _strategy.LatestBar;
            }
        }

        class LetItRideState : State<RSIDivergenceStrategy>
        {
            protected Bar _lastBar;

            public LetItRideState(RSIDivergenceStrategy strategy) : base(strategy){}

            public override IState Evaluate()
            {
                if (!_isInitialized)
                    InitializeOrders();

                if (!HasBeenRequested(_strategy.StopOrder))
                    PlaceOrder(_strategy.StopOrder);

                if (EvaluateOrder(_strategy.StopOrder, null, out _))
                {
                    _strategy.Executions.Clear();
                    return GetState<MonitoringState>();
                }

                UpdateOrders();

                return this;
            }

            protected override void InitializeOrders()
            {
                var previousMITOrder = _strategy.MITOrder;
                _strategy.Executions.TryGetValue(previousMITOrder.Id, out OrderExecution execution);
                var qty = execution.Shares;

                // TODO : to test
                _strategy.StopOrder = new StopOrder { Action = OrderAction.SELL, TotalQuantity = qty, StopPrice = _strategy.BollingerBands_1Min.LatestResult.Sma.Value};
                _isInitialized = true;
            }

            protected void UpdateOrders()
            {
                // TODO : modify orders? There are some fees associated to that apparently... need to be careful
                // https://www.interactivebrokers.ca/en/accounts/fees/cancelModifyExamples.php
                // Examples are not very clear. 0.01¢/order? If yes then it's not that bad...

                if (_lastBar != null && _lastBar != _strategy.LatestBar)
                {
                    StopOrder stopOrder = (_strategy.StopOrder as StopOrder);

                    double stop = _strategy.BollingerBands_1Min.LatestResult.Sma.Value;
                    //double upperHalf = (_strategy.BollingerBands_1Min.Result.UpperBand.Value + _strategy.BollingerBands_1Min.Result.Sma.Value) / 2;
                    //if (_strategy.LatestBar.Close > upperHalf)
                    //{
                    //    stop = upperHalf;
                    //}

                    var previousStop = stopOrder.StopPrice;
                    var nextStop = Math.Max(stop, previousStop);
                    if (!nextStop.AlmostEqual(previousStop, 2))
                    {
                        stopOrder.StopPrice = nextStop;
                        ModifyOrder(_strategy.StopOrder);
                    }
                }

                _lastBar = _strategy.LatestBar;
            }
        }

        #endregion States
    }
}
