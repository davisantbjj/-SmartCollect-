namespace SmartCollect.Application.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartCollect.Application.Interfaces;

public class PendingDispatchBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PendingTitlesSyncInterval = TimeSpan.FromHours(12);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingDispatchBackgroundService> _logger;
    private DateTime _lastPendingTitlesSyncUtc = DateTime.MinValue;
    private DateTime _lastOccurrencesSyncUtc = DateTime.MinValue;

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
}