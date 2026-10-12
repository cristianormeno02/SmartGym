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

public class MembershipExpirationProcessTests
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
            Options.Create(new MembershipsOptions
            {
                ExpiringWarningDays = 7,
                PendingGraceDays = 7,
                MaxSuspensionDays = 90
            }),
            auditService);
    }

    private static async Task<(Role StudentRole, MembershipPlan MonthlyPlan, MembershipPlan DailyPlan)> SeedPlansAsync(SmartGymDbContext db)
    {
        var role = new Role
        {
            Id = (int)RoleType.Student,
            Name = RoleType.Student.ToString(),
            Description = "Alumno"
        };
        db.Roles.Add(role);

        var monthlyPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Mensual 8",
            Price = 20000m,
            DurationDays = 30,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        var dailyPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Diario",
            Price = 4000m,
            DurationDays = 1,
            Credits = 1,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.MembershipPlans.AddRange(monthlyPlan, dailyPlan);
        await db.SaveChangesAsync();

        return (role, monthlyPlan, dailyPlan);
    }

    private static async Task<Person> CreateStudentAsync(SmartGymDbContext db, Role role, string firstName, string dni)
    {
        var student = Person.Create(firstName, "Test", dni);
        student.AddRole(role);
        db.People.Add(student);
        await db.SaveChangesAsync();
        return student;
    }

    [Fact]
    public async Task ProcessExpirationsAndWarningsAsync_HandlesReactivations_Expirations_FrozenSuspensions_PendingAutoCancel_Warnings_AndIsIdempotent()
    {
        // Arrange (Criterio 16)
        await using var db = CreateDbContext();
        var start = new DateOnly(2026, 11, 1); // vence 30/11
        var calendar = new StubGymCalendar(start);
        var (role, monthlyPlan, dailyPlan) = await SeedPlansAsync(db);
        var service = CreateService(db, calendar);

        // 1. Alumno con suspensión con extensión que finaliza el 14/11 -> se reactiva el 15/11 con EndDate = 05/12
        var sReactivateExt = await CreateStudentAsync(db, role, "S1", "40000001");
        var mReactivateExt = await service.CreateAsync(
            new CreateMembershipRequest(sReactivateExt.Id, monthlyPlan.Id, start, InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());
        calendar.Today = new DateOnly(2026, 11, 10);
        await service.SuspendAsync(
            mReactivateExt.Id,
            new SuspendMembershipRequest("Viaje", new DateOnly(2026, 11, 14), ExtendExpirationDate: true),
            Guid.NewGuid());

        calendar.Today = new DateOnly(2026, 11, 15);
        var batchOn15 = await service.ProcessExpirationsAndWarningsAsync();
        Assert.Equal(1, batchOn15.ReactivatedCount);
        var reloadedReactivated = await service.GetByIdAsync(mReactivateExt.Id);
        Assert.Equal(MembershipStatus.Active, reloadedReactivated.Status);
        Assert.Equal(new DateOnly(2026, 12, 5), reloadedReactivated.EndDate);

        // 2. Alumno con membresía Active que vence el 30/11 (con 8 créditos remanentes)
        calendar.Today = start;
        var sExpireActive = await CreateStudentAsync(db, role, "S2", "40000002");
        var mExpireActive = await service.CreateAsync(
            new CreateMembershipRequest(sExpireActive.Id, monthlyPlan.Id, start, InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());

        // 3. Alumno con membresía Suspended SIN extensión que vence el 30/11 -> debe pasar a Expired el 01/12
        var sExpireSusp = await CreateStudentAsync(db, role, "S3", "40000003");
        var mExpireSusp = await service.CreateAsync(
            new CreateMembershipRequest(sExpireSusp.Id, monthlyPlan.Id, start, InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());
        calendar.Today = new DateOnly(2026, 11, 20);
        await service.SuspendAsync(
            mExpireSusp.Id,
            new SuspendMembershipRequest("Lesión sin extensión", EndDate: null, ExtendExpirationDate: false),
            Guid.NewGuid());

        // 4. Alumno con membresía Suspended CON extensión hasta el 09/12 (vencimiento original 30/11) -> NO debe vencer el 01/12
        calendar.Today = start;
        var sFrozenSusp = await CreateStudentAsync(db, role, "S4", "40000004");
        var mFrozenSusp = await service.CreateAsync(
            new CreateMembershipRequest(sFrozenSusp.Id, monthlyPlan.Id, start, InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());
        calendar.Today = new DateOnly(2026, 11, 25);
        await service.SuspendAsync(
            mFrozenSusp.Id,
            new SuspendMembershipRequest("Cirugía con extensión", EndDate: new DateOnly(2026, 12, 9), ExtendExpirationDate: true),
            Guid.NewGuid());

        // 5. Alumno con membresía Pending impaga (StartDate 01/11, más de 7 días de gracia) con promoción canjeada -> se cancela y devuelve el canje
        calendar.Today = start;
        var promo = new Promotion
        {
            Id = Guid.NewGuid(),
            Code = "PROMOPEND",
            Name = "Promo Pendiente",
            ConditionType = PromotionConditionType.CouponCode,
            BenefitType = PromotionBenefitType.PercentageDiscount,
            BenefitValue = 10m,
            ValidFrom = start.AddDays(-5),
            ValidTo = start.AddDays(60),
            MaxUsesPerPerson = 1,
            IsActive = true
        };
        db.Promotions.Add(promo);
        await db.SaveChangesAsync();

        var sPending = await CreateStudentAsync(db, role, "S5", "40000005");
        var mPending = await service.CreateAsync(
            new CreateMembershipRequest(sPending.Id, monthlyPlan.Id, start, PromotionCode: "PROMOPEND"),
            Guid.NewGuid());

        // 6. Membresías para evaluar avisos de vencimiento próximo el 01/12:
        //    - mReactivateExt vence el 05/12 (dentro de 7 días, duración 30 > 7, sin renovar) -> DEBE recibir aviso
        //    - Pase diario contratado el 01/12 (duración 1 <= 7) -> NO debe recibir aviso
        //    - Membresía mensual que vence el 04/12 pero YA TIENE renovación contratada -> NO debe recibir aviso
        calendar.Today = new DateOnly(2026, 11, 5); // vence 04/12
        var sAlreadyRenewed = await CreateStudentAsync(db, role, "S6", "40000006");
        var mAlreadyRenewed = await service.CreateAsync(
            new CreateMembershipRequest(sAlreadyRenewed.Id, monthlyPlan.Id, new DateOnly(2026, 11, 5), InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());
        await service.RenewAsync(
            mAlreadyRenewed.Id,
            new RenewMembershipRequest(InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());

        calendar.Today = new DateOnly(2026, 12, 1);
        calendar.UtcNow = new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc);
        var sDaily = await CreateStudentAsync(db, role, "S7", "40000007");
        var mDaily = await service.CreateAsync(
            new CreateMembershipRequest(sDaily.Id, dailyPlan.Id, new DateOnly(2026, 12, 1), InitialPayment: new InitialMembershipPaymentRequest(4000m, PaymentMethod.Cash)),
            Guid.NewGuid());

        // Act: primera ejecución el 01/12
        var firstRun = await service.ProcessExpirationsAndWarningsAsync();

        // Assert primera ejecución
        Assert.Equal(0, firstRun.ReactivatedCount);
        Assert.Equal(2, firstRun.ExpiredCount); // mExpireActive y mExpireSusp
        Assert.Equal(1, firstRun.AutoCancelledCount); // mPending
        Assert.Equal(1, firstRun.WarningsSentCount); // sólo mReactivateExt

        Assert.Equal(MembershipStatus.Expired, (await service.GetByIdAsync(mExpireActive.Id)).Status);
        Assert.Equal(0, (await service.GetByIdAsync(mExpireActive.Id)).AvailableCredits);
        Assert.Equal(MembershipStatus.Expired, (await service.GetByIdAsync(mExpireSusp.Id)).Status);
        Assert.Equal(MembershipStatus.Suspended, (await service.GetByIdAsync(mFrozenSusp.Id)).Status); // congelada, no venció
        Assert.Equal(8, (await service.GetByIdAsync(mFrozenSusp.Id)).AvailableCredits);

        Assert.Equal(MembershipStatus.Cancelled, (await service.GetByIdAsync(mPending.Id)).Status);
        Assert.False((await db.PromotionRedemptions.SingleAsync(r => r.Id == mPending.PromotionRedemptionId)).IsActive);

        // Ni el pase diario ni la membresía ya renovada recibieron notificación MembershipExpiring
        var expiringNotifs = await db.NotificationMessages
            .Where(n => n.Type == NotificationType.MembershipExpiring)
            .ToListAsync();
        Assert.Single(expiringNotifs);
        Assert.Equal(sReactivateExt.Id, expiringNotifs[0].RecipientPersonId);

        // Act: segunda ejecución inmediata en el mismo día -> 0 efectos ni notificaciones duplicadas (Criterio 16)
        var secondRun = await service.ProcessExpirationsAndWarningsAsync();
        Assert.Equal(0, secondRun.ReactivatedCount);
        Assert.Equal(0, secondRun.ExpiredCount);
        Assert.Equal(0, secondRun.AutoCancelledCount);
        Assert.Equal(0, secondRun.WarningsSentCount);
        Assert.Single(await db.NotificationMessages.Where(n => n.Type == NotificationType.MembershipExpiring).ToListAsync());
        Assert.Equal(2, await db.NotificationMessages.CountAsync(n => n.Type == NotificationType.MembershipExpired));
    }

    [Fact]
    public async Task FirstWorkerCycle_OnMigratedHistoricalMemberships_ProducesZeroMovementsAndZeroNotifications()
    {
        // Arrange: simula una copia con datos históricos tras aplicar la migración RefactorMembershipsModule (Tarea 8.2)
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 12, 1);
        var migrationTimestampUtc = new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc);
        var calendar = new StubGymCalendar(today, new DateTime(2026, 12, 1, 8, 0, 0, DateTimeKind.Utc));
        var (role, monthlyPlan, _) = await SeedPlansAsync(db);
        var service = CreateService(db, calendar);

        var sHistoricalExpired = await CreateStudentAsync(db, role, "Hist1", "41000001");
        var mHistoricalExpired = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = sHistoricalExpired.Id,
            MembershipPlanId = monthlyPlan.Id,
            Status = MembershipStatus.Expired,
            StartDate = new DateOnly(2026, 10, 1),
            EndDate = new DateOnly(2026, 10, 30),
            PurchasedAtUtc = migrationTimestampUtc.AddDays(-60),
            ActivatedAtUtc = migrationTimestampUtc.AddDays(-60),
            DurationDaysSnapshot = 30,
            InitialCredits = 8,
            ExtraCreditsGranted = 0,
            TotalCredits = 8,
            AvailableCredits = 0,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            InitialCreditsLoaded = true,
            OriginalPrice = 20000m,
            DiscountAmount = 0m,
            PurchasePrice = 20000m,
            Currency = "ARS",
            ExpiringNotificationSentAtUtc = migrationTimestampUtc,
            ExpiredNotificationSentAtUtc = migrationTimestampUtc,
            Notes = "Precio estimado desde el plan vigente al migrar",
            IsActive = true
        };

        var sHistoricalExpiringSoon = await CreateStudentAsync(db, role, "Hist2", "41000002");
        var mHistoricalExpiringSoon = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = sHistoricalExpiringSoon.Id,
            MembershipPlanId = monthlyPlan.Id,
            Status = MembershipStatus.Active,
            StartDate = new DateOnly(2026, 11, 5),
            EndDate = new DateOnly(2026, 12, 4), // vence dentro de 7 días de hoy 01/12
            PurchasedAtUtc = migrationTimestampUtc.AddDays(-26),
            ActivatedAtUtc = migrationTimestampUtc.AddDays(-26),
            DurationDaysSnapshot = 30,
            InitialCredits = 8,
            ExtraCreditsGranted = 0,
            TotalCredits = 8,
            AvailableCredits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            InitialCreditsLoaded = true,
            OriginalPrice = 20000m,
            DiscountAmount = 0m,
            PurchasePrice = 20000m,
            Currency = "ARS",
            ExpiringNotificationSentAtUtc = migrationTimestampUtc,
            Notes = "Precio estimado desde el plan vigente al migrar",
            IsActive = true
        };

        db.Memberships.AddRange(mHistoricalExpired, mHistoricalExpiringSoon);
        await db.SaveChangesAsync();

        var initialMovementsCount = await db.MembershipCreditMovements.CountAsync();
        var initialNotificationsCount = await db.NotificationMessages.CountAsync();

        // Act: primer ciclo del worker después de la migración
        var result = await service.ProcessExpirationsAndWarningsAsync();

        // Assert: ningún movimiento ni notificación generada para las membresías históricas migradas
        Assert.Equal(0, result.ReactivatedCount);
        Assert.Equal(0, result.ExpiredCount);
        Assert.Equal(0, result.AutoCancelledCount);
        Assert.Equal(0, result.WarningsSentCount);
        Assert.Equal(initialMovementsCount, await db.MembershipCreditMovements.CountAsync());
        Assert.Equal(initialNotificationsCount, await db.NotificationMessages.CountAsync());
    }
}
