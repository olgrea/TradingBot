using System;
using System.Collections.Generic;
using System.Text;

namespace TradingBot.Broker
{
    public enum OrderStatus
    {
        ApiPending, PendingSubmit, PendingCancel, PreSubmitted, Submitted, ApiCancelled, Cancelled, Filled, Inactive
    }

    public class OrderState
    {
        public OrderStatus Status { get; set; }
        public decimal Commission { get; set; }
        public decimal MinCommission { get; set; }
        public decimal MaxCommission { get; set; }
        public string CommissionCurrency { get; set; }
        public string WarningText { get; set; }
        public DateTime CompletedTime { get; set; }
        public string CompletedStatus { get; set; }
    }
}
