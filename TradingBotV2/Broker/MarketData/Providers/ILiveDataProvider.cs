namespace TradingBotV2.Broker.MarketData.Providers
{
    public interface ILiveDataProvider
    {
        public event Action<string, BidAsk> BidAskReceived;
        public event Action<string, Last> LastReceived;
        public event Action<string, Bar> BarReceived;

        public void RequestBarUpdates(string ticker, BarLength barLength);
        public void CancelBarUpdates(string ticker, BarLength barLength);
        public void RequestBidAskUpdates(string ticker);
        public void CancelBidAskUpdates(string ticker);
        public void RequestLastTradedPriceUpdates(string ticker);
        public void CancelLastTradedPriceUpdates(string ticker);
    }
}
