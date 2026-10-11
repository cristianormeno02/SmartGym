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
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipOverlapTests
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

    private static async Task<(Person Student, Activity Act1, Activity Act2, MembershipPlan PlanAct1, MembershipPlan PlanAct2, MembershipPlan UnlimitedPlan)> SeedAsync(SmartGymDbContext db)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student = Person.Create("Nicolás", "Vega", "35111222");
        student.AddRole(role);

        var act1 = new Activity("FUNC", "Funcional", defaultCapacity: 20);
        var act2 = new Activity("YOGA", "Yoga", defaultCapacity: 15);
        db.Activities.AddRange(act1, act2);

        var planAct1 = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Funcional",
            Price = 18000m,
            DurationDays = 30,
            Credits = 2,
            IsUnlimited = false,
            AppliesToAllActivities = false,
            IsActive = true
        };
        planAct1.AllowedActivities.Add(new MembershipPlanActivity { MembershipPlanId = planAct1.Id, ActivityId = act1.Id });

        var planAct2 = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Yoga",
            Price = 18000m,
            DurationDays = 30,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = false,
            IsActive = true
        };
        planAct2.AllowedActivities.Add(new MembershipPlanActivity { MembershipPlanId = planAct2.Id, ActivityId = act2.Id });

        var unlimitedPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Libre",
            Price = 32000m,
            DurationDays = 30,
            Credits = 0,
            IsUnlimited = true,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.People.Add(student);
        db.MembershipPlans.AddRange(planAct1, planAct2, unlimitedPlan);
        await db.SaveChangesAsync();

        return (student, act1, act2, planAct1, planAct2, unlimitedPlan);
    }

    [Fact]
    public async Task CreateAsync_OverlappingWithActiveWithCreditsOrPendingWithCreditsOrUnlimited_ThrowsConflictWithOverlappingId()
    {
        // Arrange
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 10, 1);
        var calendar = new StubGymCalendar(today);
        var (student, _, _, planAct1, _, unlimitedPlan) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        // 1. Contratar membresía Pending con 2 créditos por acreditar (01/10 al 30/10)
        var pending = await service.CreateAsync(
            new CreateMembershipRequest(student.Id, planAct1.Id, StartDate: today),
            Guid.NewGuid(),
            isCallerAdministrator: false);
        Assert.Equal(MembershipStatus.Pending, pending.Status);

        // Intentar contratar otra superpuesta para la misma actividad (05/10 al 03/11) -> 409
        var exPending = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(
                new CreateMembershipRequest(student.Id, planAct1.Id, StartDate: today.AddDays(4)),
                Guid.NewGuid(),
                isCallerAdministrator: false));
        Assert.Contains(pending.Id.ToString(), exPending.Message);
        Assert.NotNull(exPending.Details);

        // 2. Activar la membresía y volver a intentar superponer mientras tiene créditos -> 409
        await service.ActivateAsync(pending.Id, new ActivateMembershipRequest("Cortesía admin"), Guid.NewGuid(), isCallerAdministrator: true);

        var exActive = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(
                new CreateMembershipRequest(student.Id, unlimitedPlan.Id, StartDate: today.AddDays(10)),
                Guid.NewGuid(),
                isCallerAdministrator: false));
        Assert.Contains(pending.Id.ToString(), exActive.Message);
    }

    [Fact]
    public async Task CreateAsync_AllowsDisjointActivities_ConsecutivePeriods_AndOverlappingWhenExistingIsDepleted()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 10, 1);
        var calendar = new StubGymCalendar(today);
        var (student, _, _, planAct1, planAct2, _) = await SeedAsync(db);
        var service = CreateService(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);

        // 1. Membresía activa de Funcional (01/10 al 30/10)
        var funcMembership = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                planAct1.Id,
                StartDate: today,
                InitialPayment: new InitialMembershipPaymentRequest(18000m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        // 2. Admite otra simultánea de Yoga (actividades disjuntas) en el mismo rango (01/10 al 30/10)
        var yogaMembership = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                planAct2.Id,
                StartDate: today,
                InitialPayment: new InitialMembershipPaymentRequest(18000m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);
        Assert.Equal(MembershipStatus.Active, yogaMembership.Status);

        // 3. Admite período consecutivo de Funcional (inicia 31/10)
        var consecutiveFunc = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                planAct1.Id,
                StartDate: new DateOnly(2026, 10, 31)),
            Guid.NewGuid(),
            isCallerAdministrator: false);
        Assert.Equal(new DateOnly(2026, 10, 31), consecutiveFunc.StartDate);

        // 4. Si funcMembership (01/10..30/10) agota sus 2 créditos el 15/10, permite contratar una nueva de Funcional el 15/10
        calendar.Today = new DateOnly(2026, 10, 15);
        var trackedFunc = await db.Memberships.SingleAsync(m => m.Id == funcMembership.Id);
        await ledger.ConsumeCreditForReservationAsync(trackedFunc, Guid.NewGuid(), calendar.Today, "Clase 1");
        await ledger.ConsumeCreditForReservationAsync(trackedFunc, Guid.NewGuid(), calendar.Today, "Clase 2");
        await db.SaveChangesAsync();

        // Cancelamos la futura consecutiva para probar solapamiento sólo con la agotada
        await service.CancelAsync(consecutiveFunc.Id, new CancelMembershipRequest("Reemplazo por inicio inmediato"), Guid.NewGuid());

        var replacementFunc = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                planAct1.Id,
                StartDate: calendar.Today,
                InitialPayment: new InitialMembershipPaymentRequest(18000m, PaymentMethod.Cash)),
            Guid.NewGuid(),
            isCallerAdministrator: false);

        Assert.Equal(MembershipStatus.Active, replacementFunc.Status);
        Assert.Equal(new DateOnly(2026, 10, 15), replacementFunc.StartDate);
    }
}
