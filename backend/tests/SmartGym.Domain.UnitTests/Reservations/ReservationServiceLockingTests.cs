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
/// Verifica que las operaciones que modifican el cupo serialicen el acceso a la clase
/// (bloqueo de fila dentro de la transacción) antes de leer su estado.
/// </summary>
public class ReservationServiceLockingTests
{
    // InMemory ignora las transacciones; que el bloqueo ocurra dentro de una lo garantiza
    // SmartGymDbContext.LockClassSessionForUpdateAsync en PostgreSQL (lanza si no hay transacción activa).
    private sealed record LockCall(Guid ClassSessionId, bool SessionAlreadyLoaded);

    private sealed class LockSpyDbContext : SmartGymDbContext
    {
        public List<LockCall> LockCalls { get; } = new();

        public LockSpyDbContext(DbContextOptions<SmartGymDbContext> options) : base(options) { }

        public override Task LockClassSessionForUpdateAsync(Guid classSessionId, CancellationToken cancellationToken = default)
        {
            LockCalls.Add(new LockCall(
                classSessionId,
                ChangeTracker.Entries<ClassSession>().Any(e => e.Entity.Id == classSessionId)));
            return Task.CompletedTask;
        }
    }

    private readonly LockSpyDbContext _dbContext;
    private readonly ReservationService _service;
    private readonly Guid _sessionId = Guid.NewGuid();
    private readonly Guid _studentId = Guid.NewGuid();

    public ReservationServiceLockingTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var seed = new SmartGymDbContext(options);
        var studentRole = new Role { Id = (int)RoleType.Student, Name = "Student", Description = "Student" };
        seed.Roles.Add(studentRole);

        var student = new Person { Id = _studentId, FirstName = "Juan", LastName = "Pérez", Dni = "30000001", Email = "juan@example.com" };
        student.AddRole(studentRole);
        var instructor = new Person { Id = Guid.NewGuid(), FirstName = "Florencia", LastName = "Inst", Dni = "30000002", Email = "flor@example.com" };
        var room = new Room { Id = Guid.NewGuid(), Name = "Indoor", Capacity = 20 };
        var activity = new Activity { Id = Guid.NewGuid(), Name = "Funcional", MaxCapacity = 10 };
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
            StartTime = new TimeOnly(8, 30),
            EndTime = new TimeOnly(9, 30),
            MaxCapacity = 10
        };

        var plan = new MembershipPlan { Id = Guid.NewGuid(), Name = "Mensual 8", Credits = 8, Price = 1000 };
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            StudentId = _studentId,
            Student = student,
            MembershipPlanId = plan.Id,
            MembershipPlan = plan,
            StartDate = classDate.AddDays(-10),
            EndDate = classDate.AddDays(10),
            TotalCredits = 8,
            AvailableCredits = 8
        };

        seed.AddRange(student, instructor, room, activity, session, plan, membership);
        seed.SaveChanges();

        _dbContext = new LockSpyDbContext(options);
        _service = new ReservationService(_dbContext);
    }

    [Fact]
    public async Task CreateReservationAsync_ShouldLockClassSession_BeforeReadingIt()
    {
        await _service.CreateReservationAsync(new CreateReservationRequest(_sessionId, _studentId), Guid.NewGuid());

        var call = Assert.Single(_dbContext.LockCalls);
        Assert.Equal(_sessionId, call.ClassSessionId);
        Assert.False(call.SessionAlreadyLoaded);
    }

    [Fact]
    public async Task CancelReservationAsync_ShouldLockClassSession_BeforeReadingIt()
    {
        var reservation = await _service.CreateReservationAsync(new CreateReservationRequest(_sessionId, _studentId), Guid.NewGuid());
        _dbContext.ChangeTracker.Clear();
        _dbContext.LockCalls.Clear();

        await _service.CancelReservationAsync(reservation.Id, new CancelReservationRequest("No puedo asistir"), Guid.NewGuid());

        var call = Assert.Single(_dbContext.LockCalls);
        Assert.Equal(_sessionId, call.ClassSessionId);
        Assert.False(call.SessionAlreadyLoaded);
    }
}
