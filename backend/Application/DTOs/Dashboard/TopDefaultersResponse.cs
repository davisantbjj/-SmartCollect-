namespace SmartCollect.Application.DTOs.Dashboard;

public record TopDefaultersResponse(
    List<DefaulterItem> Items
);

public record DefaulterItem(
    string ClientName,
    string TaxId,
    decimal TotalAmount,
    int TitleCount
);
