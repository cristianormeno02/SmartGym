using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

/// <summary>
/// Reglas de la actividad que valida la reserva: estado ACTIVE y rango etario
/// (delta de la spec reservations-attendance del cambio activities-catalog).
/// </summary>
public class ReservationActivityRulesTests
{
    private readonly DbContextOptions<SmartGymDbContext> _options;
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _studentId = Guid.NewGuid();
    private readonly Guid _activityId;

    public ReservationActivityRulesTests()
    {
        _options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var seed = new SmartGymDbContext(_options);
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        seed.Roles.Add(studentRole);

        var instructor = Person.Create("Florencia", "Inst", "flor@example.com");
        var room = new Room { Id = Guid.NewGuid(), Name = "Indoor", Capacity = 20 };
        var activity = new Activity("INFANTIL", "Gimnasia Infantil", defaultCapacity: 10, minAge: 6, maxAge: 12);
        _activityId = activity.Id;
        var classDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));

        var session = new ClassSession
        {
            Id = _sessionId,
            ActivityId = activity.Id,
            Activity = activity,
            RoomId = room.Id,
            Room = room,
            InstructorId = instructor.Id,
            Instructor = instructor,
            Date = classDate,
            StartTime = new TimeOnly(17, 0),
            EndTime = new TimeOnly(18, 0),
            MaxCapacity = 10
        };

        var plan = new MembershipPlan { Id = Guid.NewGuid(), Name = "Mensual 8", Credits = 8, Price = 1000 };
        seed.AddRange(instructor, room, activity, session, plan);
        seed.SaveChanges();
    }

    private void AddStudent(DateTime? birthDate)
    {
        using var db = new SmartGymDbContext(_options);
        var studentRole = db.Roles.Single(r => r.Id == (int)RoleType.Student);
        var plan = db.MembershipPlans.Single();
        var session = db.ClassSessions.Single();

        // Los menores requieren contacto de emergencia completo
        var student = Person.Create("Juan", "Pérez", "juan@example.com", birthDate: birthDate,
            emergencyContact: EmergencyContact.Create("Madre Tutora", "+54911111111", "Madre"));
        student.Id = _studentId;
        student.AddRole(studentRole);

        db.AddRange(student, new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = _studentId,
            MembershipPlanId = plan.Id,
            StartDate = session.Date.AddDays(-10),
            EndDate = session.Date.AddDays(10),
            TotalCredits = 8,
            AvailableCredits = 8
        });
        db.SaveChanges();
    }

    private void SetActivityStatus(ActivityStatus status)
    {
        using var db = new SmartGymDbContext(_options);
        var activity = db.Activities.Single(a => a.Id == _activityId);
        activity.ChangeStatus(ActivityStatus.Inactive);
        if (status == ActivityStatus.Archived) activity.ChangeStatus(ActivityStatus.Archived);
        db.SaveChanges();
    }

    private Task<ReservationDto> Reserve()
    {
        var db = new SmartGymDbContext(_options);
        return new ReservationService(db).CreateReservationAsync(new CreateReservationRequest(_sessionId, _studentId), Guid.NewGuid());
    }

    private static DateTime BirthDateForAge(int age) => DateTime.UtcNow.Date.AddYears(-age).AddDays(-1);

    [Fact]
    public async Task Reserve_WithinAgeRange_ShouldSucceed()
    {
        AddStudent(BirthDateForAge(8));

        var reservation = await Reserve();

        Assert.Equal(_sessionId, reservation.ClassSessionId);
    }

    [Fact]
    public async Task Reserve_OutsideAgeRange_ShouldBeRejected()
    {
        AddStudent(BirthDateForAge(15));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(Reserve);
        Assert.Contains("no admite", ex.Message);
    }

    [Fact]
    public async Task Reserve_StudentWithoutBirthDate_ShouldSkipAgeRestriction()
    {
        AddStudent(birthDate: null);

        var reservation = await Reserve();

        Assert.Equal(_sessionId, reservation.ClassSessionId);
    }

    [Theory]
    [InlineData(ActivityStatus.Inactive)]
    [InlineData(ActivityStatus.Archived)]
    public async Task Reserve_WhenActivityIsNotActive_ShouldBeRejected(ActivityStatus status)
    {
        AddStudent(BirthDateForAge(8));
        SetActivityStatus(status);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(Reserve);
        Assert.Contains("no está disponible", ex.Message);
    }
}
