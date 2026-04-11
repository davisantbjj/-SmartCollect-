namespace SmartCollect.Domain.Enums;

public enum DispatchStatus
{
    Pending,
    Sent,
    Delivered,
    Viewed,
    Error,
    Cancelled   // Cancelled because title was paid/cancelled before dispatch
}
