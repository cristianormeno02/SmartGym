using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Category)
            .IsRequired();

        builder.Property(p => p.Channel)
            .IsRequired();

        builder.Property(p => p.Enabled)
            .IsRequired();

        builder.HasIndex(p => new { p.PersonId, p.Category, p.Channel })
            .IsUnique()
            .HasDatabaseName("UX_NotificationPreferences_Person_Category_Channel");

        builder.HasOne(p => p.Person)
            .WithMany()
            .HasForeignKey(p => p.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
