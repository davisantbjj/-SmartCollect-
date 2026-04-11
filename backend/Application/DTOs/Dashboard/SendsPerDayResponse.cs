namespace SmartCollect.Application.DTOs.Dashboard;

public record SendsPerDayResponse(
    List<SendsDayItem> Items
);

public record SendsDayItem(
    string Day,
    int EmailCount,
    int WhatsAppCount
);
