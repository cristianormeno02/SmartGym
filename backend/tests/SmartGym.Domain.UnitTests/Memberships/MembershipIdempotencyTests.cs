using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipIdempotencyTests
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

    private sealed class LockingSpyDbContext : SmartGymDbContext
    {
        private readonly SemaphoreSlim _personLock = new(1, 1);
        public List<Guid> LockedPersonIds { get; } = new();

        public LockingSpyDbContext(DbContextOptions<SmartGymDbContext> options) : base(options)
        {
        }

        public override async Task LockPersonForUpdateAsync(Guid personId, CancellationToken cancellationToken = default)
        {
            await _personLock.WaitAsync(cancellationToken);
            try
            {
                LockedPersonIds.Add(personId);
                await base.LockPersonForUpdateAsync(personId, cancellationToken);
            }
            finally
            {
                _personLock.Release();
            }
        }
    }

    private static LockingSpyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new LockingSpyDbContext(options);
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

    private static async Task<(Person Student, MembershipPlan Plan1, MembershipPlan Plan2)> SeedAsync(SmartGymDbContext db)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student = Person.Create("Sofía", "Luna", "37654321");
        student.AddRole(role);

        var plan1 = new MembershipPlan
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

        var plan2 = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 12 Clases",
            Price = 28000m,
            DurationDays = 30,
            Credits = 12,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.People.Add(student);
        db.MembershipPlans.AddRange(plan1, plan2);
        await db.SaveChangesAsync();

        return (student, plan1, plan2);
    }

    [Fact]
    public async Task CreateAsync_SameIdempotencyKeyAndParameters_ReturnsExistingMembershipWithoutDuplicatingCreditsOrPayments()
    {
        // Arrange (Criterio 7 & 15)
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 6, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan1, _) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        var request = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: plan1.Id,
            StartDate: today,
            InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash),
            IdempotencyKey: "idem-key-001");

        // Act
        var first = await service.CreateAsync(request, Guid.NewGuid(), isCallerAdministrator: false);
        var second = await service.CreateAsync(request, Guid.NewGuid(), isCallerAdministrator: false);

        // Assert
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(8, second.AvailableCredits);
        Assert.Equal(1, await db.Memberships.CountAsync());
        Assert.Equal(1, await db.MembershipCreditMovements.CountAsync());
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(2, db.LockedPersonIds.Count(id => id == student.Id));
    }

    [Fact]
    public async Task CreateAsync_SameIdempotencyKeyWithDifferentPlanOrStartDate_ThrowsConflictException()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 6, 1);
        var calendar = new StubGymCalendar(today);
        var (student, plan1, plan2) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        var firstReq = new CreateMembershipRequest(
            StudentId: student.Id,
            MembershipPlanId: plan1.Id,
            StartDate: today,
            IdempotencyKey: "idem-key-002");

        await service.CreateAsync(firstReq, Guid.NewGuid(), isCallerAdministrator: false);

        // Misma clave pero otro plan
        var differentPlanReq = firstReq with { MembershipPlanId = plan2.Id };
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(differentPlanReq, Guid.NewGuid(), isCallerAdministrator: false));

        // Misma clave pero otra fecha de inicio
        var differentDateReq = firstReq with { StartDate = today.AddDays(5) };
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(differentDateReq, Guid.NewGuid(), isCallerAdministrator: false));
    }
}
