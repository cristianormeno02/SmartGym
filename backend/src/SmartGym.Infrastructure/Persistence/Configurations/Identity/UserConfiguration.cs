using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Identity;

namespace SmartGym.Infrastructure.Persistence.Configurations.Identity;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(256);

        builder.HasIndex(u => u.Username)
            .IsUnique();

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500);

        builder.Property(u => u.GoogleSubjectId)
            .HasMaxLength(150);

        builder.HasIndex(u => u.GoogleSubjectId)
            .IsUnique()
            .HasFilter("\"GoogleSubjectId\" IS NOT NULL");
    }
}
