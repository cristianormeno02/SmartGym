using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Infrastructure.Persistence.Configurations.Memberships;

public class MembershipSuspensionConfiguration : IEntityTypeConfiguration<MembershipSuspension>
{
    public void Configure(EntityTypeBuilder<MembershipSuspension> builder)
    {
        builder.ToTable("MembershipSuspensions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.StartDate)
            .IsRequired();

        builder.Property(s => s.ExtendExpirationDate)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(s => s.ExtensionDaysApplied)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(s => s.Notes)
            .HasMaxLength(1000);

        builder.Property(s => s.SuspendedByUserId)
            .IsRequired();

        builder.Property(s => s.SuspendedAtUtc)
            .IsRequired();

        builder.Property(s => s.IsActiveSuspension)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(s => new { s.MembershipId, s.IsActiveSuspension });

        builder.HasOne(s => s.Membership)
            .WithMany(m => m.Suspensions)
            .HasForeignKey(s => s.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.SuspendedByUser)
            .WithMany()
            .HasForeignKey(s => s.SuspendedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.ReactivatedByUser)
            .WithMany()
            .HasForeignKey(s => s.ReactivatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
