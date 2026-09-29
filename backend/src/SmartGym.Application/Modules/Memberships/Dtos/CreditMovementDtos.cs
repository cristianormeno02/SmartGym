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
