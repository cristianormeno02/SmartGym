using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class PaymentRefundConfiguration : IEntityTypeConfiguration<PaymentRefund>
{
    public void Configure(EntityTypeBuilder<PaymentRefund> builder)
    {
        builder.ToTable("PaymentRefunds", t =>
            t.HasCheckConstraint("CK_PaymentRefunds_Amount", "\"Amount\" > 0"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.Currency)
            .IsRequired()
            .HasMaxLength(3);

        builder.Property(r => r.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(r => r.Notes)
            .HasMaxLength(500);

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(100);

        builder.Property(r => r.Status)
            .IsRequired()
            .HasDefaultValue(SmartGym.Domain.Enums.PaymentRefundStatus.Completed);

        builder.Property(r => r.ExternalRefundId)
            .HasMaxLength(150);

        builder.Property(r => r.FailureReason)
            .HasMaxLength(500);

        builder.HasIndex(r => r.CreatedAtUtc);
        builder.HasIndex(r => r.CompletedAtUtc);

        builder.HasIndex(r => new { r.PaymentId, r.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_PaymentRefunds_Payment_IdempotencyKey")
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");
    }
}
