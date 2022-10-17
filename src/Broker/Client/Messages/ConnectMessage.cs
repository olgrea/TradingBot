using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;

namespace TradingBot.Broker.Client.Messages
{
    internal class ConnectMessage
    {
        public int NextValidOrderId { get; set; }
        public string AccountCode { get; set; }

        public bool IsSet() => NextValidOrderId > 0 && !string.IsNullOrEmpty(AccountCode);
    }
}
