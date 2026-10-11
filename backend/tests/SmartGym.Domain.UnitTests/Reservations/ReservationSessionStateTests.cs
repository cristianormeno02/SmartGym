using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationSessionStateTests
{
    private readonly DbContextOptions<SmartGymDbContext> _options;
    private readonly IGymCalendar _gymCalendar;
    private readonly Guid _activityId;
    private readonly Guid _roomId;
    private readonly Guid _instructorId;
    private readonly Guid _studentId = Guid.NewGuid();
    private readonly Guid _planId = Guid.NewGuid();

    public ReservationSessionStateTests()
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

        var student = Person.Create("Alumno", "Test", "student@gym.com");
        student.Id = _studentId;
        student.AddRole(studentRole);

        var instructor = Person.Create("Profe", "Test", "instructor@gym.com");
        _instructorId = instructor.Id;
        instructor.AddRole(instructorRole);

        var room = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 30, IsActive = true };
        _roomId = room.Id;

        var activity = new Activity("SPINNING", "Spinning", defaultCapacity: 20);
        _activityId = activity.Id;

        var plan = new MembershipPlan { Id = _planId, Name = "Plan Pase Libre", Credits = 100, Price = 5000, IsActive = true };

        seed.AddRange(student, instructor, room, activity, plan);
        seed.SaveChanges();
    }

    private void SetupStudentMembership(DateOnly startDate, DateOnly endDate)
    {
        using var db = new SmartGymDbContext(_options);
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = _studentId,
            MembershipPlanId = _planId,
            StartDate = startDate,
            EndDate = endDate,
            TotalCredits = 20,
            AvailableCredits = 20,
            Status = MembershipStatus.Active,
            IsActive = true
        };
        db.Memberships.Add(membership);
        db.SaveChanges();
    }

    private ReservationService CreateService(SmartGymDbContext db)
    {
        return new ReservationService(db, _gymCalendar);
    }

    [Fact]
    public async Task Reserve_WhenSessionIsScheduled_ThrowsInvalidOperationException()
    {
        using var db = new SmartGymDbContext(_options);
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activityId, _roomId, starts, ends, 15, _instructorId, null);
        // Session remains Scheduled
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync();

        var localStart = DateOnly.FromDateTime(_gymCalendar.ToLocal(starts).DateTime);
        SetupStudentMembership(localStart.AddDays(-5), localStart.AddDays(5));

        var service = CreateService(db);
        var req = new CreateReservationRequest(session.Id, _studentId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateReservationAsync(req, Guid.NewGuid()));
        Assert.Contains("Scheduled", ex.Message);
    }

    [Fact]
    public async Task Reserve_WhenSessionIsInTerminalState_ThrowsInvalidOperationException()
    {
        using var db = new SmartGymDbContext(_options);
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activityId, _roomId, starts, ends, 15, _instructorId, null);
        session.Cancel(ClassSessionCancellationReason.Other, "Cancelada por fuerza mayor", null, DateTime.UtcNow);
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync();

        var localStart = DateOnly.FromDateTime(_gymCalendar.ToLocal(starts).DateTime);
        SetupStudentMembership(localStart.AddDays(-5), localStart.AddDays(5));

        var service = CreateService(db);
        var req = new CreateReservationRequest(session.Id, _studentId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateReservationAsync(req, Guid.NewGuid()));
        Assert.Contains("Cancelled", ex.Message);
    }

    [Fact]
    public async Task Reserve_WhenSessionIsOpen_Succeeds()
    {
        using var db = new SmartGymDbContext(_options);
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activityId, _roomId, starts, ends, 15, _instructorId, null);
        session.Open(DateTime.UtcNow);
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync();

        var localStart = DateOnly.FromDateTime(_gymCalendar.ToLocal(starts).DateTime);
        SetupStudentMembership(localStart.AddDays(-5), localStart.AddDays(5));

        var service = CreateService(db);
        var req = new CreateReservationRequest(session.Id, _studentId);

        var result = await service.CreateReservationAsync(req, Guid.NewGuid());
        Assert.NotNull(result);
        Assert.Equal(ReservationStatus.Reserved, result.Status);
    }

    [Fact]
    public async Task Reserve_ValidatesMembershipAgainstLocalSessionDate()
    {
        // Supongamos que la clase es mañana a las 22:00 San Juan (UTC-3).
        // En UTC es el día siguiente a las 01:00 UTC.
        // La membresía termina mañana: DEBE ser válida porque la clase en fecha local es mañana.
        var localDate = _gymCalendar.Today.AddDays(1);
        var localTime = new TimeOnly(22, 0);
        var startsUtc = _gymCalendar.ToUtc(localDate, localTime);
        var endsUtc = startsUtc.AddHours(1);

        using var db = new SmartGymDbContext(_options);
        var session = ClassSession.CreateExtraordinary(_activityId, _roomId, startsUtc, endsUtc, 15, _instructorId, null);
        session.Open(DateTime.UtcNow);
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync();

        // Membresía válida hasta localDate (en UTC la sesión cae en localDate + 1 día)
        SetupStudentMembership(localDate.AddDays(-8), localDate);

        var service = CreateService(db);
        var req = new CreateReservationRequest(session.Id, _studentId);

        var result = await service.CreateReservationAsync(req, Guid.NewGuid());
        Assert.NotNull(result);
        Assert.Equal(ReservationStatus.Reserved, result.Status);
    }

    [Fact]
    public async Task CancelReservation_CalculatesLateCancellationCorrectlyUsingStartsAtUtc()
    {
        using var db = new SmartGymDbContext(_options);
        // Sesión que empieza en 1 hora (menos de 2 horas)
        var starts = DateTime.UtcNow.AddHours(1);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activityId, _roomId, starts, ends, 15, _instructorId, null);
        session.Open(DateTime.UtcNow);
        db.ClassSessions.Add(session);
        await db.SaveChangesAsync();

        var localStart = DateOnly.FromDateTime(_gymCalendar.ToLocal(starts).DateTime);
        SetupStudentMembership(localStart.AddDays(-5), localStart.AddDays(5));

        var service = CreateService(db);
        var reservationDto = await service.CreateReservationAsync(new CreateReservationRequest(session.Id, _studentId), Guid.NewGuid());

        var cancelResult = await service.CancelReservationAsync(reservationDto.Id, new CancelReservationRequest("Problema personal"), Guid.NewGuid());
        Assert.True(cancelResult.IsLateCancellation);
    }
}
