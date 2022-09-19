using System;

namespace TradingBot.Broker.Orders
{
    internal enum Status
    {
        Unknown, ApiPending, PendingSubmit, PendingCancel, PreSubmitted, Submitted, ApiCancelled, Cancelled, Filled, Inactive
    }

    internal class OrderState
    {
        public Status Status { get; set; }
        public string WarningText { get; set; }
        public DateTime CompletedTime { get; set; }
        public string CompletedStatus { get; set; }
        
        public double Commission { get; set; }
        public double MinCommission { get; set; }
        public double MaxCommission { get; set; }
        public string CommissionCurrency { get; set; }
    }
}
