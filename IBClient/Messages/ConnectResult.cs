namespace InteractiveBrokers.Messages
{
    internal class ConnectResult
    {
        public int NextValidOrderId { get; set; }
        public string AccountCode { get; set; }

        public bool IsSet() => NextValidOrderId > 0 && !string.IsNullOrEmpty(AccountCode);
    }
}
