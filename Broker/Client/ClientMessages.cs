using System;

namespace TradingBot.Broker.Client
{
    public abstract class ClientMessage
    {
        public string Message { get; set; }
    }

    public class ClientError : ClientMessage
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

    public class ClientException : ClientError
    {
        public ClientException(Exception e) : base(e.Message) => Exception = e;
        public Exception Exception { get; set; }
    }

    public class ClientNotification : ClientMessage
    {
        public ClientNotification(string message) => Message = message;
    }
}
