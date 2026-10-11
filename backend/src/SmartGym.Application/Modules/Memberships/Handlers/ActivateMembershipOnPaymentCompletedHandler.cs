using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Events.Operations;

namespace SmartGym.Application.Modules.Memberships.Handlers;

public class ActivateMembershipOnPaymentCompletedHandler : IDomainEventHandler<PaymentCompletedEvent>
{
    private readonly IMembershipActivationService _activationService;

    public ActivateMembershipOnPaymentCompletedHandler(IMembershipActivationService activationService)
    {
        _activationService = activationService;
    }

    public async Task HandleAsync(PaymentCompletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (!domainEvent.MembershipId.HasValue)
        {
            return;
        }

        await _activationService.TryActivateAsync(
            domainEvent.MembershipId.Value,
            authorizedByUserId: domainEvent.RegisteredByUserId,
            activationReason: $"Activación automática por confirmación de pago ({domainEvent.PaymentId})",
            isExplicitActivation: false,
            isCallerAdministrator: false,
            saveChanges: false,
            cancellationToken: cancellationToken);
    }
}
