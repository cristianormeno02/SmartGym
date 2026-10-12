using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.IntegrationTests.Fixtures;

namespace SmartGym.IntegrationTests.Payments;

/// <summary>
/// Concurrencia de la pasarela contra PostgreSQL, con un proveedor en memoria: cada operación corre en su propio
/// scope de DI (su propio DbContext y transacción), como una solicitud HTTP.
/// </summary>
[Collection("PostgresIntegration")]
public class PaymentGatewayConcurrencyIntegrationTests : IDisposable
{
    /// <summary>Proveedor falso seguro para uso concurrente. Una notificación es auténtica si trae la cabecera de prueba.</summary>
    private sealed class InMemoryPaymentProvider : IPaymentProvider
    {
        public const string SignatureHeader = "x-test-signature";

        public PaymentProvider Provider => PaymentProvider.MercadoPago;
        public ConcurrentDictionary<string, ExternalPaymentSnapshot> Payments { get; } = new();
        public ConcurrentBag<(string TransactionId, decimal Amount)> RefundRequests { get; } = new();
        public ConcurrentBag<Guid> Checkouts { get; } = new();

        public Task<PaymentCheckoutResult> CreateCheckoutAsync(PaymentCheckoutContext context, CancellationToken cancellationToken = default)
        {
            Checkouts.Add(context.PaymentId);
            return Task.FromResult(new PaymentCheckoutResult($"pref-{context.PaymentId:N}", $"https://pay.test/{context.PaymentId:N}"));
        }

        public Task<ExternalPaymentSnapshot?> GetPaymentAsync(string externalTransactionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payments.TryGetValue(externalTransactionId, out var snapshot) ? snapshot : null);

        public Task<ExternalPaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(Payments.Values.FirstOrDefault(p => p.PaymentReference == paymentReference));

        public async Task<ExternalRefundResult> RequestRefundAsync(
            string externalTransactionId, decimal amount, string idempotencyKey, CancellationToken cancellationToken = default)
        {
            RefundRequests.Add((externalTransactionId, amount));
            // Da tiempo a que la otra solicitud llegue mientras ésta espera al proveedor
            await Task.Delay(150, cancellationToken);
            return new ExternalRefundResult(true, $"ref-{Guid.NewGuid():N}");
        }

        public ExternalWebhookEvent? VerifyAndParseWebhook(WebhookRequest request) =>
            request.Headers.TryGetValue(SignatureHeader, out var signature) && signature == "valid"
                ? new ExternalWebhookEvent(request.Headers["x-request-id"], request.Query["data.id"], "payment")
                : null;

        public void Approve(string transactionId, PaymentDto payment) =>
            Payments[transactionId] = new ExternalPaymentSnapshot(
                transactionId, ExternalPaymentStatus.Approved, payment.Amount, payment.Currency, 0m, payment.Id.ToString(), "approved");

        public static WebhookRequest Webhook(string transactionId, string eventId) =>
            new(
                "{}",
                new Dictionary<string, string> { [SignatureHeader] = "valid", ["x-request-id"] = eventId },
                new Dictionary<string, string> { ["data.id"] = transactionId });
    }

    private readonly PostgresDatabaseFixture _fixture;
    private readonly ServiceProvider _provider;
    private readonly InMemoryPaymentProvider _paymentProvider = new();

    // Sin operador: los cambios de estado de la membresía referencian a Users por FK y aquí no se siembra ninguno.
    private readonly Guid _staffUserId = Guid.Empty;

    public PaymentGatewayConcurrencyIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddApplication();
        services.AddSingleton<IPaymentProvider>(_paymentProvider);
        services.AddScoped(sp => _fixture.CreateDbContext(sp));
        services.AddScoped<ISmartGymDbContext>(sp => sp.GetRequiredService<SmartGymDbContext>());
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    private sealed record Scenario(Guid StudentId, Guid MembershipId);

    private async Task<Scenario> SeedPendingMembershipAsync(decimal price)
    {
        await using var db = _fixture.CreateDbContext();
        var suffix = Guid.NewGuid().ToString("N");

        var student = Person.Create("Alumno", "Pasarela", $"pasarela.{suffix}@test.com");
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = $"Plan pasarela {suffix}"[..30],
            Description = "Concurrencia de pasarela",
            DurationDays = 30,
            Price = price,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(
            studentId: student.Id,
            plan: plan,
            startDate: DateOnly.FromDateTime(DateTime.UtcNow),
            purchasedAtUtc: DateTime.UtcNow,
            originalPrice: price,
            discountAmount: 0m,
            purchasePrice: price,
            currency: "ARS",
            extraCreditsGranted: 0);
        membership.Status = MembershipStatus.Pending;
        db.Memberships.Add(membership);

        await db.SaveChangesAsync();
        return new Scenario(student.Id, membership.Id);
    }

    /// <summary>Ejecuta la operación en un scope propio y devuelve su resultado o la excepción que lanzó.</summary>
    private async Task<(T? Result, Exception? Error)> RunInScopeAsync<T>(Func<IPaymentService, Task<T>> operation)
    {
        // Task.Run: asegura que las operaciones arranquen en paralelo y no encadenadas por la parte síncrona.
        return await Task.Run(async () =>
        {
            await using var scope = _provider.CreateAsyncScope();
            try
            {
                return (await operation(scope.ServiceProvider.GetRequiredService<IPaymentService>()), (Exception?)null);
            }
            catch (Exception ex)
            {
                return (default(T), ex);
            }
        });
    }

    private async Task<PaymentDto> CheckoutAsync(Scenario scenario)
    {
        var (result, error) = await RunInScopeAsync(service =>
            service.CreateCheckoutAsync(new CreatePaymentCheckoutRequest(scenario.MembershipId), _staffUserId, null));
        Assert.Null(error);
        return result!.Value;
    }

    private Task<(WebhookProcessingResult Result, Exception? Error)> WebhookAsync(string transactionId, string eventId) =>
        RunInScopeAsync(service =>
            service.ProcessWebhookAsync(PaymentProvider.MercadoPago, InMemoryPaymentProvider.Webhook(transactionId, eventId)));

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static void AssertConflict(Exception? error, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictException>(error);
        Assert.Equal(expectedCode, conflict.Code);
    }

    [Fact]
    public async Task ConcurrentCheckouts_LeaveASingleOpenCheckoutPerMembership()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RunInScopeAsync(service =>
            service.CreateCheckoutAsync(new CreatePaymentCheckoutRequest(scenario.MembershipId), _staffUserId, null))));

        Assert.Single(outcomes, o => o.Error == null);
        foreach (var failure in outcomes.Where(o => o.Error != null))
        {
            AssertConflict(failure.Error, PaymentErrorCodes.CheckoutAlreadyOpen);
        }

        await using var db = _fixture.CreateDbContext();
        var open = await db.Payments.SingleAsync(p => p.MembershipId == scenario.MembershipId && p.Status == PaymentStatus.Pending);
        Assert.Equal(PaymentProvider.MercadoPago, open.Provider);
        Assert.Equal(60000m, open.Amount);
        Assert.False(string.IsNullOrWhiteSpace(open.CheckoutUrl));
    }

    [Fact]
    public async Task SimultaneousCopiesOfTheSameWebhook_ConfirmAndActivateOnce()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var checkout = await CheckoutAsync(scenario);
        var transactionId = NewId("MP");
        var eventId = NewId("evt");
        _paymentProvider.Approve(transactionId, checkout);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => WebhookAsync(transactionId, eventId)));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Single(outcomes, o => o.Result == WebhookProcessingResult.Processed);
        Assert.Equal(5, outcomes.Count(o => o.Result == WebhookProcessingResult.AlreadyProcessed));

        await using var db = _fixture.CreateDbContext();
        var payment = await db.Payments.SingleAsync(p => p.Id == checkout.Id);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(transactionId, payment.ExternalTransactionId);
        Assert.Equal(1, await db.PaymentWebhookEvents.CountAsync(e => e.ExternalEventId == eventId));

        var membership = await db.Memberships.SingleAsync(m => m.Id == scenario.MembershipId);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(1, await db.MembershipCreditMovements.CountAsync(m => m.MembershipId == scenario.MembershipId));
        Assert.Equal(0, await db.PaymentDiscrepancies.CountAsync(d => d.PaymentId == checkout.Id));
    }

    [Fact]
    public async Task DifferentNotificationsOfTheSameApprovedTransaction_ConfirmOnce()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var checkout = await CheckoutAsync(scenario);
        var transactionId = NewId("MP");
        _paymentProvider.Approve(transactionId, checkout);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => WebhookAsync(transactionId, NewId("evt"))));

        Assert.All(outcomes, o => Assert.Null(o.Error));

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(PaymentStatus.Completed, (await db.Payments.SingleAsync(p => p.Id == checkout.Id)).Status);
        Assert.Equal(1, await db.MembershipCreditMovements.CountAsync(m => m.MembershipId == scenario.MembershipId));
        Assert.Equal(0, await db.PaymentDiscrepancies.CountAsync(d => d.PaymentId == checkout.Id));
    }

    [Fact]
    public async Task ApprovedWebhookRacingACashPayment_NeverLosesTheCollectedMoney()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var checkout = await CheckoutAsync(scenario);
        var transactionId = NewId("MP");
        _paymentProvider.Approve(transactionId, checkout);

        var webhook = RunInScopeAsync(async service =>
        {
            await service.ProcessWebhookAsync(PaymentProvider.MercadoPago, InMemoryPaymentProvider.Webhook(transactionId, NewId("evt")));
            return true;
        });
        var cash = RunInScopeAsync(async service =>
        {
            await service.RegisterPaymentAsync(
                new RegisterPaymentRequest(scenario.StudentId, scenario.MembershipId, null, 60000m, PaymentMethod.Cash, null, null),
                _staffUserId);
            return true;
        });
        var outcomes = await Task.WhenAll(webhook, cash);

        // El cobro del proveedor siempre se confirma: ya ocurrió
        Assert.Null(outcomes[0].Error);

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(PaymentStatus.Completed, (await db.Payments.SingleAsync(p => p.Id == checkout.Id)).Status);
        var membership = await db.Memberships.SingleAsync(m => m.Id == scenario.MembershipId);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(1, await db.MembershipCreditMovements.CountAsync(m => m.MembershipId == scenario.MembershipId));

        var overpayments = await db.PaymentDiscrepancies.CountAsync(d => d.PaymentId == checkout.Id && d.Type == PaymentDiscrepancyType.Overpayment);
        var completed = await db.Payments.CountAsync(p => p.MembershipId == scenario.MembershipId && p.Status == PaymentStatus.Completed);

        if (outcomes[1].Error == null)
        {
            // El efectivo entró primero: la membresía quedó pagada dos veces y eso queda a la vista
            Assert.Equal(2, completed);
            Assert.Equal(1, overpayments);
        }
        else
        {
            // El webhook entró primero: el cobro en mostrador se rechaza porque ya no hay saldo
            AssertConflict(outcomes[1].Error, PaymentErrorCodes.ObligationOverpaid);
            Assert.Equal(1, completed);
            Assert.Equal(0, overpayments);
        }
    }

    [Fact]
    public async Task ApprovedWebhookRacingCancellation_EndsCompletedOrCancelledWithDiscrepancy()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var checkout = await CheckoutAsync(scenario);
        var transactionId = NewId("MP");
        _paymentProvider.Approve(transactionId, checkout);

        var webhook = RunInScopeAsync(async service =>
        {
            await service.ProcessWebhookAsync(PaymentProvider.MercadoPago, InMemoryPaymentProvider.Webhook(transactionId, NewId("evt")));
            return true;
        });
        var cancel = RunInScopeAsync(async service =>
        {
            await service.CancelPaymentAsync(checkout.Id, new CancelPaymentRequest("Desistió"), _staffUserId);
            return true;
        });
        var outcomes = await Task.WhenAll(webhook, cancel);

        Assert.Null(outcomes[0].Error);

        await using var db = _fixture.CreateDbContext();
        var payment = await db.Payments.SingleAsync(p => p.Id == checkout.Id);
        var membership = await db.Memberships.SingleAsync(m => m.Id == scenario.MembershipId);
        var lateApprovals = await db.PaymentDiscrepancies.CountAsync(
            d => d.PaymentId == checkout.Id && d.Type == PaymentDiscrepancyType.ApprovedAfterTerminalState);

        if (payment.Status == PaymentStatus.Completed)
        {
            // El webhook ganó: la cancelación llegó tarde
            AssertConflict(outcomes[1].Error, PaymentErrorCodes.InvalidTransition);
            Assert.Equal(MembershipStatus.Active, membership.Status);
            Assert.Equal(0, lateApprovals);
        }
        else
        {
            // La cancelación ganó: el cobro aprobado queda registrado para revisión
            Assert.Null(outcomes[1].Error);
            Assert.Equal(PaymentStatus.Cancelled, payment.Status);
            Assert.Equal(MembershipStatus.Pending, membership.Status);
            Assert.Equal(1, lateApprovals);
        }
    }

    [Fact]
    public async Task ConcurrentExternalRefunds_CallTheProviderOnlyForWhatFits()
    {
        var scenario = await SeedPendingMembershipAsync(20000m);
        var checkout = await CheckoutAsync(scenario);
        var transactionId = NewId("MP");
        _paymentProvider.Approve(transactionId, checkout);
        Assert.Null((await WebhookAsync(transactionId, NewId("evt"))).Error);

        // Dos devoluciones de 15000 sobre 20000: la segunda no puede llegar al proveedor
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => RunInScopeAsync(service =>
            service.CreateRefundAsync(checkout.Id, new CreatePaymentRefundRequest(15000m, $"Concurrente {i}"), _staffUserId, null))));

        Assert.Single(outcomes, o => o.Error == null);
        AssertConflict(outcomes.Single(o => o.Error != null).Error, PaymentErrorCodes.RefundAmountExceeded);
        Assert.Single(_paymentProvider.RefundRequests, r => r.TransactionId == transactionId);

        await using var db = _fixture.CreateDbContext();
        var payment = await db.Payments.SingleAsync(p => p.Id == checkout.Id);
        Assert.Equal(15000m, payment.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        var refund = await db.PaymentRefunds.SingleAsync(r => r.PaymentId == checkout.Id);
        Assert.Equal(PaymentRefundStatus.Completed, refund.Status);
        Assert.NotNull(refund.ExternalRefundId);
    }

    [Fact]
    public async Task ExpirationRacingAnApprovedWebhook_EndsInOneConsistentState()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var checkout = await CheckoutAsync(scenario);
        var transactionId = NewId("MP");
        _paymentProvider.Approve(transactionId, checkout);

        // Se fuerza el vencimiento del checkout
        await using (var db = _fixture.CreateDbContext())
        {
            await db.Payments.Where(p => p.Id == checkout.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }

        var webhook = WebhookAsync(transactionId, NewId("evt"));
        var expiration = RunInScopeAsync(service => service.ExpireCheckoutsAsync());
        await Task.WhenAll(webhook, expiration);

        Assert.Null(webhook.Result.Error);
        Assert.Null(expiration.Result.Error);

        await using var verify = _fixture.CreateDbContext();
        var payment = await verify.Payments.SingleAsync(p => p.Id == checkout.Id);
        var membership = await verify.Memberships.SingleAsync(m => m.Id == scenario.MembershipId);
        var lateApprovals = await verify.PaymentDiscrepancies.CountAsync(
            d => d.PaymentId == checkout.Id && d.Type == PaymentDiscrepancyType.ApprovedAfterTerminalState);

        if (payment.Status == PaymentStatus.Completed)
        {
            Assert.Equal(MembershipStatus.Active, membership.Status);
            Assert.Equal(0, lateApprovals);
        }
        else
        {
            Assert.Equal(PaymentStatus.Cancelled, payment.Status);
            Assert.Equal(MembershipStatus.Pending, membership.Status);
            Assert.Equal(1, lateApprovals);
        }
    }
}
