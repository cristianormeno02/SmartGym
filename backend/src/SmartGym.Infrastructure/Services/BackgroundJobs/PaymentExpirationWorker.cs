using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Operations.Services;

namespace SmartGym.Infrastructure.Services.BackgroundJobs;

/// <summary>Cancela los checkouts externos que vencieron sin pagarse, para que la membresía admita uno nuevo.</summary>
public class PaymentExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PaymentsOptions> _options;
    private readonly ILogger<PaymentExpirationWorker> _logger;

    public PaymentExpirationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PaymentsOptions> options,
        ILogger<PaymentExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentExpirationWorker iniciado.");

        await RunExpirationBatchAsync(stoppingToken);

        var interval = _options.Value.ExpirationCheckInterval;
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromMinutes(5);
        }

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunExpirationBatchAsync(stoppingToken);
        }

        _logger.LogInformation("PaymentExpirationWorker detenido.");
    }

    public async Task RunExpirationBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var paymentService = scope.ServiceProvider.GetRequiredService<IPaymentService>();

            var expired = await paymentService.ExpireCheckoutsAsync(cancellationToken);
            if (expired > 0)
            {
                _logger.LogInformation("Checkouts vencidos cancelados: {Expired}.", expired);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Apagado normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la cancelación de checkouts vencidos.");
        }
    }
}
