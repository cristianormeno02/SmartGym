using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationWaitlistTests
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

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; }
        public Guid? PersonId { get; set; }
        public string? Email { get; set; } = "user@smartgym.local";
        public bool IsAuthenticated { get; set; } = true;
        public IReadOnlyList<string> Roles { get; set; } = [SmartGym.Application.Common.Security.Roles.Secretary];
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static async Task<(ClassSession Session, List<(Person Student, Membership Membership)> Students)> SeedSessionAndStudentsAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar,
        int capacity = 1,
        int studentCount = 4,
        DateTime? startsAtUtc = null)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var instructor = Person.Create("Instructor", "Uno", "20000111");
        var activity = new Activity("SPIN", "Spinning", defaultCapacity: capacity);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Spinning", Capacity = 20, IsActive = true };

        db.People.Add(instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);

        var start = startsAtUtc ?? calendar.UtcNow.AddHours(5);
        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity.Id, room.Id, start, start.AddHours(1), capacity, instructor.Id, null);
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
        var students = new List<(Person Student, Membership Membership)>();

        for (int i = 1; i <= studentCount; i++)
        {
            var s = Person.Create($"Alumno{i}", "Apellido", $"3000000{i}", birthDate: new DateTime(1995, 1, i));
            s.AddRole(studentRole);
            db.People.Add(s);

            var m = Membership.Create(s.Id, plan, calendar.Today, calendar.UtcNow, 10000m, 0m, 10000m);
            m.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
            await ledger.RecordInitialCreditsAsync(m);
            db.Memberships.Add(m);

            students.Add((s, m));
        }

        await db.SaveChangesAsync();
        return (session, students);
    }

    [Fact]
    public async Task WaitlistEntry_DoesNotConsumeCreditOrSpot_EnforcesDisabledAndMaxWaitlistSize_AndCompactsPositionsOnLeave()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 8, 12));
        var (session, students) = await SeedSessionAndStudentsAsync(db, calendar, capacity: 1, studentCount: 4);
        var currentUser = new StubCurrentUserService { UserId = Guid.NewGuid(), Roles = [Roles.Secretary] };
        var options = Options.Create(new ReservationsOptions { EnableWaitlist = true, MaxWaitlistSize = 2 });
        var service = new ReservationService(db, calendar, currentUserService: currentUser, reservationsOptions: options);

        // Alumno 1 ocupa el único cupo
        var r1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[0].Student.Id), Guid.NewGuid());
        Assert.Equal(ReservationStatus.Confirmed, r1.Status);

        // Si AllowWaitlist = false -> 409 CLASS_FULL
        var exFull = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session.Id, students[1].Student.Id, AllowWaitlist: false),
                Guid.NewGuid()));
        Assert.Equal(ReservationErrorCodes.ClassFull, exFull.Code);

        // Alumno 2 y Alumno 3 ingresan a lista de espera sin consumir créditos
        var w1 = await service.JoinWaitlistAsync(
            session.Id,
            new JoinWaitlistRequest(StudentId: students[1].Student.Id),
            Guid.NewGuid());
        var w2 = await service.CreateReservationAsync(
            new CreateReservationRequest(session.Id, students[2].Student.Id, AllowWaitlist: true),
            Guid.NewGuid());

        Assert.Equal(ReservationStatus.Waitlisted, w1.Status);
        Assert.Equal(1, w1.WaitListPosition);
        Assert.Equal(ReservationStatus.Waitlisted, w2.Status);
        Assert.Equal(2, w2.WaitListPosition);
        Assert.Equal(4, (await db.Memberships.SingleAsync(m => m.Id == students[1].Membership.Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);

        // Alumno 4 intenta ingresar cuando ya hay MaxWaitlistSize (2) -> 409 WAITLIST_FULL
        var exWaitlistFull = await Assert.ThrowsAsync<ConflictException>(() =>
            service.JoinWaitlistAsync(session.Id, new JoinWaitlistRequest(StudentId: students[3].Student.Id), Guid.NewGuid()));
        Assert.Equal(ReservationErrorCodes.WaitlistFull, exWaitlistFull.Code);

        // Alumno 2 sale voluntariamente de la lista -> Alumno 3 pasa a posición 1
        currentUser.Roles = [Roles.Student];
        currentUser.PersonId = students[1].Student.Id;
        var left = await service.LeaveWaitlistAsync(session.Id, Guid.NewGuid());
        Assert.Equal(ReservationStatus.Cancelled, left.Status);

        var w2Reloaded = await db.Reservations.SingleAsync(r => r.Id == w2.Id);
        Assert.Equal(1, w2Reloaded.WaitListPosition);
    }

    [Fact]
    public async Task AutomaticPromotion_SkipsIneligibleCandidateWithoutBlockingNext_AndRespectsMinMinutesBeforeStartThreshold()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 8, 12));
        var (session, students) = await SeedSessionAndStudentsAsync(db, calendar, capacity: 1, studentCount: 3);
        var ledger = new CreditLedgerService(db, calendar);
        var options = Options.Create(new ReservationsOptions
        {
            EnableWaitlist = true,
            WaitlistPromotionMode = WaitlistPromotionMode.Automatic,
            MinMinutesBeforeStartForWaitlistPromotion = 15
        });
        var service = new ReservationService(db, calendar, reservationsOptions: options);

        var confirmed = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[0].Student.Id), Guid.NewGuid());
        var wait1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[1].Student.Id), Guid.NewGuid());
        var wait2 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[2].Student.Id), Guid.NewGuid());

        // Candidato 1 agota sus créditos antes de que se libere el cupo
        var m1 = await db.Memberships.SingleAsync(m => m.Id == students[1].Membership.Id);
        for (int i = 0; i < 4; i++)
        {
            await ledger.ConsumeCreditForReservationAsync(m1, Guid.NewGuid(), calendar.Today, "Otra clase");
        }
        await db.SaveChangesAsync();

        // Cancelamos la reserva confirmada -> se omite wait1 (conserva posición 1) y se promueve wait2 a Confirmed!
        await service.CancelReservationAsync(confirmed.Id, new CancelReservationRequest("Baja"), Guid.NewGuid());

        var w1After = await db.Reservations.SingleAsync(r => r.Id == wait1.Id);
        var w2After = await db.Reservations.SingleAsync(r => r.Id == wait2.Id);

        Assert.Equal(ReservationStatus.Waitlisted, w1After.Status);
        Assert.Equal(1, w1After.WaitListPosition);
        Assert.Equal(ReservationStatus.Confirmed, w2After.Status);
        Assert.Null(w2After.WaitListPosition);
        Assert.NotNull(w2After.WaitlistPromotedAtUtc);
        Assert.Equal(3, (await db.Memberships.SingleAsync(m => m.Id == students[2].Membership.Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);

        // Si ahora le devolvemos crédito a m1 pero faltan sólo 10 minutos para el inicio (< MinMinutesBeforeStartForWaitlistPromotion = 15),
        // al cancelar w2After NO se promueve a w1After
        await ledger.RefundCreditForReservationAsync(m1, Guid.NewGuid(), "Devolución");
        await db.SaveChangesAsync();
        calendar.UtcNow = session.StartsAtUtc.AddMinutes(-10);

        await service.CancelReservationAsync(w2After.Id, new CancelReservationRequest("Baja sobre la hora"), Guid.NewGuid());

        var w1Final = await db.Reservations.SingleAsync(r => r.Id == wait1.Id);
        Assert.Equal(ReservationStatus.Waitlisted, w1Final.Status);
        Assert.Equal(0, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);
    }

    [Fact]
    public async Task OfferWithConfirmationMode_CapsExpirationAtSessionStart_HandlesDecline_IneligibleAccept_AndValidAccept()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 8, 12));
        var (session, students) = await SeedSessionAndStudentsAsync(db, calendar, capacity: 1, studentCount: 4);
        var ledger = new CreditLedgerService(db, calendar);
        var currentUser = new StubCurrentUserService { UserId = Guid.NewGuid(), Roles = [Roles.Secretary] };
        var options = Options.Create(new ReservationsOptions
        {
            EnableWaitlist = true,
            WaitlistPromotionMode = WaitlistPromotionMode.OfferWithConfirmation,
            WaitlistOfferExpirationMinutes = 30,
            MinMinutesBeforeStartForWaitlistPromotion = 15
        });
        var service = new ReservationService(db, calendar, currentUserService: currentUser, reservationsOptions: options);

        var r1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[0].Student.Id), Guid.NewGuid());
        var w1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[1].Student.Id), Guid.NewGuid());
        var w2 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[2].Student.Id), Guid.NewGuid());
        var w3 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[3].Student.Id), Guid.NewGuid());

        // 1. Cancelamos r1 faltando 20 minutos para el inicio -> w1 pasa a Pending con vencimiento acotado a StartsAtUtc (20 min < 30 min)
        calendar.UtcNow = session.StartsAtUtc.AddMinutes(-20);
        await service.CancelReservationAsync(r1.Id, new CancelReservationRequest("Baja"), Guid.NewGuid());

        var w1Offered = await db.Reservations.SingleAsync(r => r.Id == w1.Id);
        Assert.Equal(ReservationStatus.Pending, w1Offered.Status);
        Assert.Equal(session.StartsAtUtc, w1Offered.WaitlistOfferExpiresAtUtc);
        Assert.Equal(4, (await db.Memberships.SingleAsync(m => m.Id == students[1].Membership.Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);

        // 2. w1 rechaza la oferta -> w1 pasa a Cancelled (WaitlistDeclined) y el cupo se ofrece a w2
        var declined = await service.DeclineWaitlistOfferAsync(w1.Id, Guid.NewGuid());
        Assert.Equal(ReservationStatus.Cancelled, declined.Status);
        Assert.Equal(ReservationCancellationSource.WaitlistDeclined, declined.CancellationSource);

        var w2Offered = await db.Reservations.SingleAsync(r => r.Id == w2.Id);
        Assert.Equal(ReservationStatus.Pending, w2Offered.Status);

        // 3. w2 pierde elegibilidad (agota sus créditos) e intenta aceptar -> se cancela su oferta, se promueve a w3 y lanza 409 ConflictException
        var m2 = await db.Memberships.SingleAsync(m => m.Id == students[2].Membership.Id);
        for (int i = 0; i < 4; i++)
        {
            await ledger.ConsumeCreditForReservationAsync(m2, Guid.NewGuid(), calendar.Today, "Otra clase");
        }
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.AcceptWaitlistOfferAsync(w2.Id, Guid.NewGuid()));

        var w2AfterFailedAccept = await db.Reservations.SingleAsync(r => r.Id == w2.Id);
        var w3Offered = await db.Reservations.SingleAsync(r => r.Id == w3.Id);
        Assert.Equal(ReservationStatus.Cancelled, w2AfterFailedAccept.Status);
        Assert.Equal(ReservationCancellationSource.WaitlistOfferExpired, w2AfterFailedAccept.CancellationSource);
        Assert.Equal(ReservationStatus.Pending, w3Offered.Status);

        // 4. w3 acepta dentro del plazo y siendo elegible -> pasa a Confirmed y consume 1 crédito
        var accepted = await service.AcceptWaitlistOfferAsync(w3.Id, Guid.NewGuid());
        Assert.Equal(ReservationStatus.Confirmed, accepted.Status);
        Assert.Equal(3, (await db.Memberships.SingleAsync(m => m.Id == students[3].Membership.Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);
    }

    [Fact]
    public async Task UpdateCapacity_EmitsClassSessionCapacityIncreased_AndPromotesWaitlistEntriesUpToNewCapacity()
    {
        var dbOptions = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var calendar = new StubGymCalendar(new DateOnly(2026, 8, 12));

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        SmartGymDbContext? db = null;
        services.AddScoped<ISmartGymDbContext>(_ => db!);
        services.AddSingleton<IGymCalendar>(calendar);
        services.AddScoped<SmartGym.Application.Modules.Reservations.Handlers.PromoteWaitlistOnCapacityIncreasedHandler>();
        services.AddScoped<IDomainEventHandler<SmartGym.Domain.Events.Activities.ClassSessionCapacityIncreased>>(
            sp => sp.GetRequiredService<SmartGym.Application.Modules.Reservations.Handlers.PromoteWaitlistOnCapacityIncreasedHandler>());
        var sp = services.BuildServiceProvider();

        db = new SmartGymDbContext(dbOptions, sp);
        var (session, students) = await SeedSessionAndStudentsAsync(db, calendar, capacity: 1, studentCount: 3);

        var service = new ReservationService(db, calendar);
        var r1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[0].Student.Id), Guid.NewGuid());
        var w1 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[1].Student.Id), Guid.NewGuid());
        var w2 = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, students[2].Student.Id), Guid.NewGuid());

        Assert.Equal(ReservationStatus.Confirmed, r1.Status);
        Assert.Equal(ReservationStatus.Waitlisted, w1.Status);
        Assert.Equal(ReservationStatus.Waitlisted, w2.Status);

        // Aumentamos la capacidad de 1 a 3 y guardamos -> se emite ClassSessionCapacityIncreased y se promueven w1 y w2
        session.UpdateCapacity(3, calendar.UtcNow);
        await db.SaveChangesAsync();

        var w1After = await db.Reservations.SingleAsync(r => r.Id == w1.Id);
        var w2After = await db.Reservations.SingleAsync(r => r.Id == w2.Id);
        Assert.Equal(ReservationStatus.Confirmed, w1After.Status);
        Assert.Equal(ReservationStatus.Confirmed, w2After.Status);
        Assert.Equal(3, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);
    }
}

