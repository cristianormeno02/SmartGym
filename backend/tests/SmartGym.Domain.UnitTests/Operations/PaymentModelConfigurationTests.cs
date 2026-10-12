using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentModelConfigurationTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;
        using var dbContext = new SmartGymDbContext(options);
        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    [Fact]
    public void Payment_NewColumnsAndConcurrencyToken_ShouldBeConfigured()
    {
        var paymentType = BuildModel().FindEntityType(typeof(Payment))!;

        Assert.Equal("Payments", paymentType.GetTableName());

        var provider = paymentType.FindProperty(nameof(Payment.Provider))!;
        Assert.False(provider.IsNullable);

        var currency = paymentType.FindProperty(nameof(Payment.Currency))!;
        Assert.False(currency.IsNullable);
        Assert.Equal(3, currency.GetMaxLength());

        var refunded = paymentType.FindProperty(nameof(Payment.RefundedAmount))!;
        Assert.False(refunded.IsNullable);
        Assert.Equal(18, refunded.GetPrecision());
        Assert.Equal(2, refunded.GetScale());

        Assert.True(paymentType.FindProperty(nameof(Payment.ConfirmedAtUtc))!.IsNullable);
        Assert.True(paymentType.FindProperty(nameof(Payment.CancelledAtUtc))!.IsNullable);
        Assert.Equal(100, paymentType.FindProperty(nameof(Payment.IdempotencyKey))!.GetMaxLength());

        // RowVersion (xmin)
        var version = paymentType.FindProperty(nameof(Payment.Version))!;
        Assert.True(version.IsConcurrencyToken);
        Assert.Equal("xmin", version.GetColumnName());

        // Propiedades calculadas: no se persisten
        Assert.Null(paymentType.FindProperty(nameof(Payment.NetAmount)));
        Assert.Null(paymentType.FindProperty(nameof(Payment.IsConfirmed)));
        Assert.Null(paymentType.FindProperty(nameof(Payment.IsApplied)));
    }

    [Fact]
    public void Payment_CheckConstraintsIndexesAndRestrictFks_ShouldBeConfigured()
    {
        var paymentType = BuildModel().FindEntityType(typeof(Payment))!;

        var checks = paymentType.GetCheckConstraints().ToList();
        Assert.Contains(checks, c =>
            c.ModelName == "CK_Payments_Amounts" &&
            c.Sql == "\"Amount\" > 0 AND \"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\"");
        Assert.Contains(checks, c =>
            c.ModelName == "CK_Payments_SingleTarget" &&
            c.Sql == "\"MembershipId\" IS NULL OR \"ClassSessionId\" IS NULL");

        var idempotencyIdx = Assert.Single(
            paymentType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_Payments_Payer_IdempotencyKey");
        Assert.True(idempotencyIdx.IsUnique);
        Assert.Equal(
            new[] { nameof(Payment.PayerPersonId), nameof(Payment.IdempotencyKey) },
            idempotencyIdx.Properties.Select(p => p.Name));
        Assert.Equal("\"IdempotencyKey\" IS NOT NULL", idempotencyIdx.GetFilter());

        Assert.Contains(paymentType.GetIndexes(), i => i.Properties.Single().Name == nameof(Payment.ConfirmedAtUtc));
        Assert.Contains(paymentType.GetIndexes(), i => i.Properties.Single().Name == nameof(Payment.ClassSessionId));

        // Un pago no debe perder su concepto: nada de SetNull
        var membershipFk = Assert.Single(paymentType.GetForeignKeys(), fk => fk.Properties.Single().Name == nameof(Payment.MembershipId));
        Assert.Equal(DeleteBehavior.Restrict, membershipFk.DeleteBehavior);
        var sessionFk = Assert.Single(paymentType.GetForeignKeys(), fk => fk.Properties.Single().Name == nameof(Payment.ClassSessionId));
        Assert.Equal(DeleteBehavior.Restrict, sessionFk.DeleteBehavior);
    }

    [Fact]
    public void PaymentRefund_ColumnsCheckConstraintIndexAndFk_ShouldBeConfigured()
    {
        var refundType = BuildModel().FindEntityType(typeof(PaymentRefund))!;

        Assert.Equal("PaymentRefunds", refundType.GetTableName());

        var amount = refundType.FindProperty(nameof(PaymentRefund.Amount))!;
        Assert.Equal(18, amount.GetPrecision());
        Assert.Equal(2, amount.GetScale());
        Assert.Equal(3, refundType.FindProperty(nameof(PaymentRefund.Currency))!.GetMaxLength());

        var reason = refundType.FindProperty(nameof(PaymentRefund.Reason))!;
        Assert.False(reason.IsNullable);
        Assert.Equal(500, reason.GetMaxLength());

        Assert.True(refundType.FindProperty(nameof(PaymentRefund.RequestedByUserId))!.IsNullable);
        Assert.Equal(100, refundType.FindProperty(nameof(PaymentRefund.IdempotencyKey))!.GetMaxLength());

        Assert.Contains(refundType.GetCheckConstraints(), c =>
            c.ModelName == "CK_PaymentRefunds_Amount" && c.Sql == "\"Amount\" > 0");

        var idempotencyIdx = Assert.Single(
            refundType.GetIndexes(),
            i => i.GetDatabaseName() == "UX_PaymentRefunds_Payment_IdempotencyKey");
        Assert.True(idempotencyIdx.IsUnique);
        Assert.Equal(
            new[] { nameof(PaymentRefund.PaymentId), nameof(PaymentRefund.IdempotencyKey) },
            idempotencyIdx.Properties.Select(p => p.Name));
        Assert.Equal("\"IdempotencyKey\" IS NOT NULL", idempotencyIdx.GetFilter());

        var paymentFk = Assert.Single(refundType.GetForeignKeys());
        Assert.Equal(typeof(Payment), paymentFk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, paymentFk.DeleteBehavior);
    }
}
