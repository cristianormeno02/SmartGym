using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Reservations;

namespace SmartGym.Application.Modules.Operations.Handlers;

/// <summary>
/// Crea la notificación del alumno (y del responsable si corresponde) ante cada cambio del ciclo de vida de su reserva.
/// Los eventos son post-commit: el handler corre en su propio scope, con la operación de reserva ya confirmada,
/// y delega en INotificationDispatcher con clave de idempotencia determinística.
/// </summary>
public class NotifyStudentOnReservationLifecycleHandler :
    IDomainEventHandler<ReservationConfirmedEvent>,
    IDomainEventHandler<ReservationWaitlistedEvent>,
    IDomainEventHandler<ReservationCancelledEvent>,
    IDomainEventHandler<ReservationPromotedFromWaitlistEvent>,
    IDomainEventHandler<WaitlistOfferCreatedEvent>,
    IDomainEventHandler<WaitlistOfferExpiredEvent>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly INotificationDispatcher _dispatcher;

    public NotifyStudentOnReservationLifecycleHandler(
        ISmartGymDbContext dbContext,
        IGymCalendar gymCalendar,
        INotificationDispatcher? dispatcher = null)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
        _dispatcher = dispatcher ?? new NotificationDispatcher(
            dbContext,
            new NotificationRecipientResolver(dbContext, gymCalendar),
            new NotificationTemplateRenderer(dbContext),
            new NotificationService(dbContext, gymCalendar),
            gymCalendar);
    }

    public Task HandleAsync(ReservationConfirmedEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            domainEvent.ReservationId,
            NotificationType.ReservationConfirmed,
            $"res-confirmed:{domainEvent.ReservationId}",
            c => $"Reserva confirmada: {c.Activity} ({c.Date})",
            c => $"Tu reserva para la clase de {c.Activity} del día {c.Date} a las {c.Time} está confirmada.",
            c => new Dictionary<string, string?>
            {
                ["activityName"] = c.Activity,
                ["sessionDate"] = $"{c.Date} {c.Time}",
                ["roomName"] = c.Room
            },
            expiresAtUtc: null,
            cancellationToken);

    public Task HandleAsync(ReservationWaitlistedEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            domainEvent.ReservationId,
            NotificationType.ReservationWaitlisted,
            $"res-waitlisted:{domainEvent.ReservationId}:{domainEvent.WaitListPosition}",
            c => $"Lista de espera: {c.Activity} ({c.Date})",
            c => $"La clase de {c.Activity} del día {c.Date} a las {c.Time} está completa. Quedaste en lista de espera, en la posición {domainEvent.WaitListPosition}.",
            c => new Dictionary<string, string?>
            {
                ["activityName"] = c.Activity,
                ["sessionDate"] = $"{c.Date} {c.Time}",
                ["waitlistPosition"] = domainEvent.WaitListPosition.ToString()
            },
            expiresAtUtc: null,
            cancellationToken);

    public Task HandleAsync(ReservationCancelledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        // El cambio de clase ya lo avisa NotifyStudentsOnSessionChangeHandler.
        // La anulación de presente en mostrador no envía notificación para evitar confusiones al alumno presencial.
        if (domainEvent.CancellationSource == ReservationCancellationSource.ClassSessionChange ||
            domainEvent.CancellationSource == ReservationCancellationSource.AttendanceRevoked)
        {
            return Task.CompletedTask;
        }

        var penalty = domainEvent.IsLateCancellation
            ? " Al ser una cancelación fuera de término, el crédito o derecho utilizado no se restituye."
            : string.Empty;

        return NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            domainEvent.ReservationId,
            NotificationType.ReservationCancelled,
            $"res-cancelled:{domainEvent.ReservationId}",
            c => $"Reserva cancelada: {c.Activity} ({c.Date})",
            c => $"Se canceló tu reserva para la clase de {c.Activity} del día {c.Date} a las {c.Time}. Motivo: {domainEvent.Reason}.{penalty}",
            c => new Dictionary<string, string?>
            {
                ["activityName"] = c.Activity,
                ["sessionDate"] = $"{c.Date} {c.Time}",
                ["cancellationReason"] = domainEvent.Reason.ToString(),
                ["creditOutcome"] = $"Motivo: {domainEvent.Reason}.{penalty}"
            },
            expiresAtUtc: null,
            cancellationToken);
    }

    public Task HandleAsync(ReservationPromotedFromWaitlistEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            domainEvent.ReservationId,
            NotificationType.ReservationPromotedFromWaitlist,
            $"res-promoted:{domainEvent.ReservationId}",
            c => $"¡Tenés lugar!: {c.Activity} ({c.Date})",
            c => $"Se liberó un cupo y tu reserva para la clase de {c.Activity} del día {c.Date} a las {c.Time} quedó confirmada.",
            c => new Dictionary<string, string?>
            {
                ["activityName"] = c.Activity,
                ["sessionDate"] = $"{c.Date} {c.Time}",
                ["roomName"] = c.Room
            },
            expiresAtUtc: null,
            cancellationToken);

    public Task HandleAsync(WaitlistOfferCreatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var expiresLocal = _gymCalendar.ToLocal(domainEvent.OfferExpiresAtUtc);
        var expiresFormatted = $"{expiresLocal:dd/MM} a las {expiresLocal:HH:mm}";

        return NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            domainEvent.ReservationId,
            NotificationType.WaitlistSpotOffered,
            $"waitlist-offer:{domainEvent.ReservationId}:{domainEvent.OfferExpiresAtUtc:O}",
            c => $"Cupo disponible: {c.Activity} ({c.Date})",
            c => $"Se liberó un cupo en la clase de {c.Activity} del día {c.Date} a las {c.Time}. Tenés hasta el {expiresFormatted} para aceptarlo.",
            c => new Dictionary<string, string?>
            {
                ["activityName"] = c.Activity,
                ["sessionDate"] = $"{c.Date} {c.Time}",
                ["offerExpiresAt"] = expiresFormatted
            },
            expiresAtUtc: domainEvent.OfferExpiresAtUtc,
            cancellationToken);
    }

    public Task HandleAsync(WaitlistOfferExpiredEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            domainEvent.ReservationId,
            NotificationType.WaitlistOfferExpired,
            $"waitlist-expired:{domainEvent.ReservationId}",
            c => $"Oferta de cupo vencida: {c.Activity} ({c.Date})",
            c => $"Venció el plazo para aceptar el cupo en la clase de {c.Activity} del día {c.Date} a las {c.Time}.",
            c => new Dictionary<string, string?>
            {
                ["activityName"] = c.Activity,
                ["sessionDate"] = $"{c.Date} {c.Time}"
            },
            expiresAtUtc: null,
            cancellationToken);

    private sealed record ClassContext(string Activity, string Date, string Time, string Room);

    private async Task NotifyAsync(
        Guid classSessionId,
        Guid studentId,
        Guid reservationId,
        NotificationType type,
        string idempotencyKey,
        Func<ClassContext, string> title,
        Func<ClassContext, string> body,
        Func<ClassContext, IReadOnlyDictionary<string, string?>> variablesFactory,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.ClassSessions
            .AsNoTracking()
            .Where(cs => cs.Id == classSessionId)
            .Select(cs => new
            {
                cs.StartsAtUtc,
                ActivityName = cs.Activity != null ? cs.Activity.Name : "Actividad",
                RoomName = cs.Room != null ? cs.Room.Name : "Sala"
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            return;
        }

        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);
        var context = new ClassContext(
            session.ActivityName,
            $"{localStart:dd/MM}",
            $"{localStart:HH:mm}",
            session.RoomName);

        await _dispatcher.DispatchAsync(
            new DispatchNotificationCommand(
                TargetPersonId: studentId,
                Type: type,
                Variables: variablesFactory(context),
                IdempotencyKey: idempotencyKey,
                RelatedEntityType: "Reservation",
                RelatedEntityId: reservationId,
                ActionUrl: "/portal/alumno",
                ExpiresAtUtc: expiresAtUtc,
                DirectTitleOverride: title(context),
                DirectBodyOverride: body(context),
                SaveChanges: true),
            cancellationToken);
    }
}
