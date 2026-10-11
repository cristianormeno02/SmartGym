using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Handlers;
using SmartGym.Application.Modules.Reservations.Handlers;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Activities;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotifyStudentsOnSessionChangeTests
{
    private readonly DbContextOptions<SmartGymDbContext> _options;
    private readonly IGymCalendar _gymCalendar;
    private readonly Guid _activityId;
    private readonly Guid _roomId;
    private readonly Guid _room2Id;
    private readonly Guid _instructorId;
    private readonly Guid _student1Id = Guid.NewGuid();
    private readonly Guid _student2Id = Guid.NewGuid();

    public NotifyStudentsOnSessionChangeTests()
    {
        _options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _gymCalendar = new GymCalendar(
            Options.Create(new GymOptions { TimeZoneId = "America/Argentina/San_Juan" }),
            TimeProvider.System);

        using var seed = new SmartGymDbContext(_options);

        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        var instructorRole = new Role { Id = (int)RoleType.Instructor, Name = RoleType.Instructor.ToString(), Description = "Instructor" };
        seed.Roles.AddRange(studentRole, instructorRole);

        var s1 = Person.Create("Maria", "Student", "maria@gym.com");
        s1.Id = _student1Id;
        s1.AddRole(studentRole);

        var s2 = Person.Create("Carlos", "Student", "carlos@gym.com");
        s2.Id = _student2Id;
        s2.AddRole(studentRole);

        var inst = Person.Create("Profe", "Test", "inst@gym.com");
        _instructorId = inst.Id;
        inst.AddRole(instructorRole);

        var room1 = new Room { Id = Guid.NewGuid(), Name = "Sala Ciclo", Capacity = 30, IsActive = true };
        _roomId = room1.Id;

        var room2 = new Room { Id = Guid.NewGuid(), Name = "Sala Principal", Capacity = 40, IsActive = true };
        _room2Id = room2.Id;

        var activity = new Activity("SPINNING", "Spinning", defaultCapacity: 20);
        _activityId = activity.Id;

        seed.AddRange(s1, s2, inst, room1, room2, activity);
        seed.SaveChanges();
    }

    private ServiceProvider BuildServiceProvider(SmartGymDbContext dbContext)
    {
        var services = new ServiceCollection();
        services.AddScoped<ISmartGymDbContext>(_ => dbContext);
        services.AddSingleton(_gymCalendar);

        services.AddScoped<CancelReservationsOnSessionVoidedHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionSuspended>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionCancelled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionRescheduled>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());

        services.AddScoped<NotifyStudentsOnSessionChangeHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionSuspended>>(sp => sp.GetRequiredService<NotifyStudentsOnSessionChangeHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionCancelled>>(sp => sp.GetRequiredService<NotifyStudentsOnSessionChangeHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionRescheduled>>(sp => sp.GetRequiredService<NotifyStudentsOnSessionChangeHandler>());
        services.AddScoped<IDomainEventHandler<ClassSessionRoomChanged>>(sp => sp.GetRequiredService<NotifyStudentsOnSessionChangeHandler>());

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SuspendSession_GeneratesInAppNotificationForEveryStudentWithLocalTime()
    {
        // Supongamos que la clase es a las 21:00 UTC (18:00 San Juan)
        var startsUtc = new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);
        var endsUtc = startsUtc.AddHours(1);

        using (var setupDb = new SmartGymDbContext(_options))
        {
            var session = ClassSession.CreateExtraordinary(_activityId, _roomId, startsUtc, endsUtc, 20, _instructorId, null);
            session.Open(DateTime.UtcNow);
            setupDb.ClassSessions.Add(session);

            setupDb.Reservations.Add(new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = _student1Id,
                Status = ReservationStatus.Confirmed,
                ReservedAtUtc = DateTime.UtcNow,
                IsActive = true
            });
            setupDb.Reservations.Add(new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = _student2Id,
                Status = ReservationStatus.Confirmed,
                ReservedAtUtc = DateTime.UtcNow,
                IsActive = true
            });

            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        SmartGymDbContext? db = null;
        services.AddScoped<ISmartGymDbContext>(_ => db!);
        services.AddSingleton(_gymCalendar);
        services.AddScoped<NotifyStudentsOnSessionChangeHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionSuspended>>(sp => sp.GetRequiredService<NotifyStudentsOnSessionChangeHandler>());
        var sp = services.BuildServiceProvider();

        db = new SmartGymDbContext(_options, sp);

        var sessionToSuspend = await db.ClassSessions.FirstAsync();
        sessionToSuspend.Suspend(ClassSessionSuspensionReason.Weather, "Lluvia", Guid.NewGuid(), DateTime.UtcNow);

        await db.SaveChangesAsync();

        using var verifyDb = new SmartGymDbContext(_options);
        var notifications = await verifyDb.NotificationMessages.ToListAsync();

        Assert.Equal(2, notifications.Count);
        Assert.Contains(notifications, n => n.RecipientPersonId == _student1Id);
        Assert.Contains(notifications, n => n.RecipientPersonId == _student2Id);

        var note = notifications.First();
        Assert.Contains("18:00", note.Body); // Hora local de San Juan (UTC-3)
        Assert.Contains("15/10", note.Body); // Fecha local
    }

    [Fact]
    public async Task Suspend_WithCancellationHandlerRunningFirst_StillNotifiesStudentsWithActiveReservations()
    {
        var startsUtc = DateTime.UtcNow.AddDays(2);

        using (var setupDb = new SmartGymDbContext(_options))
        {
            var session = ClassSession.CreateExtraordinary(_activityId, _roomId, startsUtc, startsUtc.AddHours(1), 20, _instructorId, null);
            session.Open(DateTime.UtcNow);
            setupDb.ClassSessions.Add(session);
            setupDb.Reservations.Add(new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = _student1Id,
                Status = ReservationStatus.Confirmed,
                ReservedAtUtc = DateTime.UtcNow,
                IsActive = true
            });
            await setupDb.SaveChangesAsync();
        }

        // Mismo orden de registro que Application/DependencyInjection: cancelación antes que notificación
        var services = new ServiceCollection();
        SmartGymDbContext? db = null;
        services.AddScoped<ISmartGymDbContext>(_ => db!);
        services.AddSingleton(_gymCalendar);
        services.AddScoped<CancelReservationsOnSessionVoidedHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionSuspended>>(sp => sp.GetRequiredService<CancelReservationsOnSessionVoidedHandler>());
        services.AddScoped<NotifyStudentsOnSessionChangeHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionSuspended>>(sp => sp.GetRequiredService<NotifyStudentsOnSessionChangeHandler>());
        var sp = services.BuildServiceProvider();

        db = new SmartGymDbContext(_options, sp);

        var sessionToSuspend = await db.ClassSessions.FirstAsync();
        sessionToSuspend.Suspend(ClassSessionSuspensionReason.Weather, "Lluvia", Guid.NewGuid(), DateTime.UtcNow);
        await db.SaveChangesAsync();

        using var verifyDb = new SmartGymDbContext(_options);
        var notification = Assert.Single(await verifyDb.NotificationMessages.ToListAsync());
        Assert.Equal(_student1Id, notification.RecipientPersonId);
    }

    [Fact]
    public async Task RoomChanged_DoesNotNotifyStudentsWithoutActiveReservation()
    {
        var startsUtc = DateTime.UtcNow.AddDays(2);

        using (var setupDb = new SmartGymDbContext(_options))
        {
            var session = ClassSession.CreateExtraordinary(_activityId, _roomId, startsUtc, startsUtc.AddHours(1), 20, _instructorId, null);
            session.Open(DateTime.UtcNow);
            setupDb.ClassSessions.Add(session);

            setupDb.Reservations.Add(new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = _student1Id,
                Status = ReservationStatus.Confirmed,
                ReservedAtUtc = DateTime.UtcNow,
                IsActive = true
            });
            // Alumno que canceló su reserva antes del cambio de sala
            setupDb.Reservations.Add(new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = _student2Id,
                Status = ReservationStatus.Cancelled,
                ReservedAtUtc = DateTime.UtcNow,
                CancelledAtUtc = DateTime.UtcNow,
                IsActive = true
            });

            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        SmartGymDbContext? db = null;
        services.AddScoped<ISmartGymDbContext>(_ => db!);
        services.AddSingleton(_gymCalendar);
        services.AddScoped<NotifyStudentsOnSessionChangeHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionRoomChanged>>(sp => sp.GetRequiredService<NotifyStudentsOnSessionChangeHandler>());
        var sp = services.BuildServiceProvider();

        db = new SmartGymDbContext(_options, sp);

        var sessionToChange = await db.ClassSessions.FirstAsync();
        sessionToChange.ChangeRoom(_room2Id, Guid.NewGuid(), DateTime.UtcNow);
        await db.SaveChangesAsync();

        using var verifyDb = new SmartGymDbContext(_options);
        var notification = Assert.Single(await verifyDb.NotificationMessages.ToListAsync());
        Assert.Equal(_student1Id, notification.RecipientPersonId);
    }

    [Fact]
    public async Task RoomChanged_GeneratesNotificationWithNewRoomNameAndLocalTime()
    {
        var startsUtc = new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);
        var endsUtc = startsUtc.AddHours(1);

        using (var setupDb = new SmartGymDbContext(_options))
        {
            var session = ClassSession.CreateExtraordinary(_activityId, _roomId, startsUtc, endsUtc, 20, _instructorId, null);
            session.Open(DateTime.UtcNow);
            setupDb.ClassSessions.Add(session);

            setupDb.Reservations.Add(new Reservation
            {
                Id = Guid.NewGuid(),
                ClassSessionId = session.Id,
                StudentId = _student1Id,
                Status = ReservationStatus.Confirmed,
                ReservedAtUtc = DateTime.UtcNow,
                IsActive = true
            });

            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();
        SmartGymDbContext? db = null;
        services.AddScoped<ISmartGymDbContext>(_ => db!);
        services.AddSingleton(_gymCalendar);
        services.AddScoped<NotifyStudentsOnSessionChangeHandler>();
        services.AddScoped<IDomainEventHandler<ClassSessionRoomChanged>>(sp => sp.GetRequiredService<NotifyStudentsOnSessionChangeHandler>());
        var sp = services.BuildServiceProvider();

        db = new SmartGymDbContext(_options, sp);

        var sessionToChange = await db.ClassSessions.FirstAsync();
        sessionToChange.ChangeRoom(_room2Id, Guid.NewGuid(), DateTime.UtcNow);

        await db.SaveChangesAsync();

        using var verifyDb = new SmartGymDbContext(_options);
        var notification = await verifyDb.NotificationMessages.FirstOrDefaultAsync();

        Assert.NotNull(notification);
        Assert.Equal(_student1Id, notification.RecipientPersonId);
        Assert.Contains("Sala Principal", notification.Body);
        Assert.Contains("18:00", notification.Body);
    }
}
