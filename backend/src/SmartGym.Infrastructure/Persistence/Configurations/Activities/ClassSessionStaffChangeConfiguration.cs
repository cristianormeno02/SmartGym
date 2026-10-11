using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Infrastructure.Persistence.Configurations.Activities;

public class ClassSessionStaffChangeConfiguration : IEntityTypeConfiguration<ClassSessionStaffChange>
{
    public void Configure(EntityTypeBuilder<ClassSessionStaffChange> builder)
    {
        builder.ToTable("ClassSessionStaffChanges");

        builder.HasKey(sc => sc.Id);

        builder.Property(sc => sc.Role)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(sc => sc.Reason)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(sc => sc.ChangedAtUtc)
            .IsRequired();

        builder.HasOne(sc => sc.ClassSession)
            .WithMany(cs => cs.StaffChanges)
            .HasForeignKey(sc => sc.ClassSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sc => sc.PreviousPerson)
            .WithMany()
            .HasForeignKey(sc => sc.PreviousPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sc => sc.NewPerson)
            .WithMany()
            .HasForeignKey(sc => sc.NewPersonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(sc => sc.ChangedByUser)
            .WithMany()
            .HasForeignKey(sc => sc.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ClassSessionStaffChanges_Role",
                "\"Role\" IN ('Instructor', 'Secretary')");
        });

        builder.HasIndex(sc => sc.ClassSessionId);
    }
}
