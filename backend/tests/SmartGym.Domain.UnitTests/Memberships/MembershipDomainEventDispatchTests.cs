using Microsoft.EntityFrameworkCore;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.UnitTests.Common;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Memberships;

/// <summary>
/// Ejercita suspensión y cancelación con el despacho de eventos de dominio activo (como en la aplicación),
/// para garantizar que los efectos sobre reservas, créditos y cupos se apliquen exactamente una vez.
/// </summary>
public class MembershipDomainEventDispatchTests
{
    private static readonly DateOnly Start = new(2026, 11, 1);

    private sealed record Scenario(
        SmartGymDbContext Db,
        MembershipService Service,
        Membership Membership,
        ClassSession Session,
        Reservation Reservation);

    /// <summary>
    /// Membresía activa de 8 créditos con una reserva para el 12/11 en una clase
    /// que además tiene otro lugar ocupado por un tercero (ReservedCount = 2).
    /// </summary>
    private static async Task<Scenario> ArrangeAsync()
    {
        var calendar = new TestGymCalendar(Start);
        var db = MembershipTestSupport.CreateDbContextWithMembershipEventHandlers(calendar);
        var service = MembershipTestSupport.CreateMembershipService(db, calendar);
        var ledger = new CreditLedgerService(db, calendar);

        var role = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Alumno" };
        var student = Person.Create("Camila", "Sosa", "39111222");
        student.AddRole(role);
        var instructor = Person.Create("Diego", "Pérez", "29111222");
        var activity = new Activity("FUNC", "Funcional", defaultCapacity: 20);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala 1", Capacity = 20, IsActive = true };
        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            Price = 20000m,
            DurationDays = 30,
            Credits = 8,
            AppliesToAllActivities = true,
            IsActive = true
        };

        db.Roles.Add(role);
        db.People.AddRange(student, instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);
        db.MembershipPlans.Add(plan);
        await db.SaveChangesAsync();

        var created = await service.CreateAsync(
            new CreateMembershipRequest(
                student.Id,
                plan.Id,
                StartDate: Start,
                InitialPayment: new InitialMembershipPaymentRequest(20000m, PaymentMethod.Cash)),
            Guid.NewGuid());
        var membership = await db.Memberships.SingleAsync(m => m.Id == created.Id);

        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            activity.Id,
            room.Id,
            new DateTime(2026, 11, 12, 18, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 12, 19, 0, 0, DateTimeKind.Utc),
            20,
            instructor.Id,
            null);
        session.IncrementReservedCount(); // reserva del alumno
        session.IncrementReservedCount(); // lugar ocupado por un tercero
        db.ClassSessions.Add(session);

        var debit = await ledger.ConsumeCreditForReservationAsync(membership, session.Id, new DateOnly(2026, 11, 12), "Reserva 12/11");
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = session.Id,
            StudentId = student.Id,
            MembershipId = membership.Id,
            CreditMovementId = debit.Id,
            Status = ReservationStatus.Reserved,
            ReservedAtUtc = calendar.UtcNow,
            IsActive = true
        };
        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        calendar.Today = new DateOnly(2026, 11, 10);
        calendar.UtcNow = new DateTime(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);

        return new Scenario(db, service, membership, session, reservation);
    }

    [Fact]
    public async Task SuspendAsync_WithEventDispatch_RefundsEachCancelledReservationExactlyOnce()
    {
        var s = await ArrangeAsync();
        await using var db = s.Db;
        Assert.Equal(7, s.Membership.AvailableCredits);

        var suspended = await s.Service.SuspendAsync(
            s.Membership.Id,
            new SuspendMembershipRequest(Reason: "Viaje", EndDate: new DateOnly(2026, 11, 14)),
            Guid.NewGuid());

        Assert.Equal(8, suspended.AvailableCredits);
        Assert.Equal(1, await db.MembershipCreditMovements.CountAsync(m =>
            m.MembershipId == s.Membership.Id && m.Type == CreditMovementType.ClassCancellationRefund));
        Assert.Equal(ReservationStatus.Cancelled, (await db.Reservations.SingleAsync(r => r.Id == s.Reservation.Id)).Status);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(cs => cs.Id == s.Session.Id)).ReservedCount);
    }

    [Fact]
    public async Task CancelAsync_WithEventDispatch_ReleasesEachReservedSeatExactlyOnce()
    {
        var s = await ArrangeAsync();
        await using var db = s.Db;

        await s.Service.CancelAsync(
            s.Membership.Id,
            new CancelMembershipRequest(Reason: "Baja voluntaria"),
            Guid.NewGuid());

        Assert.Equal(ReservationStatus.Cancelled, (await db.Reservations.SingleAsync(r => r.Id == s.Reservation.Id)).Status);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(cs => cs.Id == s.Session.Id)).ReservedCount);
    }
}
