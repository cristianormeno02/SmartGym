using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;

namespace SmartGym.Domain.Entities.Promotions;

public class Promotion : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public PromotionConditionType ConditionType { get; set; }
    public PromotionBenefitType BenefitType { get; set; }
    public decimal BenefitValue { get; set; }

    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }

    public int? MaxUsesTotal { get; set; }
    public int CurrentUsesTotal { get; set; } = 0;
    public int MaxUsesPerPerson { get; set; } = 1;

    public Guid? ApplicablePlanId { get; set; }
    public MembershipPlan? ApplicablePlan { get; set; }

    public ICollection<PromotionRedemption> Redemptions { get; set; } = new List<PromotionRedemption>();

    public bool IsValidOn(DateOnly date)
    {
        if (!IsActive) return false;
        if (date < ValidFrom) return false;
        if (ValidTo.HasValue && date > ValidTo.Value) return false;
        if (MaxUsesTotal.HasValue && CurrentUsesTotal >= MaxUsesTotal.Value) return false;
        return true;
    }

    public bool CanBeUsedBy(Guid personId, int previousUsesCount)
    {
        return previousUsesCount < MaxUsesPerPerson;
    }

    public decimal CalculateDiscount(decimal basePrice)
    {
        return BenefitType switch
        {
            PromotionBenefitType.PercentageDiscount => Math.Round(basePrice * (BenefitValue / 100m), 2),
            PromotionBenefitType.FixedAmountDiscount => Math.Min(basePrice, BenefitValue),
            _ => 0m
        };
    }

    public int GetExtraCredits()
    {
        return BenefitType == PromotionBenefitType.ExtraCredits ? (int)BenefitValue : 0;
    }

    public void IncrementUse()
    {
        CurrentUsesTotal++;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
