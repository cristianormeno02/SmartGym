using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SmartGym.Application.Common;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionGeneratorTests
{
    private readonly SmartGymDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly Mock<IAuditService> _mockAudit = new();
    private readonly Mock<ILogger<ClassSessionGenerator>> _mockLogger = new();
    private readonly Room _room;
    private readonly Person _instructor;
    private readonly Person _secretary;

    private static readonly DateOnly Monday = new(2030, 1, 7);

    public ClassSessionGeneratorTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new SmartGymDbContext(options);

        _gymCalendar = new GymCalendar(
            Options.Create(new GymOptions { TimeZoneId = "America/Argentina/San_Juan" }),
            TimeProvider.System);

        var instructorRole = new Role { Id = (int)RoleType.Instructor, Name = RoleType.Instructor.ToString(), Description = "Instructor" };
        var secretaryRole = new Role { Id = (int)RoleType.Secretary, Name = RoleType.Secretary.ToString(), Description = "Secretary" };
        _instructor = Person.Create("Florencia", "Instructora", "flor@example.com");
        _instructor.AddRole(instructorRole);
        _secretary = Person.Create("Carla", "Secretaria", "carla@example.com");
        _secretary.AddRole(secretaryRole);

        _room = new Room { Id = Guid.NewGuid(), Name = "Indoor", Capacity = 30 };

        _dbContext.AddRange(instructorRole, secretaryRole, _instructor, _secretary, _room);
        _dbContext.SaveChanges();
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

    private RecurringSchedule AddSchedule(Activity activity, int? maxCapacity, DayOfWeek day = DayOfWeek.Monday)
    {
        var schedule = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = activity.Id,
            RoomId = _room.Id,
            InstructorId = _instructor.Id,
            SecretaryId = _secretary.Id,
            DayOfWeek = day,
            MaxCapacity = maxCapacity,
            IsActive = true
        };
        schedule.SetTimeSlot(new TimeOnly(18, 0), new TimeOnly(19, 0));
        schedule.SetValidityPeriod(Monday, Monday.AddDays(60));

        _dbContext.RecurringSchedules.Add(schedule);
        _dbContext.SaveChanges();
        return schedule;
    }

    private ClassSessionGenerator CreateGenerator(ClassSessionsOptions? options = null)
    {
        return new ClassSessionGenerator(
            _dbContext,
            _gymCalendar,
            Options.Create(options ?? new ClassSessionsOptions()),
            _mockAudit.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task Generate_CreatesSessionsWithCorrectUtcAndStaffAndCapacity()
    {
        var activity = AddActivity("ZUMBA", defaultCapacity: 20);
        var schedule = AddSchedule(activity, maxCapacity: 15);
        var generator = CreateGenerator();

        var result = await generator.GenerateAsync(Monday, Monday);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Skipped);
        Assert.Empty(result.Conflicts);

        var session = Assert.Single(_dbContext.ClassSessions);
        Assert.Equal(schedule.Id, session.RecurringScheduleId);
        Assert.Equal(15, session.Capacity);
        Assert.Equal(_instructor.Id, session.ScheduledInstructorId);
        Assert.Equal(_instructor.Id, session.EffectiveInstructorId);
        Assert.Equal(_secretary.Id, session.ScheduledSecretaryId);
        Assert.Equal(_secretary.Id, session.EffectiveSecretaryId);
        Assert.Equal(ClassSessionStatus.Scheduled, session.Status);

        // Horario 18:00 local en San Juan (UTC-3) => 21:00 UTC
        Assert.Equal(new DateTime(2030, 1, 7, 21, 0, 0, DateTimeKind.Utc), session.StartsAtUtc);
    }

    [Fact]
    public async Task Generate_FallsBackToActivityDefaultCapacity_WhenScheduleCapacityIsNull()
    {
        var activity = AddActivity("YOGA", defaultCapacity: 25);
        AddSchedule(activity, maxCapacity: null);
        var generator = CreateGenerator();

        var result = await generator.GenerateAsync(Monday, Monday);

        Assert.Equal(1, result.Created);
        var session = Assert.Single(_dbContext.ClassSessions);
        Assert.Equal(25, session.Capacity);
    }

    [Fact]
    public async Task Generate_ReportsNoCapacityConflict_WhenCapacityCannotBeResolved()
    {
        var activity = AddActivity("RUNNING", defaultCapacity: null);
        AddSchedule(activity, maxCapacity: null);
        var generator = CreateGenerator();

        var result = await generator.GenerateAsync(Monday, Monday);

        Assert.Equal(0, result.Created);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("NO_CAPACITY", conflict.ConflictType);
        Assert.Equal(Monday, conflict.Date);
        Assert.Empty(_dbContext.ClassSessions);
    }

    [Fact]
    public async Task Generate_SkipsAlreadyExistingOccurrence_EvenIfCancelled()
    {
        var activity = AddActivity("PILATES", defaultCapacity: 10);
        var schedule = AddSchedule(activity, maxCapacity: 10);
        var generator = CreateGenerator();

        // Primera generación
        var res1 = await generator.GenerateAsync(Monday, Monday);
        Assert.Equal(1, res1.Created);

        // Cancelamos la sesión existente
        var session = Assert.Single(_dbContext.ClassSessions);
        session.Cancel(ClassSessionCancellationReason.AdministrativeDecision, "Cancelada administrativamente", null, DateTime.UtcNow);
        _dbContext.SaveChanges();

        // Segunda ejecución sobre el mismo rango
        var res2 = await generator.GenerateAsync(Monday, Monday);
        Assert.Equal(0, res2.Created);
        Assert.Equal(1, res2.Skipped);
        Assert.Single(_dbContext.ClassSessions); // Sigue habiendo solo 1
    }

    [Fact]
    public async Task Generate_ReportsRoomOverlapConflict_WithoutAborting()
    {
        var act1 = AddActivity("BOX", defaultCapacity: 10);
        var act2 = AddActivity("KICKBOX", defaultCapacity: 10);

        var sch1 = AddSchedule(act1, 10);
        var sch2 = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = act2.Id,
            RoomId = _room.Id, // Misma sala
            InstructorId = Guid.NewGuid(), // Distinto instructor
            DayOfWeek = DayOfWeek.Monday,
            MaxCapacity = 10,
            IsActive = true
        };
        sch2.SetTimeSlot(new TimeOnly(18, 0), new TimeOnly(19, 0)); // Mismo horario
        sch2.SetValidityPeriod(Monday, Monday.AddDays(60));
        _dbContext.RecurringSchedules.Add(sch2);
        _dbContext.SaveChanges();

        var generator = CreateGenerator();
        var result = await generator.GenerateAsync(Monday, Monday);

        Assert.Equal(1, result.Created);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("ROOM_OVERLAP", conflict.ConflictType);
    }

    [Fact]
    public async Task Generate_ReportsInstructorOverlapConflict_WithoutAborting()
    {
        var room2 = new Room { Id = Guid.NewGuid(), Name = "Outdoor", Capacity = 50 };
        _dbContext.Rooms.Add(room2);
        _dbContext.SaveChanges();

        var act1 = AddActivity("BOX", defaultCapacity: 10);
        var act2 = AddActivity("KICKBOX", defaultCapacity: 10);

        AddSchedule(act1, 10); // En _room con _instructor
        var sch2 = new RecurringSchedule
        {
            Id = Guid.NewGuid(),
            ActivityId = act2.Id,
            RoomId = room2.Id, // Distinta sala
            InstructorId = _instructor.Id, // Mismo instructor
            DayOfWeek = DayOfWeek.Monday,
            MaxCapacity = 10,
            IsActive = true
        };
        sch2.SetTimeSlot(new TimeOnly(18, 0), new TimeOnly(19, 0)); // Mismo horario
        sch2.SetValidityPeriod(Monday, Monday.AddDays(60));
        _dbContext.RecurringSchedules.Add(sch2);
        _dbContext.SaveChanges();

        var generator = CreateGenerator();
        var result = await generator.GenerateAsync(Monday, Monday);

        Assert.Equal(1, result.Created);
        var conflict = Assert.Single(result.Conflicts);
        Assert.Equal("INSTRUCTOR_OVERLAP", conflict.ConflictType);
    }

    [Fact]
    public async Task Generate_SkipsInactiveSchedulesAndNonActiveActivities()
    {
        var inactiveAct = AddActivity("SPINNING", 10, ActivityStatus.Inactive);
        AddSchedule(inactiveAct, 10);

        var activeAct = AddActivity("FUNCTIONAL", 10);
        var inactiveSch = AddSchedule(activeAct, 10);
        inactiveSch.IsActive = false;
        _dbContext.SaveChanges();

        var generator = CreateGenerator();
        var result = await generator.GenerateAsync(Monday, Monday);

        Assert.Equal(0, result.Created);
        Assert.Empty(_dbContext.ClassSessions);
    }

    [Fact]
    public async Task AutoOpen_OpensScheduledSessionsWithinLeadTime_AndLeavesOthersScheduled()
    {
        var activity = AddActivity("ZUMBA", 20);
        var starts1 = new DateTime(2030, 1, 10, 18, 0, 0, DateTimeKind.Utc); // En 3 días
        var starts2 = new DateTime(2030, 1, 20, 18, 0, 0, DateTimeKind.Utc); // En 13 días

        var session1 = ClassSession.CreateFromSchedule(Guid.NewGuid(), activity.Id, _room.Id, starts1, starts1.AddHours(1), 20, _instructor.Id, null);
        var session2 = ClassSession.CreateFromSchedule(Guid.NewGuid(), activity.Id, _room.Id, starts2, starts2.AddHours(1), 20, _instructor.Id, null);

        _dbContext.ClassSessions.AddRange(session1, session2);
        _dbContext.SaveChanges();

        var now = new DateTime(2030, 1, 7, 12, 0, 0, DateTimeKind.Utc);
        var generator = CreateGenerator(new ClassSessionsOptions { AutoOpenLeadTime = TimeSpan.FromDays(7) });

        var opened = await generator.AutoOpenAsync(now);

        Assert.Equal(1, opened);
        Assert.Equal(ClassSessionStatus.Open, session1.Status);
        Assert.Equal(ClassSessionStatus.Scheduled, session2.Status);
    }

    [Fact]
    public async Task AutoOpen_DoesNotOpenExpiredOrTerminalSessions()
    {
        var activity = AddActivity("PILATES", 15);
        var now = new DateTime(2030, 1, 7, 12, 0, 0, DateTimeKind.Utc);

        // Expired (starts in the past)
        var pastStarts = now.AddHours(-1);
        var expiredSession = ClassSession.CreateFromSchedule(Guid.NewGuid(), activity.Id, _room.Id, pastStarts, pastStarts.AddHours(1), 15, _instructor.Id, null);

        // Terminal (Suspended) within lead time
        var futureStarts = now.AddDays(2);
        var suspendedSession = ClassSession.CreateFromSchedule(Guid.NewGuid(), activity.Id, _room.Id, futureStarts, futureStarts.AddHours(1), 15, _instructor.Id, null);
        suspendedSession.Suspend(ClassSessionSuspensionReason.Weather, "Lluvia", null, now);

        _dbContext.ClassSessions.AddRange(expiredSession, suspendedSession);
        _dbContext.SaveChanges();

        var generator = CreateGenerator(new ClassSessionsOptions { AutoOpenLeadTime = TimeSpan.FromDays(7) });
        var opened = await generator.AutoOpenAsync(now);

        Assert.Equal(0, opened);
        Assert.Equal(ClassSessionStatus.Scheduled, expiredSession.Status);
        Assert.Equal(ClassSessionStatus.Suspended, suspendedSession.Status);
    }

    [Fact]
    public async Task AutoOpen_WhenLeadTimeIsNull_DoesNotOpenAnySession()
    {
        var activity = AddActivity("YOGA", 15);
        var now = new DateTime(2030, 1, 7, 12, 0, 0, DateTimeKind.Utc);
        var futureStarts = now.AddDays(2);

        var session = ClassSession.CreateFromSchedule(Guid.NewGuid(), activity.Id, _room.Id, futureStarts, futureStarts.AddHours(1), 15, _instructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        _dbContext.SaveChanges();

        var generator = CreateGenerator(new ClassSessionsOptions { AutoOpenLeadTime = null });
        var opened = await generator.AutoOpenAsync(now);

        Assert.Equal(0, opened);
        Assert.Equal(ClassSessionStatus.Scheduled, session.Status);
    }
}
