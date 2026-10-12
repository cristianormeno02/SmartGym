using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Code)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(t => t.Type)
            .IsRequired();

        builder.Property(t => t.Channel)
            .IsRequired();

        builder.Property(t => t.Locale)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("es-AR");

        builder.Property(t => t.SubjectTemplate)
            .HasMaxLength(200);

        builder.Property(t => t.BodyTemplate)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(t => t.AllowedVariables)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(t => t.Version)
            .IsRequired();

        builder.Property(t => t.Status)
            .IsRequired();

        builder.Property(t => t.VersionToken)
            .IsRowVersion();

        builder.HasIndex(t => new { t.Code, t.Channel, t.Locale, t.Version })
            .IsUnique()
            .HasDatabaseName("UX_NotificationTemplates_Code_Channel_Locale_Version");

        builder.HasIndex(t => new { t.Type, t.Channel, t.Locale })
            .IsUnique()
            .HasDatabaseName("UX_NotificationTemplates_Active_Type_Channel_Locale")
            .HasFilter("\"Status\" = 2 AND \"IsActive\"");
    }
}
