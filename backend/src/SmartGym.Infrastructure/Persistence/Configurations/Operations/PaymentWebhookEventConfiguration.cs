using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable("PaymentWebhookEvents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider)
            .IsRequired();

        builder.Property(e => e.ExternalEventId)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(e => e.ExternalTransactionId)
            .HasMaxLength(150);

        builder.Property(e => e.EventType)
            .HasMaxLength(100);

        builder.Property(e => e.Status)
            .IsRequired();

        builder.Property(e => e.Detail)
            .HasMaxLength(500);

        // Deduplica las notificaciones repetidas del proveedor
        builder.HasIndex(e => new { e.Provider, e.ExternalEventId })
            .IsUnique()
            .HasDatabaseName("UX_PaymentWebhookEvents_Provider_ExternalEventId");

        builder.HasIndex(e => e.PaymentId);

        builder.HasOne(e => e.Payment)
            .WithMany()
            .HasForeignKey(e => e.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
