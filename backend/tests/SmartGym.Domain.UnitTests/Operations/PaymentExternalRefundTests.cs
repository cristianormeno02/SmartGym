using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Modules.Operations;
using SmartGym.Application.Modules.Operations.Dtos;
using SmartGym.Application.Modules.Operations.Interfaces;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using Xunit;
using static SmartGym.Domain.UnitTests.Common.PaymentGatewayHarness;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentExternalRefundTests
{
    private static async Task<(PaymentGatewayHarness H, PaymentDto Payment)> ConfirmedExternalPaymentAsync(decimal amount = 40000m)
    {
        var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var membership = await h.AddMembershipAsync(student, amount);
        var checkout = await h.CheckoutAsync(membership);
        var payment = await h.ApproveByWebhookAsync(checkout, "MP-1");
        return (h, payment);
    }

    [Fact]
    public async Task Refund_AcceptedByTheProvider_IsCompleted_AndUpdatesThePayment()
    {
        var (h, payment) = await ConfirmedExternalPaymentAsync();
        await using var _ = h;
        h.Provider.RefundResponse = _ => new ExternalRefundResult(true, "MP-REF-9");
        var adminId = Guid.NewGuid();

        var result = await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Ajuste"), adminId, null);

        Assert.True(result.Created);
        Assert.Equal(PaymentRefundStatus.Completed, result.Value.Status);
        Assert.Equal("MP-REF-9", result.Value.ExternalRefundId);
        Assert.Equal(h.Calendar.UtcNow, result.Value.CompletedAtUtc);

        var current = (await h.Service.GetByIdAsync(payment.Id))!;
        Assert.Equal(PaymentStatus.PartiallyRefunded, current.Status);
        Assert.Equal(10000m, current.RefundedAmount);

        // El proveedor recibe la transacción, el importe y el Id de la devolución como clave de idempotencia
        var request = Assert.Single(h.Provider.RefundRequests);
        Assert.Equal("MP-1", request.ExternalTransactionId);
        Assert.Equal(10000m, request.Amount);
        Assert.Equal(result.Value.Id.ToString(), request.IdempotencyKey);

        var actions = await h.Db.AuditLogs.Where(a => a.EntityId == payment.Id.ToString()).Select(a => a.Action).ToListAsync();
        Assert.Contains("PAYMENT_REFUND_REQUESTED", actions);
        Assert.Contains("PAYMENT_REFUNDED", actions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Refund_RejectedByTheProviderOrUnreachable_IsFailed_AndLeavesThePaymentIntact(bool providerThrows)
    {
        var (h, payment) = await ConfirmedExternalPaymentAsync();
        await using var _ = h;
        if (providerThrows)
        {
            h.Provider.RefundFailure = new HttpRequestException("timeout");
        }
        else
        {
            h.Provider.RefundResponse = _ => new ExternalRefundResult(false, null, "Saldo insuficiente en la cuenta");
        }

        await AssertExternalFailureAsync(PaymentErrorCodes.ProviderRefundFailed, () =>
            h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Ajuste"), h.StaffUserId, null));

        var current = (await h.Service.GetByIdAsync(payment.Id))!;
        Assert.Equal(PaymentStatus.Completed, current.Status);
        Assert.Equal(0m, current.RefundedAmount);

        var refund = Assert.Single(await h.Service.GetRefundsAsync(payment.Id));
        Assert.Equal(PaymentRefundStatus.Failed, refund.Status);
        Assert.False(string.IsNullOrWhiteSpace(refund.FailureReason));
        Assert.Null(refund.CompletedAtUtc);
        Assert.Contains(await h.Db.AuditLogs.ToListAsync(), a => a.Action == "PAYMENT_REFUND_FAILED");

        // Una devolución fallida no reserva importe: se puede volver a pedir el total
        h.Provider.RefundFailure = null;
        h.Provider.RefundResponse = null;
        var retry = await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(40000m, "Total"), h.StaffUserId, null);
        Assert.Equal(PaymentRefundStatus.Completed, retry.Value.Status);
        Assert.Equal(PaymentStatus.Refunded, (await h.Service.GetByIdAsync(payment.Id))!.Status);
    }

    [Fact]
    public async Task Refund_WhileAnotherIsPendingAtTheProvider_CannotExceedTheReservedAmount()
    {
        var (h, payment) = await ConfirmedExternalPaymentAsync(20000m);
        await using var _ = h;
        Exception? secondAttempt = null;
        var nested = false;

        // Mientras el proveedor procesa la primera devolución (15000), llega otra por 10000
        h.Provider.OnRefundRequested = async () =>
        {
            if (nested)
            {
                return;
            }

            nested = true;
            secondAttempt = await Record.ExceptionAsync(() =>
                h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Concurrente"), h.StaffUserId, null));
        };

        var first = await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(15000m, "Primera"), h.StaffUserId, null);

        Assert.Equal(PaymentRefundStatus.Completed, first.Value.Status);
        var conflict = Assert.IsType<SmartGym.Application.Common.Exceptions.ConflictException>(secondAttempt);
        Assert.Equal(PaymentErrorCodes.RefundAmountExceeded, conflict.Code);
        Assert.Single(h.Provider.RefundRequests);
        Assert.Equal(15000m, (await h.Service.GetByIdAsync(payment.Id))!.RefundedAmount);
    }

    [Fact]
    public async Task Refund_WithIdempotencyKey_IsRequestedToTheProviderOnce()
    {
        var (h, payment) = await ConfirmedExternalPaymentAsync();
        await using var _ = h;
        var request = new CreatePaymentRefundRequest(10000m, "Ajuste");

        var first = await h.Service.CreateRefundAsync(payment.Id, request, h.StaffUserId, "ref-1");
        var second = await h.Service.CreateRefundAsync(payment.Id, request, h.StaffUserId, "ref-1");

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Single(h.Provider.RefundRequests);
        Assert.Equal(10000m, (await h.Service.GetByIdAsync(payment.Id))!.RefundedAmount);
    }

    [Fact]
    public async Task Reconcile_ResolvesRefundsLeftPending_AccordingToWhatTheProviderReports()
    {
        var (h, payment) = await ConfirmedExternalPaymentAsync(40000m);
        await using var _ = h;

        // Dos devoluciones quedaron pendientes por una interrupción tras llamar al proveedor
        var entity = await h.Db.Payments.SingleAsync(p => p.Id == payment.Id);
        var done = entity.RequestRefund(10000m, "Llegó al proveedor", null, pendingRefundsAmount: 0m, nowUtc: h.Calendar.UtcNow);
        var lost = entity.RequestRefund(5000m, "No llegó", null, pendingRefundsAmount: 10000m, nowUtc: h.Calendar.UtcNow.AddMinutes(1));
        h.Db.PaymentRefunds.AddRange(done, lost);
        await h.Db.SaveChangesAsync();

        // El proveedor sólo registra 10000 devueltos
        h.Provider.SetPayment("MP-1", payment.Id, 40000m, ExternalPaymentStatus.Approved, refundedAmount: 10000m);

        var reconciled = await h.Service.ReconcileAsync(payment.Id, h.StaffUserId);

        Assert.Equal(PaymentStatus.PartiallyRefunded, reconciled.Status);
        Assert.Equal(10000m, reconciled.RefundedAmount);
        var refunds = await h.Service.GetRefundsAsync(payment.Id);
        Assert.Equal(PaymentRefundStatus.Completed, refunds.Single(r => r.Id == done.Id).Status);
        Assert.Equal(PaymentRefundStatus.Failed, refunds.Single(r => r.Id == lost.Id).Status);
        // Lo devuelto por el proveedor coincide con lo registrado: no es una devolución externa
        Assert.Empty(await h.DiscrepanciesAsync());
    }

    [Fact]
    public async Task Webhook_RefundNotification_WhileOurRefundIsInFlight_IsNotAnExternalRefund()
    {
        var (h, payment) = await ConfirmedExternalPaymentAsync(40000m);
        await using var _ = h;

        // El proveedor notifica el reintegro antes de que termine nuestra llamada
        h.Provider.OnRefundRequested = async () =>
        {
            h.Provider.SetPayment("MP-1", payment.Id, 40000m, ExternalPaymentStatus.Approved, refundedAmount: 10000m);
            await h.WebhookAsync("MP-1");
        };

        await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Ajuste"), h.StaffUserId, null);

        Assert.Empty(await h.DiscrepanciesAsync());
        Assert.Equal(10000m, (await h.Service.GetByIdAsync(payment.Id))!.RefundedAmount);
    }

    [Fact]
    public async Task ManualPaymentRefund_StaysSingleStep_WithoutCallingAnyProvider()
    {
        await using var h = new PaymentGatewayHarness();
        var student = await h.AddStudentAsync();
        var manual = await h.RegisterManualAsync(student, 30000m);

        var result = await h.Service.CreateRefundAsync(manual.Id, new CreatePaymentRefundRequest(10000m, "Ajuste"), h.StaffUserId, null);

        Assert.Equal(PaymentRefundStatus.Completed, result.Value.Status);
        Assert.Equal(h.Calendar.UtcNow, result.Value.CompletedAtUtc);
        Assert.Null(result.Value.ExternalRefundId);
        Assert.Empty(h.Provider.RefundRequests);
    }

    [Fact]
    public async Task Dashboard_SubtractsOnlyCompletedRefunds_ByCompletionDate()
    {
        var (h, payment) = await ConfirmedExternalPaymentAsync(40000m);
        await using var _ = h;

        h.Provider.RefundResponse = _ => new ExternalRefundResult(false, null, "Rechazado");
        await Record.ExceptionAsync(() =>
            h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(25000m, "Fallida"), h.StaffUserId, null));
        h.Provider.RefundResponse = null;
        await h.Service.CreateRefundAsync(payment.Id, new CreatePaymentRefundRequest(10000m, "Efectiva"), h.StaffUserId, null);

        var entity = await h.Db.Payments.SingleAsync(p => p.Id == payment.Id);
        var pending = entity.RequestRefund(5000m, "En curso", null, pendingRefundsAmount: 0m, nowUtc: h.Calendar.UtcNow);
        h.Db.PaymentRefunds.Add(pending);
        await h.Db.SaveChangesAsync();

        var metrics = await new DashboardService(h.Db, h.Calendar).GetDashboardMetricsAsync();

        // 40000 confirmados - 10000 devueltos; la devolución fallida y la pendiente no restan
        Assert.Equal(30000m, metrics.MonthlyRevenue);
    }
}
