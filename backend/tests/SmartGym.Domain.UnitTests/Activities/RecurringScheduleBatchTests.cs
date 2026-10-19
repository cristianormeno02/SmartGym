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

public class RecurringScheduleBatchTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly RecurringScheduleService _scheduleService;
    private readonly Activity _activity;
    private readonly Room _room1;
    private readonly Room _room2;
    private readonly Person _instructor1;
    private readonly Person _instructor2;

    private static readonly DateOnly MondayDate = new(2026, 3, 2);

    public RecurringScheduleBatchTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _dbContext = new SmartGymDbContext(options);

        var instructorRole = new Role { Id = (int)RoleType.Instructor, Name = RoleType.Instructor.ToString(), Description = "Instructor" };
        _instructor1 = Person.Create("Instructor", "Uno", "inst1@example.com");
        _instructor1.AddRole(instructorRole);

        _instructor2 = Person.Create("Instructor", "Dos", "inst2@example.com");
        _instructor2.AddRole(instructorRole);

        _room1 = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 30 };
        _room2 = new Room { Id = Guid.NewGuid(), Name = "Sala B", Capacity = 30 };

        _activity = new Activity("FUNCIONAL", "Funcional", defaultCapacity: 20);

        _dbContext.AddRange(instructorRole, _instructor1, _instructor2, _room1, _room2, _activity);
        _dbContext.SaveChanges();

        var mockSync = new Mock<IClassSessionScheduleSync>();
        mockSync.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _scheduleService = new RecurringScheduleService(_dbContext, mockSync.Object);
    }

    [Fact]
    public async Task CreateBatch_WhenValid_CreatesAllSchedulesWithResolvedCapacity()
    {
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, new TimeOnly(9, 30), new TimeOnly(10, 30)),
            new(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday }, new TimeOnly(16, 0), new TimeOnly(17, 0))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, null, slots);

        var result = await _scheduleService.CreateBatchAsync(request);

        Assert.Equal(6, result.Count);
        Assert.All(result, s =>
        {
            Assert.Equal(_activity.Id, s.ActivityId);
            Assert.Equal(_room1.Id, s.RoomId);
            Assert.Equal(_instructor1.Id, s.InstructorId);
            Assert.Equal(20, s.MaxCapacity); // Resuelto por la actividad
            Assert.True(s.IsActive);
        });

        // Verificación en base de datos
        var inDb = await _dbContext.RecurringSchedules.Where(s => s.ActivityId == _activity.Id).ToListAsync();
        Assert.Equal(6, inDb.Count);
    }

    [Fact]
    public async Task CreateBatch_WhenNoSlots_ThrowsArgumentException()
    {
        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, 20, Array.Empty<BatchScheduleSlotRequest>());

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateBatchAsync(request));
    }

    [Fact]
    public async Task CreateBatch_WhenSlotHasNoDays_ThrowsArgumentException()
    {
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(Array.Empty<DayOfWeek>(), new TimeOnly(9, 0), new TimeOnly(10, 0))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, 20, slots);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateBatchAsync(request));
    }

    [Fact]
    public async Task CreateBatch_WhenSlotHasDuplicateDays_ThrowsArgumentException()
    {
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday, DayOfWeek.Monday }, new TimeOnly(9, 0), new TimeOnly(10, 0))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, 20, slots);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateBatchAsync(request));
    }

    [Fact]
    public async Task CreateBatch_WhenEndTimeIsBeforeOrEqualToStartTime_ThrowsArgumentException()
    {
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday }, new TimeOnly(10, 0), new TimeOnly(9, 0))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, 20, slots);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateBatchAsync(request));
    }

    [Fact]
    public async Task CreateBatch_WhenValidToBeforeValidFrom_ThrowsArgumentException()
    {
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday }, new TimeOnly(9, 0), new TimeOnly(10, 0))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 1), 20, slots);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateBatchAsync(request));
    }

    [Fact]
    public async Task CreateBatch_WhenExceedsMax50Schedules_ThrowsArgumentException()
    {
        // Crear 11 slots con 5 días cada uno = 55 horarios
        var slots = new List<BatchScheduleSlotRequest>();
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
        for (int i = 0; i < 11; i++)
        {
            slots.Add(new BatchScheduleSlotRequest(days, new TimeOnly(7 + i, 0), new TimeOnly(7 + i, 50)));
        }

        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, 20, slots);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateBatchAsync(request));
        Assert.Contains("50", ex.Message);
    }

    [Fact]
    public async Task CreateBatch_WhenActivityNotFoundOrInactive_ThrowsArgumentException()
    {
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday }, new TimeOnly(9, 0), new TimeOnly(10, 0))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            Guid.NewGuid(), _room1.Id, _instructor1.Id, null,
            MondayDate, null, 20, slots);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateBatchAsync(request));
    }

    [Fact]
    public async Task CreateBatch_WithoutResolvableCapacity_ThrowsArgumentException()
    {
        var activityWithoutCap = new Activity("PILATES", "Pilates", defaultCapacity: null);
        _dbContext.Activities.Add(activityWithoutCap);
        _dbContext.SaveChanges();

        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday }, new TimeOnly(9, 0), new TimeOnly(10, 0))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            activityWithoutCap.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, null, slots);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.CreateBatchAsync(request));
    }

    [Fact]
    public async Task CreateBatch_WhenSlotsOverlapWithinBatch_ThrowsScheduleConflictExceptionWithWithinBatchConflict()
    {
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday }, new TimeOnly(9, 0), new TimeOnly(11, 0)),
            new(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, new TimeOnly(10, 0), new TimeOnly(12, 0)) // Monday 10:00 - 12:00 overlaps Monday 9:00 - 11:00
        };

        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, 20, slots);

        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(() => _scheduleService.CreateBatchAsync(request));

        Assert.Contains(ex.Conflicts, c => c.Kind == ScheduleConflictKind.WithinBatch && c.DayOfWeek == DayOfWeek.Monday);
        Assert.Empty(_dbContext.RecurringSchedules);
    }

    [Fact]
    public async Task CreateBatch_WhenCollidesWithExistingRoomOrInstructor_ThrowsScheduleConflictExceptionReportingAllConflicts()
    {
        // 1. Horario existente en Room 1: Lunes 09:00 - 10:00 con Instructor 2
        var existingRoom1 = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = _activity.Id,
            RoomId = _room1.Id,
            InstructorId = _instructor2.Id,
            DayOfWeek = DayOfWeek.Monday,
            MaxCapacity = 20,
            IsActive = true
        };
        existingRoom1.SetTimeSlot(new TimeOnly(9, 0), new TimeOnly(10, 0));
        existingRoom1.SetValidityPeriod(MondayDate, null);

        // 2. Horario existente para Instructor 1 en Room 2: Miércoles 15:00 - 16:00
        var existingInst1 = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = _activity.Id,
            RoomId = _room2.Id,
            InstructorId = _instructor1.Id,
            DayOfWeek = DayOfWeek.Wednesday,
            MaxCapacity = 20,
            IsActive = true
        };
        existingInst1.SetTimeSlot(new TimeOnly(15, 0), new TimeOnly(16, 0));
        existingInst1.SetValidityPeriod(MondayDate, null);

        _dbContext.RecurringSchedules.AddRange(existingRoom1, existingInst1);
        _dbContext.SaveChanges();

        // Solicitud en lote:
        // Slot 1: Lunes 09:30 - 10:30 en Room 1 con Instructor 1 (choca con Room 1)
        // Slot 2: Miércoles 15:30 - 16:30 en Room 1 con Instructor 1 (choca con Instructor 1 en Room 2)
        var slots = new List<BatchScheduleSlotRequest>
        {
            new(new[] { DayOfWeek.Monday }, new TimeOnly(9, 30), new TimeOnly(10, 30)),
            new(new[] { DayOfWeek.Wednesday }, new TimeOnly(15, 30), new TimeOnly(16, 30))
        };

        var request = new CreateBatchRecurringScheduleRequest(
            _activity.Id, _room1.Id, _instructor1.Id, null,
            MondayDate, null, 20, slots);

        var ex = await Assert.ThrowsAsync<ScheduleConflictException>(() => _scheduleService.CreateBatchAsync(request));

        // Debe reportar ambos conflictos (acumulados, no se detiene en el primero)
        Assert.Equal(2, ex.Conflicts.Count);
        Assert.Contains(ex.Conflicts, c => c.Kind == ScheduleConflictKind.Room && c.DayOfWeek == DayOfWeek.Monday);
        Assert.Contains(ex.Conflicts, c => c.Kind == ScheduleConflictKind.Instructor && c.DayOfWeek == DayOfWeek.Wednesday);

        // Verificamos que no se persistió NINGÚN horario nuevo en la base de datos
        var inDb = await _dbContext.RecurringSchedules.CountAsync();
        Assert.Equal(2, inDb); // Solo los 2 preexistentes
    }
}
