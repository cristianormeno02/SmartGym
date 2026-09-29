using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Promotions;

namespace SmartGym.Infrastructure.Persistence.Configurations.Promotions;

public class FamilyGroupMemberConfiguration : IEntityTypeConfiguration<FamilyGroupMember>
{
    public void Configure(EntityTypeBuilder<FamilyGroupMember> builder)
    {
        builder.ToTable("FamilyGroupMembers");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Relationship)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(m => m.PersonId);
        builder.HasIndex(m => new { m.FamilyGroupId, m.PersonId });

        builder.HasOne(m => m.Person)
            .WithMany()
            .HasForeignKey(m => m.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
