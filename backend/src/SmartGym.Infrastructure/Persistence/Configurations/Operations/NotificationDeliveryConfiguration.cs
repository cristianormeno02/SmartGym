using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries", t =>
        {
            t.HasCheckConstraint(
                "CK_NotificationDeliveries_Attempts",
                "\"AttemptCount\" >= 0 AND \"MaxAttempts\" >= 1");
        });

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Channel)
            .IsRequired();

        builder.Property(d => d.Provider)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(d => d.RecipientAddress)
            .HasMaxLength(256);

        builder.Property(d => d.Status)
            .IsRequired();

        builder.Property(d => d.AttemptCount)
            .IsRequired();

        builder.Property(d => d.MaxAttempts)
            .IsRequired();

        builder.Property(d => d.ProviderMessageId)
            .HasMaxLength(200);

        builder.Property(d => d.LastErrorCode)
            .HasMaxLength(80);

        builder.Property(d => d.LastErrorMessage)
            .HasMaxLength(1000);

        builder.Property(d => d.RetryReason)
            .HasMaxLength(500);

        builder.HasIndex(d => new { d.NotificationId, d.Channel })
            .IsUnique()
            .HasDatabaseName("UX_NotificationDeliveries_Notification_Channel");

        builder.HasIndex(d => new { d.Status, d.NextAttemptAtUtc })
            .HasDatabaseName("IX_NotificationDeliveries_Status_NextAttemptAtUtc");
    }
}
