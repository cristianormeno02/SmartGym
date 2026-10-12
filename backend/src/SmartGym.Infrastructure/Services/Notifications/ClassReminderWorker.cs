using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Operations.Services;

namespace SmartGym.Infrastructure.Services.Notifications;

public class ClassReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<NotificationsOptions> _options;
    private readonly ILogger<ClassReminderWorker> _logger;

    public ClassReminderWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationsOptions> options,
        ILogger<ClassReminderWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClassReminderWorker iniciado.");

        await RunRemindersBatchAsync(stoppingToken);

        var intervalMinutes = Math.Max(1, _options.Value.ClassReminderPollIntervalMinutes);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(intervalMinutes));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunRemindersBatchAsync(stoppingToken);
        }

        _logger.LogInformation("ClassReminderWorker detenido.");
    }

    public async Task RunRemindersBatchAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var reminderService = scope.ServiceProvider.GetRequiredService<IClassReminderService>();

            var result = await reminderService.ProcessUpcomingClassRemindersAsync(
                _options.Value.ClassReminderLeadHours,
                cancellationToken);

            if (result.RemindersSent > 0)
            {
                _logger.LogInformation(
                    "Recordatorios de clases próximas procesados: {Sent} enviados de {Evaluated} candidatos ({Deduplicated} deduplicados).",
                    result.RemindersSent,
                    result.CandidatesEvaluated,
                    result.DeduplicatedCount);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Apagado normal
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el procesamiento de recordatorios de clases próximas.");
        }
    }
}
