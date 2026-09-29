using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class NotificationMessageConfiguration : IEntityTypeConfiguration<NotificationMessage>
{
    public void Configure(EntityTypeBuilder<NotificationMessage> builder)
    {
        builder.ToTable("NotificationMessages");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(n => n.Body)
            .IsRequired()
            .HasMaxLength(2000);

        builder.HasIndex(n => n.RecipientPersonId);
        builder.HasIndex(n => new { n.RecipientPersonId, n.IsRead });

        builder.HasOne(n => n.RecipientPerson)
            .WithMany()
            .HasForeignKey(n => n.RecipientPersonId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
