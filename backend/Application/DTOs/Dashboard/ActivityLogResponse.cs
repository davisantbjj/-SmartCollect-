namespace SmartCollect.Application.DTOs.Dashboard;

public record ActivityLogResponse(
    List<ActivityLogItem> Items
);

public record ActivityLogItem(
    Guid Id,
    DateTime Timestamp,
    string Channel,
    string Status,
    string Recipient,
    string Summary
);
