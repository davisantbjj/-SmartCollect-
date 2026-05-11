namespace SmartCollect.Application.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class PendingDispatchBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PendingTitlesSyncInterval = TimeSpan.FromHours(12);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingDispatchBackgroundService> _logger;
    private DateTime _lastPendingTitlesSyncUtc = DateTime.MinValue;
    private DateTime _lastOccurrencesSyncUtc = DateTime.MinValue;
    private DateTime _lastStatusRefreshUtc = DateTime.MinValue;

    public PendingDispatchBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<PendingDispatchBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await RefreshTitleStatusesAsync(scope.ServiceProvider, stoppingToken);
                await ExecuteAutomaticSyncsAsync(scope.ServiceProvider, stoppingToken);

                var deliveryService = scope.ServiceProvider.GetRequiredService<IDispatchDeliveryService>();
                var processed = await deliveryService.ProcessPendingDispatchesAsync(null, stoppingToken);

                if (processed > 0)
                    _logger.LogInformation("Dispatch engine processed {Count} pending sends.", processed);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pending dispatch background cycle failed.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ExecuteAutomaticSyncsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var shouldSyncPendingTitles = now - _lastPendingTitlesSyncUtc >= PendingTitlesSyncInterval;
        var shouldSyncOccurrences = _lastOccurrencesSyncUtc.Date < now.Date;

        if (!shouldSyncPendingTitles && !shouldSyncOccurrences)
            return;

        var db = services.GetRequiredService<IAppDbContext>();
        var syncService = services.GetRequiredService<ISyncService>();

        var tenantIds = await db.Tenants
            .Where(t => t.Active)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (tenantIds.Count == 0)
        {
            if (shouldSyncPendingTitles)
                _lastPendingTitlesSyncUtc = now;

            if (shouldSyncOccurrences)
                _lastOccurrencesSyncUtc = now;

            return;
        }

        var anyPendingSyncSucceeded = false;
        var anyOccurrenceSyncSucceeded = false;

        foreach (var tenantId in tenantIds)
        {
            if (shouldSyncPendingTitles)
            {
                try
                {
                    var syncedTitles = await syncService.SyncPendingTitlesAsync(tenantId);
                    anyPendingSyncSucceeded = true;

                    if (syncedTitles > 0)
                        _logger.LogInformation(
                            "Automatic sync imported {Count} pending titles for tenant {TenantId}.",
                            syncedTitles,
                            tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Automatic pending-title sync failed for tenant {TenantId}.", tenantId);
                }
            }

            if (shouldSyncOccurrences)
            {
                try
                {
                    var syncedOccurrences = await syncService.SyncOccurrencesAsync(tenantId);
                    anyOccurrenceSyncSucceeded = true;

                    if (syncedOccurrences > 0)
                        _logger.LogInformation(
                            "Automatic sync processed {Count} occurrences for tenant {TenantId}.",
                            syncedOccurrences,
                            tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Automatic occurrence sync failed for tenant {TenantId}.", tenantId);
                }
            }
        }

        if (shouldSyncPendingTitles && anyPendingSyncSucceeded)
            _lastPendingTitlesSyncUtc = now;

        if (shouldSyncOccurrences && anyOccurrenceSyncSucceeded)
            _lastOccurrencesSyncUtc = now;
    }

    private async Task RefreshTitleStatusesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var nowDate = DateTime.UtcNow.Date;
        if (_lastStatusRefreshUtc.Date == nowDate)
            return;

        var db = services.GetRequiredService<IAppDbContext>();

        var tenantIds = await db.Tenants
            .Where(t => t.Active)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (tenantIds.Count == 0)
        {
            _lastStatusRefreshUtc = DateTime.UtcNow;
            return;
        }

        foreach (var tenantId in tenantIds)
        {
            var titles = await db.Titles
                .Include(t => t.Client)
                    .ThenInclude(c => c.Contacts)
                .Where(t => t.TenantId == tenantId && t.Status != TitleStatus.Paid && t.Status != TitleStatus.Cancelled)
                .ToListAsync(cancellationToken);

            var changed = 0;
            foreach (var title in titles)
            {
                var hasContactInfo = title.Client.Contacts.Any(c =>
                    HasMeaningfulEmail(c.Email) || HasMeaningfulPhone(c.WhatsAppPhone));

                var nextStatus = !hasContactInfo
                    ? TitleStatus.PendingData
                    : title.DueDate.Date < nowDate
                        ? TitleStatus.Overdue
                        : TitleStatus.Open;

                if (title.Status == nextStatus)
                    continue;

                var oldStatus = title.Status;
                title.Status = nextStatus;
                changed += 1;

                await db.TitleHistories.AddAsync(new Domain.Entities.TitleHistory
                {
                    Id = Guid.NewGuid(),
                    TitleId = title.Id,
                    TenantId = tenantId,
                    Action = "Atualizacao automatica",
                    Description = $"Status ajustado de {oldStatus} para {nextStatus} pela verificacao de vencimento"
                }, cancellationToken);
            }

            if (changed > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Auto status refresh updated {Count} titles for tenant {TenantId}.", changed, tenantId);
            }
        }

        _lastStatusRefreshUtc = DateTime.UtcNow;
    }

    private static bool HasMeaningfulEmail(string? email)
        => !string.IsNullOrWhiteSpace(email);

    private static bool HasMeaningfulPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length >= 10;
    }
}