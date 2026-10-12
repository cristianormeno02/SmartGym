using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;
using SmartGym.Infrastructure.Persistence;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentLifecycleTests
{
    private sealed class CapturingPaymentCompletedHandler : IDomainEventHandler<PaymentCompletedEvent>
    {
        public List<PaymentCompletedEvent> Events { get; } = new();

        public Task HandleAsync(PaymentCompletedEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class StubServiceProvider : IServiceProvider
    {
        private readonly CapturingPaymentCompletedHandler _handler;

        public StubServiceProvider(CapturingPaymentCompletedHandler handler)
        {
            _handler = handler;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IEnumerable<IDomainEventHandler<PaymentCompletedEvent>>))
            {
                return new IDomainEventHandler<PaymentCompletedEvent>[] { _handler };
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

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public Guid? PersonId { get; set; }
        public string? Email { get; set; } = "staff@smartgym.com";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles { get; set; } = new[] { "Administrator" };
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private static (SmartGymDbContext Db, CapturingPaymentCompletedHandler Handler) CreateDbContext()
    {
        var handler = new CapturingPaymentCompletedHandler();
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new SmartGymDbContext(options, new StubServiceProvider(handler));
        return (db, handler);
    }

    [Fact]
    public async Task RegisterPayment_DefaultStatus_ShouldBeCompleted_AndEmitPaymentCompletedEventOnce()
    {
        var (db, handler) = CreateDbContext();
        await using var _ = db;
        IPaymentService service = new PaymentService(db);

        var payer = Person.Create("Martín", "Gómez", "31222333");
        db.People.Add(payer);
        await db.SaveChangesAsync();

        var staffUserId = Guid.NewGuid();
        var dto = await service.RegisterPaymentAsync(
            new RegisterPaymentRequest(
                payer.Id,
                MembershipId: null,
                ClassSessionId: null,
                Amount: 25000m,
                Method: PaymentMethod.Cash,
                ExternalTransactionId: "REC-001",
                Notes: "Pago en mostrador"),
            staffUserId);

        Assert.Equal(PaymentStatus.Completed, dto.Status);
        var evt = Assert.Single(handler.Events);
        Assert.Equal(dto.Id, evt.PaymentId);
        Assert.Equal(25000m, evt.Amount);
    }

    [Fact]
    public async Task RegisterPayment_WithPendingStatus_ShouldNotEmitEventUntilConfirmed_AndEmitOnlyOnce()
    {
        var (db, handler) = CreateDbContext();
        await using var _ = db;
        IPaymentService service = new PaymentService(db);

        var payer = Person.Create("Lucía", "Fernández", "32444555");
        db.People.Add(payer);
        await db.SaveChangesAsync();

        var staffUserId = Guid.NewGuid();
        var pendingDto = await service.RegisterPaymentAsync(
            new RegisterPaymentRequest(
                payer.Id,
                MembershipId: Guid.NewGuid(),
                ClassSessionId: null,
                Amount: 30000m,
                Method: PaymentMethod.BankTransfer,
                ExternalTransactionId: null,
                Notes: "Transferencia pendiente de acreditación",
                Status: PaymentStatus.Pending),
            staffUserId);

        Assert.Equal(PaymentStatus.Pending, pendingDto.Status);
        Assert.Empty(handler.Events);

        var confirmedDto = await service.ConfirmPaymentAsync(
            pendingDto.Id,
            new ConfirmPaymentRequest("TRF-998877"),
            staffUserId);

        Assert.Equal(PaymentStatus.Completed, confirmedDto.Status);
        Assert.Equal("TRF-998877", confirmedDto.ExternalTransactionId);
        Assert.Single(handler.Events);

        // Trying to confirm an already Completed payment throws ConflictException (409) and does not emit again
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ConfirmPaymentAsync(pendingDto.Id, new ConfirmPaymentRequest("TRF-998877"), staffUserId));
        Assert.Single(handler.Events);
    }

    [Fact]
    public async Task FailPayment_FromPending_ShouldTransitionToFailed_AndRejectSubsequentConfirmOrFailWithConflict()
    {
        var (db, handler) = CreateDbContext();
        await using var _ = db;
        IPaymentService service = new PaymentService(db);

        var payer = Person.Create("Diego", "Alonso", "33555666");
        db.People.Add(payer);
        await db.SaveChangesAsync();

        var staffUserId = Guid.NewGuid();
        var pendingDto = await service.RegisterPaymentAsync(
            new RegisterPaymentRequest(
                payer.Id,
                MembershipId: Guid.NewGuid(),
                ClassSessionId: null,
                Amount: 18000m,
                Method: PaymentMethod.MercadoPago,
                ExternalTransactionId: "MP-111",
                Notes: null,
                Status: PaymentStatus.Pending),
            staffUserId);

        var failedDto = await service.FailPaymentAsync(
            pendingDto.Id,
            new FailPaymentRequest("Comprobante inválido"),
            staffUserId);

        Assert.Equal(PaymentStatus.Failed, failedDto.Status);
        Assert.Contains("Comprobante inválido", failedDto.Notes);
        Assert.Empty(handler.Events);

        // Cannot fail again or confirm a Failed payment (409 Conflict)
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.FailPaymentAsync(pendingDto.Id, new FailPaymentRequest("Otro motivo"), staffUserId));
        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ConfirmPaymentAsync(pendingDto.Id, new ConfirmPaymentRequest("MP-222"), staffUserId));
    }

    [Fact]
    public async Task PaymentsController_ConfirmAndFailEndpoints_ShouldReturn200Or409Conflict()
    {
        var (db, _) = CreateDbContext();
        await using var _ = db;
        IPaymentService service = new PaymentService(db);
        var currentUser = new StubCurrentUserService();
        var controller = new PaymentsController(service, currentUser);

        var payer = Person.Create("Valeria", "Paz", "34666777");
        db.People.Add(payer);
        await db.SaveChangesAsync();

        var pending = await service.RegisterPaymentAsync(
            new RegisterPaymentRequest(
                payer.Id,
                MembershipId: null,
                ClassSessionId: null,
                Amount: 12000m,
                Method: PaymentMethod.BankTransfer,
                ExternalTransactionId: null,
                Notes: null,
                Status: PaymentStatus.Pending),
            currentUser.UserId!.Value);

        var confirmResult = await controller.ConfirmPayment(
            pending.Id,
            new ConfirmPaymentRequest("BANK-123"),
            CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(confirmResult);
        var confirmedDto = Assert.IsType<PaymentDto>(okResult.Value);
        Assert.Equal(PaymentStatus.Completed, confirmedDto.Status);

        // Second confirm -> 409 Conflict
        var secondConfirm = await controller.ConfirmPayment(
            pending.Id,
            new ConfirmPaymentRequest("BANK-123"),
            CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(secondConfirm);

        // Fail on already completed -> 409 Conflict
        var failAfterConfirm = await controller.FailPayment(
            pending.Id,
            new FailPaymentRequest("Motivo tardío"),
            CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(failAfterConfirm);
    }
}
