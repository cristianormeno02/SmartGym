using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Modules.Activities.Dtos;
using SmartGym.Application.Modules.Activities.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Activities;

public class ClassSessionServiceTests
{
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

        public int DeferOverlapCalls { get; private set; }
        public int EnforceDeferredCalls { get; private set; }

        public override Task DeferClassSessionOverlapConstraintsAsync(CancellationToken cancellationToken = default)
        {
            DeferOverlapCalls++;
            return Task.CompletedTask;
        }

        public override Task EnforceDeferredConstraintsAsync(CancellationToken cancellationToken = default)
        {
            EnforceDeferredCalls++;
            return Task.CompletedTask;
        }
    }

    private readonly LockSpyDbContext _dbContext;
    private readonly IGymCalendar _gymCalendar;
    private readonly Mock<ICurrentUserService> _mockCurrentUser = new();
    private readonly Mock<IAuditService> _mockAudit = new();
    private readonly ClassSessionService _service;

    private readonly Activity _activity;
    private readonly Room _room;
    private readonly Person _instructor;
    private readonly Person _otherInstructor;
    private readonly Person _secretary;
    private readonly Guid _currentUserId = Guid.NewGuid();

    public ClassSessionServiceTests()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var seed = new SmartGymDbContext(options);

        var instructorRole = new Role { Id = (int)RoleType.Instructor, Name = "Instructor", Description = "Instructor" };
        var secretaryRole = new Role { Id = (int)RoleType.Secretary, Name = "Secretary", Description = "Secretary" };
        var adminRole = new Role { Id = (int)RoleType.Administrator, Name = "Administrator", Description = "Admin" };
        seed.Roles.AddRange(instructorRole, secretaryRole, adminRole);

        _instructor = Person.Create("Juan", "Perez", "juan@gym.com");
        _instructor.AddRole(instructorRole);

        _otherInstructor = Person.Create("Pedro", "Gomez", "pedro@gym.com");
        _otherInstructor.AddRole(instructorRole);

        _secretary = Person.Create("Maria", "Lopez", "maria@gym.com");
        _secretary.AddRole(secretaryRole);

        _room = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 30, IsActive = true };
        _activity = new Activity("ZUMBA", "Zumba", defaultCapacity: 20);

        var currentUser = new User
        {
            Id = _currentUserId,
            PersonId = _instructor.Id,
            Username = "admin@gym.com"
        };

        seed.AddRange(_instructor, _otherInstructor, _secretary, _room, _activity, currentUser);
        seed.SaveChanges();

        _dbContext = new LockSpyDbContext(options);

        _gymCalendar = new GymCalendar(
            Options.Create(new GymOptions { TimeZoneId = "America/Argentina/San_Juan" }),
            TimeProvider.System);

        _mockCurrentUser.Setup(u => u.UserId).Returns(_currentUserId);
        _mockCurrentUser.Setup(u => u.PersonId).Returns(_instructor.Id);
        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Administrator.ToString())).Returns(true);
        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Secretary.ToString())).Returns(true);
        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Instructor.ToString())).Returns(true);

        _service = new ClassSessionService(_dbContext, _gymCalendar, _mockCurrentUser.Object, _mockAudit.Object);
    }

    // --- Task 5.3: Alta y edición ---

    [Fact]
    public async Task CreateExtraordinaryAsync_WhenActivityInactive_ThrowsArgumentException()
    {
        var inactiveAct = new Activity("BOX", "Box", defaultCapacity: 10);
        inactiveAct.ChangeStatus(ActivityStatus.Inactive);
        _dbContext.Activities.Add(inactiveAct);
        await _dbContext.SaveChangesAsync();

        var request = new CreateClassSessionRequest(
            inactiveAct.Id, _room.Id, _instructor.Id, null,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            10, null);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExtraordinaryAsync(request));
    }

    [Fact]
    public async Task CreateExtraordinaryAsync_WithoutResolvableCapacity_ThrowsArgumentException()
    {
        var actNoCap = new Activity("RUNNING", "Running", defaultCapacity: null);
        _dbContext.Activities.Add(actNoCap);
        await _dbContext.SaveChangesAsync();

        var request = new CreateClassSessionRequest(
            actNoCap.Id, _room.Id, _instructor.Id, null,
            DateTimeOffset.UtcNow.AddDays(1), DateTimeOffset.UtcNow.AddDays(1).AddHours(1),
            null, null);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExtraordinaryAsync(request));
    }

    [Fact]
    public async Task CreateExtraordinaryAsync_WhenRoomOverlaps_ThrowsConflictExceptionWithIds()
    {
        var starts = DateTime.UtcNow.AddDays(1).Date.AddHours(10);
        var ends = starts.AddHours(1);

        var existing = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _otherInstructor.Id, null);
        _dbContext.ClassSessions.Add(existing);
        await _dbContext.SaveChangesAsync();

        var request = new CreateClassSessionRequest(
            _activity.Id, _room.Id, _instructor.Id, null,
            new DateTimeOffset(starts), new DateTimeOffset(ends),
            20, null);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateExtraordinaryAsync(request));
        Assert.Contains("sala", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesCapacityAndNotes_AndAudits()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _instructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateClassSessionRequest(25, "Nuevas notas", session.Version);
        var result = await _service.UpdateAsync(session.Id, request);

        Assert.Equal(25, result.Capacity);
        Assert.Equal("Nuevas notas", result.Notes);

        _mockAudit.Verify(a => a.LogAsync(
            "CLASS_SESSION_UPDATED",
            "ClassSession",
            session.Id.ToString(),
            null,
            It.IsAny<string>(),
            _currentUserId,
            It.IsAny<string>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_WhenVersionMismatch_ThrowsConflictException()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _instructor.Id, null);
        session.Version = 1;
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateClassSessionRequest(25, null, Version: 2);
        await Assert.ThrowsAsync<ConflictException>(() => _service.UpdateAsync(session.Id, request));
    }

    [Fact]
    public async Task UpdateAsync_OnClosedSessionWithUnchangedCapacity_UpdatesNotesOnly()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, starts.AddHours(1), 20, _instructor.Id, null);
        session.Cancel(ClassSessionCancellationReason.LowDemand, null, _currentUserId, DateTime.UtcNow);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateClassSessionRequest(20, "Observación posterior", session.Version);
        var result = await _service.UpdateAsync(session.Id, request);

        Assert.Equal("Observación posterior", result.Notes);
        Assert.Equal(20, result.Capacity);
    }

    [Fact]
    public async Task UpdateAsync_OnClosedSessionWithChangedCapacity_ThrowsInvalidOperationException()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, starts.AddHours(1), 20, _instructor.Id, null);
        session.Cancel(ClassSessionCancellationReason.LowDemand, null, _currentUserId, DateTime.UtcNow);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var request = new UpdateClassSessionRequest(25, "Nota", session.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateAsync(session.Id, request));
    }

    // --- Task 5.4: Ciclo de vida y locking ---

    [Fact]
    public async Task OpenAsync_LocksSessionAndTransitionsToOpen()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _instructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        _dbContext.LockCalls.Clear();
        var result = await _service.OpenAsync(session.Id);

        var call = Assert.Single(_dbContext.LockCalls);
        Assert.Equal(session.Id, call.ClassSessionId);
        Assert.Equal(ClassSessionStatus.Open, result.Status);
    }

    [Fact]
    public async Task SuspendAsync_LocksSessionAndTransitionsToSuspended()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _instructor.Id, null);
        session.Open(DateTime.UtcNow);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        _dbContext.LockCalls.Clear();
        var request = new SuspendClassSessionRequest(ClassSessionSuspensionReason.Weather, "Lluvia torrencial");
        var result = await _service.SuspendAsync(session.Id, request);

        var call = Assert.Single(_dbContext.LockCalls);
        Assert.Equal(session.Id, call.ClassSessionId);
        Assert.Equal(ClassSessionStatus.Suspended, result.Status);
    }

    [Fact]
    public async Task SuspendAsync_WhenInstructorNotAssigned_ThrowsNotFoundException()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        // Session assigned to _otherInstructor
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _otherInstructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        // Current user is instructor ONLY (not admin, not secretary)
        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Administrator.ToString())).Returns(false);
        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Secretary.ToString())).Returns(false);
        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Instructor.ToString())).Returns(true);
        _mockCurrentUser.Setup(u => u.PersonId).Returns(_instructor.Id);

        var request = new SuspendClassSessionRequest(ClassSessionSuspensionReason.Weather, "Lluvia");
        await Assert.ThrowsAsync<NotFoundException>(() => _service.SuspendAsync(session.Id, request));
    }

    [Fact]
    public async Task RescheduleAsync_CreatesLinkedExtraordinarySessionAndMarksOriginal()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _instructor.Id, _secretary.Id, "Notas previas");
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var newStarts = DateTimeOffset.UtcNow.AddDays(5);
        var newEnds = newStarts.AddHours(1);
        var request = new RescheduleClassSessionRequest(newStarts, newEnds, null, "Reprogramado por feriado");

        var newSessionDetail = await _service.RescheduleAsync(session.Id, request);

        Assert.NotNull(newSessionDetail);
        Assert.Equal(ClassSessionStatus.Scheduled, newSessionDetail.Status);
        Assert.Equal(session.Id, newSessionDetail.RescheduledFromSessionId);
        Assert.Equal(_instructor.Id, newSessionDetail.ScheduledInstructorId);
        Assert.Equal(_secretary.Id, newSessionDetail.ScheduledSecretaryId);

        var original = await _dbContext.ClassSessions.FindAsync(session.Id);
        Assert.Equal(ClassSessionStatus.Rescheduled, original!.Status);
        Assert.Equal(newSessionDetail.Id, original.RescheduledToSessionId);
    }

    [Fact]
    public async Task RescheduleAsync_ToSlotOverlappingTheOriginal_DoesNotConflictWithItself()
    {
        var starts = DateTime.UtcNow.AddDays(2).Date.AddHours(13);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, starts.AddHours(1), 20, _instructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var newStarts = new DateTimeOffset(starts.AddMinutes(30));
        var request = new RescheduleClassSessionRequest(newStarts, newStarts.AddHours(1), null, "Se corre media hora");

        var result = await _service.RescheduleAsync(session.Id, request);

        Assert.Equal(session.Id, result.RescheduledFromSessionId);
    }

    [Fact]
    public async Task RescheduleAsync_DefersOverlapConstraintsWithinTransaction()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, starts.AddHours(1), 20, _instructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var newStarts = DateTimeOffset.UtcNow.AddDays(5);
        await _service.RescheduleAsync(session.Id, new RescheduleClassSessionRequest(newStarts, newStarts.AddHours(1), null, "Feriado"));

        Assert.Equal(1, _dbContext.DeferOverlapCalls);
        Assert.Equal(1, _dbContext.EnforceDeferredCalls);
    }

    // --- Task 5.5: Staff y sala ---

    [Fact]
    public async Task ReplaceInstructorAsync_ReplacesEffectiveInstructorAndRecordsHistory()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _instructor.Id, null);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var request = new ReplaceStaffRequest(_otherInstructor.Id, "Enfermedad", session.Version);
        var updated = await _service.ReplaceInstructorAsync(session.Id, request);

        Assert.Equal(_instructor.Id, updated.ScheduledInstructorId);
        Assert.Equal(_otherInstructor.Id, updated.EffectiveInstructorId);

        var change = Assert.Single(updated.StaffChanges);
        Assert.Equal("Instructor", change.Role);
        Assert.Equal(_instructor.Id, change.PreviousPersonId);
        Assert.Equal(_otherInstructor.Id, change.NewPersonId);
        Assert.Equal("Enfermedad", change.Reason);
    }

    [Fact]
    public async Task ReplaceSecretaryAsync_AllowsNullToUnset()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _instructor.Id, _secretary.Id);
        _dbContext.ClassSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var request = new ReplaceStaffRequest(null, "Sin secretario hoy", session.Version);
        var updated = await _service.ReplaceSecretaryAsync(session.Id, request);

        Assert.Null(updated.EffectiveSecretaryId);
        Assert.Equal(_secretary.Id, updated.ScheduledSecretaryId);
    }

    [Fact]
    public async Task ChangeRoomAsync_ChangesRoomAndAudits()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var ends = starts.AddHours(1);
        var session = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, ends, 20, _instructor.Id, null);
        _dbContext.ClassSessions.Add(session);

        var newRoom = new Room { Id = Guid.NewGuid(), Name = "Sala Outdoor", Capacity = 50, IsActive = true };
        _dbContext.Rooms.Add(newRoom);
        await _dbContext.SaveChangesAsync();

        var request = new ChangeRoomRequest(newRoom.Id, session.Version);
        var updated = await _service.ChangeRoomAsync(session.Id, request);

        Assert.Equal(newRoom.Id, updated.RoomId);
        Assert.True(updated.IsDetachedFromSchedule);

        _mockAudit.Verify(a => a.LogAsync(
            "CLASS_SESSION_ROOM_CHANGED",
            "ClassSession",
            session.Id.ToString(),
            null,
            It.IsAny<string>(),
            _currentUserId,
            It.IsAny<string>(),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- Task 5.6: Consultas y filtros ---

    [Fact]
    public async Task GetPagedAsync_InstructorFilter_RestrictsToInstructorSessions()
    {
        var starts = DateTime.UtcNow.AddDays(2);
        var s1 = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts, starts.AddHours(1), 20, _instructor.Id, null);
        var s2 = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, starts.AddHours(2), starts.AddHours(3), 20, _otherInstructor.Id, null);
        _dbContext.ClassSessions.AddRange(s1, s2);
        await _dbContext.SaveChangesAsync();

        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Administrator.ToString())).Returns(false);
        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Secretary.ToString())).Returns(false);
        _mockCurrentUser.Setup(u => u.IsInRole(RoleType.Instructor.ToString())).Returns(true);
        _mockCurrentUser.Setup(u => u.PersonId).Returns(_instructor.Id);

        var paged = await _service.GetPagedAsync();

        Assert.Single(paged.Items);
        Assert.Equal(s1.Id, paged.Items[0].Id);
    }

    [Fact]
    public async Task GetAvailableAsync_ReturnsOnlyOpenFutureActiveActivitySessions()
    {
        var futureStarts = DateTime.UtcNow.AddDays(2);
        var sOpen = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, futureStarts, futureStarts.AddHours(1), 20, _instructor.Id, null);
        sOpen.Open(DateTime.UtcNow);

        var sScheduled = ClassSession.CreateExtraordinary(_activity.Id, _room.Id, futureStarts.AddHours(2), futureStarts.AddHours(3), 20, _instructor.Id, null);

        _dbContext.ClassSessions.AddRange(sOpen, sScheduled);
        await _dbContext.SaveChangesAsync();

        var available = await _service.GetAvailableAsync();

        var item = Assert.Single(available);
        Assert.Equal(sOpen.Id, item.Id);
        Assert.Equal(20, item.AvailableSpots);
        Assert.False(item.IsFull);
    }
}
