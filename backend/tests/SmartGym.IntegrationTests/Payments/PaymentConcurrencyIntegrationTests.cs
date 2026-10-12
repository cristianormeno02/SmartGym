using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.IntegrationTests.Fixtures;

namespace SmartGym.IntegrationTests.Payments;

/// <summary>
/// Concurrencia real contra PostgreSQL: cada operación corre en su propio scope de DI (su propio DbContext y
/// transacción), como una solicitud HTTP, y compite por los bloqueos de la membresía y del pago.
/// </summary>
[Collection("PostgresIntegration")]
public class PaymentConcurrencyIntegrationTests : IDisposable
{
    private readonly PostgresDatabaseFixture _fixture;
    private readonly ServiceProvider _provider;
    // Sin operador: los cambios de estado de la membresía referencian a Users por FK y aquí no se siembra ninguno.
    private readonly Guid _staffUserId = Guid.Empty;

    public PaymentConcurrencyIntegrationTests(PostgresDatabaseFixture fixture)
    {
        _fixture = fixture;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddApplication();
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
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var student = Person.Create("Alumno", "Pagos", $"pagos.{suffix}@test.com");
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = $"Plan pagos {suffix}"[..30],
            Description = "Concurrencia de pagos",
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
            startDate: today,
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

    private async Task<PaymentDto> RegisterAsync(
        Scenario scenario, decimal amount, PaymentStatus status, Guid? membershipId = null)
    {
        var (result, error) = await RunInScopeAsync(service => service.RegisterPaymentAsync(
            new RegisterPaymentRequest(scenario.StudentId, membershipId, null, amount, PaymentMethod.Cash, null, null, status),
            _staffUserId));
        Assert.Null(error);
        return result!;
    }

    private static void AssertConflict(Exception? error, string expectedCode)
    {
        var conflict = Assert.IsType<ConflictException>(error);
        Assert.Equal(expectedCode, conflict.Code);
    }

    [Fact]
    public async Task ConcurrentRefunds_NeverExceedThePaymentAmount()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var payment = await RegisterAsync(scenario, 20000m, PaymentStatus.Completed);
        var (_, seedError) = await RunInScopeAsync(service =>
            service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(5000m, "Inicial"), _staffUserId, null));
        Assert.Null(seedError);

        // Disponible: 15000. Dos devoluciones de 10000 con claves distintas compiten.
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => RunInScopeAsync(service =>
            service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, $"Concurrente {i}"), _staffUserId, $"refund-{i}"))));

        Assert.Single(outcomes, o => o.Error == null);
        AssertConflict(outcomes.Single(o => o.Error != null).Error, PaymentErrorCodes.RefundAmountExceeded);

        await using var db = _fixture.CreateDbContext();
        var stored = await db.Payments.SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(15000m, stored.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, stored.Status);
        Assert.Equal(15000m, await db.PaymentRefunds.Where(r => r.PaymentId == payment.Id).SumAsync(r => r.Amount));
    }

    [Fact]
    public async Task ManyConcurrentRefunds_GrantExactlyTheAvailableAmount()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var payment = await RegisterAsync(scenario, 30000m, PaymentStatus.Completed);

        // 8 devoluciones de 10000 sobre 30000: sólo caben 3
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => RunInScopeAsync(service =>
            service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, $"Concurrente {i}"), _staffUserId, null))));

        Assert.Equal(3, outcomes.Count(o => o.Error == null));
        foreach (var failure in outcomes.Where(o => o.Error != null))
        {
            var conflict = Assert.IsType<ConflictException>(failure.Error);
            Assert.Contains(conflict.Code, new[] { PaymentErrorCodes.RefundAmountExceeded, PaymentErrorCodes.InvalidTransition });
        }

        await using var db = _fixture.CreateDbContext();
        var stored = await db.Payments.SingleAsync(p => p.Id == payment.Id);
        Assert.Equal(30000m, stored.RefundedAmount);
        Assert.Equal(PaymentStatus.Refunded, stored.Status);
    }

    [Fact]
    public async Task ConcurrentRegistrationsWithTheSameIdempotencyKey_CreateASinglePaymentAndActivateOnce()
    {
        var scenario = await SeedPendingMembershipAsync(20000m);
        var request = new RegisterPaymentRequest(
            scenario.StudentId, scenario.MembershipId, null, 20000m, PaymentMethod.Cash, null, null);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RunInScopeAsync(service =>
            service.RegisterPaymentAsync(request, _staffUserId, "same-key"))));

        Assert.All(outcomes, o => Assert.Null(o.Error));
        Assert.Single(outcomes, o => o.Result!.Created);
        Assert.Single(outcomes.Select(o => o.Result!.Value.Id).Distinct());

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(1, await db.Payments.CountAsync(p => p.MembershipId == scenario.MembershipId));
        var membership = await db.Memberships.SingleAsync(m => m.Id == scenario.MembershipId);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(8, membership.AvailableCredits);
        Assert.Equal(1, await db.MembershipCreditMovements.CountAsync(m => m.MembershipId == scenario.MembershipId));
    }

    [Fact]
    public async Task ConcurrentConfirmationsOfPartialPayments_ActivateTheMembershipOnce()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var first = await RegisterAsync(scenario, 30000m, PaymentStatus.Pending, scenario.MembershipId);
        var second = await RegisterAsync(scenario, 30000m, PaymentStatus.Pending, scenario.MembershipId);

        var outcomes = await Task.WhenAll(new[] { first.Id, second.Id }.Select(id => RunInScopeAsync(service =>
            service.ConfirmPaymentAsync(id, null, _staffUserId))));

        Assert.All(outcomes, o => Assert.Null(o.Error));

        await using var db = _fixture.CreateDbContext();
        Assert.Equal(2, await db.Payments.CountAsync(p => p.MembershipId == scenario.MembershipId && p.Status == PaymentStatus.Completed));
        var membership = await db.Memberships.SingleAsync(m => m.Id == scenario.MembershipId);
        Assert.Equal(MembershipStatus.Active, membership.Status);
        Assert.Equal(8, membership.AvailableCredits);
        Assert.Equal(1, await db.MembershipCreditMovements.CountAsync(m => m.MembershipId == scenario.MembershipId));
    }

    [Fact]
    public async Task ConcurrentConfirmationsThatTogetherOverpay_ConfirmOnlyWhatFits()
    {
        var scenario = await SeedPendingMembershipAsync(60000m);
        var pendings = new List<PaymentDto>();
        for (var i = 0; i < 4; i++)
        {
            pendings.Add(await RegisterAsync(scenario, 30000m, PaymentStatus.Pending, scenario.MembershipId));
        }

        var outcomes = await Task.WhenAll(pendings.Select(p => RunInScopeAsync(service =>
            service.ConfirmPaymentAsync(p.Id, null, _staffUserId))));

        Assert.Equal(2, outcomes.Count(o => o.Error == null));
        foreach (var failure in outcomes.Where(o => o.Error != null))
        {
            AssertConflict(failure.Error, PaymentErrorCodes.ObligationOverpaid);
        }

        await using var db = _fixture.CreateDbContext();
        var netPaid = await db.Payments
            .Where(p => p.MembershipId == scenario.MembershipId && p.Status == PaymentStatus.Completed)
            .SumAsync(p => p.Amount);
        Assert.Equal(60000m, netPaid);
        Assert.Equal(2, await db.Payments.CountAsync(p => p.MembershipId == scenario.MembershipId && p.Status == PaymentStatus.Pending));
    }

    [Fact]
    public async Task ConcurrentConfirmAndCancelOfTheSamePayment_ApplyExactlyOne()
    {
        var scenario = await SeedPendingMembershipAsync(20000m);
        var pending = await RegisterAsync(scenario, 20000m, PaymentStatus.Pending, scenario.MembershipId);

        var confirm = RunInScopeAsync(service => service.ConfirmPaymentAsync(pending.Id, null, _staffUserId));
        var cancel = RunInScopeAsync(service =>
            service.CancelPaymentAsync(pending.Id, new CancelPaymentRequest("Desistió"), _staffUserId));
        var outcomes = await Task.WhenAll(confirm, cancel);

        Assert.Single(outcomes, o => o.Error == null);
        AssertConflict(outcomes.Single(o => o.Error != null).Error, PaymentErrorCodes.InvalidTransition);

        await using var db = _fixture.CreateDbContext();
        var stored = await db.Payments.SingleAsync(p => p.Id == pending.Id);
        var membership = await db.Memberships.SingleAsync(m => m.Id == scenario.MembershipId);
        Assert.Equal(
            stored.Status == PaymentStatus.Completed ? MembershipStatus.Active : MembershipStatus.Pending,
            membership.Status);
        Assert.Contains(stored.Status, new[] { PaymentStatus.Completed, PaymentStatus.Cancelled });
    }

    [Fact]
    public async Task ConfirmationsAndRefundsOnTheSameMembership_DoNotDeadlock()
    {
        var scenario = await SeedPendingMembershipAsync(100000m);
        var completed = new List<PaymentDto>();
        var pendings = new List<PaymentDto>();
        for (var i = 0; i < 4; i++)
        {
            completed.Add(await RegisterAsync(scenario, 10000m, PaymentStatus.Completed, scenario.MembershipId));
            pendings.Add(await RegisterAsync(scenario, 10000m, PaymentStatus.Pending, scenario.MembershipId));
        }

        var refunds = completed.Select(p => RunInScopeAsync(async service =>
        {
            await service.CreateRefundAsync(p.Id, new CreatePaymentRefundRequest(2500m, "Ajuste"), _staffUserId, null);
            return true;
        }));
        var confirms = pendings.Select(p => RunInScopeAsync(async service =>
        {
            await service.ConfirmPaymentAsync(p.Id, null, _staffUserId);
            return true;
        }));

        var outcomes = await Task.WhenAll(refunds.Concat(confirms));

        Assert.All(outcomes, o => Assert.Null(o.Error));

        await using var db = _fixture.CreateDbContext();
        var netPaid = await db.Payments
            .Where(p => p.MembershipId == scenario.MembershipId &&
                        (p.Status == PaymentStatus.Completed || p.Status == PaymentStatus.PartiallyRefunded))
            .SumAsync(p => p.Amount - p.RefundedAmount);
        Assert.Equal(70000m, netPaid);
    }
}
