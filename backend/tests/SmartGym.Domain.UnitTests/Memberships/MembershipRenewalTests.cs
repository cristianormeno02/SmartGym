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

public class MembershipRenewalTests
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

    private static async Task<(Person Student, MembershipPlan Plan8, MembershipPlan Plan12)> SeedAsync(SmartGymDbContext db)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student = Person.Create("Julieta", "Ramos", "34555666");
        student.AddRole(role);

        var plan8 = new MembershipPlan
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

        var plan12 = new MembershipPlan
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
        db.MembershipPlans.AddRange(plan8, plan12);
        await db.SaveChangesAsync();

        return (student, plan8, plan12);
    }

    [Fact]
    public async Task RenewAsync_EarlyRenewalOnActiveMembership_CreatesNewConsecutiveEntityLinkedByRenewedFromMembershipId()
    {
        // Arrange (Criterio 10)
        await using var db = CreateDbContext();
        var start = new DateOnly(2026, 9, 21); // vence 20/10 (30 días inclusivos)
        var calendar = new StubGymCalendar(start);
        var (student, plan8, plan12) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        var original = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                plan8.Id,
                StartDate: start,
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);
        Assert.Equal(new DateOnly(2026, 10, 20), original.EndDate);

        // El 18/10 renueva anticipadamente cambiando al Plan 12
        calendar.Today = new DateOnly(2026, 10, 18);
        var renewed = await service.RenewAsync(
            original.Id,
            new RenewMembershipRequest(
                MembershipPlanId: plan12.Id,
                StartDate: null,
                InitialPayment: new InitialMembershipPaymentRequest(28000m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        // Assert
        Assert.NotEqual(original.Id, renewed.Id);
        Assert.Equal(original.Id, renewed.RenewedFromMembershipId);
        Assert.Equal(new DateOnly(2026, 10, 21), renewed.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 19), renewed.EndDate); // 30 días inclusivos desde 21/10
        Assert.Equal(28000m, renewed.PurchasePrice);
        Assert.Equal(12, renewed.AvailableCredits);
        Assert.True(renewed.StartsInFuture);

        // La membresía original permanece intacta
        var reloadedOriginal = await service.GetByIdAsync(original.Id);
        Assert.Equal(20000m, reloadedOriginal.PurchasePrice);
        Assert.Equal(new DateOnly(2026, 10, 20), reloadedOriginal.EndDate);
    }

    [Fact]
    public async Task RenewAsync_FromExpiredMembership_StartsTodayByDefault_AndRejectsPendingOrCancelled()
    {
        await using var db = CreateDbContext();
        var start = new DateOnly(2026, 8, 1);
        var calendar = new StubGymCalendar(start);
        var (student, plan8, _) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        var original = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                plan8.Id,
                StartDate: start,
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        // Avanzar fecha a después del vencimiento (vence 30/08, hoy es 05/09)
        calendar.Today = new DateOnly(2026, 9, 5);
        var renewedFromExpired = await service.RenewAsync(
            original.Id,
            new RenewMembershipRequest(), // queda Pending porque no envía pago
            Guid.NewGuid(),
            isCallerAdministrator: false);

        Assert.Equal(new DateOnly(2026, 9, 5), renewedFromExpired.StartDate);
        Assert.Equal(MembershipStatus.Pending, renewedFromExpired.Status);

        // No permite renovar una Pending -> 409
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RenewAsync(renewedFromExpired.Id, new RenewMembershipRequest(), Guid.NewGuid()));

        // Cancelamos la Pending y verificamos que tampoco permite renovar una Cancelled -> 409
        await service.CancelAsync(renewedFromExpired.Id, new CancelMembershipRequest("No abonó"), Guid.NewGuid());
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RenewAsync(renewedFromExpired.Id, new RenewMembershipRequest(), Guid.NewGuid()));
    }
}
