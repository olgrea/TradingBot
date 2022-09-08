using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TradingBot.Strategies
{
    public abstract class StrategyBase : IStrategy
    {
        IState _currentState;

        public Dictionary<string, IState> States { get; protected set; }

        public IState CurrentState
        {
            get => _currentState;
            set
            {
                if (value != _currentState)
                {
                    _currentState?.UnsubscribeToData();
                    _currentState = value;
                    _currentState?.SubscribeToData();
                }
            }
        }

        Task _evaluateTask;
        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public virtual void Start()
        {
            if (_evaluateTask == null || !_evaluateTask.IsCompleted)
                return;

            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            _evaluateTask = Task.Factory.StartNew(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    CurrentState = CurrentState?.Evaluate();
                }
            }
            ,token
            ,TaskCreationOptions.LongRunning
            ,TaskScheduler.Default);
        }

        public virtual void Stop()
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            CurrentState = null;
            _cancellationTokenSource = null;
        }
    }
}
