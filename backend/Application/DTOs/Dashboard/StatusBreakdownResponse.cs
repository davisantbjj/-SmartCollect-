namespace SmartCollect.Application.DTOs.Dashboard;

public record StatusBreakdownResponse(
    int Open,
    int PendingData,
    int Overdue,
    int Paid,
    int Cancelled
);
