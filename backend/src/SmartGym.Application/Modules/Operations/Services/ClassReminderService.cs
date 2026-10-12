using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Operations.Services;

public class ClassReminderService : IClassReminderService
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly INotificationDispatcher _dispatcher;
    private readonly IGymCalendar _gymCalendar;
    private readonly NotificationsOptions _options;
    private readonly ILogger<ClassReminderService>? _logger;

    public ClassReminderService(
        ISmartGymDbContext dbContext,
        INotificationDispatcher dispatcher,
        IGymCalendar? gymCalendar = null,
        IOptions<NotificationsOptions>? options = null,
        ILogger<ClassReminderService>? logger = null)
    {
        _dbContext = dbContext;
        _dispatcher = dispatcher;
        _gymCalendar = gymCalendar ?? new GymCalendar(Options.Create(new GymOptions()));
        _options = options?.Value ?? new NotificationsOptions();
        _logger = logger;
    }

    public async Task<ClassReminderBatchResult> ProcessUpcomingClassRemindersAsync(
        int? leadHours = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveLeadHours = Math.Max(1, leadHours ?? _options.ClassReminderLeadHours);
        var nowUtc = _gymCalendar.UtcNow;
        var windowEndUtc = nowUtc.AddHours(effectiveLeadHours);

        var candidates = await _dbContext.Reservations
            .AsNoTracking()
            .Where(r => r.IsActive &&
                        r.Status == ReservationStatus.Confirmed &&
                        r.ClassSession.IsActive &&
                        (r.ClassSession.Status == ClassSessionStatus.Open ||
                         r.ClassSession.Status == ClassSessionStatus.Scheduled) &&
                        r.ClassSession.StartsAtUtc > nowUtc &&
                        r.ClassSession.StartsAtUtc <= windowEndUtc)
            .Select(r => new
            {
                ReservationId = r.Id,
                r.StudentId,
                r.ClassSessionId,
                r.ClassSession.StartsAtUtc,
                ActivityName = r.ClassSession.Activity != null ? r.ClassSession.Activity.Name : "Actividad",
                RoomName = r.ClassSession.Room != null ? r.ClassSession.Room.Name : "Sala"
            })
            .ToListAsync(cancellationToken);

        var remindersSent = 0;
        var deduplicatedCount = 0;

        // Un único round-trip descarta las reservas ya recordadas en barridos anteriores.
        var candidateKeys = candidates.Select(c => ReminderKey(c.ReservationId)).ToList();
        var alreadyReminded = (await _dbContext.NotificationMessages
                .AsNoTracking()
                .Where(m => m.IdempotencyKey != null && candidateKeys.Contains(m.IdempotencyKey))
                .Select(m => new { m.RecipientPersonId, m.IdempotencyKey })
                .ToListAsync(cancellationToken))
            .Select(m => (m.RecipientPersonId, m.IdempotencyKey!))
            .ToHashSet();

        foreach (var candidate in candidates)
        {
            if (alreadyReminded.Contains((candidate.StudentId, ReminderKey(candidate.ReservationId))))
            {
                deduplicatedCount++;
                continue;
            }

            try
            {
                var localStart = _gymCalendar.ToLocal(candidate.StartsAtUtc);
                var result = await _dispatcher.DispatchAsync(
                    new DispatchNotificationCommand(
                        TargetPersonId: candidate.StudentId,
                        Type: NotificationType.ClassReminder,
                        Variables: new Dictionary<string, string?>
                        {
                            ["activityName"] = candidate.ActivityName,
                            ["sessionDate"] = $"{localStart:dd/MM} {localStart:HH:mm}",
                            ["roomName"] = candidate.RoomName
                        },
                        IdempotencyKey: ReminderKey(candidate.ReservationId),
                        RelatedEntityType: "Reservation",
                        RelatedEntityId: candidate.ReservationId,
                        ActionUrl: "/portal/alumno",
                        ExpiresAtUtc: candidate.StartsAtUtc),
                    cancellationToken);

                if (result.CreatedMessages.Count > 0)
                {
                    remindersSent++;
                }

                if (result.DeduplicatedCount > 0)
                {
                    deduplicatedCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(
                    ex,
                    "Error al procesar el recordatorio de clase para la reserva {ReservationId}.",
                    candidate.ReservationId);
            }
        }

        return new ClassReminderBatchResult(candidates.Count, remindersSent, deduplicatedCount);
    }

    private static string ReminderKey(Guid reservationId) => $"class-reminder:{reservationId}";
}
