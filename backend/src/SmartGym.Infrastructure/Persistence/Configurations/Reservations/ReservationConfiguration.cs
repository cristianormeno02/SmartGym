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

        builder.Property(r => r.ReservedAtUtc)
            .IsRequired();

        builder.Property(r => r.CancellationReason)
            .HasMaxLength(500);

        builder.HasIndex(r => r.StudentId);
        builder.HasIndex(r => r.ClassSessionId);
        builder.HasIndex(r => new { r.ClassSessionId, r.StudentId });
        builder.HasIndex(r => new { r.ClassSessionId, r.Status });

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
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.CreditMovement)
            .WithMany()
            .HasForeignKey(r => r.CreditMovementId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.RefundCreditMovement)
            .WithMany()
            .HasForeignKey(r => r.RefundCreditMovementId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
