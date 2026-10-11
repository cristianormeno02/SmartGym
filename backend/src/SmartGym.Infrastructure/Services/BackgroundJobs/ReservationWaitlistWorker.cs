using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;

namespace SmartGym.Infrastructure.Services.BackgroundJobs;

public class ReservationWaitlistWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ReservationsOptions> _options;
    private readonly ILogger<ReservationWaitlistWorker> _logger;

    public ReservationWaitlistWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ReservationsOptions> options,
        ILogger<ReservationWaitlistWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReservationWaitlistWorker iniciado.");

        await RunIterationAsync(stoppingToken);

        var intervalSeconds = _options.Value.WaitlistWorkerIntervalSeconds;
        var interval = intervalSeconds > 0
            ? TimeSpan.FromSeconds(intervalSeconds)
            : TimeSpan.FromSeconds(60);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunIterationAsync(stoppingToken);
        }

        _logger.LogInformation("ReservationWaitlistWorker detenido.");
    }

    public async Task<WaitlistProcessResultDto> RunIterationAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.EnableWaitlist)
        {
            return new WaitlistProcessResultDto(0, 0, 0);
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reservationService = scope.ServiceProvider.GetRequiredService<IReservationService>();
            var result = await reservationService.ProcessWaitlistAsync(cancellationToken);

            if (result.ExpiredOffersCancelled > 0 || result.StartedSessionsWaitlistExpired > 0 || result.CandidatesPromoted > 0)
            {
                _logger.LogInformation(
                    "Lote de lista de espera procesado: {ExpiredOffers} ofertas vencidas canceladas, {StartedWaitlists} listas vencidas al inicio, {Promoted} candidatos promovidos.",
                    result.ExpiredOffersCancelled,
                    result.StartedSessionsWaitlistExpired,
                    result.CandidatesPromoted);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new WaitlistProcessResultDto(0, 0, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el procesamiento de la lista de espera de reservas.");
            throw;
        }
    }
}
