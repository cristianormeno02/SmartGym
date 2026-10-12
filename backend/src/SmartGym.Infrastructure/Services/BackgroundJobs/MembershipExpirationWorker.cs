using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Memberships.Services;

namespace SmartGym.Infrastructure.Services.BackgroundJobs;

public class MembershipExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<MembershipsOptions> _options;
    private readonly ILogger<MembershipExpirationWorker> _logger;

    public MembershipExpirationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<MembershipsOptions> options,
        ILogger<MembershipExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MembershipExpirationWorker iniciado.");

        await RunExpirationBatchAsync(stoppingToken);

        var interval = _options.Value.ExpirationCheckInterval;
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromHours(1);
        }

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunExpirationBatchAsync(stoppingToken);
        }

        _logger.LogInformation("MembershipExpirationWorker detenido.");
    }

    public async Task RunExpirationBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var membershipService = scope.ServiceProvider.GetRequiredService<IMembershipService>();

            var result = await membershipService.ProcessExpirationsAndWarningsAsync(cancellationToken);

            if (result.ReactivatedCount > 0 ||
                result.ExpiredCount > 0 ||
                result.AutoCancelledCount > 0 ||
                result.WarningsSentCount > 0)
            {
                _logger.LogInformation(
                    "Lote de membresías procesado: {Reactivated} reactivadas, {Expired} vencidas, {AutoCancelled} canceladas por falta de pago, {Warnings} avisos enviados.",
                    result.ReactivatedCount,
                    result.ExpiredCount,
                    result.AutoCancelledCount,
                    result.WarningsSentCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Apagado normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la ejecución del lote de vencimientos y alertas de membresías.");
        }
    }
}
