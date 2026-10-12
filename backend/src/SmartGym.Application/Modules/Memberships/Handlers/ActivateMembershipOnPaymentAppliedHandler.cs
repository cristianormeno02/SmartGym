using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Events.Operations;

namespace SmartGym.Application.Modules.Memberships.Handlers;

/// <summary>
/// Un pago ya confirmado que se aplica a una membresía puede completar su precio: se evalúa la activación
/// igual que al confirmar un pago (<see cref="ActivateMembershipOnPaymentCompletedHandler"/>).
/// </summary>
public class ActivateMembershipOnPaymentAppliedHandler : IDomainEventHandler<PaymentAppliedEvent>
{
    private readonly IMembershipActivationService _activationService;

    public ActivateMembershipOnPaymentAppliedHandler(IMembershipActivationService activationService)
    {
        _activationService = activationService;
    }

    public async Task HandleAsync(PaymentAppliedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        await _activationService.TryActivateAsync(
            domainEvent.MembershipId,
            authorizedByUserId: domainEvent.AppliedByUserId,
            activationReason: $"Activación automática por aplicación de pago ({domainEvent.PaymentId})",
            isExplicitActivation: false,
            isCallerAdministrator: false,
            saveChanges: false,
            cancellationToken: cancellationToken);
    }
}
