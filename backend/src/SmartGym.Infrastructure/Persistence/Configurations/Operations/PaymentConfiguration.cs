using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.RefundedAmount)
            .HasPrecision(18, 2)
            .IsRequired()
            .HasDefaultValue(0m);

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("ARS");

        builder.Property(p => p.Method)
            .IsRequired();

        builder.Property(p => p.Provider)
            .IsRequired()
            .HasDefaultValue(PaymentProvider.Manual);

        builder.Property(p => p.Status)
            .IsRequired();

        builder.Property(p => p.ExternalTransactionId)
            .HasMaxLength(150);

        builder.Property(p => p.ReceiptUrl)
            .HasMaxLength(500);

        builder.Property(p => p.Notes)
            .HasMaxLength(500);

        builder.Property(p => p.IdempotencyKey)
            .HasMaxLength(100);

        // Concurrencia optimista con PostgreSQL xmin
        builder.Property(p => p.Version)
            .IsRowVersion();

        builder.Ignore(p => p.NetAmount);
        builder.Ignore(p => p.IsConfirmed);
        builder.Ignore(p => p.IsApplied);

        builder.ToTable(t =>
        {
            // Última barrera del tope de devoluciones si algún camino omitiera el bloqueo del pago
            t.HasCheckConstraint("CK_Payments_Amounts",
                "\"Amount\" > 0 AND \"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\"");

            t.HasCheckConstraint("CK_Payments_SingleTarget",
                "\"MembershipId\" IS NULL OR \"ClassSessionId\" IS NULL");
        });

        builder.HasIndex(p => p.PayerPersonId);
        builder.HasIndex(p => p.MembershipId);
        builder.HasIndex(p => p.ClassSessionId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.PaidAtUtc);
        builder.HasIndex(p => p.ConfirmedAtUtc);

        builder.HasIndex(p => new { p.PayerPersonId, p.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_Payments_Payer_IdempotencyKey")
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        builder.HasOne(p => p.PayerPerson)
            .WithMany()
            .HasForeignKey(p => p.PayerPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        // Un pago no debe perder el concepto al que se aplicó
        builder.HasOne(p => p.Membership)
            .WithMany()
            .HasForeignKey(p => p.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.ClassSession)
            .WithMany()
            .HasForeignKey(p => p.ClassSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Refunds)
            .WithOne(r => r.Payment)
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
