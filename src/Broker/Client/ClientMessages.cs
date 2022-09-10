using System;

namespace TradingBot.Broker.Client
{
    internal abstract class ClientMessage
    {
        public string Message { get; set; }
    }

    internal class ClientError : ClientMessage
    {
        public ClientError(int reqId, int errorCode, string message)
        {
            ReqId = reqId;
            ErrorCode = errorCode;
            Message = message;
        }

        public ClientError(string message)
        {
            Message = message;
        }

        public int ReqId { get; set; } 
        public int ErrorCode { get; set; }
    }

    internal class ClientException : ClientError
    {
        public ClientException(Exception e) : base(e.Message) => Exception = e;
        public Exception Exception { get; set; }
    }

    internal class ClientNotification : ClientMessage
    {
        public ClientNotification(string message) => Message = message;
    }
}
