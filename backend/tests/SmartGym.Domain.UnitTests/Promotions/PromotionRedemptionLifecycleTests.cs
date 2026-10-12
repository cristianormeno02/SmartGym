using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Promotions;
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
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
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

        var expired = await Assert.ThrowsAsync<ConflictException>(() =>
            service.EvaluatePromotionAsync(new ApplyPromotionRequest("JULIO10", student.Id, null, 20000m)));
        Assert.Equal(PromotionErrorCodes.NotApplicable, expired.Code);
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
        var reused = await Assert.ThrowsAsync<ConflictException>(() =>
            service.EvaluatePromotionAsync(new ApplyPromotionRequest("UNICOUSO", student.Id, null, 15000m)));
        Assert.Equal(PromotionErrorCodes.LimitReached, reused.Code);

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

    [Fact]
    public async Task EvaluateAndRedeemPromotionForReservation_FullAccessBenefitsSucceed_AndLinkToReservationId()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        IPromotionService service = new PromotionService(db, calendar);

        var student = Person.Create("Valentina", "Ríos", "37888999");
        db.People.Add(student);

        var freeClassPromo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "CLASEGRATIS100",
            Name = "Clase 100% Bonificada",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 100m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            MaxUsesTotal = 10,
            MaxUsesPerPerson = 1,
            IsActive = true
        };

        var extraCreditPromo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "PRIMERACLASE",
            Name = "Primera Clase Gratis",
            ConditionType = PromotionConditionType.NewStudent,
            BenefitType = PromotionBenefitType.ExtraCredits,
            BenefitValue = 1m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            MaxUsesTotal = 10,
            MaxUsesPerPerson = 1,
            IsActive = true
        };

        db.Promotions.AddRange(freeClassPromo, extraCreditPromo);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        var activityId = Guid.NewGuid();
        var reservation1Id = Guid.NewGuid();
        var reservation2Id = Guid.NewGuid();

        var eval1 = await service.EvaluatePromotionForReservationAsync(student.Id, sessionId, activityId, "CLASEGRATIS100");
        Assert.Equal(freeClassPromo.Id, eval1.PromotionId);

        var redemption1 = await service.RedeemPromotionForReservationAsync(
            student.Id,
            sessionId,
            activityId,
            "CLASEGRATIS100",
            reservation1Id);

        Assert.Equal(reservation1Id, redemption1.ReservationId);
        var persisted1 = await db.PromotionRedemptions.SingleAsync(r => r.Id == redemption1.Id);
        Assert.Equal(reservation1Id, persisted1.ReservationId);
        Assert.Equal(1, (await db.Promotions.SingleAsync(p => p.Id == freeClassPromo.Id)).CurrentUsesTotal);

        var eval2 = await service.EvaluatePromotionForReservationAsync(student.Id, sessionId, activityId, "PRIMERACLASE");
        Assert.Equal(extraCreditPromo.Id, eval2.PromotionId);
        Assert.Equal(1, eval2.ExtraCreditsGranted);

        var redemption2 = await service.RedeemPromotionForReservationAsync(
            student.Id,
            sessionId,
            activityId,
            "PRIMERACLASE",
            reservation2Id);
        Assert.Equal(reservation2Id, redemption2.ReservationId);
    }

    [Fact]
    public async Task EvaluatePromotionForReservation_PartialDiscountOrPlanSpecific_ThrowsConflictWithPromotionNotApplicable()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        IPromotionService service = new PromotionService(db, calendar);

        var student = Person.Create("Mateo", "Silva", "38999000");
        db.People.Add(student);

        var partialPromo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "DESC50",
            Name = "50% de descuento",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 50m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            MaxUsesPerPerson = 1,
            IsActive = true
        };

        var fixedPromo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "FIJO2000",
            Name = "$2000 de descuento",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.FixedAmountDiscount,
            BenefitValue = 2000m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            MaxUsesPerPerson = 1,
            IsActive = true
        };

        var planPromo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "SOLOPLAN",
            Name = "100% en plan específico",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 100m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            ApplicablePlanId = Guid.NewGuid(),
            MaxUsesPerPerson = 1,
            IsActive = true
        };

        db.Promotions.AddRange(partialPromo, fixedPromo, planPromo);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        var activityId = Guid.NewGuid();

        var ex1 = await Assert.ThrowsAsync<SmartGym.Application.Common.Exceptions.ConflictException>(() =>
            service.EvaluatePromotionForReservationAsync(student.Id, sessionId, activityId, "DESC50"));
        Assert.Equal("PROMOTION_NOT_APPLICABLE", ex1.Code);

        var ex2 = await Assert.ThrowsAsync<SmartGym.Application.Common.Exceptions.ConflictException>(() =>
            service.EvaluatePromotionForReservationAsync(student.Id, sessionId, activityId, "FIJO2000"));
        Assert.Equal("PROMOTION_NOT_APPLICABLE", ex2.Code);

        var ex3 = await Assert.ThrowsAsync<SmartGym.Application.Common.Exceptions.ConflictException>(() =>
            service.EvaluatePromotionForReservationAsync(student.Id, sessionId, activityId, "SOLOPLAN"));
        Assert.Equal("PROMOTION_NOT_APPLICABLE", ex3.Code);
    }

    private static Promotion FullAccessPromotion(string code, Action<Promotion>? configure = null)
    {
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 100m,
            ValidFrom = new DateOnly(2026, 6, 1),
            ValidTo = new DateOnly(2026, 6, 30),
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        configure?.Invoke(promo);
        return promo;
    }

    private static async Task AssertRejectedInBothPathsAsync(
        IPromotionService service,
        Guid personId,
        string code,
        string expectedCode)
    {
        var request = new ApplyPromotionRequest(code, personId, null, 10000m);

        var evaluate = await Assert.ThrowsAsync<ConflictException>(() => service.EvaluatePromotionAsync(request));
        Assert.Equal(expectedCode, evaluate.Code);

        var redeem = await Assert.ThrowsAsync<ConflictException>(() => service.RedeemPromotionAsync(request, membershipId: null));
        Assert.Equal(expectedCode, redeem.Code);

        var evaluateReservation = await Assert.ThrowsAsync<ConflictException>(() =>
            service.EvaluatePromotionForReservationAsync(personId, Guid.NewGuid(), Guid.NewGuid(), code));
        Assert.Equal(expectedCode, evaluateReservation.Code);

        var redeemReservation = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RedeemPromotionForReservationAsync(personId, Guid.NewGuid(), Guid.NewGuid(), code, Guid.NewGuid()));
        Assert.Equal(expectedCode, redeemReservation.Code);
    }

    [Fact]
    public async Task EvaluateAndRedeem_RejectionsInBothPaths_ReturnStableErrorCodes()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        IPromotionService service = new PromotionService(db, calendar);

        var student = Person.Create("Julia", "Vera", "35111222");
        db.People.Add(student);

        db.Promotions.AddRange(
            FullAccessPromotion("VENCIDA", p => p.ValidTo = new DateOnly(2026, 6, 10)),
            FullAccessPromotion("FUTURA", p => p.ValidFrom = new DateOnly(2026, 6, 20)),
            FullAccessPromotion("PAUSADA", p => p.Status = PromotionStatus.Paused),
            FullAccessPromotion("ARCHIVADA", p => p.Status = PromotionStatus.Archived),
            FullAccessPromotion("REFERIDO", p => p.ConditionType = PromotionConditionType.Referral),
            FullAccessPromotion("DOSXUNO", p => p.ConditionType = PromotionConditionType.TwoForOne),
            FullAccessPromotion("FAMILIA", p => p.ConditionType = PromotionConditionType.FamilyGroup),
            FullAccessPromotion("AGOTADA", p =>
            {
                p.MaxUsesTotal = 3;
                p.CurrentUsesTotal = 3;
            }));
        await db.SaveChangesAsync();

        await AssertRejectedInBothPathsAsync(service, student.Id, "NOEXISTE", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, student.Id, "VENCIDA", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, student.Id, "FUTURA", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, student.Id, "PAUSADA", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, student.Id, "ARCHIVADA", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, student.Id, "REFERIDO", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, student.Id, "DOSXUNO", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, student.Id, "FAMILIA", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, Guid.NewGuid(), "AGOTADA", PromotionErrorCodes.NotApplicable);
        await AssertRejectedInBothPathsAsync(service, student.Id, "AGOTADA", PromotionErrorCodes.LimitReached);

        Assert.Equal(0, await db.PromotionRedemptions.CountAsync());
        Assert.Equal(3, (await db.Promotions.SingleAsync(p => p.Code == "AGOTADA")).CurrentUsesTotal);
    }

    [Fact]
    public async Task EvaluatePromotion_PlanMismatch_ThrowsNotApplicable()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        IPromotionService service = new PromotionService(db, calendar);

        var student = Person.Create("Tomás", "Ríos", "36111222");
        db.People.Add(student);
        db.Promotions.Add(FullAccessPromotion("SOLOPLANA", p => p.ApplicablePlanId = Guid.NewGuid()));
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.EvaluatePromotionAsync(new ApplyPromotionRequest("SOLOPLANA", student.Id, Guid.NewGuid(), 10000m)));
        Assert.Equal(PromotionErrorCodes.NotApplicable, ex.Code);
    }

    [Fact]
    public async Task RedeemPromotionForReservation_PerPersonLimitReached_ThrowsLimitReached()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar { Today = new DateOnly(2026, 6, 15) };
        IPromotionService service = new PromotionService(db, calendar);

        var student = Person.Create("Nora", "Luna", "37111222");
        db.People.Add(student);
        db.Promotions.Add(FullAccessPromotion("CLASEGRATIS"));
        await db.SaveChangesAsync();

        await service.RedeemPromotionForReservationAsync(
            student.Id, Guid.NewGuid(), Guid.NewGuid(), "CLASEGRATIS", Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RedeemPromotionForReservationAsync(
                student.Id, Guid.NewGuid(), Guid.NewGuid(), "CLASEGRATIS", Guid.NewGuid()));
        Assert.Equal(PromotionErrorCodes.LimitReached, ex.Code);
        Assert.Equal(1, await db.PromotionRedemptions.CountAsync());
    }

    [Fact]
    public async Task CreateAsync_DuplicateCodeIgnoringCase_ThrowsCodeTaken()
    {
        await using var db = CreateDbContext();
        IPromotionService service = new PromotionService(db, new StubGymCalendar());

        var request = new CreatePromotionRequest(
            "Bienvenida", "Bienvenida", null,
            PromotionConditionType.CouponCode, PromotionBenefitType.PercentageDiscount, 10m,
            new DateOnly(2026, 1, 1), null, null, 1, null);
        await service.CreateAsync(request);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(request with { Code = "BIENVENIDA" }));
        Assert.Equal(PromotionErrorCodes.CodeTaken, ex.Code);
    }

    [Fact]
    public async Task AttachAndVoidRedemption_UnknownRedemption_ThrowNotFound()
    {
        await using var db = CreateDbContext();
        IPromotionService service = new PromotionService(db, new StubGymCalendar());

        await Assert.ThrowsAsync<NotFoundException>(() => service.VoidRedemptionAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AttachRedemptionToMembershipAsync(Guid.NewGuid(), Guid.NewGuid()));
    }
}
