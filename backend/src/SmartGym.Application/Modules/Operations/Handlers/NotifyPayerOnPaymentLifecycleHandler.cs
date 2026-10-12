using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;

namespace SmartGym.Application.Modules.Operations.Handlers;

/// <summary>
/// Crea la notificación in-app del pagador ante la confirmación, el rechazo o la devolución de un pago.
/// Los eventos son post-commit: el handler corre con el pago ya confirmado en la base y guarda por su cuenta.
/// Una falla aquí se registra en el log y no afecta al pago ni a la membresía.
/// </summary>
public class NotifyPayerOnPaymentLifecycleHandler :
    IDomainEventHandler<PaymentConfirmedNotificationEvent>,
    IDomainEventHandler<PaymentFailedNotificationEvent>,
    IDomainEventHandler<PaymentRefundedNotificationEvent>
{
    private readonly ISmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;

    public NotifyPayerOnPaymentLifecycleHandler(ISmartGymDbContext dbContext, IGymCalendar gymCalendar)
    {
        _dbContext = dbContext;
        _gymCalendar = gymCalendar;
    }

    public Task HandleAsync(PaymentConfirmedNotificationEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.PayerPersonId,
            NotificationType.PaymentConfirmed,
            "Pago confirmado",
            $"Registramos tu pago de {Money(domainEvent.Amount, domainEvent.Currency)}.",
            cancellationToken);

    public Task HandleAsync(PaymentFailedNotificationEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.PayerPersonId,
            NotificationType.PaymentFailed,
            "Pago rechazado",
            string.IsNullOrWhiteSpace(domainEvent.Reason)
                ? $"Tu pago de {Money(domainEvent.Amount, domainEvent.Currency)} fue rechazado."
                : $"Tu pago de {Money(domainEvent.Amount, domainEvent.Currency)} fue rechazado. Motivo: {domainEvent.Reason}.",
            cancellationToken);

    public Task HandleAsync(PaymentRefundedNotificationEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.PayerPersonId,
            NotificationType.PaymentRefunded,
            "Devolución registrada",
            $"Registramos una devolución de {Money(domainEvent.RefundAmount, domainEvent.Currency)}.",
            cancellationToken);

    private static string Money(decimal amount, string currency) => $"{amount:0.00} {currency}";

    private async Task NotifyAsync(
        Guid recipientPersonId,
        NotificationType type,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        var nowUtc = _gymCalendar.UtcNow;

        _dbContext.NotificationMessages.Add(new NotificationMessage
        {
            Id = Guid.NewGuid(),
            RecipientPersonId = recipientPersonId,
            Type = type,
            Channel = NotificationChannel.InApp,
            Title = title,
            Body = body,
            IsRead = false,
            SentAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            IsActive = true
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
