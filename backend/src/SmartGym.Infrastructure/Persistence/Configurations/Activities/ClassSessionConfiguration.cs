using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Infrastructure.Persistence.Configurations.Activities;

public class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
{
    public void Configure(EntityTypeBuilder<ClassSession> builder)
    {
        builder.ToTable("ClassSessions");

        builder.HasKey(cs => cs.Id);

        builder.Property(cs => cs.Date)
            .IsRequired();

        builder.Property(cs => cs.StartTime)
            .IsRequired();

        builder.Property(cs => cs.EndTime)
            .IsRequired();

        builder.Property(cs => cs.MaxCapacity)
            .IsRequired();

        builder.Property(cs => cs.ReservedCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(cs => cs.AttendedCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(cs => cs.Status)
            .IsRequired();

        builder.Property(cs => cs.CancellationReason)
            .HasMaxLength(500);

        // Índices para búsquedas eficientes por fecha y estado
        builder.HasIndex(cs => cs.Date);
        builder.HasIndex(cs => new { cs.Date, cs.Status });
        builder.HasIndex(cs => new { cs.RecurringScheduleId, cs.Date });

        builder.HasOne(cs => cs.RecurringSchedule)
            .WithMany()
            .HasForeignKey(cs => cs.RecurringScheduleId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(cs => cs.Activity)
            .WithMany()
            .HasForeignKey(cs => cs.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.Room)
            .WithMany()
            .HasForeignKey(cs => cs.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.Instructor)
            .WithMany()
            .HasForeignKey(cs => cs.InstructorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.OriginalInstructor)
            .WithMany()
            .HasForeignKey(cs => cs.OriginalInstructorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.SubstituteInstructor)
            .WithMany()
            .HasForeignKey(cs => cs.SubstituteInstructorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
