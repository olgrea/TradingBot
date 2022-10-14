using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker.Client.Messages
{
    internal class ConnectMessage
    {
        public int NextValidOrderId { get; set; }
        public string AccountCode { get; set; }
    }
}
