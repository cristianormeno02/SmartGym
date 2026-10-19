using Microsoft.EntityFrameworkCore;
using Moq;
using SmartGym.Application.Modules.Activities;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class RecurringScheduleCollisionTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly RecurringScheduleService _scheduleService;
    private readonly Activity _activity;
    private readonly Room _room1;
    private readonly Room _room2;
    private readonly Person _instructor1;
    private readonly Person _instructor2;

    private static readonly DateOnly MondayDate = new(2026, 3, 2);

    public RecurringScheduleCollisionTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new SmartGymDbContext(options);

        var instructorRole = new Role { Id = (int)RoleType.Instructor, Name = RoleType.Instructor.ToString(), Description = "Instructor" };
        _instructor1 = Person.Create("Instructor", "Uno", "inst1@example.com");
        _instructor1.AddRole(instructorRole);

        _instructor2 = Person.Create("Instructor", "Dos", "inst2@example.com");
        _instructor2.AddRole(instructorRole);

        _room1 = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 20 };
        _room2 = new Room { Id = Guid.NewGuid(), Name = "Sala B", Capacity = 20 };

        _activity = new Activity("FUNCIONAL", "Funcional", defaultCapacity: 20);

        _dbContext.AddRange(instructorRole, _instructor1, _instructor2, _room1, _room2, _activity);
        _dbContext.SaveChanges();

        var mockSync = new Mock<IClassSessionScheduleSync>();
        mockSync.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _scheduleService = new RecurringScheduleService(_dbContext, mockSync.Object);
    }

    [Fact]
    public async Task CreateSchedule_WhenRoomCollision_ShouldThrowScheduleConflictExceptionWithRoomConflict()
    {
        // Horario existente: Sala 1, Instructor 1, Lunes 18:00 - 19:00
        var req1 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);
        await _scheduleService.CreateAsync(req1);

        // Intento de horario: Sala 1, Instructor 2 (otro instructor), Lunes 18:30 - 19:30 (solapado)
        var req2 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor2.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 30), new TimeOnly(19, 30),
            MondayDate, null, 20);

        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(() => _scheduleService.CreateAsync(req2));

        Assert.Single(ex.Conflicts);
        var conflict = ex.Conflicts[0];
        Assert.Equal(ScheduleConflictKind.Room, conflict.Kind);
        Assert.Equal(DayOfWeek.Monday, conflict.DayOfWeek);
        Assert.Equal(new TimeOnly(18, 30), conflict.StartTime);
        Assert.Equal(new TimeOnly(19, 30), conflict.EndTime);
    }

    [Fact]
    public async Task CreateSchedule_WhenInstructorCollisionInDifferentRoom_ShouldThrowScheduleConflictExceptionWithInstructorConflict()
    {
        // Horario existente: Sala 1, Instructor 1, Lunes 18:00 - 19:00
        var req1 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);
        await _scheduleService.CreateAsync(req1);

        // Intento de horario: Sala 2 (otra sala), mismo Instructor 1, Lunes 18:30 - 19:30 (solapado)
        var req2 = new CreateRecurringScheduleRequest(
            _activity.Id, _room2.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 30), new TimeOnly(19, 30),
            MondayDate, null, 20);

        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(() => _scheduleService.CreateAsync(req2));

        Assert.Single(ex.Conflicts);
        var conflict = ex.Conflicts[0];
        Assert.Equal(ScheduleConflictKind.Instructor, conflict.Kind);
        Assert.Equal(DayOfWeek.Monday, conflict.DayOfWeek);
    }

    [Fact]
    public async Task CreateSchedule_WhenContiguousTime_ShouldNotCollide()
    {
        // Horario 1: Sala 1, Instructor 1, Lunes 18:00 - 19:00
        var req1 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);
        await _scheduleService.CreateAsync(req1);

        // Horario 2: Sala 1, Instructor 1, Lunes 19:00 - 20:00 (contiguo exacto)
        var req2 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(19, 0), new TimeOnly(20, 0),
            MondayDate, null, 20);

        var created2 = await _scheduleService.CreateAsync(req2);
        Assert.NotNull(created2);
    }

    [Fact]
    public async Task CreateSchedule_WhenDisjointValidities_ShouldNotCollide()
    {
        // Horario 1: Sala 1, Instructor 1, Lunes 18:00 - 19:00 (vigente hasta 30 de Junio)
        var req1 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), 20);
        await _scheduleService.CreateAsync(req1);

        // Horario 2: Misma sala, mismo instructor, mismo horario (vigente a partir de 1 de Julio)
        var req2 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            new DateOnly(2026, 7, 1), new DateOnly(2026, 12, 31), 20);

        var created2 = await _scheduleService.CreateAsync(req2);
        Assert.NotNull(created2);
    }

    [Fact]
    public async Task UpdateSchedule_WhenInstructorCollisionInDifferentRoom_ShouldThrowScheduleConflictException()
    {
        // Horario 1: Sala 1, Instructor 1, Lunes 18:00 - 19:00
        var req1 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);
        await _scheduleService.CreateAsync(req1);

        // Horario 2: Sala 2, Instructor 2, Lunes 18:00 - 19:00
        var req2 = new CreateRecurringScheduleRequest(
            _activity.Id, _room2.Id, _instructor2.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);
        var created2 = await _scheduleService.CreateAsync(req2);

        // Intentar actualizar Horario 2 para que lo dicte Instructor 1 (choca con Horario 1)
        var updateReq = new UpdateRecurringScheduleRequest(
            _room2.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20, IsActive: true);

        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(() => _scheduleService.UpdateAsync(created2.Id, updateReq));
        Assert.Single(ex.Conflicts);
        Assert.Equal(ScheduleConflictKind.Instructor, ex.Conflicts[0].Kind);
    }

    [Fact]
    public async Task UpdateSchedule_WhenSameScheduleWithoutChanges_ShouldNotCollideWithItself()
    {
        var req1 = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);
        var created = await _scheduleService.CreateAsync(req1);

        var updateReq = new UpdateRecurringScheduleRequest(
            _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 25, IsActive: true);

        var updated = await _scheduleService.UpdateAsync(created.Id, updateReq);
        Assert.NotNull(updated);
        Assert.Equal(25, updated.MaxCapacity);
    }
}
