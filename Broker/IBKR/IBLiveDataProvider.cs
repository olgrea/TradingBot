using System.Diagnostics;
using TradingBot.IBKR.Client;

namespace TradingBot.IBKR
{
    internal class IBLiveDataProvider : LiveDataProviderBase
    {
        IBClient _client;

        public IBLiveDataProvider(IBClient client)
        {
            _client = client;
        }

        protected override void RequestFiveSecondsBarUpdates(string ticker)
        {
            _client.RequestFiveSecondsBarUpdates(ticker);
        }

        protected override void SubscribeToRealtimeBarCallback()
        {
            _client.Responses.RealtimeBar += OnFiveSecondsBarReceived;
            Debug.Assert(_client.Responses.RealtimeBar.GetInvocationList().Length == 1);
        }

        protected override void CancelFiveSecondsBarsUpdates(string ticker)
        {
            _client.CancelFiveSecondsBarsUpdates(ticker);
        }

        protected override void UnsubscribeFromRealtimeBarCallback()
        {
            _client.Responses.RealtimeBar -= OnFiveSecondsBarReceived;
        }
        
        protected override void RequestBidAskData(string ticker)
        {
            _client.RequestTickByTickData(ticker, "BidAsk");
        }
        protected override void SubscribeToBidAskCallback()
        {
            _client.Responses.TickByTickBidAsk += TickByTickBidAsk;
        }

        protected override void CancelBidAskData(string ticker)
        {
            _client.CancelTickByTickData(ticker, "BidAsk");
        }

        protected override void UnsubscribeFromBidAskDataCallback()
        {
            _client.Responses.TickByTickBidAsk -= TickByTickBidAsk;
        }

        protected override void RequestLastData(string ticker)
        {
            _client.RequestTickByTickData(ticker, "Last");
        }

        protected override void SubscribeToLastDataCallback()
        {
            _client.Responses.TickByTickAllLast += TickByTickLast;
        }

        protected override void CancelLastData(string ticker)
        {
            _client.CancelTickByTickData(ticker, "Last");
        }

        protected override void UnsubscribeFromLastDataCallback()
        {
            _client.Responses.TickByTickAllLast -= TickByTickLast;
        }
    }
}
