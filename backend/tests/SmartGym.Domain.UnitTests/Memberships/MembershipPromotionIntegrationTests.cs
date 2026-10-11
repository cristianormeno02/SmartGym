using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipPromotionIntegrationTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public StubGymCalendar(DateOnly today, DateTime? utcNow = null)
        {
            Today = today;
            UtcNow = utcNow ?? today.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
        }

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateOnly Today { get; set; }
        public DateTime UtcNow { get; set; }
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
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static MembershipService CreateService(SmartGymDbContext db, IGymCalendar calendar)
    {
        var creditLedger = new CreditLedgerService(db, calendar);
        var auditService = new AuditService(db);
        var activationService = new MembershipActivationService(db, creditLedger, calendar, auditService);
        var paymentService = new PaymentService(db);
        var promotionService = new PromotionService(db, calendar);
        var eligibilityService = new MembershipEligibilityService(
            db,
            calendar,
            Options.Create(new GymOptions { DefaultCurrency = "ARS" }));

        return new MembershipService(
            db,
            creditLedger,
            activationService,
            eligibilityService,
            paymentService,
            promotionService,
            calendar,
            Options.Create(new GymOptions { DefaultCurrency = "ARS" }),
            Options.Create(new MembershipsOptions()),
            auditService);
    }

    private static async Task<(Person Student, MembershipPlan Plan)> SeedStudentAndPlanAsync(SmartGymDbContext db)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student = Person.Create("Lucas", "Paz", "38123456");
        student.AddRole(role);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            Price = 20000m,
            DurationDays = 30,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.People.Add(student);
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        return (student, plan);
    }

    [Fact]
    public async Task CreateAsync_WithPercentageDiscountPromotionAndNewStudentCondition_EvaluatesBeforeInsertAndLinksRedemption()
    {
        // Arrange (Criterio 12)
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 5, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan) = await SeedStudentAndPlanAsync(db);
        var service = CreateService(db, calendar);

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "BIENVENIDA20",
            Name = "20% Off Nuevos Alumnos",
            ConditionType = PromotionConditionType.NewStudent,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 20m,
            ValidFrom = today.AddDays(-5),
            ValidTo = today.AddDays(30),
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        var request = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: plan.Id,
            StartDate: today,
            PromotionCode: "bienvenida20",
            InitialPayment: new InitialMembershipPaymentRequest(
                Amount: 16000m,
                Method: PaymentMethod.Cash));

        // Act
        var result = await service.CreateAsync(request, Guid.NewGuid(), isCallerAdministrator: false);

        // Assert
        Assert.Equal(MembershipStatus.Active, result.Status);
        Assert.Equal(20000m, result.OriginalPrice);
        Assert.Equal(4000m, result.DiscountAmount);
        Assert.Equal(16000m, result.PurchasePrice);
        Assert.Equal(0m, result.PendingBalance);
        Assert.Equal(promo.Id, result.PromotionId);
        Assert.NotNull(result.PromotionRedemptionId);

        var redemption = await db.PromotionRedemptions.SingleAsync(r => r.Id == result.PromotionRedemptionId);
        Assert.Equal(result.Id, redemption.MembershipId);
        Assert.True(redemption.IsActive);
    }

    [Fact]
    public async Task CreateAsync_WithExtraCreditsPromotion_LoadsInitialAndPromotionBonusMovementsUponActivation()
    {
        // Arrange (Criterio 12)
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 5, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan) = await SeedStudentAndPlanAsync(db);
        var service = CreateService(db, calendar);

        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "BONUS2",
            Name = "2 Clases Extra",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.ExtraCredits,
            BenefitValue = 2m,
            ValidFrom = today.AddDays(-5),
            ValidTo = today.AddDays(30),
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        var request = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: plan.Id,
            StartDate: today,
            PromotionCode: "BONUS2",
            InitialPayment: new InitialMembershipPaymentRequest(
                Amount: 20000m,
                Method: PaymentMethod.Cash));

        // Act
        var result = await service.CreateAsync(request, Guid.NewGuid(), isCallerAdministrator: false);

        // Assert
        Assert.Equal(MembershipStatus.Active, result.Status);
        Assert.Equal(8, result.InitialCredits);
        Assert.Equal(2, result.ExtraCreditsGranted);
        Assert.Equal(10, result.TotalCredits);
        Assert.Equal(10, result.AvailableCredits);
        Assert.NotNull(result.CreditsSummary);
        Assert.Equal(8, result.CreditsSummary!.InitialCredits);
        Assert.Equal(2, result.CreditsSummary.PromotionBonusCredits);
        Assert.Equal(10, result.CreditsSummary.AvailableCredits);

        var movements = await db.MembershipCreditMovements
            .Where(m => m.MembershipId == result.Id)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, m => m.Type == CreditMovementType.InitialPurchase && m.Amount == 8);
        Assert.Contains(movements, m => m.Type == CreditMovementType.PromotionBonus && m.Amount == 2);
    }
}
