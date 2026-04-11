namespace SmartCollect.Application.DTOs.Dashboard;

public record FunnelDataResponse(
    List<FunnelItem> Items
);

public record FunnelItem(
    string Month,
    decimal Receivable,
    decimal Overdue,
    decimal Recovered
);
