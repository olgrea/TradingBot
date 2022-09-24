using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Broker.Client;

namespace TradingBot.Utils
{
    internal class AsyncHelper<TResult>
    {
        public static TResult AsyncToSync<T1, T2>(Action async, ref Action<T1, T2> @event, Func<T1, T2, TResult> resultFunc, int timeoutInSec = 30)
        {
            var tcs = new TaskCompletionSource<TResult>();
            var callback = new Action<T1, T2>((t1, t2) => tcs.SetResult(resultFunc(t1, t2)));
            try
            {
                @event += callback;
                async.Invoke();
                tcs.Task.Wait(timeoutInSec * 1000);
            }
            finally
            {
                @event -= callback;
            }

            return tcs.Task.IsCompletedSuccessfully ? tcs.Task.Result : default(TResult);
        }
    }
}
