
namespace TradingBotV2.Backtesting
{
    //TODO : inherit from IProgress instead?
    public struct BacktesterProgress
    {
        public DateTime Time { get; set; }

        public override string? ToString() => Time.ToString();
    }
}
