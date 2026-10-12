using System.Globalization;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;

namespace SmartGym.Application.Modules.Operations.Handlers;

/// <summary>
/// Notifica al pagador (y al responsable si corresponde) la confirmación, el rechazo o la devolución de un pago.
/// Los eventos son post-commit: el handler corre con el pago ya confirmado en la base y delega en
/// INotificationDispatcher con clave de idempotencia determinística. Una falla aquí se registra en el log y no
/// afecta al pago ni a la membresía.
/// </summary>
public class NotifyPayerOnPaymentLifecycleHandler :
    IDomainEventHandler<PaymentConfirmedNotificationEvent>,
    IDomainEventHandler<PaymentFailedNotificationEvent>,
    IDomainEventHandler<PaymentRefundedNotificationEvent>
{
    private readonly INotificationDispatcher _dispatcher;

    public NotifyPayerOnPaymentLifecycleHandler(
        ISmartGymDbContext dbContext,
        IGymCalendar gymCalendar,
        INotificationDispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? new NotificationDispatcher(
            dbContext,
            new NotificationRecipientResolver(dbContext, gymCalendar),
            new NotificationTemplateRenderer(dbContext),
            new NotificationService(dbContext, gymCalendar),
            gymCalendar);
    }

    public Task HandleAsync(PaymentConfirmedNotificationEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.PayerPersonId,
            domainEvent.PaymentId,
            NotificationType.PaymentConfirmed,
            $"payment-confirmed:{domainEvent.PaymentId}",
            domainEvent.Amount,
            domainEvent.Currency,
            reason: null,
            cancellationToken);

    public Task HandleAsync(PaymentFailedNotificationEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.PayerPersonId,
            domainEvent.PaymentId,
            NotificationType.PaymentFailed,
            $"payment-failed:{domainEvent.PaymentId}",
            domainEvent.Amount,
            domainEvent.Currency,
            domainEvent.Reason,
            cancellationToken);

    public Task HandleAsync(PaymentRefundedNotificationEvent domainEvent, CancellationToken cancellationToken = default) =>
        NotifyAsync(
            domainEvent.PayerPersonId,
            domainEvent.PaymentId,
            NotificationType.PaymentRefunded,
            // Un pago admite varias devoluciones: la clave es por devolución
            $"payment-refunded:{domainEvent.RefundId}",
            domainEvent.RefundAmount,
            domainEvent.Currency,
            reason: null,
            cancellationToken);

    private Task NotifyAsync(
        Guid payerPersonId,
        Guid paymentId,
        NotificationType type,
        string idempotencyKey,
        decimal amount,
        string currency,
        string? reason,
        CancellationToken cancellationToken) =>
        _dispatcher.DispatchAsync(
            new DispatchNotificationCommand(
                TargetPersonId: payerPersonId,
                Type: type,
                Variables: new Dictionary<string, string?>
                {
                    ["amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture),
                    ["currency"] = currency,
                    ["reason"] = string.IsNullOrWhiteSpace(reason) ? "sin detalle" : reason
                },
                IdempotencyKey: idempotencyKey,
                RelatedEntityType: "Payment",
                RelatedEntityId: paymentId,
                ActionUrl: "/portal/alumno",
                SaveChanges: true),
            cancellationToken);
}
