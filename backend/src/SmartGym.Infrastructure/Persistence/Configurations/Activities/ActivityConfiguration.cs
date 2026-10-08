using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Application.Modules.Activities;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Enums;

namespace SmartGym.Infrastructure.Persistence.Configurations.Activities;

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("Activities", t =>
        {
            t.HasCheckConstraint("CK_Activities_Code_Format", "\"Code\" ~ '^[A-Z0-9_]{3,50}$'");
            t.HasCheckConstraint("CK_Activities_Capacity", "\"DefaultCapacity\" IS NULL OR \"DefaultCapacity\" > 0");
            t.HasCheckConstraint(
                "CK_Activities_Age",
                "(\"MinAge\" IS NULL OR \"MinAge\" >= 0) AND " +
                "(\"MaxAge\" IS NULL OR \"MaxAge\" >= 0) AND " +
                "(\"MinAge\" IS NULL OR \"MaxAge\" IS NULL OR \"MinAge\" <= \"MaxAge\")");
            t.HasCheckConstraint("CK_Activities_ColorHex", "\"ColorHex\" IS NULL OR \"ColorHex\" ~ '^#[0-9A-F]{6}$'");
            t.HasCheckConstraint("CK_Activities_Status_IsActive", "(\"Status\" = 1 AND \"IsActive\" = TRUE) OR (\"Status\" <> 1 AND \"IsActive\" = FALSE)");
        });

        builder.HasKey(a => a.Id);

        // Concurrencia optimista con PostgreSQL xmin
        builder.Property(a => a.Version).IsRowVersion();

        // IsActive se deriva de Status: EF completa _isActiveColumn al materializar para no invocar el setter
        builder.Property(a => a.IsActive)
            .HasField("_isActiveColumn")
            .UsePropertyAccessMode(PropertyAccessMode.FieldDuringConstruction);

        builder.Property(a => a.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(a => a.Code)
            .IsUnique()
            .HasDatabaseName(ActivityConstraintNames.CodeUnique);

        builder.Property(a => a.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.NormalizedName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.ShortDescription)
            .HasMaxLength(250);

        builder.Property(a => a.Description)
            .HasMaxLength(2000);

        builder.Property(a => a.EquipmentNotes)
            .HasMaxLength(500);

        builder.Property(a => a.ColorHex)
            .HasMaxLength(7);

        builder.Property(a => a.Status)
            .IsRequired();

        builder.HasIndex(a => a.Status);

        builder.HasMany(a => a.Media)
            .WithOne(m => m.Activity)
            .HasForeignKey(m => m.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
