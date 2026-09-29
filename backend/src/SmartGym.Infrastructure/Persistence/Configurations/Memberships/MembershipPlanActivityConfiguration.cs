using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Infrastructure.Persistence.Configurations.Memberships;

public class MembershipPlanActivityConfiguration : IEntityTypeConfiguration<MembershipPlanActivity>
{
    public void Configure(EntityTypeBuilder<MembershipPlanActivity> builder)
    {
        builder.ToTable("MembershipPlanActivities");

        builder.HasKey(pa => new { pa.MembershipPlanId, pa.ActivityId });

        builder.HasOne(pa => pa.MembershipPlan)
            .WithMany(p => p.AllowedActivities)
            .HasForeignKey(pa => pa.MembershipPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(pa => pa.Activity)
            .WithMany()
            .HasForeignKey(pa => pa.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
