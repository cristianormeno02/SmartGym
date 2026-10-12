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

    public PromotionStatus Status { get; set; } = PromotionStatus.Active;
    public uint Version { get; set; } // xmin

    public ICollection<PromotionRedemption> Redemptions { get; set; } = new List<PromotionRedemption>();

    public bool HasReachedTotalLimit => MaxUsesTotal.HasValue && CurrentUsesTotal >= MaxUsesTotal.Value;

    public bool IsWithinValidity(DateOnly date)
    {
        if (!IsActive || Status != PromotionStatus.Active) return false;
        if (date < ValidFrom) return false;
        if (ValidTo.HasValue && date > ValidTo.Value) return false;
        return true;
    }

    public bool IsValidOn(DateOnly date) => IsWithinValidity(date) && !HasReachedTotalLimit;

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

    public void DecrementUse()
    {
        if (CurrentUsesTotal > 0)
        {
            CurrentUsesTotal--;
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public void Pause(DateTime nowUtc)
    {
        if (Status != PromotionStatus.Active)
        {
            throw new InvalidOperationException($"Sólo se puede pausar una promoción activa (estado actual: {Status}).");
        }

        Status = PromotionStatus.Paused;
        UpdatedAtUtc = nowUtc;
    }

    public void Activate(DateTime nowUtc)
    {
        if (Status != PromotionStatus.Paused)
        {
            throw new InvalidOperationException($"Sólo se puede activar una promoción pausada (estado actual: {Status}).");
        }

        Status = PromotionStatus.Active;
        UpdatedAtUtc = nowUtc;
    }

    public void Archive(DateTime nowUtc)
    {
        if (Status == PromotionStatus.Archived)
        {
            throw new InvalidOperationException("La promoción ya se encuentra archivada.");
        }

        Status = PromotionStatus.Archived;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Edita la promoción. Con canjes registrados (vigentes o anulados) sólo admite cambios en nombre,
    /// descripción, fecha de fin y topes, para no alterar el significado de los canjes existentes.
    /// </summary>
    public void Update(
        string code,
        string name,
        string? description,
        PromotionConditionType conditionType,
        PromotionBenefitType benefitType,
        decimal benefitValue,
        DateOnly validFrom,
        DateOnly? validTo,
        int? maxUsesTotal,
        int maxUsesPerPerson,
        Guid? applicablePlanId,
        bool hasRedemptions,
        int maxActiveRedemptionsPerPerson,
        DateTime nowUtc)
    {
        if (Status == PromotionStatus.Archived)
        {
            throw new InvalidOperationException("No se puede editar una promoción archivada.");
        }

        var changesLockedFields =
            code != Code ||
            conditionType != ConditionType ||
            benefitType != BenefitType ||
            benefitValue != BenefitValue ||
            validFrom != ValidFrom ||
            applicablePlanId != ApplicablePlanId;

        if (hasRedemptions && changesLockedFields)
        {
            throw new InvalidOperationException(
                "La promoción ya tiene canjes registrados: sólo se pueden modificar el nombre, la descripción, la fecha de fin y los topes de uso.");
        }

        if (maxUsesTotal.HasValue && maxUsesTotal.Value < CurrentUsesTotal)
        {
            throw new ArgumentException(
                $"El tope general no puede ser menor que los usos ya consumidos ({CurrentUsesTotal}).", nameof(maxUsesTotal));
        }

        if (maxUsesPerPerson < maxActiveRedemptionsPerPerson)
        {
            throw new ArgumentException(
                $"El tope por persona no puede ser menor que los usos ya consumidos por una misma persona ({maxActiveRedemptionsPerPerson}).",
                nameof(maxUsesPerPerson));
        }

        Code = code;
        Name = name;
        Description = description;
        ConditionType = conditionType;
        BenefitType = benefitType;
        BenefitValue = benefitValue;
        ValidFrom = validFrom;
        ValidTo = validTo;
        MaxUsesTotal = maxUsesTotal;
        MaxUsesPerPerson = maxUsesPerPerson;
        ApplicablePlanId = applicablePlanId;
        UpdatedAtUtc = nowUtc;
    }
}

