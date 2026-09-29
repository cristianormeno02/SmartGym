using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Operations;

namespace SmartGym.Infrastructure.Persistence.Configurations.Operations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.Method)
            .IsRequired();

        builder.Property(p => p.Status)
            .IsRequired();

        builder.Property(p => p.ExternalTransactionId)
            .HasMaxLength(150);

        builder.Property(p => p.ReceiptUrl)
            .HasMaxLength(500);

        builder.Property(p => p.Notes)
            .HasMaxLength(500);

        builder.HasIndex(p => p.PayerPersonId);
        builder.HasIndex(p => p.MembershipId);
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.PaidAtUtc);

        builder.HasOne(p => p.PayerPerson)
            .WithMany()
            .HasForeignKey(p => p.PayerPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.Membership)
            .WithMany()
            .HasForeignKey(p => p.MembershipId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.ClassSession)
            .WithMany()
            .HasForeignKey(p => p.ClassSessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
