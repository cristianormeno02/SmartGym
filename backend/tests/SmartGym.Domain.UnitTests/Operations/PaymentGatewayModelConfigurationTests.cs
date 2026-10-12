using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class PaymentGatewayModelConfigurationTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only")
            .Options;
        using var dbContext = new SmartGymDbContext(options);
        return dbContext.GetService<IDesignTimeModel>().Model;
    }

    private static IIndex Index(IEntityType type, string name) =>
        Assert.Single(type.GetIndexes(), i => i.GetDatabaseName() == name);

    [Fact]
    public void Payment_CheckoutColumnsAndGatewayIndexes_ShouldBeConfigured()
    {
        var paymentType = BuildModel().FindEntityType(typeof(Payment))!;

        Assert.Equal(150, paymentType.FindProperty(nameof(Payment.ExternalReference))!.GetMaxLength());
        Assert.Equal(500, paymentType.FindProperty(nameof(Payment.CheckoutUrl))!.GetMaxLength());
        Assert.True(paymentType.FindProperty(nameof(Payment.ExpiresAtUtc))!.IsNullable);
        Assert.Null(paymentType.FindProperty(nameof(Payment.IsExternal)));

        // La unicidad por transacción externa excluye los pagos manuales, cuyo valor es texto libre
        var externalTx = Index(paymentType, "UX_Payments_Provider_ExternalTx");
        Assert.True(externalTx.IsUnique);
        Assert.Equal(
            new[] { nameof(Payment.Provider), nameof(Payment.ExternalTransactionId) },
            externalTx.Properties.Select(p => p.Name));
        Assert.Equal("\"Provider\" <> 1 AND \"ExternalTransactionId\" IS NOT NULL", externalTx.GetFilter());

        var openCheckout = Index(paymentType, "UX_Payments_OpenCheckout");
        Assert.True(openCheckout.IsUnique);
        Assert.Equal(nameof(Payment.MembershipId), Assert.Single(openCheckout.Properties).Name);
        Assert.Equal("\"Provider\" <> 1 AND \"Status\" = 1", openCheckout.GetFilter());
    }

    [Fact]
    public void PaymentRefund_StatusAndProviderColumns_ShouldBeConfigured()
    {
        var refundType = BuildModel().FindEntityType(typeof(PaymentRefund))!;

        Assert.False(refundType.FindProperty(nameof(PaymentRefund.Status))!.IsNullable);
        Assert.Equal(150, refundType.FindProperty(nameof(PaymentRefund.ExternalRefundId))!.GetMaxLength());
        Assert.True(refundType.FindProperty(nameof(PaymentRefund.CompletedAtUtc))!.IsNullable);
        Assert.Equal(500, refundType.FindProperty(nameof(PaymentRefund.FailureReason))!.GetMaxLength());
        Assert.Contains(refundType.GetIndexes(), i => i.Properties.Single().Name == nameof(PaymentRefund.CompletedAtUtc));
    }

    [Fact]
    public void PaymentWebhookEvent_ShouldBeUniquePerProviderAndExternalEvent()
    {
        var eventType = BuildModel().FindEntityType(typeof(PaymentWebhookEvent))!;

        Assert.Equal("PaymentWebhookEvents", eventType.GetTableName());
        Assert.Equal(150, eventType.FindProperty(nameof(PaymentWebhookEvent.ExternalEventId))!.GetMaxLength());
        Assert.False(eventType.FindProperty(nameof(PaymentWebhookEvent.ExternalEventId))!.IsNullable);
        Assert.True(eventType.FindProperty(nameof(PaymentWebhookEvent.PaymentId))!.IsNullable);

        var unique = Index(eventType, "UX_PaymentWebhookEvents_Provider_ExternalEventId");
        Assert.True(unique.IsUnique);
        Assert.Equal(
            new[] { nameof(PaymentWebhookEvent.Provider), nameof(PaymentWebhookEvent.ExternalEventId) },
            unique.Properties.Select(p => p.Name));
        Assert.Null(unique.GetFilter());

        var paymentFk = Assert.Single(eventType.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, paymentFk.DeleteBehavior);
    }

    [Fact]
    public void PaymentDiscrepancy_ShouldAllowASingleOpenDiscrepancyPerCause()
    {
        var discrepancyType = BuildModel().FindEntityType(typeof(PaymentDiscrepancy))!;

        Assert.Equal("PaymentDiscrepancies", discrepancyType.GetTableName());
        Assert.True(discrepancyType.FindProperty(nameof(PaymentDiscrepancy.PaymentId))!.IsNullable);
        Assert.Equal(18, discrepancyType.FindProperty(nameof(PaymentDiscrepancy.ExternalAmount))!.GetPrecision());
        Assert.Equal(1000, discrepancyType.FindProperty(nameof(PaymentDiscrepancy.ResolutionNotes))!.GetMaxLength());

        var byPayment = Index(discrepancyType, "UX_PaymentDiscrepancies_Open_Payment");
        Assert.True(byPayment.IsUnique);
        Assert.Equal(
            new[] { nameof(PaymentDiscrepancy.PaymentId), nameof(PaymentDiscrepancy.Type) },
            byPayment.Properties.Select(p => p.Name));
        Assert.Equal("\"Status\" = 1 AND \"PaymentId\" IS NOT NULL", byPayment.GetFilter());

        var byTransaction = Index(discrepancyType, "UX_PaymentDiscrepancies_Open_Unknown");
        Assert.True(byTransaction.IsUnique);
        Assert.Equal(
            new[] { nameof(PaymentDiscrepancy.Provider), nameof(PaymentDiscrepancy.ExternalTransactionId), nameof(PaymentDiscrepancy.Type) },
            byTransaction.Properties.Select(p => p.Name));
        Assert.Equal("\"Status\" = 1 AND \"PaymentId\" IS NULL", byTransaction.GetFilter());

        var paymentFk = Assert.Single(discrepancyType.GetForeignKeys());
        Assert.Equal(DeleteBehavior.Restrict, paymentFk.DeleteBehavior);
    }
}
