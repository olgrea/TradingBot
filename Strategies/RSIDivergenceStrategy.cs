using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using TradingBot.Broker.MarketData;
using TradingBot.Broker;
using TradingBot.Broker.Orders;
using TradingBot.Indicators;
using System.Threading.Tasks;
using TradingBot.Broker.Client;
using System.Diagnostics;
using System.Linq;

namespace TradingBot.Strategies
{
    public class RSIDivergenceStrategy : StrategyBase
    {
        Trader _trader;

        public RSIDivergenceStrategy(Trader trader)
        {
            _trader = trader;

            States = new Dictionary<string, IState>()
            {
                { nameof(InitState), new InitState(this, trader)},
                { nameof(MonitoringState), new MonitoringState(this, trader)},
                { nameof(OversoldState), new OversoldState(this, trader)},
                { nameof(BoughtState), new BoughtState(this, trader)},
            };

            BollingerBands_1Min = new BollingerBands();
            RSIDivergence_1Min = new RSIDivergence();
            RSIDivergence_5Sec = new RSIDivergence();
        }

        public BollingerBands BollingerBands_1Min { get; private set; }
        public RSIDivergence RSIDivergence_1Min { get; private set; }
        public RSIDivergence RSIDivergence_5Sec { get; private set; }
        public OrderChain Order { get; set; }

        void OnBarReceived(Contract contract, Bar bar)
        {
            if(bar.BarLength == BarLength._1Min)
            {
                BollingerBands_1Min.Update(bar);
                RSIDivergence_1Min.Update(bar);
            }
            else if(bar.BarLength == BarLength._5Sec)
            {
                RSIDivergence_5Sec.Update(bar);
            }
        }

        public override void Start()
        {
            InitIndicators(BarLength._1Min, RSIDivergence_1Min, BollingerBands_1Min);
            InitIndicators(BarLength._5Sec, RSIDivergence_5Sec);

            _trader.Broker.BarReceived[BarLength._1Min] += OnBarReceived;
            _trader.Broker.BarReceived[BarLength._5Sec] += OnBarReceived;
            _trader.Broker.RequestBars(_trader.Contract, BarLength._1Min);
            _trader.Broker.RequestBars(_trader.Contract, BarLength._5Sec);

            CurrentState = States[nameof(InitState)];
            base.Start();
        }

        public override void Stop()
        {
            RSIDivergence_1Min.Reset();
            RSIDivergence_5Sec.Reset();
            BollingerBands_1Min.Reset();

            _trader.Broker.BarReceived[BarLength._1Min] -= OnBarReceived;
            _trader.Broker.BarReceived[BarLength._5Sec] -= OnBarReceived;
            _trader.Broker.CancelBarsRequest(_trader.Contract, BarLength._1Min);
            _trader.Broker.CancelBarsRequest(_trader.Contract, BarLength._5Sec);

            base.Stop();
        }

        //TODO : move this elsewhere
        void InitIndicators(BarLength barLength, params IIndicator[] indicators)
        {
            if (!indicators.Any())
                return;

            var longestPeriod = indicators.Max(i => i.NbPeriods);

            var pastBars = _trader.Broker.GetPastBars(_trader.Contract, barLength, longestPeriod);

            foreach (var indicator in indicators)
            {
                for (int i = pastBars.Count - indicator.NbPeriods + 1; i < pastBars.Count; ++i)
                    indicator.Update(pastBars[i]);
            }
        }

        #region States

        class InitState : IState
        {
            RSIDivergenceStrategy _strategy;

            public InitState(RSIDivergenceStrategy strategy, Trader trader)
            {
                _strategy = strategy;
                Trader = trader;
            } 

            public Trader Trader { get; }

            public IState Evaluate()
            {
                // Initialization of indicators
                if (_strategy.BollingerBands_1Min.IsReady && _strategy.RSIDivergence_1Min.IsReady && _strategy.RSIDivergence_5Sec.IsReady)
                    return _strategy.States[nameof(MonitoringState)];
                else
                    return this;
            }
        }

        class MonitoringState : IState
        {
            RSIDivergenceStrategy _strategy;
            public MonitoringState(RSIDivergenceStrategy strategy, Trader trader)
            {
                _strategy = strategy;
                Trader = trader;
            }

            public Trader Trader { get; }

            public IState Evaluate()
            {
                // We want to find a bar candle that goes below the lower BB
                if (_strategy.BollingerBands_1Min.Bars.Last.Value.Close < _strategy.BollingerBands_1Min.LowerBB
                    && _strategy.RSIDivergence_1Min.Value < 0)
                    return _strategy.States[nameof(OversoldState)];
                else
                    return this;
            }
        }

        class OversoldState : IState
        {
            RSIDivergenceStrategy _strategy;
            Bar _lastBar;
            double _lastRSIdivValue = double.MinValue;
            int _counter = 0;

            public OversoldState(RSIDivergenceStrategy strategy, Trader trader)
            {
                _strategy = strategy;
                Trader = trader;
            }

            public Trader Trader { get; }

            public IState Evaluate()
            {
                // At this moment, we switch to a 5 secs resolution.
                var RSIdiv5sec = _strategy.RSIDivergence_5Sec;

                if (_lastBar == null || _lastBar == RSIdiv5sec.LatestBar)
                {
                    _lastBar = RSIdiv5sec.LatestBar;
                    return this;
                }

                // We buy as soon as an upward trend starts. 
                if(_lastRSIdivValue <= RSIdiv5sec.Value)
                {
                    _counter = 0;
                    _lastRSIdivValue = RSIdiv5sec.Value;
                    return this;
                }
                else if (_counter < 3)
                {
                    _counter++;
                    return this;
                }
                else
                {
                    double funds = Trader.GetAvailableFunds();
                    var order = BuildOrderChain(funds);
                    var nextState = PlaceOrder(order);
                    ResetState();
                    return nextState.Result;
                }
            }

            Task<IState> PlaceOrder(OrderChain chain)
            {
                var completion = new TaskCompletionSource<IState>();
                var orderPlaced = new Action<Contract, Order, OrderState>((c, o, os) =>
                {
                    Debug.Assert(chain.Order.Id > 0);
                    if(chain.Order.Id == o.Id && (os.Status == Status.Submitted || os.Status == Status.PreSubmitted))
                    {
                        _strategy.Order = o;
                    }
                });

                var orderExecuted = new Action<Contract, OrderExecution>((c, oe) =>
                {
                    if (chain.Order.Id == oe.OrderId)
                    {
                        completion.SetResult(_strategy.States[nameof(BoughtState)]);
                    }
                });

                var error = new Action<ClientMessage>(msg =>
                {
                    if(msg is ClientError)
                    {
                        completion.SetResult(_strategy.States[nameof(MonitoringState)]);
                    }
                });

                Trader.Broker.OrderOpened += orderPlaced;
                Trader.Broker.OrderExecuted += orderExecuted;
                Trader.Broker.ClientMessageReceived += error;

                completion.Task.ContinueWith(t => 
                {
                    Trader.Broker.OrderOpened -= orderPlaced;
                    Trader.Broker.OrderExecuted -= orderExecuted;
                    Trader.Broker.ClientMessageReceived -= error;
                });

                Trader.Broker.PlaceOrder(Trader.Contract, chain);
                return completion.Task;
            }

            void ResetState()
            {
                _lastBar = null;
                _lastRSIdivValue = double.MinValue;
                _counter = 0;
            }

            OrderChain BuildOrderChain(double funds)
            {
                var latestBar = _strategy.RSIDivergence_5Sec.LatestBar;
                int qty = (int)(funds / latestBar.Close);

                var m = new MarketOrder() { Action = OrderAction.BUY, TotalQuantity = qty };

                // TODO : manage stop price better. Should be in relation to the bought price.
                var trl = new TrailingStopOrder { Action = OrderAction.SELL, TotalQuantity = qty, TrailingPercent = latestBar.Close * 0.003 };

                var qtyLeft = qty - (qty / 2);
                var mit = new MarketIfTouchedOrder() { Action = OrderAction.SELL, TotalQuantity = qty / 2, TouchPrice = _strategy.BollingerBands_1Min.MovingAverage };


                var mit2 = new MarketIfTouchedOrder() { Action = OrderAction.SELL, TotalQuantity = qtyLeft, TouchPrice = _strategy.BollingerBands_1Min.UpperBB };
                var stp = new StopOrder() { Action = OrderAction.SELL, TotalQuantity = qtyLeft, StopPrice = _strategy.BollingerBands_1Min.MovingAverage * 0.997 };

                var innerChain = new OrderChain(mit, new List<OrderChain>() { mit2, stp });
                var chain = new OrderChain(m, new List<OrderChain>() { innerChain, trl });

                return chain;
            }
        }

        class BoughtState : IState
        {
            RSIDivergenceStrategy _strategy;
            public BoughtState(RSIDivergenceStrategy strategy, Trader trader)
            {
                _strategy = strategy;
                Trader = trader;
            }

            public Trader Trader { get; }

            public IState Evaluate()
            {
                // TODO : adjust opened orders
                return this;
            }
        }

        #endregion States
    }
}
