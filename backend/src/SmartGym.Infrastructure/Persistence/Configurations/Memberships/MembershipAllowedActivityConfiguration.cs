using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Infrastructure.Persistence.Configurations.Memberships;

public class MembershipAllowedActivityConfiguration : IEntityTypeConfiguration<MembershipAllowedActivity>
{
    public void Configure(EntityTypeBuilder<MembershipAllowedActivity> builder)
    {
        builder.ToTable("MembershipAllowedActivities");

        builder.HasKey(aa => new { aa.MembershipId, aa.ActivityId });

        builder.HasOne(aa => aa.Membership)
            .WithMany(m => m.AllowedActivities)
            .HasForeignKey(aa => aa.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(aa => aa.Activity)
            .WithMany()
            .HasForeignKey(aa => aa.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
