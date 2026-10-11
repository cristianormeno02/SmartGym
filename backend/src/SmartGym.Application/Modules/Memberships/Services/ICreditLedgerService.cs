using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Application.Modules.Memberships.Services;

public interface ICreditLedgerService
{
    Task<List<CreditMovementDto>> GetMovementsByStudentAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<List<CreditMovementDto>> GetMovementsByMembershipAsync(Guid membershipId, CancellationToken cancellationToken = default);
    Task<MembershipCreditsSummaryDto> GetMembershipCreditsSummaryAsync(Guid membershipId, CancellationToken cancellationToken = default);

    Task RecordInitialCreditsAsync(
        Membership membership,
        Guid? performedByUserId = null,
        CancellationToken cancellationToken = default);

    Task<MembershipCreditMovement> ConsumeCreditForReservationAsync(
        Membership membership,
        Guid classSessionId,
        DateOnly sessionLocalDate,
        string reason,
        Guid? performedByUserId = null,
        Guid? reservationId = null,
        CancellationToken cancellationToken = default);

    Task<MembershipCreditMovement> RefundCreditForReservationAsync(
        Membership membership,
        Guid classSessionId,
        string reason,
        Guid? performedByUserId = null,
        Guid? reservationId = null,
        CancellationToken cancellationToken = default);

    Task<MembershipCreditMovement?> ForfeitRemainingCreditsOnCancelAsync(
        Membership membership,
        string reason,
        Guid? performedByUserId = null,
        CancellationToken cancellationToken = default);

    Task<MembershipCreditMovement?> ExpireRemainingCreditsAsync(
        Membership membership,
        string reason = "Expiración de créditos por finalización de vigencia",
        CancellationToken cancellationToken = default);

    Task<CreditMovementDto> AddCompensatoryCreditAsync(
        AddCompensatoryCreditRequest request,
        Guid performedByUserId,
        CancellationToken cancellationToken = default);

    Task<CreditMovementDto> PurchaseSingleClassTicketAsync(
        PurchaseSingleClassTicketRequest request,
        Guid performedByUserId,
        CancellationToken cancellationToken = default);
}
