using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Infrastructure.Persistence.Configurations.Memberships;

public class MembershipPlanConfiguration : IEntityTypeConfiguration<MembershipPlan>
{
    public void Configure(EntityTypeBuilder<MembershipPlan> builder)
    {
        builder.ToTable("MembershipPlans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.DurationDays)
            .IsRequired();

        builder.Property(p => p.Price)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.Credits)
            .IsRequired();

        builder.Property(p => p.IsUnlimited)
            .IsRequired();

        builder.Property(p => p.AppliesToAllActivities)
            .IsRequired();

        builder.HasMany(p => p.AllowedActivities)
            .WithOne(pa => pa.MembershipPlan)
            .HasForeignKey(pa => pa.MembershipPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
