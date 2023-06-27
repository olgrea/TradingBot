namespace TradingBot.IBKR.Client
{
    // https://interactivebrokers.github.io/tws-api/message_codes.html
    // https://interactivebrokers.github.io/tws-api/classIBApi_1_1EClientErrors.html
    public enum MessageCode
    {
        Unknown = -1,
        OrderNotCancellableCode = 161,
        HistoricalMarketDataServiceErrorCode = 162,
        OrderCancelledCode = 202,
        OrderMessageError = 399,

        TWSConnectionLost = 1100,
        TWSConnectionRestored_MarketDataRequestsLost = 1101,
        TWSConnectionRestored_MarketDataRequestsRestored = 1102,
        TWSSocketPortHasBeenReset = 1300,
        MarketDataConnectionLost = 2103,
        MarketDataConnectionEstablished = 2104,
    }

    public class Message
    {
        string _text;

        public Message(string msg) : this(-1, -1, msg) { }

        public Message(int code, string msg) : this(-1, code, msg) { }

        public Message(int id, int code, string msg)
        {
            Id = id;
            Code = (MessageCode)code;
            _text = msg;
        }
        public int Id { get; set; } = -1;
        public MessageCode Code { get; set; } = MessageCode.Unknown;
        public string Text
        {
            get
            {
                string baseMsg = _text;
                if (Code >= 0 || Id >= 0)
                {
                    baseMsg += " (";
                    if (Code >= 0)
                        baseMsg += $" code={(int)Code} ";
                    if (Id >= 0)
                        baseMsg += $" rId={Id} ";
                    baseMsg += ")";
                }

                return baseMsg;
            }
        }

        public override string ToString()
        {
            return Text;
        }

        public static bool IsWarningMessage(int code) => (code >= 2100 && code < 2200) || code == 1101 || code == 1102;
        public static bool IsConnectionLostCode(MessageCode code)
        {
            return code == MessageCode.MarketDataConnectionLost
                || code == MessageCode.TWSConnectionLost
                || code == MessageCode.TWSSocketPortHasBeenReset
                ;
        }

        public static bool IsConnectionReestablishedCode(MessageCode code)
        {
            return code == MessageCode.MarketDataConnectionEstablished
                || code == MessageCode.TWSConnectionRestored_MarketDataRequestsLost
                || code == MessageCode.TWSConnectionRestored_MarketDataRequestsRestored
                ;
        }

        //public static bool IsSystemMessage(int code) => code >= 1100 && code <= 1300;
        //public static bool IsClientErrorMessageException(int code) => 
        //    (code >= 501 && code <= 508 && code != 507) ||
        //    (code >= 510 && code <= 549) ||
        //    (code >= 551 && code <= 584) ||
        //    code == 10038;

        //public static bool IsTWSErrorMessageException(int code) =>
        //    (code >= 100 && code <= 168) ||
        //    (code >= 200 && code <= 449) ||
        //    code == 507 ||
        //    (code >= 10000 && code <= 10027) ||
        //    code == 10090 ||
        //    (code >= 10148 && code <= 10284);
    }

    public class ErrorMessageException: Exception
    {
        public ErrorMessageException(Exception innerException) : this(innerException.Message, innerException) { }
        public ErrorMessageException(string msg, Exception? innerException = null) : this(-1, -1, msg, innerException) { }
        public ErrorMessageException(int errorCode, string msg, Exception? innerException = null) : this(-1, errorCode, msg, innerException) { }
        public ErrorMessageException(int id, int errorCode, string msg, Exception? innerException = null) : this(new Message(id, errorCode, msg), innerException) { }
        public ErrorMessageException(Message msg, Exception? innerException = null) : base(msg.Text, innerException) 
        {
            ErrorMessage = msg;
        }

        public Message ErrorMessage { get; init; }
    }
}
