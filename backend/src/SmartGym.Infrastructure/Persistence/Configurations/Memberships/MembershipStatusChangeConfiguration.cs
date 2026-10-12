using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Infrastructure.Persistence.Configurations.Memberships;

public class MembershipStatusChangeConfiguration : IEntityTypeConfiguration<MembershipStatusChange>
{
    public void Configure(EntityTypeBuilder<MembershipStatusChange> builder)
    {
        builder.ToTable("MembershipStatusChanges");

        builder.HasKey(sc => sc.Id);
        builder.Property(sc => sc.Id).ValueGeneratedNever();

        builder.Property(sc => sc.NewStatus)
            .IsRequired();

        builder.Property(sc => sc.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(sc => sc.Notes)
            .HasMaxLength(1000);

        builder.Property(sc => sc.ChangedAtUtc)
            .IsRequired();

        builder.HasIndex(sc => new { sc.MembershipId, sc.ChangedAtUtc });

        builder.HasOne(sc => sc.Membership)
            .WithMany(m => m.StatusChanges)
            .HasForeignKey(sc => sc.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(sc => sc.ChangedByUser)
            .WithMany()
            .HasForeignKey(sc => sc.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
