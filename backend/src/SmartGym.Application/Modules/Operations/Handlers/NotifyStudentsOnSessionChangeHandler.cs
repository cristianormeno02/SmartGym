using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;

namespace SmartGym.Application.Modules.Operations.Handlers;

/// <summary>
/// Crea notificaciones para los alumnos con reservas activas cuando una sesión es suspendida,
/// cancelada, reprogramada o cambia de sala, utilizando el tipo de notificación específico e idempotencia.
/// </summary>
public class NotifyStudentsOnSessionChangeHandler :
    IDomainEventHandler<ClassSessionSuspended>,
    IDomainEventHandler<ClassSessionCancelled>,
    IDomainEventHandler<ClassSessionRescheduled>,
    IDomainEventHandler<ClassSessionRoomChanged>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly INotificationDispatcher _dispatcher;
    private readonly ILogger<NotifyStudentsOnSessionChangeHandler>? _logger;

    public NotifyStudentsOnSessionChangeHandler(
        ISmartGymDbContext dbContext,
        IGymCalendar gymCalendar,
        INotificationDispatcher? dispatcher = null,
        ILogger<NotifyStudentsOnSessionChangeHandler>? logger = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
        _dispatcher = dispatcher ?? new NotificationDispatcher(
            dbContext,
            new NotificationRecipientResolver(dbContext, gymCalendar),
            new NotificationTemplateRenderer(dbContext),
            new NotificationService(dbContext, gymCalendar),
            gymCalendar);
        _logger = logger;
    }

    public async Task HandleAsync(ClassSessionSuspended domainEvent, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(domainEvent.SessionId, cancellationToken);
        if (session == null) return;

        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);
        var activityName = session.Activity?.Name ?? "Actividad";
        var title = $"Clase Suspendida: {activityName} ({localStart:dd/MM})";
        var reasonText = !string.IsNullOrWhiteSpace(domainEvent.Observations)
            ? $"{domainEvent.Reason} - {domainEvent.Observations}"
            : domainEvent.Reason.ToString();
        var body = $"Te informamos que la clase de {session.Activity?.Name ?? "actividad"} del día {localStart:dd/MM} a las {localStart:HH:mm} ha sido suspendida. Motivo: {reasonText}. Se ha reintegrado/compensado el crédito correspondiente.";

        var variables = new Dictionary<string, string?>
        {
            ["activityName"] = activityName,
            ["sessionDate"] = $"{localStart:dd/MM} {localStart:HH:mm}",
            ["reason"] = reasonText
        };

        await CreateNotificationsAsync(
            domainEvent.SessionId,
            NotificationType.ClassSuspended,
            $"session-suspended:{domainEvent.SessionId}",
            title,
            body,
            variables,
            sessionVoided: true,
            cancellationToken);
    }

    public async Task HandleAsync(ClassSessionCancelled domainEvent, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(domainEvent.SessionId, cancellationToken);
        if (session == null) return;

        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);
        var activityName = session.Activity?.Name ?? "Actividad";
        var title = $"Clase Cancelada: {activityName} ({localStart:dd/MM})";
        var reasonText = !string.IsNullOrWhiteSpace(domainEvent.Observations)
            ? $"{domainEvent.Reason} - {domainEvent.Observations}"
            : domainEvent.Reason.ToString();
        var body = $"La clase de {session.Activity?.Name ?? "actividad"} del día {localStart:dd/MM} a las {localStart:HH:mm} fue cancelada. Motivo: {reasonText}. Se ha reintegrado/compensado el crédito correspondiente.";

        var variables = new Dictionary<string, string?>
        {
            ["activityName"] = activityName,
            ["sessionDate"] = $"{localStart:dd/MM} {localStart:HH:mm}",
            ["reason"] = reasonText
        };

        await CreateNotificationsAsync(
            domainEvent.SessionId,
            NotificationType.ClassCancelled,
            $"session-cancelled:{domainEvent.SessionId}",
            title,
            body,
            variables,
            sessionVoided: true,
            cancellationToken);
    }

    public async Task HandleAsync(ClassSessionRescheduled domainEvent, CancellationToken cancellationToken = default)
    {
        var originalSession = await LoadSessionAsync(domainEvent.OriginalSessionId, cancellationToken);
        if (originalSession == null) return;

        var origLocal = _gymCalendar.ToLocal(originalSession.StartsAtUtc);
        var newLocal = _gymCalendar.ToLocal(domainEvent.NewStartsAtUtc);
        var activityName = originalSession.Activity?.Name ?? "Actividad";
        var title = $"Clase Reprogramada: {activityName} ({origLocal:dd/MM})";
        var body = $"La clase de {originalSession.Activity?.Name ?? "actividad"} originalmente prevista para el día {origLocal:dd/MM} a las {origLocal:HH:mm} ha sido reprogramada para el día {newLocal:dd/MM} a las {newLocal:HH:mm}.";

        var variables = new Dictionary<string, string?>
        {
            ["activityName"] = activityName,
            ["sessionDate"] = $"{newLocal:dd/MM} {newLocal:HH:mm}",
            ["originalSessionDate"] = $"{origLocal:dd/MM} {origLocal:HH:mm}"
        };

        await CreateNotificationsAsync(
            domainEvent.OriginalSessionId,
            NotificationType.ClassRescheduled,
            $"session-rescheduled:{domainEvent.OriginalSessionId}:{domainEvent.NewSessionId}",
            title,
            body,
            variables,
            sessionVoided: true,
            cancellationToken);
    }

    public async Task HandleAsync(ClassSessionRoomChanged domainEvent, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(domainEvent.SessionId, cancellationToken);
        if (session == null) return;

        var newRoom = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == domainEvent.NewRoomId, cancellationToken);
        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);
        var activityName = session.Activity?.Name ?? "Actividad";
        var roomName = newRoom?.Name ?? "nueva sala";
        var title = $"Cambio de Sala: {activityName} ({localStart:dd/MM})";
        var body = $"La clase de {session.Activity?.Name ?? "actividad"} del día {localStart:dd/MM} a las {localStart:HH:mm} se dictará en la sala '{roomName}'.";

        var variables = new Dictionary<string, string?>
        {
            ["activityName"] = activityName,
            ["sessionDate"] = $"{localStart:dd/MM} {localStart:HH:mm}",
            ["roomName"] = roomName
        };

        await CreateNotificationsAsync(
            domainEvent.SessionId,
            NotificationType.ClassRoomChanged,
            // Una clase puede volver a una sala anterior: el instante del cambio distingue cada ocurrencia.
            $"session-room:{domainEvent.SessionId}:{domainEvent.NewRoomId}:{domainEvent.AtUtc.Ticks}",
            title,
            body,
            variables,
            sessionVoided: false,
            cancellationToken);
    }

    private Task<ClassSession?> LoadSessionAsync(Guid sessionId, CancellationToken ct) =>
        _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .FirstOrDefaultAsync(cs => cs.Id == sessionId, ct);

    private async Task CreateNotificationsAsync(
        Guid sessionId,
        NotificationType type,
        string idempotencyPrefix,
        string title,
        string body,
        IReadOnlyDictionary<string, string?> variables,
        bool sessionVoided,
        CancellationToken cancellationToken)
    {
        try
        {
            var studentIds = await _dbContext.Reservations
                .Where(r => r.ClassSessionId == sessionId && r.IsActive &&
                            (r.Status == ReservationStatus.Confirmed ||
                             r.Status == ReservationStatus.Waitlisted ||
                             r.Status == ReservationStatus.Pending ||
                             (sessionVoided &&
                              r.Status == ReservationStatus.Cancelled &&
                              r.CancellationSource == ReservationCancellationSource.ClassSessionChange)))
                .Select(r => r.StudentId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (studentIds.Count == 0) return;

            foreach (var studentId in studentIds)
            {
                await _dispatcher.DispatchAsync(
                    new DispatchNotificationCommand(
                        TargetPersonId: studentId,
                        Type: type,
                        Variables: variables,
                        IdempotencyKey: $"{idempotencyPrefix}:{studentId}",
                        RelatedEntityType: "ClassSession",
                        RelatedEntityId: sessionId,
                        ActionUrl: "/portal/alumno",
                        DirectTitleOverride: title,
                        DirectBodyOverride: body,
                        SaveChanges: false),
                    cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(
                ex,
                "Fallo aislado al preparar notificaciones de cambio de sesión {SessionId} ({Type}).",
                sessionId,
                type);
        }
    }
}
