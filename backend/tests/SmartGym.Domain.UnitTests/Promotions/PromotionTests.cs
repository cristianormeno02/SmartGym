using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionTests
{
    [Fact]
    public void Promotion_ShouldCalculatePercentageDiscountCorrectly()
    {
        // Arrange
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "DESCUENTO20",
            Name = "20% Off Bienvenida",
            ConditionType = PromotionConditionType.NewStudent,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 20m, // 20%
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31),
            MaxUsesTotal = 100,
            MaxUsesPerPerson = 1
        };

        // Act
        var discount = promo.CalculateDiscount(30000m);
        var finalPrice = 30000m - discount;

        // Assert
        Assert.Equal(6000m, discount);
        Assert.Equal(24000m, finalPrice);
        Assert.True(promo.IsValidOn(new DateOnly(2026, 6, 1)));
        Assert.True(promo.CanBeUsedBy(Guid.NewGuid(), previousUsesCount: 0));
        Assert.False(promo.CanBeUsedBy(Guid.NewGuid(), previousUsesCount: 1));
    }

    [Fact]
    public void Promotion_ShouldCalculateFixedDiscountCorrectly()
    {
        // Arrange
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "DESCUENTO5000",
            Name = "$5000 Off Familiar",
            ConditionType = PromotionConditionType.FamilyGroup,
            BenefitType = PromotionBenefitType.FixedAmountDiscount,
            BenefitValue = 5000m,
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31)
        };

        // Act
        var discount = promo.CalculateDiscount(25000m);

        // Assert
        Assert.Equal(5000m, discount);
    }

    [Fact]
    public void Promotion_ShouldReturnExtraCreditsCorrectly()
    {
        // Arrange
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "EXTRACREDITOS",
            Name = "2 Clases Extra",
            ConditionType = PromotionConditionType.Referral,
            BenefitType = PromotionBenefitType.ExtraCredits,
            BenefitValue = 2m,
            ValidFrom = new DateOnly(2026, 1, 1),
            ValidTo = new DateOnly(2026, 12, 31)
        };

        // Act
        var discount = promo.CalculateDiscount(25000m);
        var extraCredits = promo.GetExtraCredits();

        // Assert
        Assert.Equal(0m, discount);
        Assert.Equal(2, extraCredits);
    }

    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static Promotion NewPromotion(PromotionStatus status = PromotionStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        Code = "PROMO",
        Name = "Promo",
        ConditionType = PromotionConditionType.CouponCode,
        BenefitType = PromotionBenefitType.PercentageDiscount,
        BenefitValue = 20m,
        ValidFrom = new DateOnly(2026, 1, 1),
        ValidTo = new DateOnly(2026, 12, 31),
        MaxUsesTotal = 100,
        MaxUsesPerPerson = 1,
        Status = status
    };

    private static void UpdateWith(
        Promotion promo,
        bool hasRedemptions = false,
        int maxActiveRedemptionsPerPerson = 0,
        string? code = null,
        string? name = null,
        string? description = null,
        PromotionConditionType? conditionType = null,
        PromotionBenefitType? benefitType = null,
        decimal? benefitValue = null,
        DateOnly? validFrom = null,
        DateOnly? validTo = null,
        int? maxUsesTotal = null,
        int? maxUsesPerPerson = null,
        Guid? applicablePlanId = null)
    {
        promo.Update(
            code ?? promo.Code,
            name ?? promo.Name,
            description ?? promo.Description,
            conditionType ?? promo.ConditionType,
            benefitType ?? promo.BenefitType,
            benefitValue ?? promo.BenefitValue,
            validFrom ?? promo.ValidFrom,
            validTo ?? promo.ValidTo,
            maxUsesTotal ?? promo.MaxUsesTotal,
            maxUsesPerPerson ?? promo.MaxUsesPerPerson,
            applicablePlanId ?? promo.ApplicablePlanId,
            hasRedemptions,
            maxActiveRedemptionsPerPerson,
            Now);
    }

    [Fact]
    public void Promotion_ShouldBeCreatedActive()
    {
        Assert.Equal(PromotionStatus.Active, new Promotion().Status);
    }

    [Fact]
    public void Promotion_ValidTransitions_ShouldChangeStatusAndTimestamp()
    {
        var promo = NewPromotion();

        promo.Pause(Now);
        Assert.Equal(PromotionStatus.Paused, promo.Status);
        Assert.Equal(Now, promo.UpdatedAtUtc);

        promo.Activate(Now.AddMinutes(1));
        Assert.Equal(PromotionStatus.Active, promo.Status);
        Assert.Equal(Now.AddMinutes(1), promo.UpdatedAtUtc);

        promo.Archive(Now.AddMinutes(2));
        Assert.Equal(PromotionStatus.Archived, promo.Status);

        var paused = NewPromotion(PromotionStatus.Paused);
        paused.Archive(Now);
        Assert.Equal(PromotionStatus.Archived, paused.Status);
    }

    [Fact]
    public void Promotion_InvalidTransitions_ShouldThrowAndKeepStatus()
    {
        var active = NewPromotion();
        Assert.Throws<InvalidOperationException>(() => active.Activate(Now));
        Assert.Equal(PromotionStatus.Active, active.Status);

        var paused = NewPromotion(PromotionStatus.Paused);
        Assert.Throws<InvalidOperationException>(() => paused.Pause(Now));
        Assert.Equal(PromotionStatus.Paused, paused.Status);

        var archived = NewPromotion(PromotionStatus.Archived);
        Assert.Throws<InvalidOperationException>(() => archived.Activate(Now));
        Assert.Throws<InvalidOperationException>(() => archived.Pause(Now));
        Assert.Throws<InvalidOperationException>(() => archived.Archive(Now));
        Assert.Equal(PromotionStatus.Archived, archived.Status);
    }

    [Theory]
    [InlineData(PromotionStatus.Paused)]
    [InlineData(PromotionStatus.Archived)]
    public void Promotion_IsValidOn_ShouldBeFalseWhenNotActive(PromotionStatus status)
    {
        var promo = NewPromotion(status);

        Assert.False(promo.IsValidOn(new DateOnly(2026, 6, 1)));
    }

    [Fact]
    public void Promotion_UpdateWithoutRedemptions_ShouldChangeEveryField()
    {
        var promo = NewPromotion();
        var planId = Guid.NewGuid();

        UpdateWith(
            promo,
            code: "NUEVO",
            name: "Nuevo nombre",
            description: "Nueva descripción",
            conditionType: PromotionConditionType.NewStudent,
            benefitType: PromotionBenefitType.FixedAmountDiscount,
            benefitValue: 5000m,
            validFrom: new DateOnly(2026, 2, 1),
            validTo: new DateOnly(2027, 1, 31),
            maxUsesTotal: 50,
            maxUsesPerPerson: 2,
            applicablePlanId: planId);

        Assert.Equal("NUEVO", promo.Code);
        Assert.Equal("Nuevo nombre", promo.Name);
        Assert.Equal("Nueva descripción", promo.Description);
        Assert.Equal(PromotionConditionType.NewStudent, promo.ConditionType);
        Assert.Equal(PromotionBenefitType.FixedAmountDiscount, promo.BenefitType);
        Assert.Equal(5000m, promo.BenefitValue);
        Assert.Equal(new DateOnly(2026, 2, 1), promo.ValidFrom);
        Assert.Equal(new DateOnly(2027, 1, 31), promo.ValidTo);
        Assert.Equal(50, promo.MaxUsesTotal);
        Assert.Equal(2, promo.MaxUsesPerPerson);
        Assert.Equal(planId, promo.ApplicablePlanId);
        Assert.Equal(Now, promo.UpdatedAtUtc);
    }

    [Fact]
    public void Promotion_UpdateWithRedemptions_ShouldAllowNameDescriptionValidToAndLimits()
    {
        var promo = NewPromotion();
        promo.CurrentUsesTotal = 40;

        UpdateWith(
            promo,
            hasRedemptions: true,
            maxActiveRedemptionsPerPerson: 1,
            name: "Renombrada",
            description: "Otra",
            validTo: new DateOnly(2027, 1, 31),
            maxUsesTotal: 40,
            maxUsesPerPerson: 3);

        Assert.Equal("Renombrada", promo.Name);
        Assert.Equal("Otra", promo.Description);
        Assert.Equal(new DateOnly(2027, 1, 31), promo.ValidTo);
        Assert.Equal(40, promo.MaxUsesTotal);
        Assert.Equal(3, promo.MaxUsesPerPerson);
    }

    [Fact]
    public void Promotion_UpdateWithRedemptions_ShouldRejectLockedFields()
    {
        var attempts = new Action<Promotion>[]
        {
            p => UpdateWith(p, hasRedemptions: true, code: "OTRO"),
            p => UpdateWith(p, hasRedemptions: true, conditionType: PromotionConditionType.NewStudent),
            p => UpdateWith(p, hasRedemptions: true, benefitType: PromotionBenefitType.FixedAmountDiscount),
            p => UpdateWith(p, hasRedemptions: true, benefitValue: 50m),
            p => UpdateWith(p, hasRedemptions: true, validFrom: new DateOnly(2026, 2, 1)),
            p => UpdateWith(p, hasRedemptions: true, applicablePlanId: Guid.NewGuid())
        };

        foreach (var attempt in attempts)
        {
            var promo = NewPromotion();

            Assert.Throws<InvalidOperationException>(() => attempt(promo));
            Assert.Equal("PROMO", promo.Code);
            Assert.Equal(PromotionConditionType.CouponCode, promo.ConditionType);
            Assert.Equal(PromotionBenefitType.PercentageDiscount, promo.BenefitType);
            Assert.Equal(20m, promo.BenefitValue);
            Assert.Equal(new DateOnly(2026, 1, 1), promo.ValidFrom);
            Assert.Null(promo.ApplicablePlanId);
            Assert.Null(promo.UpdatedAtUtc);
        }
    }

    [Fact]
    public void Promotion_Update_ShouldRejectLimitsBelowConsumedUses()
    {
        var promo = NewPromotion();
        promo.CurrentUsesTotal = 40;
        promo.MaxUsesPerPerson = 3;

        Assert.Throws<ArgumentException>(() => UpdateWith(promo, hasRedemptions: true, maxUsesTotal: 30));
        Assert.Throws<ArgumentException>(() =>
            UpdateWith(promo, hasRedemptions: true, maxActiveRedemptionsPerPerson: 2, maxUsesPerPerson: 1));

        Assert.Equal(100, promo.MaxUsesTotal);
        Assert.Equal(3, promo.MaxUsesPerPerson);
    }

    [Fact]
    public void Promotion_UpdateArchived_ShouldThrow()
    {
        var promo = NewPromotion(PromotionStatus.Archived);

        Assert.Throws<InvalidOperationException>(() => UpdateWith(promo, name: "Otro"));
        Assert.Equal("Promo", promo.Name);
    }

    [Fact]
    public void FamilyGroup_ShouldAddAndManageMembers()
    {
        // Arrange
        var primaryId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var group = new FamilyGroup
        {
            Id = Guid.NewGuid(),
            Name = "Familia Rossi",
            PrimaryContactId = primaryId
        };

        // Act
        group.AddMember(primaryId, "Titular");
        group.AddMember(memberId, "Hijo");

        // Assert
        Assert.Equal(2, group.Members.Count);
        Assert.True(group.HasMember(memberId));
        Assert.False(group.HasMember(Guid.NewGuid()));
    }
}
