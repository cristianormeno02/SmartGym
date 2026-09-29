using SmartGym.Application.Modules.Memberships.Dtos;

namespace SmartGym.Application.Modules.Memberships.Services;

public interface ICreditLedgerService
{
    Task<List<CreditMovementDto>> GetMovementsByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<List<CreditMovementDto>> GetMovementsByMembershipAsync(Guid membershipId, CancellationToken cancellationToken = default);
    Task<CreditMovementDto> AddCompensatoryCreditAsync(AddCompensatoryCreditRequest request, Guid performedByUserId, CancellationToken cancellationToken = default);
    Task<CreditMovementDto> PurchaseSingleClassTicketAsync(PurchaseSingleClassTicketRequest request, Guid performedByUserId, CancellationToken cancellationToken = default);
}
