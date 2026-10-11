using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Reservations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Reservations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .IsRequired();

        builder.Property(r => r.Origin)
            .IsRequired();

        builder.Property(r => r.AccessType)
            .IsRequired();

        builder.Property(r => r.ReservedAtUtc)
            .IsRequired();

        builder.Property(r => r.RequestedPromotionCode)
            .HasMaxLength(50);

        builder.Property(r => r.CancellationReason)
            .HasMaxLength(500);

        builder.Property(r => r.IsLateCancellation)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.IsPolicyOverride)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.PolicyOverrideReason)
            .HasMaxLength(500);

        builder.Property(r => r.AffectedBySessionChange)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.Notes)
            .HasMaxLength(1000);

        builder.Property(r => r.IdempotencyKey)
            .HasMaxLength(100);

        builder.Property(r => r.Version)
            .IsRowVersion();

        // Check constraints
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Reservations_Status",
                "\"Status\" IN (1, 3, 4, 5, 6, 7)");

            t.HasCheckConstraint("CK_Reservations_Origin",
                "\"Origin\" IN (1, 2, 3)");

            t.HasCheckConstraint("CK_Reservations_AccessType",
                "\"AccessType\" IN (1, 2, 3, 4, 5)");

            t.HasCheckConstraint("CK_Reservations_WaitlistPosition",
                "((\"Status\" = 6) = (\"WaitListPosition\" IS NOT NULL AND \"WaitListPosition\" >= 1))");

            t.HasCheckConstraint("CK_Reservations_PendingOffer",
                "((\"Status\" = 7) = (\"WaitlistOfferExpiresAtUtc\" IS NOT NULL))");

            t.HasCheckConstraint("CK_Reservations_Cancellation",
                "((\"Status\" = 3) = (\"CancelledAtUtc\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL))");

            t.HasCheckConstraint("CK_Reservations_PolicyOverride",
                "(NOT \"IsPolicyOverride\" OR \"PolicyOverrideReason\" IS NOT NULL)");
        });

        // Partial unique indexes
        builder.HasIndex(r => new { r.ClassSessionId, r.StudentId })
            .HasDatabaseName("UX_Reservations_Active_Session_Student")
            .IsUnique()
            .HasFilter("\"Status\" IN (1, 6, 7) AND \"IsActive\"");

        builder.HasIndex(r => new { r.StudentId, r.IdempotencyKey })
            .HasDatabaseName("UX_Reservations_Student_IdempotencyKey")
            .IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL");

        builder.HasIndex(r => r.SingleClassTicketMovementId)
            .HasDatabaseName("UX_Reservations_Held_AccessMovement")
            .IsUnique()
            .HasFilter("\"SingleClassTicketMovementId\" IS NOT NULL AND \"IsActive\" AND (\"Status\" IN (1, 4, 5, 7) OR (\"Status\" = 3 AND \"IsLateCancellation\"))");

        builder.HasIndex(r => r.PromotionRedemptionId)
            .HasDatabaseName("UX_Reservations_PromotionRedemption")
            .IsUnique()
            .HasFilter("\"PromotionRedemptionId\" IS NOT NULL");

        // Operational indexes
        builder.HasIndex(r => new { r.StudentId, r.Status, r.ReservedAtUtc })
            .HasDatabaseName("IX_Reservations_Student_Status_ReservedAt")
            .IsDescending(false, false, true);

        builder.HasIndex(r => new { r.ClassSessionId, r.Status })
            .HasDatabaseName("IX_Reservations_Session_Status");

        builder.HasIndex(r => new { r.ClassSessionId, r.WaitListPosition })
            .HasDatabaseName("IX_Reservations_Session_Waitlist")
            .HasFilter("\"Status\" = 6 AND \"IsActive\"");

        builder.HasIndex(r => r.WaitlistOfferExpiresAtUtc)
            .HasDatabaseName("IX_Reservations_PendingOffers")
            .HasFilter("\"Status\" = 7 AND \"IsActive\"");

        // Foreign keys (all Restrict)
        builder.HasOne(r => r.ClassSession)
            .WithMany()
            .HasForeignKey(r => r.ClassSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Student)
            .WithMany()
            .HasForeignKey(r => r.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Membership)
            .WithMany()
            .HasForeignKey(r => r.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CreditMovement)
            .WithMany()
            .HasForeignKey(r => r.CreditMovementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.RefundCreditMovement)
            .WithMany()
            .HasForeignKey(r => r.RefundCreditMovementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.SingleClassTicketMovement)
            .WithMany()
            .HasForeignKey(r => r.SingleClassTicketMovementId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Payment)
            .WithMany()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.PromotionRedemption)
            .WithMany()
            .HasForeignKey(r => r.PromotionRedemptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CreatedByUser)
            .WithMany()
            .HasForeignKey(r => r.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CancelledByUser)
            .WithMany()
            .HasForeignKey(r => r.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.CheckedInByUser)
            .WithMany()
            .HasForeignKey(r => r.CheckedInByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.RescheduledToSession)
            .WithMany()
            .HasForeignKey(r => r.RescheduledToSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.StatusChanges)
            .WithOne(sc => sc.Reservation)
            .HasForeignKey(sc => sc.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Metadata.FindNavigation(nameof(Reservation.StatusChanges))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
