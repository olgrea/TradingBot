using System;

namespace TradingBot.Broker.Orders
{
    public enum Status
    {
        Unknown, ApiPending, PendingSubmit, PendingCancel, PreSubmitted, Submitted, ApiCancelled, Cancelled, Filled, Inactive
    }

    public class OrderState
    {
        public Status Status { get; set; }
        public string WarningText { get; set; }
        public DateTime CompletedTime { get; set; }
        public string CompletedStatus { get; set; }
    }
}
