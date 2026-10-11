using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;

namespace SmartGym.Application.Modules.Operations.Handlers;

/// <summary>
/// Crea notificaciones in-app para los alumnos con reservas activas cuando una sesión es suspendida,
/// cancelada, reprogramada o cambia de sala. Muestra fecha y hora locales en los mensajes.
/// </summary>
public class NotifyStudentsOnSessionChangeHandler :
    IDomainEventHandler<ClassSessionSuspended>,
    IDomainEventHandler<ClassSessionCancelled>,
    IDomainEventHandler<ClassSessionRescheduled>,
    IDomainEventHandler<ClassSessionRoomChanged>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;

    public NotifyStudentsOnSessionChangeHandler(ISmartGymDbContext dbContext, IGymCalendar gymCalendar)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
    }

    public async Task HandleAsync(ClassSessionSuspended domainEvent, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(domainEvent.SessionId, cancellationToken);
        if (session == null) return;

        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);
        var title = $"Clase Suspendida: {session.Activity?.Name ?? "Actividad"} ({localStart:dd/MM})";
        var reasonText = !string.IsNullOrWhiteSpace(domainEvent.Observations)
            ? $"{domainEvent.Reason} - {domainEvent.Observations}"
            : domainEvent.Reason.ToString();
        var body = $"Te informamos que la clase de {session.Activity?.Name ?? "actividad"} del día {localStart:dd/MM} a las {localStart:HH:mm} ha sido suspendida. Motivo: {reasonText}. Se ha reintegrado/compensado el crédito correspondiente.";

        await CreateNotificationsAsync(domainEvent.SessionId, title, body, sessionVoided: true, cancellationToken);
    }

    public async Task HandleAsync(ClassSessionCancelled domainEvent, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(domainEvent.SessionId, cancellationToken);
        if (session == null) return;

        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);
        var title = $"Clase Cancelada: {session.Activity?.Name ?? "Actividad"} ({localStart:dd/MM})";
        var reasonText = !string.IsNullOrWhiteSpace(domainEvent.Observations)
            ? $"{domainEvent.Reason} - {domainEvent.Observations}"
            : domainEvent.Reason.ToString();
        var body = $"La clase de {session.Activity?.Name ?? "actividad"} del día {localStart:dd/MM} a las {localStart:HH:mm} fue cancelada. Motivo: {reasonText}. Se ha reintegrado/compensado el crédito correspondiente.";

        await CreateNotificationsAsync(domainEvent.SessionId, title, body, sessionVoided: true, cancellationToken);
    }

    public async Task HandleAsync(ClassSessionRescheduled domainEvent, CancellationToken cancellationToken = default)
    {
        var originalSession = await LoadSessionAsync(domainEvent.OriginalSessionId, cancellationToken);
        if (originalSession == null) return;

        var origLocal = _gymCalendar.ToLocal(originalSession.StartsAtUtc);
        var newLocal = _gymCalendar.ToLocal(domainEvent.NewStartsAtUtc);
        var title = $"Clase Reprogramada: {originalSession.Activity?.Name ?? "Actividad"} ({origLocal:dd/MM})";
        var body = $"La clase de {originalSession.Activity?.Name ?? "actividad"} originalmente prevista para el día {origLocal:dd/MM} a las {origLocal:HH:mm} ha sido reprogramada para el día {newLocal:dd/MM} a las {newLocal:HH:mm}.";

        await CreateNotificationsAsync(domainEvent.OriginalSessionId, title, body, sessionVoided: true, cancellationToken);
    }

    public async Task HandleAsync(ClassSessionRoomChanged domainEvent, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(domainEvent.SessionId, cancellationToken);
        if (session == null) return;

        var newRoom = await _dbContext.Rooms.FirstOrDefaultAsync(r => r.Id == domainEvent.NewRoomId, cancellationToken);
        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);
        var title = $"Cambio de Sala: {session.Activity?.Name ?? "Actividad"} ({localStart:dd/MM})";
        var body = $"La clase de {session.Activity?.Name ?? "actividad"} del día {localStart:dd/MM} a las {localStart:HH:mm} se dictará en la sala '{newRoom?.Name ?? "nueva sala"}'.";

        await CreateNotificationsAsync(domainEvent.SessionId, title, body, sessionVoided: false, cancellationToken);
    }

    private Task<ClassSession?> LoadSessionAsync(Guid sessionId, CancellationToken ct) =>
        _dbContext.ClassSessions
            .Include(cs => cs.Activity)
            .FirstOrDefaultAsync(cs => cs.Id == sessionId, ct);

    private async Task CreateNotificationsAsync(
        Guid sessionId,
        string title,
        string body,
        bool sessionVoided,
        CancellationToken cancellationToken)
    {
        // Alumnos con reserva activa o en lista de espera. Cuando la clase se anula, CancelReservationsOnSessionVoidedHandler
        // puede haber cancelado y guardado esas reservas antes de este handler: se las reconoce por su origen de
        // cancelación, que sólo ocurre una vez por clase (la anulación es terminal).
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

        if (!studentIds.Any()) return;

        var nowUtc = _gymCalendar.UtcNow;
        var notifications = studentIds.Select(studentId => new NotificationMessage
        {
            Id = Guid.NewGuid(),
            RecipientPersonId = studentId,
            Type = NotificationType.ClassSuspended,
            Channel = NotificationChannel.InApp,
            Title = title,
            Body = body,
            IsRead = false,
            SentAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        }).ToList();

        _dbContext.NotificationMessages.AddRange(notifications);
    }
}
