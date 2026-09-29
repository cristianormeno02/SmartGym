using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Promotions;

namespace SmartGym.Infrastructure.Persistence.Configurations.Promotions;

public class PromotionConfiguration : IEntityTypeConfiguration<Promotion>
{
    public void Configure(EntityTypeBuilder<Promotion> builder)
    {
        builder.ToTable("Promotions");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(p => p.Code)
            .IsUnique();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(p => p.Description)
            .HasMaxLength(1000);

        builder.Property(p => p.ConditionType)
            .IsRequired();

        builder.Property(p => p.BenefitType)
            .IsRequired();

        builder.Property(p => p.BenefitValue)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.ValidFrom)
            .IsRequired();

        builder.HasOne(p => p.ApplicablePlan)
            .WithMany()
            .HasForeignKey(p => p.ApplicablePlanId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(p => p.Redemptions)
            .WithOne(r => r.Promotion)
            .HasForeignKey(r => r.PromotionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
