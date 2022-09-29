using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Broker.MarketData;
using TradingBot.Indicators;
using TradingBot.Broker;

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
        public List<IIndicator> Indicators { get; private set; } = new List<IIndicator>();

        protected void SetStartState<TState>()
        {
            _currentState = GetState<TState>();
        }

        protected void AddIndicator(IIndicator indicator)
        {
            if (!_indicators.ContainsKey(indicator.BarLength))
            {
                _indicators.Add(indicator.BarLength, new List<IIndicator>());
                Trader.Broker.SubscribeToBars(indicator.BarLength, OnBarReceived);
                Trader.Broker.RequestBars(Trader.Contract, indicator.BarLength);
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

        protected void AddState(IState state)
        {
            _states[state.GetType().Name] = state;
        }

        protected IState GetState<TState>()
        {
            return _states[typeof(TState).Name];
        }

        public virtual void Start()
        {
            foreach(var kvp in _indicators)
                InitIndicators(kvp.Key, kvp.Value);

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
                    _currentState = _currentState?.Evaluate();
                }
            }
            ,token
            ,TaskCreationOptions.LongRunning
            ,TaskScheduler.Default);
        }

        void InitIndicators(BarLength barLength, IEnumerable<IIndicator> indicators)
        {
            if (!indicators.Any())
                return;

            var longestPeriod = indicators.Max(i => i.NbPeriods);

            var pastBars = Trader.Broker.GetPastBars(Trader.Contract, barLength, longestPeriod).ToList();

            foreach (var indicator in indicators)
            {
                for (int i = pastBars.Count - indicator.NbPeriods + 1; i < pastBars.Count; ++i)
                    indicator.Update(pastBars[i]);
            }
        }

        public virtual void Stop()
        {

            foreach (var kvp in _indicators)
            {
                Trader.Broker.UnsubscribeToBars(kvp.Key, OnBarReceived);
                Trader.Broker.CancelBarsRequest(Trader.Contract, kvp.Key);

                foreach (var indicator in kvp.Value)
                    indicator.Reset();
            }

            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _currentState = null;
            _cancellationTokenSource = null;
        }
    }
}
