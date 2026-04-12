namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Common;

public interface IDispatchDeliveryService
{
    Task<int> ProcessPendingDispatchesAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<QuickSendResult> SendQuickEmailAsync(
        Guid tenantId,
        string recipientName,
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
    Task<QuickSendResult> SendQuickWhatsAppAsync(
        Guid tenantId,
        string recipientName,
        string recipientPhone,
        string body,
        CancellationToken cancellationToken = default);
}