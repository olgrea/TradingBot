using System;
using System.Collections.Generic;
using System.Text;
using TradingBot.Broker.Client;
using TradingBot.Utils;

namespace TradingBot.Broker
{
    internal class IBBroker : IBroker
    {
        TWSClient _client;
        Account _account;
        ILogger _logger;

        public IBBroker(ILogger logger)
        {
            _logger = logger;
            
            _client = new TWSClient(logger);
            _client.AccountReceived += OnAccountReceived;
        }

        Account Account => _account;

        public void Connect()
        {
            _client.Connect();
        }

        public bool IsConnected => _client.IsConnected;

        public void GetAccount()
        {
            _client.GetAccount();
        }

        public void OnAccountReceived(Account account)
        {
            _account = account;
        }
    }
}
