using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Handlers;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Operations;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using SmartGym.WebApi.Controllers;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentLifecycleTests
{
    private sealed class CapturingHandler<TEvent> : IDomainEventHandler<TEvent>
        where TEvent : SmartGym.Domain.Common.IDomainEvent
    {
        public List<TEvent> Events { get; } = new();

        public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingNotificationHandler : IDomainEventHandler<PaymentConfirmedNotificationEvent>
    {
        public int Calls { get; private set; }

        public Task HandleAsync(PaymentConfirmedNotificationEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Notifications no disponible");
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

    /// <summary>
    /// Contexto en memoria cableado como la aplicación: confirmar o aplicar un pago dispara la activación
    /// de la membresía a través de los handlers reales.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        public TestGymCalendar Calendar { get; } = new(new DateOnly(2026, 10, 12));
        public SmartGymDbContext Db { get; }
        public IPaymentService Service { get; }
        public CapturingHandler<PaymentCompletedEvent> Completed { get; } = new();
        public CapturingHandler<PaymentAppliedEvent> Applied { get; } = new();
        public ThrowingNotificationHandler? FailingNotifications { get; }
        public Guid StaffUserId { get; } = Guid.NewGuid();

        public Harness(bool failingNotifications = false)
        {
            SmartGymDbContext? db = null;
            var services = new ServiceCollection();

            IMembershipActivationService Activation() =>
                new MembershipActivationService(db!, new CreditLedgerService(db!, Calendar), Calendar);

            services.AddSingleton<IDomainEventHandler<PaymentCompletedEvent>>(Completed);
            services.AddSingleton<IDomainEventHandler<PaymentAppliedEvent>>(Applied);
            services.AddScoped<IDomainEventHandler<PaymentCompletedEvent>>(_ =>
                new ActivateMembershipOnPaymentCompletedHandler(Activation()));
            services.AddScoped<IDomainEventHandler<PaymentAppliedEvent>>(_ =>
                new ActivateMembershipOnPaymentAppliedHandler(Activation()));

            if (failingNotifications)
            {
                FailingNotifications = new ThrowingNotificationHandler();
                services.AddSingleton<IDomainEventHandler<PaymentConfirmedNotificationEvent>>(FailingNotifications);
            }

            db = new SmartGymDbContext(MembershipTestSupport.InMemoryOptions(), services.BuildServiceProvider());
            Db = db;
            Service = new PaymentService(db, Calendar);
        }

        public async Task<Person> AddStudentAsync(string firstName = "Martín", string document = "31222333")
        {
            var student = Person.Create(firstName, "Gómez", document);
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

        public async Task<ClassSession> AddClassSessionAsync(bool cancelled = false)
        {
            var starts = Calendar.UtcNow.AddDays(2);
            var session = ClassSession.CreateFromSchedule(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), starts, starts.AddHours(1), 20, Guid.NewGuid(), null);
            if (cancelled)
            {
                session.Cancel(ClassSessionCancellationReason.AdministrativeDecision, null, null, Calendar.UtcNow);
                session.ClearDomainEvents();
            }

            Db.ClassSessions.Add(session);
            await Db.SaveChangesAsync();
            return session;
        }

        public Task<PaymentDto> RegisterAsync(
            Person payer,
            decimal amount,
            Guid? membershipId = null,
            Guid? classSessionId = null,
            PaymentStatus status = PaymentStatus.Completed,
            PaymentMethod method = PaymentMethod.Cash,
            string? currency = null) =>
            Service.RegisterPaymentAsync(
                new RegisterPaymentRequest(payer.Id, membershipId, classSessionId, amount, method, null, null, status, currency),
                StaffUserId);

        public Task<MembershipStatus> MembershipStatusAsync(Guid membershipId) =>
            Db.Memberships.AsNoTracking().Where(m => m.Id == membershipId).Select(m => m.Status).SingleAsync();

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<ConflictException> AssertConflictAsync(string code, Func<Task> action)
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(action);
        Assert.Equal(code, ex.Code);
        return ex;
    }

    // ---- Registro, confirmación, rechazo y cancelación ----

    [Fact]
    public async Task RegisterPayment_DefaultStatus_ShouldBeCompleted_AndEmitPaymentCompletedEventOnce()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();

        var dto = await h.Service.RegisterPaymentAsync(
            new RegisterPaymentRequest(payer.Id, null, null, 25000m, PaymentMethod.DebitCard, "REC-001", "Pago en mostrador"),
            h.StaffUserId);

        Assert.Equal(PaymentStatus.Completed, dto.Status);
        Assert.Equal(PaymentProvider.Manual, dto.Provider);
        Assert.Equal("ARS", dto.Currency);
        Assert.Equal(0m, dto.RefundedAmount);
        Assert.Equal(25000m, dto.NetAmount);
        Assert.Equal(h.Calendar.UtcNow, dto.ConfirmedAtUtc);

        var evt = Assert.Single(h.Completed.Events);
        Assert.Equal(dto.Id, evt.PaymentId);
        Assert.Equal(25000m, evt.Amount);
    }

    [Fact]
    public async Task RegisterPayment_WithInactivePayerOrInvalidRequest_ShouldThrowValidationException()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();

        await Assert.ThrowsAsync<ValidationException>(() => h.RegisterAsync(payer, 0m));
        await Assert.ThrowsAsync<ValidationException>(() => h.Service.RegisterPaymentAsync(
            new RegisterPaymentRequest(Guid.NewGuid(), null, null, 1000m, PaymentMethod.Cash, null, null),
            h.StaffUserId));

        Assert.Empty(await h.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task ConfirmPayment_FromPending_ShouldEmitEventOnce_AndRejectSecondConfirm()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();

        var pending = await h.RegisterAsync(payer, 30000m, status: PaymentStatus.Pending, method: PaymentMethod.BankTransfer);
        Assert.Equal(PaymentStatus.Pending, pending.Status);
        Assert.Null(pending.ConfirmedAtUtc);
        Assert.Empty(h.Completed.Events);

        var confirmed = await h.Service.ConfirmPaymentAsync(pending.Id, new ConfirmPaymentRequest("TRF-998877"), h.StaffUserId);

        Assert.Equal(PaymentStatus.Completed, confirmed.Status);
        Assert.Equal("TRF-998877", confirmed.ExternalTransactionId);
        Assert.Single(h.Completed.Events);

        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.ConfirmPaymentAsync(pending.Id, new ConfirmPaymentRequest("TRF-998877"), h.StaffUserId));
        Assert.Single(h.Completed.Events);
    }

    [Fact]
    public async Task FailPayment_FromPending_ShouldTransitionToFailed_AndRejectFurtherTransitions()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var pending = await h.RegisterAsync(payer, 18000m, status: PaymentStatus.Pending, method: PaymentMethod.DigitalWallet);

        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Service.FailPaymentAsync(pending.Id, new FailPaymentRequest(" "), h.StaffUserId));

        var failed = await h.Service.FailPaymentAsync(pending.Id, new FailPaymentRequest("Comprobante inválido"), h.StaffUserId);

        Assert.Equal(PaymentStatus.Failed, failed.Status);
        Assert.Contains("Comprobante inválido", failed.Notes);
        Assert.Empty(h.Completed.Events);

        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.FailPaymentAsync(pending.Id, new FailPaymentRequest("Otro motivo"), h.StaffUserId));
        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.ConfirmPaymentAsync(pending.Id, null, h.StaffUserId));
        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.CancelPaymentAsync(pending.Id, new CancelPaymentRequest("Tarde"), h.StaffUserId));
    }

    [Fact]
    public async Task CancelPayment_FromPending_ShouldRegisterTimestampAndReason_WithoutActivatingMembership()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 20000m);
        var pending = await h.RegisterAsync(payer, 20000m, membership.Id, status: PaymentStatus.Pending);

        var cancelled = await h.Service.CancelPaymentAsync(pending.Id, new CancelPaymentRequest("El alumno desistió"), h.StaffUserId);

        Assert.Equal(PaymentStatus.Cancelled, cancelled.Status);
        Assert.Equal(h.Calendar.UtcNow, cancelled.CancelledAtUtc);
        Assert.Contains("El alumno desistió", cancelled.Notes);
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));
    }

    [Fact]
    public async Task FailOrCancel_OnConfirmedPayment_ShouldBeRejected_KeepingStateAndAmounts()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var completed = await h.RegisterAsync(payer, 30000m);

        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.FailPaymentAsync(completed.Id, new FailPaymentRequest("Motivo tardío"), h.StaffUserId));
        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.CancelPaymentAsync(completed.Id, new CancelPaymentRequest("Motivo tardío"), h.StaffUserId));

        await h.Service.CreateRefundAsync(completed.Id, new CreatePaymentRefundRequest(1000m, "Parcial"), h.StaffUserId, null);
        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.CancelPaymentAsync(completed.Id, new CancelPaymentRequest("Motivo tardío"), h.StaffUserId));

        var current = await h.Service.GetByIdAsync(completed.Id);
        Assert.Equal(PaymentStatus.PartiallyRefunded, current!.Status);
        Assert.Equal(30000m, current.Amount);
    }

    [Fact]
    public async Task Operations_OnUnknownPayment_ShouldThrowNotFound()
    {
        await using var h = new Harness();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.ConfirmPaymentAsync(Guid.NewGuid(), null, h.StaffUserId));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            h.Service.CreateRefundAsync(Guid.NewGuid(), new CreatePaymentRefundRequest(1m, "x"), h.StaffUserId, null));
    }

    // ---- Validación de la obligación ----

    [Fact]
    public async Task RegisterPayment_ForMembership_ShouldValidateExistenceStateOwnerCurrencyAndBalance()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var other = await h.AddStudentAsync("Lucía", "32444555");

        var notFound = await Assert.ThrowsAsync<NotFoundException>(() => h.RegisterAsync(payer, 1000m, Guid.NewGuid()));
        Assert.Contains("membresía", notFound.Message);

        var cancelled = await h.AddMembershipAsync(payer, 20000m, MembershipStatus.Cancelled);
        await AssertConflictAsync(PaymentErrorCodes.ObligationCancelled, () => h.RegisterAsync(payer, 1000m, cancelled.Id));

        var foreign = await h.AddMembershipAsync(other, 20000m);
        await AssertConflictAsync(PaymentErrorCodes.ObligationStudentMismatch, () => h.RegisterAsync(payer, 1000m, foreign.Id));

        var usd = await h.AddMembershipAsync(payer, 100m, currency: "USD");
        await AssertConflictAsync(PaymentErrorCodes.CurrencyMismatch, () => h.RegisterAsync(payer, 50m, usd.Id, currency: "ARS"));

        var membership = await h.AddMembershipAsync(payer, 30000m);
        await h.RegisterAsync(payer, 20000m, membership.Id);
        await AssertConflictAsync(PaymentErrorCodes.ObligationOverpaid, () => h.RegisterAsync(payer, 20000m, membership.Id));
        await AssertConflictAsync(PaymentErrorCodes.ObligationOverpaid, () =>
            h.RegisterAsync(payer, 10000.01m, membership.Id, status: PaymentStatus.Pending));

        Assert.Single(await h.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task RegisterPayment_ForMembership_ShouldTakeMembershipCurrencyByDefault()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var usd = await h.AddMembershipAsync(payer, 100m, currency: "USD");

        var dto = await h.RegisterAsync(payer, 100m, usd.Id);

        Assert.Equal("USD", dto.Currency);
    }

    [Fact]
    public async Task RegisterPayment_ForExpiredMembershipWithBalance_ShouldBeAccepted()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var expired = await h.AddMembershipAsync(payer, 20000m, MembershipStatus.Expired);

        var dto = await h.RegisterAsync(payer, 20000m, expired.Id);

        Assert.Equal(PaymentStatus.Completed, dto.Status);
        Assert.Equal(MembershipStatus.Expired, await h.MembershipStatusAsync(expired.Id));
    }

    [Fact]
    public async Task RegisterPayment_ForClassSession_ShouldValidateExistenceAndState_WithoutBalance()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => h.RegisterAsync(payer, 5000m, classSessionId: Guid.NewGuid()));

        var cancelledSession = await h.AddClassSessionAsync(cancelled: true);
        await AssertConflictAsync(PaymentErrorCodes.ObligationCancelled, () =>
            h.RegisterAsync(payer, 5000m, classSessionId: cancelledSession.Id));

        var session = await h.AddClassSessionAsync();
        var dto = await h.RegisterAsync(payer, 5000m, classSessionId: session.Id);

        Assert.Equal(session.Id, dto.ClassSessionId);
        Assert.Null(dto.MembershipId);
    }

    // ---- Pagos parciales, saldo y activación ----

    [Fact]
    public async Task TwoPartialPayments_ShouldCoverMembership_AndActivateItOnce()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 60000m);

        await h.RegisterAsync(payer, 30000m, membership.Id);

        var afterFirst = await h.Service.GetMembershipBalanceAsync(membership.Id);
        Assert.Equal(60000m, afterFirst.PurchasePrice);
        Assert.Equal("ARS", afterFirst.Currency);
        Assert.Equal(payer.Id, afterFirst.StudentId);
        Assert.Equal(30000m, afterFirst.TotalPaidAmount);
        Assert.Equal(30000m, afterFirst.PendingBalance);
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));

        await h.RegisterAsync(payer, 30000m, membership.Id, method: PaymentMethod.BankTransfer);

        var afterSecond = await h.Service.GetMembershipBalanceAsync(membership.Id);
        Assert.Equal(60000m, afterSecond.TotalPaidAmount);
        Assert.Equal(0m, afterSecond.PendingBalance);
        Assert.Equal(2, afterSecond.Payments.Count);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));

        var initialLoads = await h.Db.MembershipCreditMovements.CountAsync(m => m.MembershipId == membership.Id);
        Assert.Equal(1, initialLoads);
    }

    [Fact]
    public async Task MembershipBalance_ShouldReportPendingPaymentsSeparately_AndNotFoundForUnknownMembership()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 60000m);

        await h.RegisterAsync(payer, 10000m, membership.Id);
        await h.RegisterAsync(payer, 25000m, membership.Id, status: PaymentStatus.Pending);

        var balance = await h.Service.GetMembershipBalanceAsync(membership.Id);

        Assert.Equal(10000m, balance.TotalPaidAmount);
        Assert.Equal(25000m, balance.PendingPaymentsAmount);
        Assert.Equal(50000m, balance.PendingBalance);

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetMembershipBalanceAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ConfirmPayment_ThatNoLongerFitsTheBalance_ShouldBeRejected_AndStayPending()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 60000m);

        var pending = await h.RegisterAsync(payer, 60000m, membership.Id, status: PaymentStatus.Pending);
        await h.RegisterAsync(payer, 60000m, membership.Id);

        await AssertConflictAsync(PaymentErrorCodes.ObligationOverpaid, () =>
            h.Service.ConfirmPaymentAsync(pending.Id, null, h.StaffUserId));

        var current = await h.Service.GetByIdAsync(pending.Id);
        Assert.Equal(PaymentStatus.Pending, current!.Status);
    }

    [Fact]
    public async Task ConfirmPayment_ThatCompletesMembership_ShouldActivateIt()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 20000m);
        var pending = await h.RegisterAsync(payer, 20000m, membership.Id, status: PaymentStatus.Pending);
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));

        await h.Service.ConfirmPaymentAsync(pending.Id, null, h.StaffUserId);

        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));
    }

    // ---- Aplicación diferida ----

    [Fact]
    public async Task ApplyPayment_AlreadyCompleted_ShouldLinkAndActivateMembership()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 25000m);
        var payment = await h.RegisterAsync(payer, 25000m);

        var applied = await h.Service.ApplyPaymentAsync(payment.Id, new ApplyPaymentRequest(membership.Id, null), h.StaffUserId);

        Assert.Equal(membership.Id, applied.MembershipId);
        Assert.Single(h.Applied.Events);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));
        Assert.Equal(1, await h.Db.MembershipCreditMovements.CountAsync(m => m.MembershipId == membership.Id));
    }

    [Fact]
    public async Task ApplyPayment_Pending_ShouldLinkWithoutActivating_UntilConfirmed()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 25000m);
        var payment = await h.RegisterAsync(payer, 25000m, status: PaymentStatus.Pending);

        await h.Service.ApplyPaymentAsync(payment.Id, new ApplyPaymentRequest(membership.Id, null), h.StaffUserId);

        Assert.Empty(h.Applied.Events);
        Assert.Equal(MembershipStatus.Pending, await h.MembershipStatusAsync(membership.Id));

        await h.Service.ConfirmPaymentAsync(payment.Id, null, h.StaffUserId);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));
    }

    [Fact]
    public async Task ApplyPayment_ShouldRejectReassignmentForeignMembershipOverpayAndInvalidRequest()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var other = await h.AddStudentAsync("Lucía", "32444555");
        var membership = await h.AddMembershipAsync(payer, 25000m);
        var foreign = await h.AddMembershipAsync(other, 25000m);
        var session = await h.AddClassSessionAsync();

        var payment = await h.RegisterAsync(payer, 30000m);

        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Service.ApplyPaymentAsync(payment.Id, new ApplyPaymentRequest(null, null), h.StaffUserId));
        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Service.ApplyPaymentAsync(payment.Id, new ApplyPaymentRequest(membership.Id, session.Id), h.StaffUserId));
        await AssertConflictAsync(PaymentErrorCodes.ObligationStudentMismatch, () =>
            h.Service.ApplyPaymentAsync(payment.Id, new ApplyPaymentRequest(foreign.Id, null), h.StaffUserId));
        await AssertConflictAsync(PaymentErrorCodes.ObligationOverpaid, () =>
            h.Service.ApplyPaymentAsync(payment.Id, new ApplyPaymentRequest(membership.Id, null), h.StaffUserId));

        var applied = await h.Service.ApplyPaymentAsync(payment.Id, new ApplyPaymentRequest(null, session.Id), h.StaffUserId);
        Assert.Equal(session.Id, applied.ClassSessionId);

        await AssertConflictAsync(PaymentErrorCodes.AlreadyApplied, () =>
            h.Service.ApplyPaymentAsync(payment.Id, new ApplyPaymentRequest(null, session.Id), h.StaffUserId));
    }

    // ---- Idempotencia ----

    [Fact]
    public async Task RegisterPayment_WithSameIdempotencyKeyAndData_ShouldReturnExistingPayment()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 20000m);
        var request = new RegisterPaymentRequest(payer.Id, membership.Id, null, 20000m, PaymentMethod.Cash, null, null);

        var first = await h.Service.RegisterPaymentAsync(request, h.StaffUserId, "key-001");
        var second = await h.Service.RegisterPaymentAsync(request, h.StaffUserId, " key-001 ");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Single(await h.Db.Payments.ToListAsync());
        Assert.Single(h.Completed.Events);
    }

    [Fact]
    public async Task RegisterPayment_WithSameIdempotencyKeyAndDifferentData_ShouldRejectWithMismatch()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var other = await h.AddStudentAsync("Lucía", "32444555");
        var request = new RegisterPaymentRequest(payer.Id, null, null, 20000m, PaymentMethod.Cash, null, null);
        await h.Service.RegisterPaymentAsync(request, h.StaffUserId, "key-001");

        await AssertConflictAsync(PaymentErrorCodes.IdempotencyKeyMismatch, () =>
            h.Service.RegisterPaymentAsync(request with { Amount = 20001m }, h.StaffUserId, "key-001"));
        await AssertConflictAsync(PaymentErrorCodes.IdempotencyKeyMismatch, () =>
            h.Service.RegisterPaymentAsync(request with { Method = PaymentMethod.BankTransfer }, h.StaffUserId, "key-001"));

        // La clave es por pagador: otro pagador puede usar la misma
        var otherResult = await h.Service.RegisterPaymentAsync(request with { PayerPersonId = other.Id }, h.StaffUserId, "key-001");
        Assert.True(otherResult.Created);

        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Service.RegisterPaymentAsync(request, h.StaffUserId, new string('k', 101)));
    }

    // ---- Devoluciones ----

    [Fact]
    public async Task CreateRefund_Partial_ShouldKeepOriginalAmount_AndListRefunds()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var payment = await h.RegisterAsync(payer, 30000m);
        var adminId = Guid.NewGuid();

        var result = await h.Service.CreateRefundAsync(
            payment.Id, new CreatePaymentRefundRequest(10000m, "Descuento comercial post-venta", "Autorizado por gerencia"), adminId, null);

        Assert.True(result.Created);
        Assert.Equal(10000m, result.Value.Amount);
        Assert.Equal("ARS", result.Value.Currency);
        Assert.Equal("Descuento comercial post-venta", result.Value.Reason);
        Assert.Equal(adminId, result.Value.RequestedByUserId);
        Assert.Equal(h.Calendar.UtcNow, result.Value.CreatedAtUtc);

        var current = await h.Service.GetByIdAsync(payment.Id);
        Assert.Equal(30000m, current!.Amount);
        Assert.Equal(10000m, current.RefundedAmount);
        Assert.Equal(20000m, current.NetAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, current.Status);

        var refunds = await h.Service.GetRefundsAsync(payment.Id);
        Assert.Equal(result.Value.Id, Assert.Single(refunds).Id);
    }

    [Fact]
    public async Task CreateRefund_Accumulated_ShouldReachRefunded_AndRejectAnyExcess()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var payment = await h.RegisterAsync(payer, 20000m);

        await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(5000m, "Primera"), h.StaffUserId, null);
        await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Segunda"), h.StaffUserId, null);

        await AssertConflictAsync(PaymentErrorCodes.RefundAmountExceeded, () =>
            h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Excede"), h.StaffUserId, null));

        var partial = await h.Service.GetByIdAsync(payment.Id);
        Assert.Equal(15000m, partial!.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyRefunded, partial.Status);

        await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(5000m, "Saldo"), h.StaffUserId, null);

        var full = await h.Service.GetByIdAsync(payment.Id);
        Assert.Equal(20000m, full!.RefundedAmount);
        Assert.Equal(PaymentStatus.Refunded, full.Status);
        Assert.Equal(3, (await h.Service.GetRefundsAsync(payment.Id)).Count);

        // Refunded es terminal
        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(1m, "Otra"), h.StaffUserId, null));
    }

    [Fact]
    public async Task CreateRefund_OnUnconfirmedPaymentOrInvalidRequest_ShouldBeRejected()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var pending = await h.RegisterAsync(payer, 20000m, status: PaymentStatus.Pending);

        await AssertConflictAsync(PaymentErrorCodes.InvalidTransition, () =>
            h.Service.CreateRefundAsync(pending.Id, new CreatePaymentRefundRequest(1000m, "Motivo"), h.StaffUserId, null));
        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Service.CreateRefundAsync(pending.Id, new CreatePaymentRefundRequest(0m, "Motivo"), h.StaffUserId, null));
        await Assert.ThrowsAsync<ValidationException>(() =>
            h.Service.CreateRefundAsync(pending.Id, new CreatePaymentRefundRequest(1000m, ""), h.StaffUserId, null));
    }

    [Fact]
    public async Task CreateRefund_WithIdempotencyKey_ShouldNotDuplicate_AndRejectDifferentData()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var payment = await h.RegisterAsync(payer, 30000m);
        var request = new CreatePaymentRefundRequest(10000m, "Descuento");

        var first = await h.Service.CreateRefundAsync(payment.Id, request, h.StaffUserId, "refund-1");
        var second = await h.Service.CreateRefundAsync(payment.Id, request, h.StaffUserId, "refund-1");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Equal(10000m, (await h.Service.GetByIdAsync(payment.Id))!.RefundedAmount);

        await AssertConflictAsync(PaymentErrorCodes.IdempotencyKeyMismatch, () =>
            h.Service.CreateRefundAsync(payment.Id, request with { Amount = 5000m }, h.StaffUserId, "refund-1"));
        await AssertConflictAsync(PaymentErrorCodes.IdempotencyKeyMismatch, () =>
            h.Service.CreateRefundAsync(payment.Id, request with { Reason = "Otro motivo" }, h.StaffUserId, "refund-1"));
    }

    [Fact]
    public async Task CreateRefund_OnActiveMembershipPayment_ShouldKeepMembershipActive_AndReflectPendingBalance()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 60000m);
        var payment = await h.RegisterAsync(payer, 60000m, membership.Id);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));
        var creditsBefore = await h.Db.Memberships.AsNoTracking().Where(m => m.Id == membership.Id).Select(m => m.AvailableCredits).SingleAsync();

        await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Ajuste"), h.StaffUserId, null);

        var balance = await h.Service.GetMembershipBalanceAsync(membership.Id);
        Assert.Equal(50000m, balance.TotalPaidAmount);
        Assert.Equal(10000m, balance.PendingBalance);
        Assert.Equal(PaymentStatus.PartiallyRefunded, Assert.Single(balance.Payments).Status);
        Assert.Equal(10000m, Assert.Single(balance.Refunds).Amount);

        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));
        var creditsAfter = await h.Db.Memberships.AsNoTracking().Where(m => m.Id == membership.Id).Select(m => m.AvailableCredits).SingleAsync();
        Assert.Equal(creditsBefore, creditsAfter);

        // El pago sigue vinculado a su membresía
        Assert.Equal(membership.Id, (await h.Service.GetByIdAsync(payment.Id))!.MembershipId);
    }

    // ---- Notificaciones post-commit ----

    [Fact]
    public async Task ConfirmPayment_WhenNotificationHandlerFails_ShouldKeepPaymentCompletedAndMembershipActive()
    {
        await using var h = new Harness(failingNotifications: true);
        var payer = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(payer, 20000m);
        var pending = await h.RegisterAsync(payer, 20000m, membership.Id, status: PaymentStatus.Pending);

        var confirmed = await h.Service.ConfirmPaymentAsync(pending.Id, null, h.StaffUserId);

        Assert.Equal(1, h.FailingNotifications!.Calls);
        Assert.Equal(PaymentStatus.Completed, confirmed.Status);
        Assert.Equal(PaymentStatus.Completed, (await h.Service.GetByIdAsync(pending.Id))!.Status);
        Assert.Equal(MembershipStatus.Active, await h.MembershipStatusAsync(membership.Id));
    }

    // ---- Consultas ----

    [Fact]
    public async Task GetPaged_ShouldFilterAndPageOnServer()
    {
        await using var h = new Harness();
        var payer = await h.AddStudentAsync();
        var other = await h.AddStudentAsync("Lucía", "32444555");
        var membership = await h.AddMembershipAsync(payer, 60000m);

        await h.RegisterAsync(payer, 10000m, membership.Id);
        await h.RegisterAsync(payer, 5000m, status: PaymentStatus.Pending, method: PaymentMethod.BankTransfer);
        await h.RegisterAsync(payer, 6000m, status: PaymentStatus.Pending, method: PaymentMethod.BankTransfer);
        await h.RegisterAsync(other, 7000m);

        var all = await h.Service.GetPagedAsync(new PaymentListFilter(Page: 1, PageSize: 3));
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(3, all.Items.Count);
        Assert.Equal(2, all.TotalPages);

        var pending = await h.Service.GetPagedAsync(new PaymentListFilter(Status: PaymentStatus.Pending));
        Assert.Equal(2, pending.TotalCount);
        Assert.All(pending.Items, p => Assert.Equal(PaymentStatus.Pending, p.Status));

        Assert.Equal(3, (await h.Service.GetPagedAsync(new PaymentListFilter(StudentId: payer.Id))).TotalCount);
        Assert.Equal(1, (await h.Service.GetPagedAsync(new PaymentListFilter(MembershipId: membership.Id))).TotalCount);
        Assert.Equal(2, (await h.Service.GetPagedAsync(new PaymentListFilter(Method: PaymentMethod.BankTransfer))).TotalCount);
        Assert.Equal(0, (await h.Service.GetPagedAsync(new PaymentListFilter(FromUtc: h.Calendar.UtcNow.AddDays(1)))).TotalCount);
        Assert.Equal(4, (await h.Service.GetPagedAsync(new PaymentListFilter(ToUtc: h.Calendar.UtcNow.AddDays(1)))).TotalCount);

        // Tamaño de página acotado
        Assert.Equal(100, (await h.Service.GetPagedAsync(new PaymentListFilter(PageSize: 5000))).PageSize);
        Assert.Equal(3, (await h.Service.GetByPersonIdAsync(payer.Id)).Count);
    }

    // ---- Controlador (compatibilidad de confirmación y rechazo) ----

    [Fact]
    public async Task PaymentsController_ConfirmAndFailEndpoints_ShouldReturn200Or409Conflict()
    {
        await using var h = new Harness();
        var currentUser = new StubCurrentUserService();
        var controller = new PaymentsController(h.Service, currentUser);
        var payer = await h.AddStudentAsync();
        var pending = await h.RegisterAsync(payer, 12000m, status: PaymentStatus.Pending, method: PaymentMethod.BankTransfer);

        var confirmResult = await controller.ConfirmPayment(pending.Id, new ConfirmPaymentRequest("BANK-123"), CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(confirmResult);
        Assert.Equal(PaymentStatus.Completed, Assert.IsType<PaymentDto>(okResult.Value).Status);

        Assert.IsType<ConflictObjectResult>(
            await controller.ConfirmPayment(pending.Id, new ConfirmPaymentRequest("BANK-123"), CancellationToken.None));
        Assert.IsType<ConflictObjectResult>(
            await controller.FailPayment(pending.Id, new FailPaymentRequest("Motivo tardío"), CancellationToken.None));
    }
}
