using System;

namespace TradingBotV2.Broker.Orders
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

        public double Commission { get; set; }
        public double MinCommission { get; set; }
        public double MaxCommission { get; set; }
        public string CommissionCurrency { get; set; }

        public static explicit operator OrderState(IBApi.OrderState ibo)
        {
            return new OrderState()
            {
                Status = Enum.Parse<Status>(ibo.Status),
                WarningText = ibo.WarningText,
                CompletedStatus = ibo.CompletedStatus,
                CompletedTime = ibo.CompletedTime != null ? DateTime.Parse(ibo.CompletedTime) : DateTime.MinValue,

                Commission = ibo.Commission,
                MinCommission = ibo.MinCommission,
                MaxCommission = ibo.MaxCommission,
                CommissionCurrency = ibo.CommissionCurrency,
            };
        }
    }
}
