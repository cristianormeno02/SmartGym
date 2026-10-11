using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Memberships.Dtos;

public record CreditMovementDto(
    Guid Id,
    Guid? MembershipId,
    Guid StudentId,
    string StudentName,
    Guid? ClassSessionId,
    CreditMovementType Type,
    int Amount,
    int BalanceAfter,
    string Reason,
    Guid? PerformedByUserId,
    DateTime CreatedAtUtc
);

public record MembershipCreditsSummaryDto(
    Guid MembershipId,
    Guid StudentId,
    bool IsUnlimited,
    int InitialCredits,
    int PromotionBonusCredits,
    int CompensatoryCredits,
    int ManualAdjustmentCredits,
    int ConsumedCredits,
    int RefundedCredits,
    int ExpiredOrForfeitedCredits,
    int AvailableCredits,
    bool IsDepleted,
    IReadOnlyList<CreditMovementDto> Movements
);

public record AddCompensatoryCreditRequest(
    Guid StudentId,
    Guid? ClassSessionId,
    string Reason
);

public record PurchaseSingleClassTicketRequest(
    Guid StudentId,
    Guid ClassSessionId,
    decimal Price,
    string Reason
);
