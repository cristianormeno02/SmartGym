using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;

namespace SmartGym.Domain.UnitTests.Operations;

public class ClassReminderServiceTests
{
    private static SmartGymDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static IGymCalendar CreateCalendar() =>
        new GymCalendar(Options.Create(new GymOptions { TimeZoneId = "America/Argentina/San_Juan" }));

    private static NotificationDispatcher CreateDispatcher(SmartGymDbContext db, IGymCalendar calendar)
    {
        var resolver = new NotificationRecipientResolver(db, calendar);
        var renderer = new NotificationTemplateRenderer(db);
        var service = new NotificationService(db, calendar);
        return new NotificationDispatcher(db, resolver, renderer, service, calendar);
    }

    [Fact]
    public async Task ProcessUpcomingClassRemindersAsync_SendsSingleReminderPerConfirmedReservationWithinWindow_AndSkipsCancelledOrSuspended()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDbContext(dbName);
        var calendar = CreateCalendar();
        var nowUtc = calendar.UtcNow;

        var student1 = Person.Create("Ana", "Gomez", "ana@test.com", new DateTime(1995, 5, 10, 0, 0, 0, DateTimeKind.Utc));
        var student2 = Person.Create("Bruno", "Paz", "bruno@test.com", new DateTime(1992, 8, 12, 0, 0, 0, DateTimeKind.Utc));
        var instructor = Person.Create("Profe", "Uno", "profe@test.com", new DateTime(1985, 3, 20, 0, 0, 0, DateTimeKind.Utc));
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala 1", Capacity = 20, IsActive = true };
        var activity = new Activity("YOGA", "Yoga", defaultCapacity: 15);

        // Session 1: Open, starts in 6 hours (within 24h lead window)
        var upcomingOpenSession = ClassSession.CreateExtraordinary(
            activity.Id, room.Id, nowUtc.AddHours(6), nowUtc.AddHours(7), 15, instructor.Id, null);
        upcomingOpenSession.Open(nowUtc);

        // Session 2: Suspended, starts in 8 hours (must be skipped)
        var upcomingSuspendedSession = ClassSession.CreateExtraordinary(
            activity.Id, room.Id, nowUtc.AddHours(8), nowUtc.AddHours(9), 15, instructor.Id, null);
        upcomingSuspendedSession.Open(nowUtc);
        upcomingSuspendedSession.Suspend(ClassSessionSuspensionReason.Weather, "Lluvia", Guid.NewGuid(), nowUtc);

        // Session 3: Open, starts in 48 hours (outside 24h lead window)
        var distantSession = ClassSession.CreateExtraordinary(
            activity.Id, room.Id, nowUtc.AddHours(48), nowUtc.AddHours(49), 15, instructor.Id, null);
        distantSession.Open(nowUtc);

        db.AddRange(student1, student2, instructor, room, activity, upcomingOpenSession, upcomingSuspendedSession, distantSession);

        // Confirmed reservation in upcomingOpenSession for student1
        var confirmedRes = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = upcomingOpenSession.Id,
            StudentId = student1.Id,
            Status = ReservationStatus.Confirmed,
            ReservedAtUtc = nowUtc,
            IsActive = true
        };

        // Cancelled reservation in upcomingOpenSession for student2 (must be skipped)
        var cancelledRes = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = upcomingOpenSession.Id,
            StudentId = student2.Id,
            Status = ReservationStatus.Cancelled,
            ReservedAtUtc = nowUtc,
            CancelledAtUtc = nowUtc,
            IsActive = true
        };

        // Confirmed reservation in distantSession for student2 (must be skipped)
        var distantRes = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = distantSession.Id,
            StudentId = student2.Id,
            Status = ReservationStatus.Confirmed,
            ReservedAtUtc = nowUtc,
            IsActive = true
        };

        db.Reservations.AddRange(confirmedRes, cancelledRes, distantRes);
        await db.SaveChangesAsync();

        var dispatcher = CreateDispatcher(db, calendar);
        var reminderService = new ClassReminderService(
            db,
            dispatcher,
            calendar,
            Options.Create(new NotificationsOptions { ClassReminderLeadHours = 24 }));

        var firstRun = await reminderService.ProcessUpcomingClassRemindersAsync();
        Assert.Equal(1, firstRun.RemindersSent);

        var messages = await db.NotificationMessages
            .Where(m => m.Type == NotificationType.ClassReminder)
            .ToListAsync();
        var singleReminder = Assert.Single(messages);
        Assert.Equal(student1.Id, singleReminder.RecipientPersonId);
        Assert.Equal($"class-reminder:{confirmedRes.Id}", singleReminder.IdempotencyKey);

        // Second run must deduplicate and send 0 new reminders
        var secondRun = await reminderService.ProcessUpcomingClassRemindersAsync();
        Assert.Equal(0, secondRun.RemindersSent);
        Assert.Equal(1, secondRun.DeduplicatedCount);
        Assert.Equal(1, await db.NotificationMessages.CountAsync(m => m.Type == NotificationType.ClassReminder));
    }

    [Fact]
    public async Task MembershipService_SuspendAndReactivate_DispatchesIdempotentNotificationsWithDeliveries()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDbContext(dbName);
        var calendar = CreateCalendar();
        var today = calendar.Today;
        var nowUtc = calendar.UtcNow;

        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var student = Person.Create("Lucia", "Mendez", "lucia@test.com", new DateTime(1997, 2, 14, 0, 0, 0, DateTimeKind.Utc));
        student.AddRole(studentRole);
        db.People.Add(student);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            Description = "8 clases al mes",
            Price = 0m,
            DurationDays = 30,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        var service = new MembershipService(db, gymCalendar: calendar);
        var created = await service.CreateAsync(
            new CreateMembershipRequest(
                StudentId: student.Id,
                MembershipPlanId: plan.Id,
                StartDate: today,
                PromotionCode: null,
                InitialPayment: null,
                PaymentId: null,
                ActivateImmediately: true,
                ActivationReason: "Alta directa",
                Notes: null,
                IdempotencyKey: "mem-create-1"),
            currentUserId: Guid.NewGuid(),
            isCallerAdministrator: true);

        var suspended = await service.SuspendAsync(
            created.Id,
            new SuspendMembershipRequest(
                EndDate: today.AddDays(5),
                ExtendExpirationDate: true,
                Reason: "Viaje",
                Notes: null,
                Version: created.Version),
            currentUserId: Guid.NewGuid());

        var reactivated = await service.ReactivateAsync(
            created.Id,
            new ReactivateMembershipRequest(
                Reason: "Regreso anticipado",
                Notes: null,
                Version: suspended.Version),
            currentUserId: Guid.NewGuid());

        Assert.Equal(MembershipStatus.Active, reactivated.Status);

        var notifications = await db.NotificationMessages
            .Where(n => n.RecipientPersonId == student.Id)
            .ToListAsync();

        Assert.Contains(notifications, n => n.Type == NotificationType.MembershipActivated && !string.IsNullOrWhiteSpace(n.IdempotencyKey));
        Assert.Contains(notifications, n => n.Type == NotificationType.MembershipSuspended && !string.IsNullOrWhiteSpace(n.IdempotencyKey));
        Assert.Contains(notifications, n => n.Type == NotificationType.MembershipReactivated && !string.IsNullOrWhiteSpace(n.IdempotencyKey));
    }
}
