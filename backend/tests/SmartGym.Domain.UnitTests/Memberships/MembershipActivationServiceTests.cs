using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Handlers;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

public class MembershipActivationServiceTests
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

    private sealed class HandlerServiceProvider : IServiceProvider
    {
        private readonly Func<IDomainEventHandler<PaymentCompletedEvent>> _factory;

        public HandlerServiceProvider(Func<IDomainEventHandler<PaymentCompletedEvent>> factory)
        {
            _factory = factory;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IEnumerable<IDomainEventHandler<PaymentCompletedEvent>>))
            {
                return new[] { _factory() };
            }

            if (serviceType.IsGenericType &&
                serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var itemType = serviceType.GetGenericArguments()[0];
                return Array.CreateInstance(itemType, 0);
            }

            return null;
        }
    }

    private static SmartGymDbContext CreateDbContext(IServiceProvider? sp = null)
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SmartGymDbContext(options, sp);
    }

    private static async Task<(Person Student, MembershipPlan Plan, Membership Membership)> SeedPendingMembershipAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar,
        decimal price = 20000m,
        int credits = 8,
        int extraCredits = 0)
    {
        var student = Person.Create("Elena", "Ríos", "30999111");
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8",
            DurationDays = 30,
            Price = price,
            Credits = credits,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: new DateOnly(2026, 6, 1),
            purchasedAtUtc: calendar.UtcNow,
            originalPrice: price,
            discountAmount: 0m,
            purchasePrice: price,
            currency: "ARS",
            extraCreditsGranted: extraCredits);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync();

        return (student, plan, membership);
    }

    [Fact]
    public async Task TryActivate_WhenPriceIsZeroOrCoveredByCompletedPayments_ShouldActivateAndLoadCreditsIdempotently()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var ledger = new CreditLedgerService(db, calendar);
        IMembershipActivationService activationService = new MembershipActivationService(db, ledger, calendar);

        // Zero price membership (e.g. 100% discount promo)
        var (_, _, zeroPriceMembership) = await SeedPendingMembershipAsync(db, calendar, price: 0m, credits: 4, extraCredits: 2);
        var activatedZero = await activationService.TryActivateAsync(
            zeroPriceMembership.Id,
            authorizedByUserId: Guid.NewGuid(),
            activationReason: null,
            isExplicitActivation: false,
            isCallerAdministrator: false);

        Assert.True(activatedZero);
        var reloadedZero = await db.Memberships.SingleAsync(m => m.Id == zeroPriceMembership.Id);
        Assert.Equal(MembershipStatus.Active, reloadedZero.Status);
        Assert.True(reloadedZero.InitialCreditsLoaded);
        Assert.Equal(6, reloadedZero.AvailableCredits);

        // Second call is idempotent and does not duplicate credits
        var secondCall = await activationService.TryActivateAsync(
            zeroPriceMembership.Id,
            authorizedByUserId: Guid.NewGuid(),
            activationReason: null,
            isExplicitActivation: true,
            isCallerAdministrator: true);
        Assert.True(secondCall);
        Assert.Equal(6, (await db.Memberships.SingleAsync(m => m.Id == zeroPriceMembership.Id)).AvailableCredits);
        Assert.Equal(2, await db.MembershipCreditMovements.CountAsync(cm => cm.MembershipId == zeroPriceMembership.Id));
    }

    [Fact]
    public async Task TryActivate_WhenOutstandingBalanceExists_ShouldRequireAdministratorAndReason()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar();
        var ledger = new CreditLedgerService(db, calendar);
        IMembershipActivationService activationService = new MembershipActivationService(db, ledger, calendar);

        var (student, _, membership) = await SeedPendingMembershipAsync(db, calendar, price: 20000m, credits: 8);

        // Partial payment (10000 < 20000) does not auto-activate when isExplicitActivation == false
        var partialPayment = new Payment
        {
            Id = Guid.NewGuid(),
            PayerPersonId = student.Id,
            MembershipId = membership.Id,
            Amount = 10000m,
            Method = PaymentMethod.Cash,
            Status = PaymentStatus.Completed
        };
        db.Payments.Add(partialPayment);
        await db.SaveChangesAsync();

        var autoResult = await activationService.TryActivateAsync(
            membership.Id,
            authorizedByUserId: Guid.NewGuid(),
            activationReason: null,
            isExplicitActivation: false,
            isCallerAdministrator: false);
        Assert.False(autoResult);
        Assert.Equal(MembershipStatus.Pending, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).Status);

        // Secretary attempting explicit activation with pending balance -> ForbiddenException (403)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            activationService.TryActivateAsync(
                membership.Id,
                authorizedByUserId: Guid.NewGuid(),
                activationReason: "Autorización de secretaría",
                isExplicitActivation: true,
                isCallerAdministrator: false));

        // Administrator without reason -> ValidationException (400)
        await Assert.ThrowsAsync<ValidationException>(() =>
            activationService.TryActivateAsync(
                membership.Id,
                authorizedByUserId: Guid.NewGuid(),
                activationReason: "   ",
                isExplicitActivation: true,
                isCallerAdministrator: true));

        // Administrator with valid reason -> succeeds!
        var adminId = Guid.NewGuid();
        var adminResult = await activationService.TryActivateAsync(
            membership.Id,
            authorizedByUserId: adminId,
            activationReason: "Cuenta corriente autorizada por gerencia",
            isExplicitActivation: true,
            isCallerAdministrator: true);

        Assert.True(adminResult);
        var activated = await db.Memberships
            .Include(m => m.StatusChanges)
            .SingleAsync(m => m.Id == membership.Id);
        Assert.Equal(MembershipStatus.Active, activated.Status);
        Assert.Equal(8, activated.AvailableCredits);
        Assert.Contains(activated.StatusChanges, sc =>
            sc.NewStatus == MembershipStatus.Active &&
            sc.Reason == "Cuenta corriente autorizada por gerencia" &&
            sc.ChangedByUserId == adminId);
    }

    [Fact]
    public async Task ActivateMembershipOnPaymentCompletedHandler_ShouldActivateWhenPaymentCompletes_AndNotWhenFailed()
    {
        var calendar = new StubGymCalendar();
        SmartGymDbContext? dbRef = null;
        var sp = new HandlerServiceProvider(() =>
        {
            var ledger = new CreditLedgerService(dbRef!, calendar);
            var activationService = new MembershipActivationService(dbRef!, ledger, calendar);
            return new ActivateMembershipOnPaymentCompletedHandler(activationService);
        });

        await using var db = CreateDbContext(sp);
        dbRef = db;
        IPaymentService paymentService = new PaymentService(db);

        var (student, _, membership1) = await SeedPendingMembershipAsync(db, calendar, price: 20000m, credits: 8);
        var staffUserId = Guid.NewGuid();

        // Register a Pending payment linked to membership1
        var pendingPayment = await paymentService.RegisterPaymentAsync(
            new RegisterPaymentRequest(
                student.Id,
                membership1.Id,
                 null,
                20000m,
                PaymentMethod.BankTransfer,
                null,
                "Transferencia a verificar",
                PaymentStatus.Pending),
            staffUserId);

        Assert.Equal(MembershipStatus.Pending, (await db.Memberships.SingleAsync(m => m.Id == membership1.Id)).Status);

        // Confirming the payment dispatches PaymentCompletedEvent -> activates membership1 and loads credits!
        await paymentService.ConfirmPaymentAsync(
            pendingPayment.Id,
            new ConfirmPaymentRequest("TRF-555"),
            staffUserId);

        var activated = await db.Memberships.SingleAsync(m => m.Id == membership1.Id);
        Assert.Equal(MembershipStatus.Active, activated.Status);
        Assert.True(activated.InitialCreditsLoaded);
        Assert.Equal(8, activated.AvailableCredits);

        // Now test a second Pending membership whose payment is Failed -> stays Pending with 0 credits
        var (_, _, membership2) = await SeedPendingMembershipAsync(db, calendar, price: 20000m, credits: 8);
        var pendingPayment2 = await paymentService.RegisterPaymentAsync(
            new RegisterPaymentRequest(
                student.Id,
                membership2.Id,
                null,
                20000m,
                PaymentMethod.BankTransfer,
                null,
                "Transferencia rechazada",
                PaymentStatus.Pending),
            staffUserId);

        await paymentService.FailPaymentAsync(
            pendingPayment2.Id,
            new FailPaymentRequest("Fondos insuficientes"),
            staffUserId);

        var stillPending = await db.Memberships.SingleAsync(m => m.Id == membership2.Id);
        Assert.Equal(MembershipStatus.Pending, stillPending.Status);
        Assert.False(stillPending.InitialCreditsLoaded);
        Assert.Equal(0, stillPending.AvailableCredits);
    }
}
