namespace SmartCollect.Application.DTOs.Dashboard;

public record DashboardSummaryResponse(
    decimal TotalReceivable,
    decimal TotalOverdue,
    decimal TotalPaid,
    decimal RecoveryRate
);
