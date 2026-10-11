using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Promotions;

namespace SmartGym.Infrastructure.Persistence.Configurations.Promotions;

public class PromotionRedemptionConfiguration : IEntityTypeConfiguration<PromotionRedemption>
{
    public void Configure(EntityTypeBuilder<PromotionRedemption> builder)
    {
        builder.ToTable("PromotionRedemptions");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.OriginalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.DiscountAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.FinalAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.RedeemedAtUtc)
            .IsRequired();

        builder.HasIndex(r => r.PromotionId);
        builder.HasIndex(r => r.PersonId);
        builder.HasIndex(r => new { r.PromotionId, r.PersonId });

        builder.HasIndex(r => r.ReservationId)
            .HasDatabaseName("UX_PromotionRedemptions_Reservation")
            .IsUnique()
            .HasFilter("\"ReservationId\" IS NOT NULL");

        builder.HasOne(r => r.Person)
            .WithMany()
            .HasForeignKey(r => r.PersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Membership)
            .WithMany()
            .HasForeignKey(r => r.MembershipId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.Reservation)
            .WithMany()
            .HasForeignKey(r => r.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
