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

        builder.Property(m => m.Status)
            .IsRequired();

        builder.Property(m => m.StartDate)
            .IsRequired();

        builder.Property(m => m.EndDate)
            .IsRequired();

        builder.Property(m => m.PurchasedAtUtc)
            .IsRequired();

        builder.Property(m => m.DurationDaysSnapshot)
            .IsRequired();

        builder.Property(m => m.InitialCredits)
            .IsRequired();

        builder.Property(m => m.ExtraCreditsGranted)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(m => m.TotalCredits)
            .IsRequired();

        builder.Property(m => m.AvailableCredits)
            .IsRequired();

        builder.Property(m => m.IsUnlimited)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.AppliesToAllActivities)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(m => m.InitialCreditsLoaded)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(m => m.OriginalPrice)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(m => m.DiscountAmount)
            .IsRequired()
            .HasPrecision(18, 2)
            .HasDefaultValue(0m);

        builder.Property(m => m.PurchasePrice)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(m => m.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("ARS");

        builder.Property(m => m.CancellationReason)
            .HasMaxLength(500);

        builder.Property(m => m.Notes)
            .HasMaxLength(1000);

        builder.Property(m => m.IdempotencyKey)
            .HasMaxLength(100);

        builder.Property(m => m.Version)
            .IsRowVersion();

        // Check constraints
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Memberships_Dates",
                "\"EndDate\" >= \"StartDate\" AND \"DurationDaysSnapshot\" >= 1");

            t.HasCheckConstraint("CK_Memberships_Status",
                "\"Status\" IN (0, 1, 2, 3, 4)");

            t.HasCheckConstraint("CK_Memberships_Prices",
                "\"OriginalPrice\" >= 0 AND \"DiscountAmount\" >= 0 AND \"PurchasePrice\" >= 0");

            t.HasCheckConstraint("CK_Memberships_Credits",
                "\"InitialCredits\" >= 0 AND \"ExtraCreditsGranted\" >= 0 AND \"TotalCredits\" >= 0 AND \"AvailableCredits\" >= 0");

            t.HasCheckConstraint("CK_Memberships_Cancellation",
                "((\"Status\" = 4) = (\"CancelledAtUtc\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL))");
        });

        // Indexes
        builder.HasIndex(m => new { m.StudentId, m.IdempotencyKey })
            .HasDatabaseName("UX_Memberships_Student_IdempotencyKey")
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        builder.HasIndex(m => new { m.StudentId, m.Status, m.EndDate })
            .HasDatabaseName("IX_Memberships_Student_Status_EndDate")
            .IsDescending(false, false, true);

        builder.HasIndex(m => new { m.Status, m.EndDate })
            .HasDatabaseName("IX_Memberships_Status_EndDate");

        builder.HasIndex(m => m.MembershipPlanId)
            .HasDatabaseName("IX_Memberships_MembershipPlanId");

        builder.HasIndex(m => m.PurchasedAtUtc)
            .HasDatabaseName("IX_Memberships_PurchasedAtUtc")
            .IsDescending(true);

        // Foreign keys (all Restrict on Membership)
        builder.HasOne(m => m.Student)
            .WithMany()
            .HasForeignKey(m => m.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.MembershipPlan)
            .WithMany()
            .HasForeignKey(m => m.MembershipPlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.RenewedFromMembership)
            .WithMany()
            .HasForeignKey(m => m.RenewedFromMembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.Promotion)
            .WithMany()
            .HasForeignKey(m => m.PromotionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.PromotionRedemption)
            .WithMany()
            .HasForeignKey(m => m.PromotionRedemptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.CreatedByUser)
            .WithMany()
            .HasForeignKey(m => m.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(m => m.CancelledByUser)
            .WithMany()
            .HasForeignKey(m => m.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(m => m.AllowedActivities)
            .WithOne(aa => aa.Membership)
            .HasForeignKey(aa => aa.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.StatusChanges)
            .WithOne(sc => sc.Membership)
            .HasForeignKey(sc => sc.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Suspensions)
            .WithOne(s => s.Membership)
            .HasForeignKey(s => s.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.CreditMovements)
            .WithOne(cm => cm.Membership)
            .HasForeignKey(cm => cm.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
