using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Promotions;

namespace SmartGym.Infrastructure.Persistence.Configurations.Promotions;

public class FamilyGroupConfiguration : IEntityTypeConfiguration<FamilyGroup>
{
    public void Configure(EntityTypeBuilder<FamilyGroup> builder)
    {
        builder.ToTable("FamilyGroups");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.HasOne(g => g.PrimaryContact)
            .WithMany()
            .HasForeignKey(g => g.PrimaryContactId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(g => g.Members)
            .WithOne(m => m.FamilyGroup)
            .HasForeignKey(m => m.FamilyGroupId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
