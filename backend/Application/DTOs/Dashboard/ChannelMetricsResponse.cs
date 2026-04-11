namespace SmartCollect.Application.DTOs.Dashboard;

public record ChannelMetricsResponse(
    int EmailSent,
    int EmailDelivered,
    int EmailViewed,
    int WhatsAppSent,
    int WhatsAppDelivered,
    int WhatsAppViewed
);
