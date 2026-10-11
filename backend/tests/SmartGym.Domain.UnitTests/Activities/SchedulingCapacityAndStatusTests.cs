using Microsoft.EntityFrameworkCore;
using Moq;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

/// <summary>
/// Resolución del cupo efectivo y restricción de programación a actividades activas
/// (delta de la spec activities-schedule del cambio activities-catalog).
/// </summary>
public class SchedulingCapacityAndStatusTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly RecurringScheduleService _scheduleService;
    private readonly Room _room;
    private readonly Person _instructor;

    // Un lunes futuro fijo para que la generación de clases sea determinista
    private static readonly DateOnly Monday = new(2030, 1, 7);

    public SchedulingCapacityAndStatusTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new SmartGymDbContext(options);

        var instructorRole = new Role { Id = (int)RoleType.Instructor, Name = RoleType.Instructor.ToString(), Description = "Instructor" };
        _instructor = Person.Create("Florencia", "Instructora", "flor@example.com");
        _instructor.AddRole(instructorRole);
        _room = new Room { Id = Guid.NewGuid(), Name = "Indoor", Capacity = 30 };

        _dbContext.AddRange(instructorRole, _instructor, _room);
        _dbContext.SaveChanges();

        var mockSync = new Mock<IClassSessionScheduleSync>();
        mockSync.Setup(s => s.SyncAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        _scheduleService = new RecurringScheduleService(_dbContext, mockSync.Object);
    }

    private Activity AddActivity(string code, int? defaultCapacity, ActivityStatus status = ActivityStatus.Active)
    {
        var activity = new Activity(code, code, defaultCapacity: defaultCapacity);
        if (status != ActivityStatus.Active)
        {
            activity.ChangeStatus(ActivityStatus.Inactive);
            if (status == ActivityStatus.Archived) activity.ChangeStatus(ActivityStatus.Archived);
        }

        _dbContext.Activities.Add(activity);
        _dbContext.SaveChanges();
        return activity;
    }

    private RecurringSchedule AddSchedule(Activity activity, int? maxCapacity)
    {
        var schedule = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = activity.Id,
            RoomId = _room.Id,
            InstructorId = _instructor.Id,
            DayOfWeek = DayOfWeek.Monday,
            MaxCapacity = maxCapacity,
            IsActive = true
        };
        schedule.SetTimeSlot(new TimeOnly(8, 30), new TimeOnly(9, 30));
        schedule.SetValidityPeriod(Monday, null);

        _dbContext.RecurringSchedules.Add(schedule);
        _dbContext.SaveChanges();
        return schedule;
    }

    private CreateRecurringScheduleRequest CreateScheduleRequest(Guid activityId, int? maxCapacity) =>
        new(activityId, _room.Id, _instructor.Id, null, DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(9, 30), Monday, null, maxCapacity);

    // --- Horarios recurrentes ---

    [Fact]
    public async Task CreateSchedule_WithoutCapacity_ShouldUseActivityDefaultCapacity()
    {
        var activity = AddActivity("FUNCIONAL", defaultCapacity: 20);

        var schedule = await _scheduleService.CreateAsync(CreateScheduleRequest(activity.Id, maxCapacity: null));

        Assert.Equal(20, schedule.MaxCapacity);
    }

    [Fact]
    public async Task CreateSchedule_WithExplicitCapacity_ShouldPreferIt()
    {
        var activity = AddActivity("FUNCIONAL", defaultCapacity: 20);

        var schedule = await _scheduleService.CreateAsync(CreateScheduleRequest(activity.Id, maxCapacity: 15));

        Assert.Equal(15, schedule.MaxCapacity);
    }

    [Fact]
    public async Task CreateSchedule_WithoutResolvableCapacity_ShouldBeRejected()
    {
        var activity = AddActivity("RUNNING", defaultCapacity: null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _scheduleService.CreateAsync(CreateScheduleRequest(activity.Id, maxCapacity: null)));

        Assert.Empty(_dbContext.RecurringSchedules);
    }

    [Theory]
    [InlineData(ActivityStatus.Inactive)]
    [InlineData(ActivityStatus.Archived)]
    public async Task CreateSchedule_ForNonActiveActivity_ShouldBeRejected(ActivityStatus status)
    {
        var activity = AddActivity("FUNCIONAL", defaultCapacity: 20, status);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _scheduleService.CreateAsync(CreateScheduleRequest(activity.Id, maxCapacity: null)));
    }

    [Fact]
    public async Task UpdateSchedule_KeepingItActiveForInactiveActivity_ShouldBeRejected()
    {
        var activity = AddActivity("FUNCIONAL", defaultCapacity: 20);
        var schedule = AddSchedule(activity, maxCapacity: 20);
        activity.ChangeStatus(ActivityStatus.Inactive);
        _dbContext.SaveChanges();

        var request = new UpdateRecurringScheduleRequest(
            _room.Id, _instructor.Id, null, DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(9, 30), Monday, null, 20, IsActive: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _scheduleService.UpdateAsync(schedule.Id, request));
    }

    [Fact]
    public async Task UpdateSchedule_WithoutResolvableCapacity_ShouldBeRejected()
    {
        var activity = AddActivity("RUNNING", defaultCapacity: null);
        var schedule = AddSchedule(activity, maxCapacity: 10);

        var request = new UpdateRecurringScheduleRequest(
            _room.Id, _instructor.Id, null, DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(9, 30), Monday, null, null, IsActive: true);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.UpdateAsync(schedule.Id, request));
    }
}
