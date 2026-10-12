using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipPurchaseTests
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

    private static async Task<(Person Student, MembershipPlan Plan, Activity Activity1, Activity Activity2)> SeedStudentAndPlanAsync(
        SmartGymDbContext db,
        PersonStatus personStatus = PersonStatus.Active,
        bool hasStudentRole = true,
        bool planActive = true)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student = Person.Create("Martina", "Gómez", "36789012");
        if (hasStudentRole)
        {
            student.AddRole(role);
        }

        if (personStatus != PersonStatus.Active)
        {
            student.ChangeStatus(personStatus, "Cambio administrativo", Guid.NewGuid(), isAdmin: true);
        }

        var activity1 = new Activity("FUNC", "Funcional", defaultCapacity: 20, minAge: 14, maxAge: 70);
        var activity2 = new Activity("SPIN", "Spinning", defaultCapacity: 15, minAge: 14, maxAge: 70);
        db.Activities.AddRange(activity1, activity2);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            Price = 24000m,
            DurationDays = 30,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = false,
            IsActive = planActive
        };
        plan.AllowedActivities.Add(new MembershipPlanActivity { MembershipPlanId = plan.Id, ActivityId = activity1.Id });

        db.People.Add(student);
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        return (student, plan, activity1, activity2);
    }

    [Fact]
    public async Task CreateAsync_ValidStudentAndActivePlanWithFullInitialPayment_CreatesActiveMembershipWithCommercialSnapshotAndCredits()
    {
        // Arrange (Criterio 1 & 2)
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 4, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, activity1, activity2) = await SeedStudentAndPlanAsync(db);
        var service = CreateService(db, calendar);
        var staffUserId = Guid.NewGuid();

        var request = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: plan.Id,
            StartDate: today,
            PromotionCode: null,
            InitialPayment: new InitialMembershipPaymentRequest(
                Amount: 24000m,
                Method: PaymentMethod.Cash,
                ExternalTransactionId: "REC-001",
                Notes: "Pago en mostrador"),
            PaymentId: null,
            ActivateImmediately: false,
            ActivationReason: null,
            Notes: "Contratación inicial",
            IdempotencyKey: "key-purchase-1");

        // Act
        var result = await service.CreateAsync(request, staffUserId, isCallerAdministrator: false);

        // Assert
        Assert.Equal(MembershipStatus.Active, result.Status);
        Assert.Equal(MembershipStatus.Active, result.EffectiveStatus);
        Assert.Equal(new DateOnly(2026, 4, 1), result.StartDate);
        Assert.Equal(new DateOnly(2026, 4, 30), result.EndDate); // 30 días inclusivos
        Assert.Equal(30, result.DurationDaysSnapshot);
        Assert.Equal(24000m, result.OriginalPrice);
        Assert.Equal(0m, result.DiscountAmount);
        Assert.Equal(24000m, result.PurchasePrice);
        Assert.Equal(24000m, result.TotalPaidAmount);
        Assert.Equal(0m, result.PendingBalance);
        Assert.False(result.HasOutstandingBalance);
        Assert.Equal(8, result.InitialCredits);
        Assert.Equal(8, result.AvailableCredits);
        Assert.True(result.InitialCreditsLoaded);
        Assert.Single(result.AllowedActivityIds);
        Assert.Contains(activity1.Id, result.AllowedActivityIds);

        // Mutate the plan afterwards and verify the membership snapshot does not change (Criterio 2)
        plan.Price = 35000m;
        plan.DurationDays = 15;
        plan.AllowedActivities.Clear();
        plan.AllowedActivities.Add(new MembershipPlanActivity { MembershipPlanId = plan.Id, ActivityId = activity2.Id });
        await db.SaveChangesAsync();

        var reloaded = await service.GetByIdAsync(result.Id);
        Assert.Equal(24000m, reloaded.PurchasePrice);
        Assert.Equal(new DateOnly(2026, 4, 30), reloaded.EndDate);
        Assert.Single(reloaded.AllowedActivityIds);
        Assert.Contains(activity1.Id, reloaded.AllowedActivityIds);
    }

    [Theory]
    [InlineData(PersonStatus.Blocked)]
    [InlineData(PersonStatus.Inactive)]
    [InlineData(PersonStatus.Deceased)]
    public async Task CreateAsync_NonActivePersonStatus_ThrowsValidationOrConflict(PersonStatus status)
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 4, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, _, _) = await SeedStudentAndPlanAsync(db, personStatus: status);
        var service = CreateService(db, calendar);

        var request = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: plan.Id,
            StartDate: today);

        var ex = await Record.ExceptionAsync(() => service.CreateAsync(request, Guid.NewGuid(), isCallerAdministrator: false));
        Assert.NotNull(ex);
        Assert.True(ex is ValidationException || ex is ConflictException);
    }

    [Fact]
    public async Task CreateAsync_InactivePlanOrPastStartDate_ThrowsValidationException()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 4, 10);
        var calendar = new StubGymCalendar(today);
        var (student, inactivePlan, _, _) = await SeedStudentAndPlanAsync(db, planActive: false);
        var service = CreateService(db, calendar);

        var inactivePlanReq = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: inactivePlan.Id,
            StartDate: today);
        var planEx = await Record.ExceptionAsync(() => service.CreateAsync(inactivePlanReq, Guid.NewGuid(), isCallerAdministrator: false));
        Assert.NotNull(planEx);
        Assert.True(planEx is ValidationException || planEx is ConflictException);

        inactivePlan.IsActive = true;
        await db.SaveChangesAsync();

        var pastStartReq = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: inactivePlan.Id,
            StartDate: today.AddDays(-1));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CreateAsync(pastStartReq, Guid.NewGuid(), isCallerAdministrator: false));
    }

    [Fact]
    public async Task CreateAsync_WithoutPaymentAndWithoutImmediateActivation_CreatesPendingMembershipWithoutLoadingCredits()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 4, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, _, _) = await SeedStudentAndPlanAsync(db);
        var service = CreateService(db, calendar);

        var request = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: plan.Id,
            StartDate: today);

        var result = await service.CreateAsync(request, Guid.NewGuid(), isCallerAdministrator: false);

        Assert.Equal(MembershipStatus.Pending, result.Status);
        Assert.False(result.InitialCreditsLoaded);
        Assert.Equal(0, result.AvailableCredits);
        Assert.Equal(24000m, result.PendingBalance);
        Assert.True(result.HasOutstandingBalance);
    }

    [Fact]
    public async Task CreateAsync_WithExistingPaymentIdAlreadyLinkedToAnotherMembership_ThrowsConflictAndRollsBackAtomically()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 4, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, _, _) = await SeedStudentAndPlanAsync(db);
        var service = CreateService(db, calendar);

        var alreadyLinkedPayment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = student.Id,
            MembershipId = Guid.NewGuid(),
            Amount = 24000m,
            Method = PaymentMethod.Cash,
            Status = PaymentStatus.Completed,
            PaidAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.Payments.Add(alreadyLinkedPayment);
        await db.SaveChangesAsync();

        var request = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: plan.Id,
            StartDate: today,
            PaymentId: alreadyLinkedPayment.Id);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(request, Guid.NewGuid(), isCallerAdministrator: false));

        Assert.Equal(0, await db.Memberships.CountAsync());
        Assert.Equal(0, await db.MembershipCreditMovements.CountAsync());
    }

    private static Payment NewUnlinkedPayment(Guid payerId, decimal amount, DateTime paidAtUtc, string currency = "ARS") => new()
    {
        Id = Guid.NewGuid(),
        PayerPersonId = payerId,
        Amount = amount,
        Currency = currency,
        Method = PaymentMethod.Cash,
        Status = PaymentStatus.Completed,
        PaidAtUtc = paidAtUtc,
        ConfirmedAtUtc = paidAtUtc,
        IsActive = true
    };

    [Fact]
    public async Task CreateAsync_WithExistingCompletedPaymentId_AppliesItThroughPayments_AndActivatesMembership()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 4, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, _, _) = await SeedStudentAndPlanAsync(db);
        var service = CreateService(db, calendar);

        var payment = NewUnlinkedPayment(student.Id, 24000m, calendar.UtcNow);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(
            new CreateMembershipRequest(student.Id, plan.Id, today, PaymentId: payment.Id),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        Assert.Equal(MembershipStatus.Active, result.Status);
        Assert.Equal(24000m, result.TotalPaidAmount);
        Assert.Equal(result.Id, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).MembershipId);

        // La aplicación pasa por Payments: queda su registro de auditoría
        Assert.Contains(await db.AuditLogs.ToListAsync(), a => a.Action == "PAYMENT_APPLIED" && a.EntityId == payment.Id.ToString());
    }

    [Theory]
    [InlineData("USD", 24000, "CURRENCY_MISMATCH")]
    [InlineData("ARS", 30000, "OBLIGATION_OVERPAID")]
    public async Task CreateAsync_WithExistingPaymentIdThatDoesNotFit_ThrowsCodedConflictAndRollsBack(
        string currency, decimal amount, string expectedCode)
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 4, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, _, _) = await SeedStudentAndPlanAsync(db);
        var service = CreateService(db, calendar);

        var payment = NewUnlinkedPayment(student.Id, amount, calendar.UtcNow, currency);
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(
            new CreateMembershipRequest(student.Id, plan.Id, today, PaymentId: payment.Id),
            Guid.NewGuid(),
            isCallerAdministrator: false));

        Assert.Equal(expectedCode, ex.Code);
        Assert.Equal(0, await db.Memberships.CountAsync());
        Assert.Null((await db.Payments.SingleAsync(p => p.Id == payment.Id)).MembershipId);
    }

    [Fact]
    public async Task GetById_ReportsNetPaidAmount_ForPartiallyRefundedAndRefundedPayments()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 4, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan, _, _) = await SeedStudentAndPlanAsync(db);
        var service = CreateService(db, calendar);

        var created = await service.CreateAsync(
            new CreateMembershipRequest(student.Id, plan.Id, today),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        Payment Linked(decimal amount, decimal refunded, PaymentStatus status)
        {
            var p = NewUnlinkedPayment(student.Id, amount, calendar.UtcNow);
            p.MembershipId = created.Id;
            p.RefundedAmount = refunded;
            p.Status = status;
            return p;
        }

        db.Payments.AddRange(
            Linked(15000m, 5000m, PaymentStatus.PartiallyRefunded),
            Linked(10000m, 0m, PaymentStatus.Completed),
            Linked(24000m, 24000m, PaymentStatus.Refunded));
        await db.SaveChangesAsync();

        var detail = await service.GetByIdAsync(created.Id);

        Assert.Equal(20000m, detail.TotalPaidAmount);
        Assert.Equal(4000m, detail.PendingBalance);
        Assert.Equal(3, detail.Payments.Count);
        Assert.Contains(detail.Payments, p => p.Status == PaymentStatus.PartiallyRefunded && p.RefundedAmount == 5000m && p.NetAmount == 10000m);

        var listed = await service.GetPagedAsync(new MembershipListFilter(StudentId: student.Id));
        Assert.Equal(20000m, Assert.Single(listed.Items).TotalPaidAmount);
    }
}
