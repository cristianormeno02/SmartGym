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

        _activity = new Activity("FUNCIONAL", "Funcional", colorHex: "#FF5733", defaultCapacity: 20);

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

    [Fact]
    public async Task UpdateSchedule_WhenDeactivatingScheduleOfInactiveActivity_ShouldSucceed()
    {
        var created = await _scheduleService.CreateAsync(new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20));

        _activity.ChangeStatus(ActivityStatus.Inactive);
        await _dbContext.SaveChangesAsync();

        var updateReq = new UpdateRecurringScheduleRequest(
            _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20, IsActive: false);

        var updated = await _scheduleService.UpdateAsync(created.Id, updateReq);

        Assert.NotNull(updated);
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task UpdateSchedule_WhenKeepingActiveScheduleOfInactiveActivity_ShouldThrowInvalidOperationException()
    {
        var created = await _scheduleService.CreateAsync(new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20));

        _activity.ChangeStatus(ActivityStatus.Inactive);
        await _dbContext.SaveChangesAsync();

        var updateReq = new UpdateRecurringScheduleRequest(
            _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20, IsActive: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _scheduleService.UpdateAsync(created.Id, updateReq));
    }

    [Fact]
    public async Task CreateAndQuerySchedule_ShouldReturnActivityColorHex()
    {
        var req = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);

        var created = await _scheduleService.CreateAsync(req);
        Assert.Equal("#FF5733", created.ActivityColorHex);

        var byId = await _scheduleService.GetByIdAsync(created.Id);
        Assert.NotNull(byId);
        Assert.Equal("#FF5733", byId.ActivityColorHex);

        var all = await _scheduleService.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("#FF5733", all[0].ActivityColorHex);
    }

    [Fact]
    public async Task CreateSchedule_WhenSecretaryHasActiveUserAndSecretaryRole_ShouldSucceed()
    {
        var secretaryRole = new Role { Id = (int)RoleType.Secretary, Name = RoleType.Secretary.ToString(), Description = "Secretary" };
        var secretary = Person.Create("Ana", "Secretaria", "ana@example.com");
        secretary.AddRole(secretaryRole);
        secretary.SetUser(new User { Id = Guid.NewGuid(), Username = "ana", IsActive = true });

        _dbContext.AddRange(secretaryRole, secretary, secretary.User!);
        await _dbContext.SaveChangesAsync();

        var req = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, secretary.Id,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);

        var created = await _scheduleService.CreateAsync(req);
        Assert.Equal(secretary.Id, created.SecretaryId);
        Assert.Equal("Ana Secretaria", created.SecretaryName);
    }

    [Fact]
    public async Task CreateAndUpdateSchedule_WhenSecretaryHasNoUserAccount_ShouldThrowArgumentException()
    {
        var secretaryRole = new Role { Id = (int)RoleType.Secretary, Name = RoleType.Secretary.ToString(), Description = "Secretary" };
        var personWithoutUser = Person.Create("Pedro", "SinUsuario", "pedro@example.com");
        personWithoutUser.AddRole(secretaryRole);

        _dbContext.AddRange(secretaryRole, personWithoutUser);
        await _dbContext.SaveChangesAsync();

        var createReq = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, personWithoutUser.Id,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateAsync(createReq));

        var validSchedule = await _scheduleService.CreateAsync(createReq with { SecretaryId = null });
        var updateReq = new UpdateRecurringScheduleRequest(
            _room1.Id, _instructor1.Id, personWithoutUser.Id,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20, IsActive: true);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.UpdateAsync(validSchedule.Id, updateReq));
    }

    [Fact]
    public async Task CreateSchedule_WhenSecretaryUserIsInactive_ShouldThrowArgumentException()
    {
        var secretaryRole = new Role { Id = (int)RoleType.Secretary, Name = RoleType.Secretary.ToString(), Description = "Secretary" };
        var secretary = Person.Create("Laura", "Inactiva", "laura@example.com");
        secretary.AddRole(secretaryRole);
        secretary.SetUser(new User { Id = Guid.NewGuid(), Username = "laura", IsActive = false });

        _dbContext.AddRange(secretaryRole, secretary, secretary.User!);
        await _dbContext.SaveChangesAsync();

        var req = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, secretary.Id,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateAsync(req));
    }

    [Fact]
    public async Task CreateSchedule_WhenSecretaryOnlyHasAdministratorRole_ShouldThrowArgumentException()
    {
        var adminRole = new Role { Id = (int)RoleType.Administrator, Name = RoleType.Administrator.ToString(), Description = "Admin" };
        var adminOnly = Person.Create("Admin", "Puro", "admin@example.com");
        adminOnly.AddRole(adminRole);
        adminOnly.SetUser(new User { Id = Guid.NewGuid(), Username = "admin", IsActive = true });

        _dbContext.AddRange(adminRole, adminOnly, adminOnly.User!);
        await _dbContext.SaveChangesAsync();

        var req = new CreateRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, adminOnly.Id,
            DayOfWeek.Monday, new TimeOnly(18, 0), new TimeOnly(19, 0),
            MondayDate, null, 20);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateAsync(req));
    }
}
