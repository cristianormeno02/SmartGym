using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Reservations;

namespace SmartGym.Application.Modules.Operations.Handlers;

/// <summary>
/// Crea la notificación in-app del alumno ante cada cambio del ciclo de vida de su reserva.
/// Los eventos son post-commit: el handler corre en su propio scope, con la operación de reserva ya confirmada,
/// y guarda por su cuenta. Una falla aquí se registra en el log y no afecta a la reserva.
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

    public NotifyStudentOnReservationLifecycleHandler(ISmartGymDbContext dbContext, IGymCalendar gymCalendar)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
    }

    public Task HandleAsync(ReservationConfirmedEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            NotificationType.ReservationConfirmed,
            c => $"Reserva confirmada: {c.Activity} ({c.Date})",
            c => $"Tu reserva para la clase de {c.Activity} del día {c.Date} a las {c.Time} está confirmada.",
            cancellationToken);

    public Task HandleAsync(ReservationWaitlistedEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            NotificationType.ReservationWaitlisted,
            c => $"Lista de espera: {c.Activity} ({c.Date})",
            c => $"La clase de {c.Activity} del día {c.Date} a las {c.Time} está completa. Quedaste en lista de espera, en la posición {domainEvent.WaitListPosition}.",
            cancellationToken);

    public Task HandleAsync(ReservationCancelledEvent domainEvent, CancellationToken cancellationToken = default)
    {
        // El cambio de clase ya lo avisa NotifyStudentsOnSessionChangeHandler.
        if (domainEvent.CancellationSource == ReservationCancellationSource.ClassSessionChange)
        {
            return Task.CompletedTask;
        }

        var penalty = domainEvent.IsLateCancellation
            ? " Al ser una cancelación fuera de término, el crédito o derecho utilizado no se restituye."
            : string.Empty;

        return NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            NotificationType.ReservationCancelled,
            c => $"Reserva cancelada: {c.Activity} ({c.Date})",
            c => $"Se canceló tu reserva para la clase de {c.Activity} del día {c.Date} a las {c.Time}. Motivo: {domainEvent.Reason}.{penalty}",
            cancellationToken);
    }

    public Task HandleAsync(ReservationPromotedFromWaitlistEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            NotificationType.ReservationPromotedFromWaitlist,
            c => $"¡Tenés lugar!: {c.Activity} ({c.Date})",
            c => $"Se liberó un cupo y tu reserva para la clase de {c.Activity} del día {c.Date} a las {c.Time} quedó confirmada.",
            cancellationToken);

    public Task HandleAsync(WaitlistOfferCreatedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        var expiresLocal = _gymCalendar.ToLocal(domainEvent.OfferExpiresAtUtc);

        return NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            NotificationType.WaitlistSpotOffered,
            c => $"Cupo disponible: {c.Activity} ({c.Date})",
            c => $"Se liberó un cupo en la clase de {c.Activity} del día {c.Date} a las {c.Time}. Tenés hasta el {expiresLocal:dd/MM} a las {expiresLocal:HH:mm} para aceptarlo.",
            cancellationToken);
    }

    public Task HandleAsync(WaitlistOfferExpiredEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.ClassSessionId,
            domainEvent.StudentId,
            NotificationType.WaitlistOfferExpired,
            c => $"Oferta de cupo vencida: {c.Activity} ({c.Date})",
            c => $"Venció el plazo para aceptar el cupo en la clase de {c.Activity} del día {c.Date} a las {c.Time}.",
            cancellationToken);

    private sealed record ClassContext(string Activity, string Date, string Time);

    private async Task NotifyAsync(
        Guid classSessionId,
        Guid studentId,
        NotificationType type,
        Func<ClassContext, string> title,
        Func<ClassContext, string> body,
        CancellationToken cancellationToken)
    {
        var session = await _dbContext.ClassSessions
            .AsNoTracking()
            .Where(cs => cs.Id == classSessionId)
            .Select(cs => new { cs.StartsAtUtc, ActivityName = cs.Activity.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (session == null)
        {
            return;
        }

        var localStart = _gymCalendar.ToLocal(session.StartsAtUtc);
        var context = new ClassContext(session.ActivityName, $"{localStart:dd/MM}", $"{localStart:HH:mm}");
        var nowUtc = _gymCalendar.UtcNow;

        _dbContext.NotificationMessages.Add(new NotificationMessage
        {
            Id = Guid.NewGuid(),
            RecipientPersonId = studentId,
            Type = type,
            Channel = NotificationChannel.InApp,
            Title = title(context),
            Body = body(context),
            IsRead = false,
            SentAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
