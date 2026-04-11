namespace SmartCollect.Application.Interfaces;

public interface IDispatchDeliveryService
{
    Task<int> ProcessPendingDispatchesAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<bool> SendQuickEmailAsync(
        Guid tenantId,
        string recipientName,
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}