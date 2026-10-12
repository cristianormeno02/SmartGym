using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Reservations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Reservations;

public class ReservationStatusChangeConfiguration : IEntityTypeConfiguration<ReservationStatusChange>
{
    public void Configure(EntityTypeBuilder<ReservationStatusChange> builder)
    {
        builder.ToTable("ReservationStatusChanges");

        builder.HasKey(sc => sc.Id);
        builder.Property(sc => sc.Id).ValueGeneratedNever();

        builder.Property(sc => sc.NewStatus)
            .IsRequired();

        builder.Property(sc => sc.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(sc => sc.Trigger)
            .IsRequired();

        builder.Property(sc => sc.ChangedAtUtc)
            .IsRequired();

        builder.HasIndex(sc => new { sc.ReservationId, sc.ChangedAtUtc });

        builder.HasOne(sc => sc.Reservation)
            .WithMany(r => r.StatusChanges)
            .HasForeignKey(sc => sc.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sc => sc.ChangedByUser)
            .WithMany()
            .HasForeignKey(sc => sc.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
