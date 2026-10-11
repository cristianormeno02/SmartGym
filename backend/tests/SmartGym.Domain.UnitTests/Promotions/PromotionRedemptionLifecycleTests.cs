using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Promotions.Dtos;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Promotions;

public class PromotionRedemptionLifecycleTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateTime UtcNow { get; set; } = new(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc);
        public DateOnly Today { get; set; } = new(2026, 6, 15);
        public DateTime ToUtc(DateOnly date, TimeOnly time) =>
            new(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        public DateTimeOffset ToLocal(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
            (ToUtc(from, TimeOnly.MinValue), ToUtc(to.AddDays(1), TimeOnly.MinValue));
    }

    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SmartGymDbContext(options);
    }

    [Fact]
    public async Task EvaluateAndGetAll_ShouldUseGymCalendarToday()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar
        {
            Today = new DateOnly(2026, 8, 10)
        };
        IPromotionService service = new PromotionService(db, calendar);

        var student = Person.Create("Sofía", "Morales", "33444555");
        db.People.Add(student);

        var promoValidInAugust = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "AGOSTO10",
            Name = "Promo Agosto",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 10m,
            ValidFrom = new DateOnly(2026, 8, 1),
            ValidTo = new DateOnly(2026, 8, 31),
            MaxUsesPerPerson = 1,
            IsActive = true
        };

        var promoExpiredInJuly = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "JULIO10",
            Name = "Promo Julio",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 10m,
            ValidFrom = new DateOnly(2026, 7, 1),
            ValidTo = new DateOnly(2026, 7, 31),
            MaxUsesPerPerson = 1,
            IsActive = true
        };

        db.Promotions.AddRange(promoValidInAugust, promoExpiredInJuly);
        await db.SaveChangesAsync();

        var activeList = await service.GetAllAsync(onlyActive: true);
        Assert.Single(activeList);
        Assert.Equal("AGOSTO10", activeList[0].Code);

        var eval = await service.EvaluatePromotionAsync(
            new ApplyPromotionRequest("AGOSTO10", student.Id, null, 20000m));
        Assert.Equal(2000m, eval.DiscountAmount);
        Assert.Equal(18000m, eval.FinalAmount);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EvaluatePromotionAsync(new ApplyPromotionRequest("JULIO10", student.Id, null, 20000m)));
    }

    [Fact]
    public async Task AttachRedemptionToMembership_ShouldLinkRedemptionToCreatedMembership()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        IPromotionService service = new PromotionService(db, calendar);

        var student = Person.Create("Lucas", "Vega", "35666777");
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            DurationDays = 30,
            Price = 20000m,
            Credits = 8
        };
        db.MembershipPlans.Add(plan);

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "BIENVENIDA25",
            Name = "Bienvenida 25%",
            ConditionType = PromotionConditionType.NewStudent,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 25m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            MaxUsesTotal = 50,
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        // Redeem before inserting the membership so NewStudent condition passes and FK is not violated
        var redemptionDto = await service.RedeemPromotionAsync(
            new ApplyPromotionRequest("BIENVENIDA25", student.Id, plan.Id, plan.Price),
            membershipId: null);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: calendar.Today,
            purchasedAtUtc: calendar.UtcNow,
            originalPrice: redemptionDto.OriginalAmount,
            discountAmount: redemptionDto.DiscountAmount,
            purchasePrice: redemptionDto.FinalAmount,
            currency: "ARS",
            extraCreditsGranted: redemptionDto.ExtraCreditsGranted,
            promotionId: promo.Id,
            promotionRedemptionId: redemptionDto.Id);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        await service.AttachRedemptionToMembershipAsync(redemptionDto.Id, membership.Id);

        var persistedRedemption = await db.PromotionRedemptions.SingleAsync(r => r.Id == redemptionDto.Id);
        Assert.Equal(membership.Id, persistedRedemption.MembershipId);
        Assert.True(persistedRedemption.IsActive);
    }

    [Fact]
    public async Task VoidRedemption_ShouldDeactivateRedemption_DecrementUseCounter_AndRestorePerPersonQuota()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        IPromotionService service = new PromotionService(db, calendar);

        var student = Person.Create("Camila", "Torres", "36777888");
        db.People.Add(student);

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "UNICOUSO",
            Name = "Promoción 1 uso",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.FixedAmountDiscount,
            BenefitValue = 3000m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            MaxUsesTotal = 1,
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        var firstRedemption = await service.RedeemPromotionAsync(
            new ApplyPromotionRequest("UNICOUSO", student.Id, null, 15000m),
            membershipId: null);

        var promoAfterRedeem = await db.Promotions.SingleAsync(p => p.Id == promo.Id);
        Assert.Equal(1, promoAfterRedeem.CurrentUsesTotal);

        // Cannot redeem again while active
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EvaluatePromotionAsync(new ApplyPromotionRequest("UNICOUSO", student.Id, null, 15000m)));

        // Void redemption (e.g. when a Pending membership is cancelled before ever activating)
        await service.VoidRedemptionAsync(firstRedemption.Id);
        // Second call is idempotent
        await service.VoidRedemptionAsync(firstRedemption.Id);

        var voidedRedemption = await db.PromotionRedemptions.SingleAsync(r => r.Id == firstRedemption.Id);
        Assert.False(voidedRedemption.IsActive);

        var promoAfterVoid = await db.Promotions.SingleAsync(p => p.Id == promo.Id);
        Assert.Equal(0, promoAfterVoid.CurrentUsesTotal);

        // Student can now evaluate and redeem the promotion again
        var secondRedemption = await service.RedeemPromotionAsync(
            new ApplyPromotionRequest("UNICOUSO", student.Id, null, 15000m),
            membershipId: null);
        Assert.NotEqual(firstRedemption.Id, secondRedemption.Id);
        Assert.Equal(1, (await db.Promotions.SingleAsync(p => p.Id == promo.Id)).CurrentUsesTotal);
    }
}
