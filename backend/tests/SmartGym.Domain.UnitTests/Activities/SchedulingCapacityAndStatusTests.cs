using Microsoft.EntityFrameworkCore;
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
    private readonly ClassSessionService _sessionService;
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

        _scheduleService = new RecurringScheduleService(_dbContext);
        _sessionService = new ClassSessionService(_dbContext);
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
        new(activityId, _room.Id, _instructor.Id, DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(9, 30), Monday, null, maxCapacity);

    private ManualCreateClassSessionRequest ManualSessionRequest(Guid activityId, int maxCapacity) =>
        new(activityId, _room.Id, _instructor.Id, Monday, new TimeOnly(18, 0), new TimeOnly(19, 0), maxCapacity);

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
            _room.Id, _instructor.Id, DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(9, 30), Monday, null, 20, IsActive: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _scheduleService.UpdateAsync(schedule.Id, request));
    }

    [Fact]
    public async Task UpdateSchedule_WithoutResolvableCapacity_ShouldBeRejected()
    {
        var activity = AddActivity("RUNNING", defaultCapacity: null);
        var schedule = AddSchedule(activity, maxCapacity: 10);

        var request = new UpdateRecurringScheduleRequest(
            _room.Id, _instructor.Id, DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(9, 30), Monday, null, null, IsActive: true);

        await Assert.ThrowsAsync<ArgumentException>(() => _scheduleService.UpdateAsync(schedule.Id, request));
    }

    // --- Generación de clases ---

    [Fact]
    public async Task GenerateSessions_ShouldUseScheduleCapacity()
    {
        var activity = AddActivity("FUNCIONAL", defaultCapacity: 20);
        AddSchedule(activity, maxCapacity: 15);

        await _sessionService.GenerateSessionsFromSchedulesAsync(Monday, Monday);

        Assert.Equal(15, Assert.Single(_dbContext.ClassSessions).MaxCapacity);
    }

    [Fact]
    public async Task GenerateSessions_ForScheduleWithoutCapacity_ShouldUseActivityDefault()
    {
        var activity = AddActivity("FUNCIONAL", defaultCapacity: 20);
        AddSchedule(activity, maxCapacity: null);

        await _sessionService.GenerateSessionsFromSchedulesAsync(Monday, Monday);

        Assert.Equal(20, Assert.Single(_dbContext.ClassSessions).MaxCapacity);
    }

    [Fact]
    public async Task GenerateSessions_WithoutResolvableCapacity_ShouldBeRejectedWithoutFixedFallback()
    {
        var activity = AddActivity("RUNNING", defaultCapacity: null);
        AddSchedule(activity, maxCapacity: null);

        await Assert.ThrowsAsync<ArgumentException>(() => _sessionService.GenerateSessionsFromSchedulesAsync(Monday, Monday));

        Assert.Empty(_dbContext.ClassSessions);
    }

    [Fact]
    public async Task GenerateSessions_ShouldSkipSchedulesOfNonActiveActivities()
    {
        var active = AddActivity("FUNCIONAL", defaultCapacity: 20);
        var inactive = AddActivity("ZUMBA", defaultCapacity: 20);
        AddSchedule(active, maxCapacity: 20);
        AddSchedule(inactive, maxCapacity: 20);
        inactive.ChangeStatus(ActivityStatus.Inactive);
        _dbContext.SaveChanges();

        var generated = await _sessionService.GenerateSessionsFromSchedulesAsync(Monday, Monday);

        Assert.Equal(1, generated);
        Assert.Equal(active.Id, Assert.Single(_dbContext.ClassSessions).ActivityId);
    }

    [Fact]
    public async Task GenerateSessions_ForSpecificScheduleOfInactiveActivity_ShouldBeRejected()
    {
        var activity = AddActivity("ZUMBA", defaultCapacity: 20);
        var schedule = AddSchedule(activity, maxCapacity: 20);
        activity.ChangeStatus(ActivityStatus.Inactive);
        _dbContext.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sessionService.GenerateSessionsFromSchedulesAsync(Monday, Monday, schedule.Id));
    }

    // --- Clases manuales ---

    [Fact]
    public async Task CreateManualSession_WithoutCapacity_ShouldUseActivityDefault()
    {
        var activity = AddActivity("FUNCIONAL", defaultCapacity: 12);

        var session = await _sessionService.CreateManualAsync(ManualSessionRequest(activity.Id, maxCapacity: 0));

        Assert.Equal(12, session.MaxCapacity);
    }

    [Fact]
    public async Task CreateManualSession_WithoutResolvableCapacity_ShouldBeRejectedWithoutFixedFallback()
    {
        var activity = AddActivity("RUNNING", defaultCapacity: null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sessionService.CreateManualAsync(ManualSessionRequest(activity.Id, maxCapacity: 0)));

        Assert.Empty(_dbContext.ClassSessions);
    }

    [Fact]
    public async Task CreateManualSession_ForInactiveActivity_ShouldBeRejected()
    {
        var activity = AddActivity("FUNCIONAL", defaultCapacity: 12, ActivityStatus.Inactive);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sessionService.CreateManualAsync(ManualSessionRequest(activity.Id, maxCapacity: 10)));
    }
}
