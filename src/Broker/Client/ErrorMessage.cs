using System;

namespace TradingBot.Broker.Client
{
    internal class ErrorMessage
    {
        public ErrorMessage(int id, int errorCode, string msg)
        {
            Id = id;
            Message = msg;
            ErrorCode = errorCode;
        }

        public ErrorMessage(string msg)
        {
            Message = msg;  
        }

        public int Id { get; set; } = -1;
        public string Message { get; set; }
        public int ErrorCode { get; set; } = -1;
    }

    internal class APIError : ErrorMessage
    {
        public APIError(Exception ex) : base(ex.Message)
        {
            Exception = ex;
        }

        public Exception Exception { get; }
    }

    internal class ClientException : Exception
    {
        public ClientException(string msg, Exception innerException = null) : base(msg, innerException) { }
        public ClientException(int id, int errorCode, string msg, Exception innerException = null) : base(msg, innerException) 
        {
            Id = id;
            ErrorCode = errorCode;
        }

        public int Id { get; set; } = -1;
        public int ErrorCode { get; set; } = -1;
    }
}
