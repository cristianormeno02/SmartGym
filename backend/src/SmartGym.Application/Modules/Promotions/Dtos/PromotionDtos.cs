using SmartGym.Domain.Enums;

namespace SmartGym.Application.Modules.Promotions.Dtos;

public record PromotionDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    PromotionConditionType ConditionType,
    PromotionBenefitType BenefitType,
    decimal BenefitValue,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxUsesTotal,
    int CurrentUsesTotal,
    int MaxUsesPerPerson,
    Guid? ApplicablePlanId,
    string? ApplicablePlanName,
    bool IsActive,
    PromotionStatus Status,
    uint Version
);

public record CreatePromotionRequest(
    string Code,
    string Name,
    string? Description,
    PromotionConditionType ConditionType,
    PromotionBenefitType BenefitType,
    decimal BenefitValue,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxUsesTotal,
    int MaxUsesPerPerson,
    Guid? ApplicablePlanId
);

/// <summary><c>Version</c> es la versión leída por el cliente (concurrencia optimista).</summary>
public record UpdatePromotionRequest(
    string Code,
    string Name,
    string? Description,
    PromotionConditionType ConditionType,
    PromotionBenefitType BenefitType,
    decimal BenefitValue,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    int? MaxUsesTotal,
    int MaxUsesPerPerson,
    Guid? ApplicablePlanId,
    uint Version
);

/// <summary>Evaluación previa para la contratación de un plan. El precio base lo aporta el servidor.</summary>
public record EvaluatePromotionRequest(
    string Code,
    Guid PersonId,
    Guid MembershipPlanId
);

/// <summary>Contrato interno entre la contratación de membresías y las promociones; no se expone por la API.</summary>
public record ApplyPromotionRequest(
    string Code,
    Guid PersonId,
    Guid? MembershipPlanId,
    decimal BasePrice
);

public record PromotionApplicationResultDto(
    Guid PromotionId,
    string Code,
    string Name,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    int ExtraCreditsGranted
);

public record PromotionRedemptionDto(
    Guid Id,
    Guid PromotionId,
    string PromotionCode,
    Guid PersonId,
    string PersonName,
    decimal OriginalAmount,
    decimal DiscountAmount,
    decimal FinalAmount,
    int ExtraCreditsGranted,
    DateTime RedeemedAtUtc,
    Guid? ReservationId = null,
    Guid? MembershipId = null,
    bool IsVoided = false
);
