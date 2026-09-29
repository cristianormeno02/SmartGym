using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartGym.Domain.Entities.Activities;

namespace SmartGym.Infrastructure.Persistence.Configurations.Activities;

public class RecurringScheduleConfiguration : IEntityTypeConfiguration<RecurringSchedule>
{
    public void Configure(EntityTypeBuilder<RecurringSchedule> builder)
    {
        builder.ToTable("RecurringSchedules");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.DayOfWeek)
            .IsRequired();

        builder.Property(s => s.StartTime)
            .IsRequired();

        builder.Property(s => s.EndTime)
            .IsRequired();

        builder.Property(s => s.ValidFrom)
            .IsRequired();

        builder.HasOne(s => s.Activity)
            .WithMany()
            .HasForeignKey(s => s.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Room)
            .WithMany()
            .HasForeignKey(s => s.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Instructor)
            .WithMany()
            .HasForeignKey(s => s.InstructorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
