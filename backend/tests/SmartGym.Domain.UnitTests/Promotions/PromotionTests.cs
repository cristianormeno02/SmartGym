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
