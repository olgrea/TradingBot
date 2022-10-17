using System.Threading.Tasks;
using NLog;
using TradingBot.Broker.Client.Messages;
using TradingBot.Utils;
using ILogger = NLog.ILogger;

namespace TradingBot.Broker.Client
{
    internal interface IErrorHandler
    {
        bool IsHandled(ErrorMessageException msg);
    }

    internal class DefaultErrorHandler : IErrorHandler
    {
        protected ILogger _logger;

        public DefaultErrorHandler(ILogger logger)
        {
            _logger = logger;
        }

        public virtual bool IsHandled(ErrorMessageException msg)
        {
            if (IsWarningMessage(msg.ErrorCode))
            {
                _logger.Warn($"{msg.Message} ({msg.ErrorCode})");
                return true;
            }

            switch (msg.ErrorCode)
            {
                //case 501: // Already Connected
                //    _logger.Debug(msg.Message);
                //    return true;

                default:
                    _logger.Error(msg);
                    // TODO : recover or kill remaining tasks so the program can exit
                    return false;
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

    internal class IBBrokerErrorHandler : DefaultErrorHandler
    {
        IBBroker _broker;
        public IBBrokerErrorHandler(IBBroker broker, ILogger logger) : base(logger)
        {
            _broker = broker;
        }

        public override bool IsHandled(ErrorMessageException msg)
        {
            switch (msg.ErrorCode)
            {
                //case 1011: // Connectivity between IB and TWS has been restored- data lost.*
                //    RestoreSubscriptions();
                //    break;

                default:
                    return base.IsHandled(msg);
            }
        }

        void RestoreSubscriptions()
        {
            // TODO : RestoreSubscriptions
            var subs = _broker.Subscriptions;
        }
    }

    internal class TraderErrorHandler : IBBrokerErrorHandler
    {
        Trader _trader;
        public TraderErrorHandler(Trader trader) : base(trader.Broker as IBBroker, trader.Logger)
        {
            _trader = trader;
        }

        public override bool IsHandled(ErrorMessageException msg)
        {
            switch (msg.ErrorCode)
            {
                //TODO : handle disconnections


                default:
                    return base.IsHandled(msg); ;
            }
        }
    }
}
