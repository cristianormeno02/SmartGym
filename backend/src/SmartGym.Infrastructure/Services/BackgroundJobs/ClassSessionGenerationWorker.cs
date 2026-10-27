using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Activities.Services;

namespace SmartGym.Infrastructure.Services.BackgroundJobs;

public class ClassSessionGenerationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ClassSessionsOptions> _options;
    private readonly IGymCalendar _gymCalendar;
    private readonly ILogger<ClassSessionGenerationWorker> _logger;

    public ClassSessionGenerationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ClassSessionsOptions> options,
        IGymCalendar gymCalendar,
        ILogger<ClassSessionGenerationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _gymCalendar = gymCalendar;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClassSessionGenerationWorker iniciado.");

        // First execution on startup
        await RunGenerationAndAutoOpenAsync(stoppingToken);

        var interval = _options.Value.GenerationInterval;
        if (interval <= TimeSpan.Zero)
        {
            interval = TimeSpan.FromHours(1);
        }

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunGenerationAndAutoOpenAsync(stoppingToken);
        }

        _logger.LogInformation("ClassSessionGenerationWorker detenido.");
    }

    public async Task RunGenerationAndAutoOpenAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var generator = scope.ServiceProvider.GetRequiredService<IClassSessionGenerator>();
            var scheduleSync = scope.ServiceProvider.GetService<IClassSessionScheduleSync>();

            var today = _gymCalendar.Today;
            var windowDays = _options.Value.WindowDays;
            var windowEnd = today.AddDays(windowDays);

            if (scheduleSync != null)
            {
                _logger.LogInformation("Sincronizando sesiones futuras con horarios activos...");
                var syncedCount = await scheduleSync.SyncAllActiveAsync(cancellationToken: cancellationToken);
                _logger.LogInformation("Sincronización de horarios completada para {Count} horarios activos.", syncedCount);
            }

            _logger.LogInformation("Iniciando generación de sesiones de clase [{From} a {To}]...", today, windowEnd);
            var result = await generator.GenerateAsync(today, windowEnd, scheduleId: null, cancellationToken);
            _logger.LogInformation("Generación completada: {Created} creadas, {Skipped} omitidas, {Conflicts} conflictos.",
                result.Created, result.Skipped, result.Conflicts.Count);

            var autoOpened = await generator.AutoOpenAsync(cancellationToken: cancellationToken);
            if (autoOpened > 0)
            {
                _logger.LogInformation("Apertura automática completada: {Opened} sesiones abiertas.", autoOpened);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la ejecución del proceso de generación/apertura de sesiones de clase.");
        }
    }
}
