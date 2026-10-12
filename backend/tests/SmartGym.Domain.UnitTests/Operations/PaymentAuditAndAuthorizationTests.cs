using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentAuditAndAuthorizationTests : IAsyncDisposable
{
    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; } = Guid.NewGuid();
        public Guid? PersonId { get; set; }
        public string? Email { get; set; } = "admin@smartgym.com";
        public bool IsAuthenticated => true;
        public IReadOnlyList<string> Roles { get; set; } = new[] { "Administrator" };
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private readonly TestGymCalendar _calendar = new(new DateOnly(2026, 10, 12));
    private readonly SmartGymDbContext _db = new(MembershipTestSupport.InMemoryOptions());
    private readonly StubCurrentUserService _currentUser = new();
    private readonly IPaymentService _service;
    private readonly Guid _operatorId = Guid.NewGuid();

    public PaymentAuditAndAuthorizationTests()
    {
        _service = new PaymentService(_db, _calendar, currentUserService: _currentUser);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    private async Task<Person> AddStudentAsync()
    {
        var student = Person.Create("Martín", "Gómez", "31222333");
        _db.People.Add(student);
        await _db.SaveChangesAsync();
        return student;
    }

    private async Task<Membership> AddMembershipAsync(Person student, decimal price)
    {
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(), Name = "Plan 8", DurationDays = 30, Price = price, Credits = 8,
            IsUnlimited = false, AppliesToAllActivities = true, IsActive = true
        };
        _db.MembershipPlans.Add(plan);
        var membership = Membership.Create(
            studentId: student.Id, plan: plan, startDate: _calendar.Today, purchasedAtUtc: _calendar.UtcNow,
            originalPrice: price, discountAmount: 0m, purchasePrice: price, currency: "ARS", extraCreditsGranted: 0);
        membership.Status = MembershipStatus.Pending;
        _db.Memberships.Add(membership);
        await _db.SaveChangesAsync();
        return membership;
    }

    private Task<PaymentDto> RegisterAsync(Person payer, decimal amount, PaymentStatus status = PaymentStatus.Completed) =>
        _service.RegisterPaymentAsync(
            new RegisterPaymentRequest(payer.Id, null, null, amount, PaymentMethod.Cash, null, null, status), _operatorId);

    private async Task<AuditLog> SingleAuditAsync(string action, Guid paymentId) =>
        Assert.Single(await _db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == action && a.EntityId == paymentId.ToString())
            .ToListAsync());

    [Fact]
    public async Task EveryOperation_RegistersItsAuditEntry_WithOperatorEntityAndTimestamp()
    {
        var payer = await AddStudentAsync();
        var membership = await AddMembershipAsync(payer, 60000m);

        var confirmedLater = await RegisterAsync(payer, 10000m, PaymentStatus.Pending);
        await _service.ConfirmPaymentAsync(confirmedLater.Id, null, _operatorId);
        await _service.ApplyPaymentAsync(confirmedLater.Id, new ApplyPaymentRequest(membership.Id, null), _operatorId);
        await _service.CreateRefundAsync(confirmedLater.Id, new CreatePaymentRefundRequest(4000m, "Ajuste"), _operatorId, null);

        var failed = await RegisterAsync(payer, 5000m, PaymentStatus.Pending);
        await _service.FailPaymentAsync(failed.Id, new FailPaymentRequest("Comprobante inválido"), _operatorId);

        var cancelled = await RegisterAsync(payer, 5000m, PaymentStatus.Pending);
        await _service.CancelPaymentAsync(cancelled.Id, new CancelPaymentRequest("El alumno desistió"), _operatorId);

        var expected = new (string Action, Guid PaymentId)[]
        {
            ("PAYMENT_REGISTERED", confirmedLater.Id),
            ("PAYMENT_CONFIRMED", confirmedLater.Id),
            ("PAYMENT_APPLIED", confirmedLater.Id),
            ("PAYMENT_REFUNDED", confirmedLater.Id),
            ("PAYMENT_REGISTERED", failed.Id),
            ("PAYMENT_FAILED", failed.Id),
            ("PAYMENT_REGISTERED", cancelled.Id),
            ("PAYMENT_CANCELLED", cancelled.Id)
        };

        foreach (var (action, paymentId) in expected)
        {
            var log = await SingleAuditAsync(action, paymentId);
            Assert.Equal(nameof(Payment), log.EntityName);
            Assert.Equal(_operatorId, log.UserId);
            Assert.Equal("admin@smartgym.com", log.UserEmail);
            Assert.Equal(_calendar.UtcNow, log.TimestampUtc);
            Assert.False(string.IsNullOrWhiteSpace(log.NewValues));
        }

        Assert.Equal(expected.Length, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task RefundAudit_RecordsAmountsCurrencyReasonAndPreviousAndNewState()
    {
        var payer = await AddStudentAsync();
        var payment = await RegisterAsync(payer, 30000m);

        await _service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Descuento comercial"), _operatorId, null);

        var log = await SingleAuditAsync("PAYMENT_REFUNDED", payment.Id);
        using var oldValues = JsonDocument.Parse(log.OldValues!);
        using var newValues = JsonDocument.Parse(log.NewValues!);

        Assert.Equal(0m, oldValues.RootElement.GetProperty("RefundedAmount").GetDecimal());
        Assert.Equal("Completed", oldValues.RootElement.GetProperty("Status").GetString());
        Assert.Equal(30000m, oldValues.RootElement.GetProperty("Amount").GetDecimal());

        Assert.Equal(10000m, newValues.RootElement.GetProperty("RefundedAmount").GetDecimal());
        Assert.Equal("PartiallyRefunded", newValues.RootElement.GetProperty("Status").GetString());
        Assert.Equal(10000m, newValues.RootElement.GetProperty("RefundAmount").GetDecimal());
        Assert.Equal("ARS", newValues.RootElement.GetProperty("Currency").GetString());
        Assert.Equal("Descuento comercial", newValues.RootElement.GetProperty("Reason").GetString());
    }

    [Fact]
    public async Task StateChangeAudits_RecordReasonAndTransition()
    {
        var payer = await AddStudentAsync();
        var pending = await RegisterAsync(payer, 5000m, PaymentStatus.Pending);

        await _service.CancelPaymentAsync(pending.Id, new CancelPaymentRequest("El alumno desistió"), _operatorId);

        var log = await SingleAuditAsync("PAYMENT_CANCELLED", pending.Id);
        using var oldValues = JsonDocument.Parse(log.OldValues!);
        using var newValues = JsonDocument.Parse(log.NewValues!);

        Assert.Equal("Pending", oldValues.RootElement.GetProperty("Status").GetString());
        Assert.Equal("Cancelled", newValues.RootElement.GetProperty("Status").GetString());
        Assert.Equal("El alumno desistió", newValues.RootElement.GetProperty("Reason").GetString());
    }

    [Fact]
    public async Task RejectedOperations_DoNotLeaveAuditEntries()
    {
        var payer = await AddStudentAsync();
        var payment = await RegisterAsync(payer, 20000m);
        var auditsBefore = await _db.AuditLogs.CountAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(20000.01m, "Excede"), _operatorId, null));
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CancelPaymentAsync(payment.Id, new CancelPaymentRequest("Tarde"), _operatorId));
        await Assert.ThrowsAsync<ValidationException>(() => RegisterAsync(payer, -1m));

        // Un guardado posterior no arrastra auditoría de las operaciones rechazadas
        await _db.SaveChangesAsync();
        Assert.Equal(auditsBefore, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task IdempotentRepetition_DoesNotDuplicateAuditEntries()
    {
        var payer = await AddStudentAsync();
        var request = new RegisterPaymentRequest(payer.Id, null, null, 20000m, PaymentMethod.Cash, null, null);

        var first = await _service.RegisterPaymentAsync(request, _operatorId, "key-1");
        await _service.RegisterPaymentAsync(request, _operatorId, "key-1");

        await SingleAuditAsync("PAYMENT_REGISTERED", first.Value.Id);
    }
}
