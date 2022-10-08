using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Broker.MarketData;
using TradingBot.Indicators;
using TradingBot.Broker;
using TradingBot.Broker.Orders;

namespace TradingBot.Strategies
{
    public abstract class Strategy : IStrategy 
    {
        IState _currentState;
        Dictionary<string, IState> _states = new Dictionary<string, IState>();
        Dictionary<BarLength, List<IIndicator>> _indicators = new Dictionary<BarLength, List<IIndicator>>();

        Task _evaluateTask;
        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public Strategy(Trader trader)
        {
            Trader = trader;
        }

        public Trader Trader { get; protected set; }
        public IEnumerable<IIndicator> Indicators => _indicators.SelectMany(i => i.Value);

        protected void AddIndicator(IIndicator indicator)
        {
            if (!_indicators.ContainsKey(indicator.BarLength))
            {
                _indicators.Add(indicator.BarLength, new List<IIndicator>());
            }

            _indicators[indicator.BarLength].Add(indicator);
        }

        void OnBarReceived(Contract contract, Bar bar)
        {
            if(_indicators.ContainsKey(bar.BarLength))
            {
                foreach (var indicator in _indicators[bar.BarLength])
                    indicator.Update(bar);
            }
        }

        protected TIndicator GetIndicator<TIndicator>(BarLength barLength) where TIndicator : IIndicator
        {
            if (!_indicators.ContainsKey(barLength))
                return default(TIndicator);

            return _indicators[barLength].OfType<TIndicator>().FirstOrDefault();
        }
        protected void SetStartState<TState>() where TState : IState
        {
            _currentState = GetState<TState>();
        }

        protected void AddState<TState>() where TState : IState
        {
            var t = typeof(TState);
            _states[t.Name] = (TState)Activator.CreateInstance(t, this);
        }

        internal IState GetState<TState>() where TState : IState
        {
            var t = typeof(TState);
            if (!_states.ContainsKey(t.Name))
                throw new ArgumentException("Invalid state type");

            // reset state on state change
            if(_currentState?.GetType() != t)
                _states[t.Name] = (TState)Activator.CreateInstance(t, this);

            return _states[t.Name];
        }

        public virtual void Start()
        {
            foreach(var kvp in _indicators)
            {
                InitIndicators(kvp.Key, kvp.Value);
                Trader.Broker.SubscribeToBars(kvp.Key, OnBarReceived);
                Trader.Broker.RequestBars(Trader.Contract, kvp.Key);
            }

            if (_currentState == null)
                throw new InvalidOperationException("No starting state has been set");

            if (_evaluateTask != null && !_evaluateTask.IsCompleted)
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            _evaluateTask = Task.Factory.StartNew(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    var nextState = _currentState?.Evaluate();
                    if (nextState != _currentState)
                        Trader.Logger.Info($"Strategy {GetType().Name} state change : {_currentState.GetType().Name} -> {nextState.GetType().Name}");
                    _currentState = nextState;
                }
            }
            ,token
            ,TaskCreationOptions.LongRunning
            ,TaskScheduler.Default);
        }

        internal void PlaceOrder(Order o)
        {
            Trader.Broker.PlaceOrder(Trader.Contract, o);
        }

        internal void ModifyOrder(Order o)
        {
            Trader.Broker.ModifyOrder(Trader.Contract, o);
        }

        internal void CancelOrder(Order o)
        {
            Trader.Broker.CancelOrder(o);
        }

        internal void PlaceOrder(OrderChain c)
        {
            Trader.Broker.PlaceOrder(Trader.Contract, c);
        }

        internal bool HasBeenRequested(Order order) => Trader.Broker.HasBeenRequested(order);
        internal bool HasBeenOpened(Order order) => Trader.Broker.HasBeenOpened(order);
        internal bool IsCancelled(Order order) => Trader.Broker.IsCancelled(order);
        internal bool IsExecuted(Order order, out OrderExecution orderExecution) => Trader.Broker.IsExecuted(order, out orderExecution);

        void InitIndicators(BarLength barLength, IEnumerable<IIndicator> indicators)
        {
            if (!indicators.Any())
                return;

            var longestPeriod = indicators.Max(i => i.NbPeriods);

            var pastBars = Trader.Broker.GetPastBars(Trader.Contract, barLength, longestPeriod).ToList();

            foreach (var indicator in indicators)
            {
                foreach(var bar in pastBars)
                    indicator.Update(bar);
            }
        }

        public virtual void Stop()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _currentState = null;
            _cancellationTokenSource = null;

            foreach (var kvp in _indicators)
            {
                Trader.Broker.UnsubscribeToBars(kvp.Key, OnBarReceived);
                Trader.Broker.CancelBarsRequest(Trader.Contract, kvp.Key);

                foreach (var indicator in kvp.Value)
                    indicator.Reset();
            }
        }
    }
}
