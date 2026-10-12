using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Operations.Services;

namespace SmartGym.Infrastructure.Services.Notifications;

public class NotificationDeliveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<NotificationsOptions> _options;
    private readonly ILogger<NotificationDeliveryWorker> _logger;

    public NotificationDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationsOptions> options,
        ILogger<NotificationDeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationDeliveryWorker iniciado.");

        await RunDeliveryCycleAsync(stoppingToken);

        var intervalSeconds = Math.Max(1, _options.Value.DeliveryPollIntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunDeliveryCycleAsync(stoppingToken);
        }

        _logger.LogInformation("NotificationDeliveryWorker detenido.");
    }

    public async Task RunDeliveryCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<INotificationDeliveryProcessor>();

            var recoveredCount = await processor.RecoverStaleProcessingDeliveriesAsync(cancellationToken);
            if (recoveredCount > 0)
            {
                _logger.LogInformation(
                    "Se recuperaron {RecoveredCount} entregas de notificaciones estancadas en estado Processing.",
                    recoveredCount);
            }

            var processedCount = await processor.ProcessPendingBatchAsync(
                _options.Value.DeliveryBatchSize,
                cancellationToken);

            if (processedCount > 0)
            {
                _logger.LogInformation(
                    "Se procesaron {ProcessedCount} entregas de notificaciones pendientes.",
                    processedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Apagado normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el ciclo de procesamiento de entregas de notificaciones.");
        }
    }
}
