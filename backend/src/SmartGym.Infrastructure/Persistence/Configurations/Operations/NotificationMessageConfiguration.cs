using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Enums;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class NotificationMessageConfiguration : IEntityTypeConfiguration<NotificationMessage>
{
    public void Configure(EntityTypeBuilder<NotificationMessage> builder)
    {
        builder.ToTable("NotificationMessages");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Category)
            .IsRequired()
            .HasDefaultValue(NotificationCategory.Reservations);

        builder.Property(n => n.Type)
            .IsRequired();

        builder.Property(n => n.Channel)
            .IsRequired()
            .HasDefaultValue(NotificationChannel.InApp);

        builder.Property(n => n.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(n => n.RelatedEntityType)
            .HasMaxLength(80);

        builder.Property(n => n.ActionUrl)
            .HasMaxLength(500);

        builder.Property(n => n.MetadataJson)
            .HasMaxLength(2000);

        builder.Property(n => n.IdempotencyKey)
            .HasMaxLength(180);

        builder.Property(n => n.CorrelationId)
            .HasMaxLength(100);

        builder.HasIndex(n => n.RecipientPersonId);
        builder.HasIndex(n => new { n.RecipientPersonId, n.IsRead });
        builder.HasIndex(n => new { n.RecipientPersonId, n.SentAtUtc })
            .HasDatabaseName("IX_NotificationMessages_Recipient_SentAtUtc");
        builder.HasIndex(n => new { n.RecipientPersonId, n.Category, n.SentAtUtc })
            .HasDatabaseName("IX_NotificationMessages_Recipient_Category_SentAtUtc");

        builder.HasIndex(n => new { n.RecipientPersonId, n.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("UX_NotificationMessages_Recipient_IdempotencyKey")
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        builder.HasOne(n => n.RecipientPerson)
            .WithMany()
            .HasForeignKey(n => n.RecipientPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(n => n.Deliveries)
            .WithOne(d => d.Notification)
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
