using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Operations.Handlers;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Reservations;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Operations;

public class NotifyStudentOnReservationLifecycleTests
{
    private sealed class ThrowingGymCalendar : IGymCalendar
    {
        public TimeZoneInfo TimeZone => throw new InvalidOperationException("Simulated notification failure.");
        public DateTime UtcNow => throw new InvalidOperationException("Simulated notification failure.");
        public DateOnly Today => throw new InvalidOperationException("Simulated notification failure.");
        public DateTime ToUtc(DateOnly date, TimeOnly time) => throw new InvalidOperationException("Simulated notification failure.");
        public DateTimeOffset ToLocal(DateTime utc) => throw new InvalidOperationException("Simulated notification failure.");
        public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
            throw new InvalidOperationException("Simulated notification failure.");
    }

    private readonly DbContextOptions<SmartGymDbContext> _options;
    private readonly IGymCalendar _gymCalendar;
    private readonly Guid _studentId;
    private readonly Guid _sessionId;
    // 21:00 UTC = 18:00 en San Juan
    private readonly DateTime _startsUtc = new(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);

    public NotifyStudentOnReservationLifecycleTests()
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
        seed.Roles.Add(studentRole);

        var student = Person.Create("Maria", "Student", "maria@gym.com");
        student.AddRole(studentRole);
        _studentId = student.Id;

        var instructor = Person.Create("Profe", "Test", "inst@gym.com");
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Ciclo", Capacity = 30, IsActive = true };
        var activity = new Activity("SPINNING", "Spinning", defaultCapacity: 20);

        var session = ClassSession.CreateExtraordinary(activity.Id, room.Id, _startsUtc, _startsUtc.AddHours(1), 20, instructor.Id, null);
        _sessionId = session.Id;

        seed.AddRange(student, instructor, room, activity, session);
        seed.SaveChanges();
    }

    private NotifyStudentOnReservationLifecycleHandler CreateHandler(SmartGymDbContext db) => new(db, _gymCalendar);

    private ServiceProvider BuildProvider(IGymCalendar calendar)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new SmartGymDbContext(_options));
        services.AddScoped<ISmartGymDbContext>(sp => sp.GetRequiredService<SmartGymDbContext>());
        services.AddSingleton(calendar);
        services.AddScoped<NotifyStudentOnReservationLifecycleHandler>();
        services.AddScoped<IDomainEventHandler<ReservationConfirmedEvent>>(sp =>
            sp.GetRequiredService<NotifyStudentOnReservationLifecycleHandler>());
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task EachLifecycleEvent_CreatesOneInAppNotificationOfItsTypeForTheStudent()
    {
        var reservationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var db = new SmartGymDbContext(_options))
        {
            var handler = CreateHandler(db);
            await handler.HandleAsync(new ReservationConfirmedEvent(reservationId, _sessionId, _studentId, ReservationAccessType.MembershipCredit, now));
            await handler.HandleAsync(new ReservationWaitlistedEvent(reservationId, _sessionId, _studentId, 3, now));
            await handler.HandleAsync(new ReservationCancelledEvent(
                reservationId, _sessionId, _studentId, ReservationStatus.Confirmed,
                ReservationCancellationSource.Student, "Cancelada por el alumno", IsLateCancellation: true, now));
            await handler.HandleAsync(new ReservationPromotedFromWaitlistEvent(reservationId, _sessionId, _studentId, ReservationAccessType.MembershipCredit, now));
            await handler.HandleAsync(new WaitlistOfferCreatedEvent(reservationId, _sessionId, _studentId, _startsUtc.AddHours(-2), now));
            await handler.HandleAsync(new WaitlistOfferExpiredEvent(reservationId, _sessionId, _studentId, now));
        }

        using var verifyDb = new SmartGymDbContext(_options);
        var notifications = await verifyDb.NotificationMessages.ToListAsync();

        Assert.Equal(6, notifications.Count);
        Assert.All(notifications, n =>
        {
            Assert.Equal(_studentId, n.RecipientPersonId);
            Assert.Equal(NotificationChannel.InApp, n.Channel);
            Assert.False(n.IsRead);
            Assert.Contains("Spinning", n.Body);
            Assert.Contains("15/10", n.Body);
            Assert.Contains("18:00", n.Body);
        });

        foreach (var type in new[]
                 {
                     NotificationType.ReservationConfirmed,
                     NotificationType.ReservationWaitlisted,
                     NotificationType.ReservationCancelled,
                     NotificationType.ReservationPromotedFromWaitlist,
                     NotificationType.WaitlistSpotOffered,
                     NotificationType.WaitlistOfferExpired
                 })
        {
            Assert.Single(notifications, n => n.Type == type);
        }

        Assert.Contains("posición 3", notifications.Single(n => n.Type == NotificationType.ReservationWaitlisted).Body);
        Assert.Contains("no se restituye", notifications.Single(n => n.Type == NotificationType.ReservationCancelled).Body);
        // La oferta vence a las 19:00 UTC = 16:00 local
        Assert.Contains("16:00", notifications.Single(n => n.Type == NotificationType.WaitlistSpotOffered).Body);
    }

    [Fact]
    public async Task CancellationCausedByClassSessionChange_DoesNotCreateASecondNotification()
    {
        using (var db = new SmartGymDbContext(_options))
        {
            await CreateHandler(db).HandleAsync(new ReservationCancelledEvent(
                Guid.NewGuid(), _sessionId, _studentId, ReservationStatus.Confirmed,
                ReservationCancellationSource.ClassSessionChange, "Clase suspendida: Weather", IsLateCancellation: false, DateTime.UtcNow));
        }

        using var verifyDb = new SmartGymDbContext(_options);
        Assert.Empty(await verifyDb.NotificationMessages.ToListAsync());
    }

    [Fact]
    public async Task ConfirmedReservation_IsNotifiedAfterItIsPersisted()
    {
        using var sp = BuildProvider(_gymCalendar);
        Guid reservationId;

        using (var db = new SmartGymDbContext(_options, sp))
        {
            var reservation = Reservation.CreateConfirmed(
                _sessionId, _studentId, ReservationOrigin.StudentSelfService, ReservationAccessType.MembershipCredit, DateTime.UtcNow);
            reservationId = reservation.Id;
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync();
        }

        using var verifyDb = new SmartGymDbContext(_options);
        Assert.NotNull(await verifyDb.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId));
        var notification = Assert.Single(await verifyDb.NotificationMessages.ToListAsync());
        Assert.Equal(NotificationType.ReservationConfirmed, notification.Type);
        Assert.Equal(_studentId, notification.RecipientPersonId);
    }

    [Fact]
    public async Task NotificationFailure_DoesNotRevertTheReservationNorPropagate()
    {
        using var sp = BuildProvider(new ThrowingGymCalendar());
        Guid reservationId;

        using (var db = new SmartGymDbContext(_options, sp))
        {
            var reservation = Reservation.CreateConfirmed(
                _sessionId, _studentId, ReservationOrigin.StudentSelfService, ReservationAccessType.MembershipCredit, DateTime.UtcNow);
            reservationId = reservation.Id;
            db.Reservations.Add(reservation);

            // No debe lanzar aunque el handler de notificaciones falle
            await db.SaveChangesAsync();
        }

        using var verifyDb = new SmartGymDbContext(_options);
        var persisted = await verifyDb.Reservations.FirstOrDefaultAsync(r => r.Id == reservationId);
        Assert.NotNull(persisted);
        Assert.Equal(ReservationStatus.Confirmed, persisted!.Status);
        Assert.Empty(await verifyDb.NotificationMessages.ToListAsync());
    }
}
