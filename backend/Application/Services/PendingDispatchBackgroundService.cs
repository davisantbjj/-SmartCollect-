namespace SmartCollect.Application.Services;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartCollect.Application.Interfaces;

public class PendingDispatchBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingDispatchBackgroundService> _logger;

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
}