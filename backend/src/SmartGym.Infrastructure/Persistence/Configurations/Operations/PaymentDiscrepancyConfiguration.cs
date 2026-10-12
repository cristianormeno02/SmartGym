using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class PaymentDiscrepancyConfiguration : IEntityTypeConfiguration<PaymentDiscrepancy>
{
    public void Configure(EntityTypeBuilder<PaymentDiscrepancy> builder)
    {
        builder.ToTable("PaymentDiscrepancies");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Provider)
            .IsRequired();

        builder.Property(d => d.Type)
            .IsRequired();

        builder.Property(d => d.Status)
            .IsRequired();

        builder.Property(d => d.InternalAmount)
            .HasPrecision(18, 2);

        builder.Property(d => d.InternalCurrency)
            .HasMaxLength(3);

        builder.Property(d => d.InternalStatus)
            .HasMaxLength(50);

        builder.Property(d => d.ExternalAmount)
            .HasPrecision(18, 2);

        builder.Property(d => d.ExternalCurrency)
            .HasMaxLength(10);

        builder.Property(d => d.ExternalStatus)
            .HasMaxLength(50);

        builder.Property(d => d.ExternalTransactionId)
            .HasMaxLength(150);

        builder.Property(d => d.ResolutionNotes)
            .HasMaxLength(1000);

        builder.HasIndex(d => d.Status);

        // Una sola discrepancia abierta por causa: repetir un webhook o una conciliación no las duplica
        builder.HasIndex(d => new { d.PaymentId, d.Type })
            .IsUnique()
            .HasDatabaseName("UX_PaymentDiscrepancies_Open_Payment")
            .HasFilter("\"Status\" = 1 AND \"PaymentId\" IS NOT NULL");

        builder.HasIndex(d => new { d.Provider, d.ExternalTransactionId, d.Type })
            .IsUnique()
            .HasDatabaseName("UX_PaymentDiscrepancies_Open_Unknown")
            .HasFilter("\"Status\" = 1 AND \"PaymentId\" IS NULL");

        builder.HasOne(d => d.Payment)
            .WithMany()
            .HasForeignKey(d => d.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
