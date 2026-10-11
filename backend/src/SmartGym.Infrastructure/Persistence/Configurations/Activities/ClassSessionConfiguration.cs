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

        builder.Property(cs => cs.StartsAtUtc)
            .IsRequired();

        builder.Property(cs => cs.EndsAtUtc)
            .IsRequired();

        builder.Property(cs => cs.Capacity)
            .IsRequired();

        builder.Property(cs => cs.ReservedCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(cs => cs.AttendedCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(cs => cs.Status)
            .IsRequired();

        builder.Property(cs => cs.IsDetachedFromSchedule)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(cs => cs.Notes)
            .HasMaxLength(2000);

        builder.Property(cs => cs.SuspensionReason)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(cs => cs.SuspensionObservations)
            .HasMaxLength(1000);

        builder.Property(cs => cs.CancellationReason)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(cs => cs.CancellationObservations)
            .HasMaxLength(1000);

        builder.Property(cs => cs.Version)
            .IsRowVersion();

        // Foreign keys
        builder.HasOne(cs => cs.RecurringSchedule)
            .WithMany()
            .HasForeignKey(cs => cs.RecurringScheduleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.Activity)
            .WithMany()
            .HasForeignKey(cs => cs.ActivityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.Room)
            .WithMany()
            .HasForeignKey(cs => cs.RoomId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.ScheduledInstructor)
            .WithMany()
            .HasForeignKey(cs => cs.ScheduledInstructorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.EffectiveInstructor)
            .WithMany()
            .HasForeignKey(cs => cs.EffectiveInstructorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.ScheduledSecretary)
            .WithMany()
            .HasForeignKey(cs => cs.ScheduledSecretaryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.EffectiveSecretary)
            .WithMany()
            .HasForeignKey(cs => cs.EffectiveSecretaryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.SuspendedByUser)
            .WithMany()
            .HasForeignKey(cs => cs.SuspendedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.CancelledByUser)
            .WithMany()
            .HasForeignKey(cs => cs.CancelledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.RescheduledFromSession)
            .WithMany()
            .HasForeignKey(cs => cs.RescheduledFromSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(cs => cs.RescheduledToSession)
            .WithMany()
            .HasForeignKey(cs => cs.RescheduledToSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(cs => cs.StaffChanges)
            .WithOne(sc => sc.ClassSession)
            .HasForeignKey(sc => sc.ClassSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Check constraints
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ClassSessions_Dates",
                "\"EndsAtUtc\" > \"StartsAtUtc\" AND \"EndsAtUtc\" - \"StartsAtUtc\" <= INTERVAL '24 hours'");

            t.HasCheckConstraint("CK_ClassSessions_Capacity",
                "\"Capacity\" > 0 AND \"ReservedCount\" >= 0 AND \"ReservedCount\" <= \"Capacity\" AND \"AttendedCount\" >= 0 AND \"AttendedCount\" <= \"ReservedCount\"");

            t.HasCheckConstraint("CK_ClassSessions_Status",
                "\"Status\" IN (1, 4, 5, 6, 7)");

            t.HasCheckConstraint("CK_ClassSessions_Suspended",
                "((\"Status\" = 4) = (\"SuspendedAtUtc\" IS NOT NULL AND \"SuspensionReason\" IS NOT NULL))");

            t.HasCheckConstraint("CK_ClassSessions_Suspension_Other",
                "(\"SuspensionReason\" IS NULL OR \"SuspensionReason\" <> 'Other' OR (\"SuspensionObservations\" IS NOT NULL AND length(trim(\"SuspensionObservations\")) > 0))");

            t.HasCheckConstraint("CK_ClassSessions_Cancelled",
                "((\"Status\" = 5) = (\"CancelledAtUtc\" IS NOT NULL AND \"CancellationReason\" IS NOT NULL))");

            t.HasCheckConstraint("CK_ClassSessions_Cancellation_Other",
                "(\"CancellationReason\" IS NULL OR \"CancellationReason\" <> 'Other' OR (\"CancellationObservations\" IS NOT NULL AND length(trim(\"CancellationObservations\")) > 0))");

            t.HasCheckConstraint("CK_ClassSessions_Rescheduled",
                "((\"Status\" = 7) = (\"RescheduledToSessionId\" IS NOT NULL))");
        });

        // Índices
        builder.HasIndex(cs => new { cs.RecurringScheduleId, cs.StartsAtUtc })
            .HasDatabaseName("UX_ClassSessions_Schedule_StartsAt")
            .IsUnique()
            .HasFilter("\"RecurringScheduleId\" IS NOT NULL");

        builder.HasIndex(cs => cs.StartsAtUtc);
        builder.HasIndex(cs => new { cs.Status, cs.StartsAtUtc });
        builder.HasIndex(cs => new { cs.ActivityId, cs.StartsAtUtc });
        builder.HasIndex(cs => new { cs.RoomId, cs.StartsAtUtc });
        builder.HasIndex(cs => new { cs.EffectiveInstructorId, cs.StartsAtUtc });
        builder.HasIndex(cs => new { cs.ScheduledInstructorId, cs.StartsAtUtc });
    }
}
