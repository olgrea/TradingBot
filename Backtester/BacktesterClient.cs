using System;
using System.Threading;
using System.Threading.Tasks;
using InteractiveBrokers;

namespace Backtester
{
    internal class BacktesterClient : IBClient
    {
        private readonly BacktesterClientSocket _socket;
        public BacktesterClient(int clientId, BacktesterClientSocket socket) 
            : base(clientId, socket)
        {
            _socket = socket;
        }

        public override async Task WaitUntil(TimeSpan endTime, IProgress<TimeSpan> progress, CancellationToken token)
        {
            _socket.TimeProgress += t => progress.Report(t);
            token.Register(() => _socket.Cancellation.Cancel());
            await _socket.PassingTimeTask;
        }
    }
}
