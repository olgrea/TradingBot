using System;

namespace TradingBotV2.IBKR
{
    public class ErrorMessage : Exception
    {
        public ErrorMessage(Exception innerException) : base(innerException.Message, innerException) { }
        public ErrorMessage(string msg, Exception innerException = null) : base(msg, innerException) { }
        public ErrorMessage(int errorCode, string msg, Exception innerException = null) : this(-1, errorCode, msg, innerException) { }
        public ErrorMessage(int id, int errorCode, string msg, Exception innerException = null) : base(msg, innerException)
        {
            Id = id;
            ErrorCode = errorCode;
        }

        public int Id { get; set; } = -1;
        public int ErrorCode { get; set; } = -1;

        public override string Message
        {
            get
            {
                string baseMsg = base.Message;
                if (ErrorCode >= 0 || Id >= 0)
                {
                    baseMsg += " (";
                    if (ErrorCode >= 0)
                        baseMsg += $" code={ErrorCode} ";
                    if (Id >= 0)
                        baseMsg += $" rId={Id} ";
                    baseMsg += ")";
                }

                return baseMsg;
            }
        }

        public override string ToString()
        {
            return Message;
        }
    }
}
