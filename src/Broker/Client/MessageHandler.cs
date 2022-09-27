using System.Threading.Tasks;
using TradingBot.Utils;

namespace TradingBot.Broker.Client
{
    class MessageHandler
    {
        protected ILogger _logger;

        public MessageHandler(ILogger logger)
        {
            _logger = logger;
        }

        public virtual void OnMessage(TWSMessage msg)
        {
            var e = new ClientException(msg.Id, msg.ErrorCode, msg.Message);

            if (IsWarningMessage(msg.ErrorCode))
            {
                _logger.LogWarning($"{msg.Message} ({msg.ErrorCode})");
                return;
            }

            switch (msg.ErrorCode)
            {
                case 501: // Already Connected
                    _logger.LogDebug(msg.Message);
                    break;

                default:
                    throw e;
            }
        }

        public static bool IsWarningMessage(int code) => code >= 2100 && code < 2200;

        //// https://interactivebrokers.github.io/tws-api/message_codes.html
        //// https://interactivebrokers.github.io/tws-api/classIBApi_1_1EClientErrors.html
        //public static bool IsSystemMessage(int code) => code >= 1100 && code <= 1300;
        //public static bool IsWarningMessage(int code) => code >= 2100 && code < 2200;
        //public static bool IsClientErrorMessage(int code) => 
        //    (code >= 501 && code <= 508 && code != 507) ||
        //    (code >= 510 && code <= 549) ||
        //    (code >= 551 && code <= 584) ||
        //    code == 10038;

        //public static bool IsTWSErrorMessage(int code) =>
        //    (code >= 100 && code <= 168) ||
        //    (code >= 200 && code <= 449) ||
        //    code == 507 ||
        //    (code >= 10000 && code <= 10027) ||
        //    code == 10090 ||
        //    (code >= 10148 && code <= 10284);
    }

    internal class IBBrokerMessageHandler : MessageHandler
    {
        IBBroker _broker;
        public IBBrokerMessageHandler(IBBroker broker, ILogger logger) : base(logger)
        {
            _broker = broker;
        }

        public override void OnMessage(TWSMessage msg)
        {
            switch (msg.ErrorCode)
            {
                //case 1011: // Connectivity between IB and TWS has been restored- data lost.*
                //    RestoreSubscriptions();
                //    break;

                default:
                    base.OnMessage(msg);
                    break;
            }
        }

        void RestoreSubscriptions()
        {
            // TODO : RestoreSubscriptions
            var subs = _broker.Subscriptions;
        }
    }

    internal class TraderMessageHandler : IBBrokerMessageHandler
    {
        Trader _trader;
        public TraderMessageHandler(Trader trader, IBBroker broker, ILogger logger) : base(broker, logger)
        {
            _trader = trader;
        }

        public override void OnMessage(TWSMessage msg)
        {
            switch (msg.ErrorCode)
            {
                //TODO : handle disconnections


                default:
                    base.OnMessage(msg);
                    break;
            }
        }
    }
}
