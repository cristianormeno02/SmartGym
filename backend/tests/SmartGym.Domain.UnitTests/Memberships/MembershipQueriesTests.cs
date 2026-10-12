using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipQueriesTests
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

    private static async Task<(Person Student1, Person Student2, MembershipPlan Plan)> SeedAsync(SmartGymDbContext db)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var student1 = Person.Create("Valeria", "Mora", "31000111");
        student1.AddRole(role);

        var student2 = Person.Create("Tomás", "Silva", "31000222");
        student2.AddRole(role);

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

        db.People.AddRange(student1, student2);
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        return (student1, student2, plan);
    }

    [Fact]
    public async Task GetPagedAsync_FiltersByEffectiveExpiredStatus_ExpiringWithinDays_AndHasAvailableCredits()
    {
        await using var db = CreateDbContext();
        var start = new DateOnly(2026, 6, 1);
        var calendar = new StubGymCalendar(start);
        var (student1, student2, plan) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        // m1: student1, inicia 01/06, vence 30/06 (activa)
        var m1 = await service.CreateAsync(
            new CreateMembershipRequest(
                student1.Id,
                plan.Id,
                StartDate: new DateOnly(2026, 6, 1),
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());

        // m2: student2, inicia 15/06, vence 14/07 (activa)
        var m2 = await service.CreateAsync(
            new CreateMembershipRequest(
                student2.Id,
                plan.Id,
                StartDate: new DateOnly(2026, 6, 15),
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());

        // Avanzamos la fecha al 09/07: m1 ya venció por fecha (aunque el worker aún no la pasó a Expired) y m2 vence en 5 días (14/07)
        calendar.Today = new DateOnly(2026, 7, 9);

        // 1. Filtro Status = Expired debe incluir m1 por estado efectivo
        var expiredPage = await service.GetPagedAsync(new MembershipListFilter(Status: MembershipStatus.Expired));
        Assert.Single(expiredPage.Items);
        Assert.Equal(m1.Id, expiredPage.Items[0].Id);
        Assert.Equal(MembershipStatus.Expired, expiredPage.Items[0].EffectiveStatus);

        // 2. Filtro Status = Active debe incluir sólo m2
        var activePage = await service.GetPagedAsync(new MembershipListFilter(Status: MembershipStatus.Active));
        Assert.Single(activePage.Items);
        Assert.Equal(m2.Id, activePage.Items[0].Id);

        // 3. Filtro ExpiringWithinDays = 7 debe incluir m2
        var expiringPage = await service.GetPagedAsync(new MembershipListFilter(ExpiringWithinDays: 7));
        Assert.Single(expiringPage.Items);
        Assert.Equal(m2.Id, expiringPage.Items[0].Id);
        Assert.Equal(6, expiringPage.Items[0].DaysRemaining); // 9..14 inclusive = 6 días
    }

    [Fact]
    public async Task GetByIdAsync_ComputesTotalPaidAmount_PendingBalance_HasOutstandingBalance_AndCurrentAndActiveQueries()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 6, 1);
        var calendar = new StubGymCalendar(today);
        var (student1, _, plan) = await SeedAsync(db);
        var service = CreateService(db, calendar);
        var paymentService = new PaymentService(db);

        // Crear membresía con pago parcial de 8000 y activación administrativa
        var created = await service.CreateAsync(
            new CreateMembershipRequest(
                student1.Id,
                plan.Id,
                StartDate: today,
                InitialPayment: new InitialMembershipPaymentRequest(8000m, PaymentMethod.Cash),
                ActivateImmediately: true,
                ActivationReason: "Pago en dos cuotas autorizado"),
            Guid.NewGuid(),
            isCallerAdministrator: true);

        // Registrar un segundo pago Pending de 12000 (no suma hasta confirmarse)
        await paymentService.RegisterPaymentAsync(
            new RegisterPaymentRequest(
                student1.Id,
                created.Id,
                null,
                12000m,
                PaymentMethod.Cash,
                null,
                "Segunda cuota pendiente",
                Status: PaymentStatus.Pending),
            Guid.NewGuid());

        var detail = await service.GetByIdAsync(created.Id);
        Assert.Equal(20000m, detail.PurchasePrice);
        Assert.Equal(8000m, detail.TotalPaidAmount);
        Assert.Equal(12000m, detail.PendingBalance);
        Assert.True(detail.HasOutstandingBalance);
        Assert.Equal(2, detail.Payments.Count);

        var current = await service.GetCurrentByStudentIdAsync(student1.Id);
        Assert.Single(current);
        Assert.Equal(created.Id, current[0].Id);

        var active = await service.GetActiveMembershipAsync(student1.Id, today);
        Assert.NotNull(active);
        Assert.Equal(created.Id, active!.Id);
    }

    [Fact]
    public async Task UpdateNotesAsync_UpdatesOnlyNotes_AndThrowsConflictOnStaleVersion()
    {
        // Arrange (Criterio 8 & Scenario: Edición con versión desactualizada)
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 6, 1);
        var calendar = new StubGymCalendar(today);
        var (student1, _, plan) = await SeedAsync(db);
        var service = CreateService(db, calendar);

        var created = await service.CreateAsync(
            new CreateMembershipRequest(
                student1.Id,
                plan.Id,
                StartDate: today,
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash),
                Notes: "Nota original"),
            Guid.NewGuid());

        var updated = await service.UpdateNotesAsync(
            created.Id,
            new UpdateMembershipNotesRequest("Nota actualizada", Version: created.Version),
            Guid.NewGuid());

        Assert.Equal("Nota actualizada", updated.Notes);
        Assert.Equal(8, updated.AvailableCredits); // Saldo intacto

        // Versión desactualizada -> ConflictException (409)
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateNotesAsync(
                created.Id,
                new UpdateMembershipNotesRequest("Intento con versión vieja", Version: created.Version + 99u),
                Guid.NewGuid()));
    }
}
