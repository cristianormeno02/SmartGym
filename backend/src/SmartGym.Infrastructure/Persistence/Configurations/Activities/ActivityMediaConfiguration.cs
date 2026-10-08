using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Application.Modules.Activities;
using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Infrastructure.Persistence.Configurations.Activities;

public class ActivityMediaConfiguration : IEntityTypeConfiguration<ActivityMedia>
{
    public void Configure(EntityTypeBuilder<ActivityMedia> builder)
    {
        builder.ToTable("ActivityMedias", t =>
        {
            t.HasCheckConstraint("CK_ActivityMedias_SizeBytes", "\"SizeBytes\" > 0");
            t.HasCheckConstraint("CK_ActivityMedias_LogoNotPrimary", "\"Type\" = 2 OR \"IsPrimary\" = FALSE");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ObjectKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasIndex(m => m.ObjectKey)
            .IsUnique();

        builder.Property(m => m.OriginalFileName)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(m => m.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.SizeBytes)
            .IsRequired();

        builder.Property(m => m.Width)
            .IsRequired();

        builder.Property(m => m.Height)
            .IsRequired();

        builder.Property(m => m.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.IsPrimary)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.Type)
            .IsRequired();

        builder.HasIndex(m => new { m.ActivityId, m.SortOrder });

        // A lo sumo una imagen principal de galería por actividad
        builder.HasIndex(m => m.ActivityId, ActivityConstraintNames.SinglePrimaryImage)
            .HasFilter("\"IsPrimary\" = TRUE AND \"Type\" = 2")
            .IsUnique();

        // A lo sumo un logo por actividad
        builder.HasIndex(m => m.ActivityId, ActivityConstraintNames.SingleLogo)
            .HasFilter("\"Type\" = 1")
            .IsUnique();
    }
}
