using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Infrastructure.Persistence.Configurations.Memberships;

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("Memberships");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.StartDate)
            .IsRequired();

        builder.Property(m => m.EndDate)
            .IsRequired();

        builder.Property(m => m.TotalCredits)
            .IsRequired();

        builder.Property(m => m.AvailableCredits)
            .IsRequired();

        builder.Property(m => m.Status)
            .IsRequired();

        builder.HasIndex(m => m.StudentId);
        builder.HasIndex(m => new { m.StudentId, m.Status });

        builder.HasOne(m => m.Student)
            .WithMany()
            .HasForeignKey(m => m.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.MembershipPlan)
            .WithMany()
            .HasForeignKey(m => m.MembershipPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.CreditMovements)
            .WithOne(cm => cm.Membership)
            .HasForeignKey(cm => cm.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
