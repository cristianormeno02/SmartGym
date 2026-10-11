namespace SmartGym.Application.Modules.Memberships.Services;

public interface IMembershipActivationService
{
    Task<bool> TryActivateAsync(
        Guid membershipId,
        Guid? authorizedByUserId = null,
        string? activationReason = null,
        bool isExplicitActivation = false,
        bool isCallerAdministrator = false,
        bool saveChanges = true,
        CancellationToken cancellationToken = default);
}
