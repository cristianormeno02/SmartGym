using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using SmartGym.Infrastructure.Services.BackgroundJobs;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationWaitlistProcessTests
{
    private sealed class StubGymCalendar : IGymCalendar
    {
        public StubGymCalendar(DateOnly today, DateTime? utcNow = null)
        {
            Today = today;
            UtcNow = utcNow ?? today.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        }

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateOnly Today { get; set; }
        public DateTime UtcNow { get; set; }
        public DateTime ToUtc(DateOnly date, TimeOnly time) =>
            new(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        public DateTimeOffset ToLocal(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
            (ToUtc(from, TimeOnly.MinValue), ToUtc(to.AddDays(1), TimeOnly.MinValue));
    }

    private static SmartGymDbContext CreateDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    [Fact]
    public async Task ProcessWaitlistAsync_ExpiresTimedOutPendingOffers_ReleasesSpot_AndPromotesNextEligibleCandidate()
    {
        await using var db = CreateDbContext();
        var today = new DateOnly(2026, 8, 15);
        var calendar = new StubGymCalendar(today);

        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var instructor = Person.Create("Instructor", "Uno", "20000111");
        var activity = new Activity("BOX", "Boxeo", defaultCapacity: 1);
        var room = new Room { Id = Guid.NewGuid(), Name = "Ring", Capacity = 10, IsActive = true };
        db.People.Add(instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);

        var start = calendar.UtcNow.AddHours(4);
        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity.Id, room.Id, start, start.AddHours(1), 1, instructor.Id, null);
        session.Open(calendar.UtcNow);
        db.ClassSessions.Add(session);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 4",
            DurationDays = 30,
            Price = 10000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var ledger = new CreditLedgerService(db, calendar);
        var students = new List<Person>();
        for (int i = 1; i <= 3; i++)
        {
            var s = Person.Create($"Alumno{i}", "Box", $"3100000{i}", birthDate: new DateTime(1996, 2, i));
            s.AddRole(studentRole);
            db.People.Add(s);

            var m = Membership.Create(s.Id, plan, today, calendar.UtcNow, 10000m, 0m, 10000m);
            m.Activate(today, calendar.UtcNow, Guid.NewGuid());
            await ledger.RecordInitialCreditsAsync(m);
            db.Memberships.Add(m);
            students.Add(s);
        }
        await db.SaveChangesAsync();

        var options = Options.Create(new ReservationsOptions
        {
            EnableWaitlist = true,
            WaitlistPromotionMode = WaitlistPromotionMode.OfferWithConfirmation,
            WaitlistOfferExpirationMinutes = 30,
            MinMinutesBeforeStartForWaitlistPromotion = 15
        });
        var service = new ReservationService(db, calendar, reservationsOptions: options);

        var r1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[0].Id), Guid.NewGuid());
        var w1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[1].Id), Guid.NewGuid());
        var w2 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[2].Id), Guid.NewGuid());

        // Cancelamos r1 -> w1 recibe oferta Pending por 30 minutos
        await service.CancelReservationAsync(r1.Id, new CancelReservationRequest("Baja"), Guid.NewGuid());
        var w1Pending = await db.Reservations.SingleAsync(r => r.Id == w1.Id);
        Assert.Equal(ReservationStatus.Pending, w1Pending.Status);

        // Avanzamos el reloj 31 minutos (oferta de w1 vencida) y ejecutamos ProcessWaitlistAsync
        calendar.UtcNow = calendar.UtcNow.AddMinutes(31);
        var result = await service.ProcessWaitlistAsync();

        Assert.Equal(1, result.ExpiredOffersCancelled);
        Assert.Equal(1, result.CandidatesPromoted);

        var w1Expired = await db.Reservations.SingleAsync(r => r.Id == w1.Id);
        var w2Offered = await db.Reservations.SingleAsync(r => r.Id == w2.Id);

        Assert.Equal(ReservationStatus.Cancelled, w1Expired.Status);
        Assert.Equal(ReservationCancellationSource.WaitlistOfferExpired, w1Expired.CancellationSource);
        Assert.Equal(ReservationStatus.Pending, w2Offered.Status);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);
    }

    [Fact]
    public async Task ProcessWaitlistAsync_RetriesPromotionOnOpenSessionsWithFreeSpots_AndWorkerExecutesViaScope()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var db = CreateDbContext(dbName);
        var today = new DateOnly(2026, 8, 15);
        var calendar = new StubGymCalendar(today);

        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var instructor = Person.Create("Instructor", "Dos", "20000222");
        var activity = new Activity("PIL", "Pilates", defaultCapacity: 1);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Pilates", Capacity = 10, IsActive = true };
        db.People.Add(instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);

        var start = calendar.UtcNow.AddHours(3);
        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity.Id, room.Id, start, start.AddHours(1), 1, instructor.Id, null);
        session.Open(calendar.UtcNow);
        db.ClassSessions.Add(session);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 1",
            DurationDays = 30,
            Price = 5000m,
            Credits = 1,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var ledger = new CreditLedgerService(db, calendar);
        var s1 = Person.Create("Alumno1", "Pil", "32000001", birthDate: new DateTime(1995, 1, 1));
        var s2 = Person.Create("Alumno2", "Pil", "32000002", birthDate: new DateTime(1995, 1, 2));
        s1.AddRole(studentRole);
        s2.AddRole(studentRole);
        db.People.AddRange(s1, s2);

        var m1 = Membership.Create(s1.Id, plan, today, calendar.UtcNow, 5000m, 0m, 5000m);
        m1.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m1);

        var m2 = Membership.Create(s2.Id, plan, today, calendar.UtcNow, 5000m, 0m, 5000m);
        m2.Activate(today, calendar.UtcNow, Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(m2);

        db.Memberships.AddRange(m1, m2);
        await db.SaveChangesAsync();

        var options = Options.Create(new ReservationsOptions
        {
            EnableWaitlist = true,
            WaitlistPromotionMode = WaitlistPromotionMode.Automatic,
            MinMinutesBeforeStartForWaitlistPromotion = 15
        });
        var service = new ReservationService(db, calendar, reservationsOptions: options);

        var r1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, s1.Id), Guid.NewGuid());
        var w1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, s2.Id), Guid.NewGuid());

        // s2 agota su único crédito antes de que s1 cancele -> queda en Waitlisted con cupo libre (ReservedCount == 0 < Capacity == 1)
        await ledger.ConsumeCreditForReservationAsync(m2, Guid.NewGuid(), today, "Otra clase");
        await db.SaveChangesAsync();

        await service.CancelReservationAsync(r1.Id, new CancelReservationRequest("Baja"), Guid.NewGuid());
        Assert.Equal(0, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);
        Assert.Equal(ReservationStatus.Waitlisted, (await db.Reservations.SingleAsync(r => r.Id == w1.Id)).Status);

        // s2 recupera un crédito (devolución) y el worker ejecuta ProcessWaitlistAsync
        await ledger.RefundCreditForReservationAsync(m2, Guid.NewGuid(), "Devolución");
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddScoped<ISmartGymDbContext>(_ => CreateDbContext(dbName));
        services.AddSingleton<IGymCalendar>(calendar);
        services.AddSingleton<IOptions<ReservationsOptions>>(options);
        services.AddScoped<IReservationService, ReservationService>();
        await using var provider = services.BuildServiceProvider();

        var worker = new ReservationWaitlistWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            options,
            NullLogger<ReservationWaitlistWorker>.Instance);

        var result = await worker.RunIterationAsync(CancellationToken.None);

        Assert.Equal(0, result.ExpiredOffersCancelled);
        Assert.Equal(1, result.CandidatesPromoted);

        await using var verifyDb = CreateDbContext(dbName);
        var promoted = await verifyDb.Reservations.SingleAsync(r => r.Id == w1.Id);
        Assert.Equal(ReservationStatus.Confirmed, promoted.Status);
        Assert.Equal(1, (await verifyDb.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);
    }
}
