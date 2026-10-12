using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class DashboardRevenueTests
{
    private static readonly DateTime September = new(2026, 9, 20, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime October = new(2026, 10, 5, 15, 0, 0, DateTimeKind.Utc);

    private static Payment Confirmed(decimal amount, DateTime confirmedAtUtc, decimal refunded = 0m) => new()
    {
        Id = Guid.NewGuid(),
        PayerPersonId = Guid.NewGuid(),
        Amount = amount,
        RefundedAmount = refunded,
        Method = PaymentMethod.Cash,
        Status = refunded == 0m
            ? PaymentStatus.Completed
            : refunded == amount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded,
        PaidAtUtc = confirmedAtUtc,
        ConfirmedAtUtc = confirmedAtUtc,
        IsActive = true
    };

    private static PaymentRefund Refund(Payment payment, decimal amount, DateTime createdAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        PaymentId = payment.Id,
        Amount = amount,
        Currency = payment.Currency,
        Reason = "Ajuste",
        CreatedAtUtc = createdAtUtc,
        IsActive = true
    };

    [Fact]
    public async Task MonthlyRevenue_ShouldBeConfirmedInMonthMinusRefundsRegisteredInMonth()
    {
        await using var db = new SmartGymDbContext(MembershipTestSupport.InMemoryOptions());
        var calendar = new TestGymCalendar(new DateOnly(2026, 10, 12));

        // Cobro de septiembre con una devolución registrada en octubre
        var septemberPayment = Confirmed(30000m, September, refunded: 10000m);
        // Cobros de octubre: uno intacto y otro devuelto por completo en el mismo mes
        var octoberPayment = Confirmed(50000m, October);
        var octoberRefunded = Confirmed(8000m, October, refunded: 8000m);
        // No cuentan: pendiente registrado en octubre y cancelado
        var pending = new Payment
        {
            Id = Guid.NewGuid(), PayerPersonId = Guid.NewGuid(), Amount = 99000m, Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Pending, PaidAtUtc = October, IsActive = true
        };
        var cancelled = new Payment
        {
            Id = Guid.NewGuid(), PayerPersonId = Guid.NewGuid(), Amount = 77000m, Method = PaymentMethod.BankTransfer,
            Status = PaymentStatus.Cancelled, PaidAtUtc = October, CancelledAtUtc = October, IsActive = true
        };

        db.Payments.AddRange(septemberPayment, octoberPayment, octoberRefunded, pending, cancelled);
        db.PaymentRefunds.AddRange(
            Refund(septemberPayment, 10000m, October.AddDays(1)),
            Refund(octoberRefunded, 8000m, October.AddDays(2)));
        await db.SaveChangesAsync();

        var metrics = await new DashboardService(db, calendar).GetDashboardMetricsAsync();

        // 50000 + 8000 confirmados en octubre - (10000 + 8000) devueltos en octubre
        Assert.Equal(40000m, metrics.MonthlyRevenue);
    }
}
