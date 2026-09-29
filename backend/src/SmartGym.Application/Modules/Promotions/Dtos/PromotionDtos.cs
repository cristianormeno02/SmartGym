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
    bool IsActive
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
    DateTime RedeemedAtUtc
);
