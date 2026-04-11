namespace SmartCollect.Application.DTOs.Dashboard;

public record AgingListResponse(
    List<AgingItem> Items
);

public record AgingItem(
    string Range,
    decimal Value,
    string Color
);
