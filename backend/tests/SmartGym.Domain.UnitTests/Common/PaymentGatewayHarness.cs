using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Handlers;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Common;

/// <summary>
/// Contexto en memoria cableado como la aplicación, con un proveedor de pagos falso: confirmar un pago por
/// webhook o conciliación dispara la activación de la membresía a través de los handlers reales.
/// </summary>
public sealed class PaymentGatewayHarness : IAsyncDisposable
{
    public TestGymCalendar Calendar { get; } = new(new DateOnly(2026, 10, 12));
    public SmartGymDbContext Db { get; }
    public FakePaymentProvider Provider { get; } = new();
    public IPaymentService Service { get; }
    public PaymentsOptions Options { get; } = new() { CheckoutExpirationMinutes = 60 };
    public Guid StaffUserId { get; } = Guid.NewGuid();

    public PaymentGatewayHarness(bool withProvider = true)
    {
        SmartGymDbContext? db = null;
        var services = new ServiceCollection();

        IMembershipActivationService Activation() =>
            new MembershipActivationService(db!, new CreditLedgerService(db!, Calendar), Calendar);

        services.AddScoped<IDomainEventHandler<PaymentCompletedEvent>>(_ =>
            new ActivateMembershipOnPaymentCompletedHandler(Activation()));
        services.AddScoped<IDomainEventHandler<PaymentAppliedEvent>>(_ =>
            new ActivateMembershipOnPaymentAppliedHandler(Activation()));

        db = new SmartGymDbContext(MembershipTestSupport.InMemoryOptions(), services.BuildServiceProvider());
        Db = db;

        var resolver = new PaymentProviderResolver(withProvider ? new IPaymentProvider[] { Provider } : Array.Empty<IPaymentProvider>());
        Service = new PaymentService(
            db,
            Calendar,
            providerResolver: resolver,
            paymentsOptions: Microsoft.Extensions.Options.Options.Create(Options));
    }

    public async Task<Person> AddStudentAsync(string firstName = "Martín", string email = "martin.gateway@test.com")
    {
        var student = Person.Create(firstName, "Gómez", email);
        Db.People.Add(student);
        await Db.SaveChangesAsync();
        return student;
    }

    public async Task<Membership> AddMembershipAsync(
        Person student,
        decimal price = 60000m,
        MembershipStatus status = MembershipStatus.Pending,
        string currency = "ARS")
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8",
            DurationDays = 30,
            Price = price,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        Db.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: Calendar.Today,
            purchasedAtUtc: Calendar.UtcNow,
            originalPrice: price,
            discountAmount: 0m,
            purchasePrice: price,
            currency: currency,
            extraCreditsGranted: 0);
        membership.Status = status;
        Db.Memberships.Add(membership);
        await Db.SaveChangesAsync();
        return membership;
    }

    public async Task<PaymentDto> CheckoutAsync(Membership membership, string? idempotencyKey = null) =>
        (await Service.CreateCheckoutAsync(
            new CreatePaymentCheckoutRequest(membership.Id), StaffUserId, idempotencyKey)).Value;

    public Task<PaymentDto> RegisterManualAsync(
        Person payer, decimal amount, Guid? membershipId = null, PaymentStatus status = PaymentStatus.Completed) =>
        Service.RegisterPaymentAsync(
            new RegisterPaymentRequest(payer.Id, membershipId, null, amount, PaymentMethod.Cash, null, null, status),
            StaffUserId);

    /// <summary>Simula que el proveedor aprobó el checkout y envía su notificación verificada.</summary>
    public async Task<PaymentDto> ApproveByWebhookAsync(PaymentDto checkout, string externalTransactionId = "MP-1001")
    {
        Provider.SetPayment(externalTransactionId, checkout.Id, checkout.Amount);
        var result = await Service.ProcessWebhookAsync(PaymentProvider.MercadoPago, FakePaymentProvider.Webhook(externalTransactionId));
        Assert.Equal(WebhookProcessingResult.Processed, result);
        return (await Service.GetByIdAsync(checkout.Id))!;
    }

    public Task<WebhookProcessingResult> WebhookAsync(string externalTransactionId, string? eventId = null, bool validSignature = true) =>
        Service.ProcessWebhookAsync(
            PaymentProvider.MercadoPago,
            FakePaymentProvider.Webhook(externalTransactionId, eventId, validSignature));

    public Task<MembershipStatus> MembershipStatusAsync(Guid membershipId) =>
        Db.Memberships.AsNoTracking().Where(m => m.Id == membershipId).Select(m => m.Status).SingleAsync();

    public Task<List<PaymentDiscrepancy>> DiscrepanciesAsync() =>
        Db.PaymentDiscrepancies.AsNoTracking().OrderBy(d => d.DetectedAtUtc).ToListAsync();

    public static async Task<ConflictException> AssertConflictAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(action);
        Assert.Equal(code, ex.Code);
        return ex;
    }

    public static async Task<ExternalServiceException> AssertExternalFailureAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<ExternalServiceException>(action);
        Assert.Equal(code, ex.Code);
        return ex;
    }

    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
