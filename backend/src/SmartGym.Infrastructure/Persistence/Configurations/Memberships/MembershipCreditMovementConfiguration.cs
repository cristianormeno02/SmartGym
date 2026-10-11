using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Memberships;

namespace SmartGym.Infrastructure.Persistence.Configurations.Memberships;

public class MembershipCreditMovementConfiguration : IEntityTypeConfiguration<MembershipCreditMovement>
{
    public void Configure(EntityTypeBuilder<MembershipCreditMovement> builder)
    {
        builder.ToTable("MembershipCreditMovements");

        builder.HasKey(cm => cm.Id);

        builder.Property(cm => cm.Type)
            .IsRequired();

        builder.Property(cm => cm.Amount)
            .IsRequired();

        builder.Property(cm => cm.BalanceAfter)
            .IsRequired();

        builder.Property(cm => cm.Reason)
            .IsRequired()
            .HasMaxLength(250);

        builder.HasIndex(cm => cm.StudentId);
        builder.HasIndex(cm => cm.MembershipId);
        builder.HasIndex(cm => cm.ClassSessionId);

        builder.HasIndex(cm => new { cm.ReservationId, cm.Type })
            .HasDatabaseName("UX_CreditMovements_Reservation_Type")
            .IsUnique()
            .HasFilter("\"ReservationId\" IS NOT NULL AND \"Type\" IN (2, 3)");

        builder.HasOne(cm => cm.Membership)
            .WithMany(m => m.CreditMovements)
            .HasForeignKey(cm => cm.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cm => cm.Student)
            .WithMany()
            .HasForeignKey(cm => cm.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cm => cm.ClassSession)
            .WithMany()
            .HasForeignKey(cm => cm.ClassSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(cm => cm.Reservation)
            .WithMany()
            .HasForeignKey(cm => cm.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cm => cm.Activity)
            .WithMany()
            .HasForeignKey(cm => cm.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
