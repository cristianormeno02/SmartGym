using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
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

public class ReservationQueriesTests
{
    private static readonly DateOnly BaseToday = new(2026, 8, 10);
    private static readonly DateTime BaseNowUtc = new(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetPagedAsync_FiltersByAllCriteria_AndPaginatesOnServer()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(BaseToday, BaseNowUtc);
        var (student1, student2, activity1, activity2, session1, session2) = await SeedMultiSessionScenarioAsync(db, calendar);

        var staffUser = new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateService(db, calendar, staffUser);

        var r1 = await service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student1.Id),
            staffUser.UserId!.Value);

        var r2 = await service.CreateReservationAsync(
            new CreateReservationRequest(session2.Id, student2.Id),
            staffUser.UserId!.Value);

        // Cancel r2 within late cancellation window so IsLateCancellation = true
        calendar.UtcNow = session2.StartsAtUtc.AddMinutes(-30);
        await service.CancelReservationAsync(
            r2.Id,
            new CancelReservationRequest("Cancelación tardía"),
            staffUser.UserId!.Value);

        // Filter by StudentId
        var byStudent = await service.GetPagedAsync(new ReservationFilterRequest(StudentId: student1.Id));
        Assert.Single(byStudent.Items);
        Assert.Equal(r1.Id, byStudent.Items[0].Id);

        // Filter by ActivityId
        var byActivity = await service.GetPagedAsync(new ReservationFilterRequest(ActivityId: activity2.Id));
        Assert.Single(byActivity.Items);
        Assert.Equal(r2.Id, byActivity.Items[0].Id);

        // Filter by Status
        var byStatus = await service.GetPagedAsync(new ReservationFilterRequest(Status: ReservationStatus.Cancelled));
        Assert.Single(byStatus.Items);
        Assert.Equal(r2.Id, byStatus.Items[0].Id);

        // Filter by IsLateCancellation
        var byLate = await service.GetPagedAsync(new ReservationFilterRequest(IsLateCancellation: true));
        Assert.Single(byLate.Items);
        Assert.Equal(r2.Id, byLate.Items[0].Id);

        // Filter by Origin & AccessType
        var byOriginAndAccess = await service.GetPagedAsync(new ReservationFilterRequest(
            Origin: ReservationOrigin.StaffDesk,
            AccessType: ReservationAccessType.MembershipCredit));
        Assert.Equal(2, byOriginAndAccess.TotalCount);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsDetailWithOrderedStatusHistory_AndEnforcesContextualAuthorization()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(BaseToday, BaseNowUtc);
        var (student1, student2, _, _, session1, _) = await SeedMultiSessionScenarioAsync(db, calendar);

        var student1User = new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            PersonId = student1.Id,
            Roles = [Roles.Student]
        };
        var serviceStudent1 = CreateService(db, calendar, student1User);

        var created = await serviceStudent1.CreateReservationAsync(
            new CreateReservationRequest(session1.Id),
            student1User.UserId!.Value);

        var detail = await serviceStudent1.GetByIdAsync(created.Id);
        Assert.Equal(created.Id, detail.Id);
        Assert.NotEmpty(detail.StatusHistory);
        Assert.Equal(ReservationStatus.Confirmed, detail.StatusHistory[^1].NewStatus);

        // Another student cannot access student1's reservation -> 403 ForbiddenException
        var student2User = new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            PersonId = student2.Id,
            Roles = [Roles.Student]
        };
        var serviceStudent2 = CreateService(db, calendar, student2User);
        await Assert.ThrowsAsync<ForbiddenException>(() => serviceStudent2.GetByIdAsync(created.Id));

        // Instructor of another class -> 404 NotFoundException
        var otherInstructorUser = new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            Roles = [Roles.Instructor]
        };
        var serviceOtherInstructor = CreateService(db, calendar, otherInstructorUser);
        await Assert.ThrowsAsync<NotFoundException>(() => serviceOtherInstructor.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task GetByStudentAsync_GetMyUpcomingAsync_AndGetWaitlistAsync_ReturnExpectedProjectionsAndEnforceScope()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(BaseToday, BaseNowUtc);
        var (student1, student2, _, _, session1, _) = await SeedMultiSessionScenarioAsync(db, calendar, session1Capacity: 1);

        var staffUser = new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var staffService = CreateService(db, calendar, staffUser);

        var r1 = await staffService.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student1.Id),
            staffUser.UserId!.Value);
        var w1 = await staffService.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student2.Id, AllowWaitlist: true),
            staffUser.UserId!.Value);

        Assert.Equal(ReservationStatus.Confirmed, r1.Status);
        Assert.Equal(ReservationStatus.Waitlisted, w1.Status);

        // Waitlist ordered by position
        var waitlist = await staffService.GetWaitlistAsync(session1.Id);
        Assert.Single(waitlist);
        Assert.Equal(student2.Id, waitlist[0].StudentId);
        Assert.Equal(1, waitlist[0].WaitListPosition);

        // Student1 upcoming reservations
        var student1User = new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            PersonId = student1.Id,
            Roles = [Roles.Student]
        };
        var student1Service = CreateService(db, calendar, student1User);
        var upcoming = await student1Service.GetMyUpcomingAsync();
        Assert.Single(upcoming);
        Assert.Equal(r1.Id, upcoming[0].Id);

        // Instructor cannot query student history -> 403 ForbiddenException
        var instructorUser = new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            PersonId = session1.EffectiveInstructorId,
            Roles = [Roles.Instructor]
        };
        var instructorService = CreateService(db, calendar, instructorUser);
        await Assert.ThrowsAsync<ForbiddenException>(() => instructorService.GetByStudentAsync(student1.Id, null));
    }

    [Fact]
    public async Task GetAvailableClassesAsync_CalculatesSpotsWaitlistOwnReservationAndEligibility_WithoutConsumingCredits()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(BaseToday, BaseNowUtc);
        var (student1, student2, activity1, activity2, session1, session2) = await SeedMultiSessionScenarioAsync(
            db,
            calendar,
            session1Capacity: 2,
            restrictStudent1ToActivity1Only: true);

        var student1User = new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            PersonId = student1.Id,
            Roles = [Roles.Student]
        };
        var student1Service = CreateService(db, calendar, student1User);

        // Student2 reserves a spot in session1 first
        var staffService = CreateService(db, calendar, new StubCurrentUserService
        {
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        });
        await staffService.CreateReservationAsync(
            new CreateReservationRequest(session1.Id, student2.Id),
            Guid.NewGuid());

        var movementsBefore = await db.MembershipCreditMovements.CountAsync();
        var creditsBefore = (await db.Memberships.SingleAsync(m => m.StudentId == student1.Id)).AvailableCredits;

        // Query available-classes twice for student1: Criterion 7 requires zero credit consumption
        var available1 = await student1Service.GetAvailableClassesAsync(
            new AvailableClassesFilterRequest(BaseToday, BaseToday.AddDays(7)));
        var available2 = await student1Service.GetAvailableClassesAsync(
            new AvailableClassesFilterRequest(BaseToday, BaseToday.AddDays(7)));

        var movementsAfter = await db.MembershipCreditMovements.CountAsync();
        var creditsAfter = (await db.Memberships.SingleAsync(m => m.StudentId == student1.Id)).AvailableCredits;
        Assert.Equal(movementsBefore, movementsAfter);
        Assert.Equal(creditsBefore, creditsAfter);
        Assert.Equal(2, available2.Count);

        var s1Dto = available2.Single(c => c.ClassSessionId == session1.Id);
        Assert.Equal(2, s1Dto.Capacity);
        Assert.Equal(1, s1Dto.ReservedCount);
        Assert.Equal(1, s1Dto.AvailableSpots);
        Assert.False(s1Dto.HasActiveReservation);
        Assert.True(s1Dto.CanReserve);
        Assert.Equal(ReservationAccessType.MembershipCredit, s1Dto.SuggestedAccessType);
        Assert.Equal(creditsBefore, s1Dto.UsableCredits);

        // Session2 belongs to activity2, which is not allowed by student1's membership
        var s2Dto = available2.Single(c => c.ClassSessionId == session2.Id);
        Assert.False(s2Dto.CanReserve);
        Assert.Contains(s2Dto.IneligibilityReasons, r => r.Code == EligibilityIssueCodes.ActivityNotAllowed);

        // Now student1 reserves session1 and queries again -> HasActiveReservation = true, CanReserve = false with ALREADY_RESERVED
        var ownRes = await student1Service.CreateReservationAsync(
            new CreateReservationRequest(session1.Id),
            student1User.UserId!.Value);

        var afterReserve = await student1Service.GetAvailableClassesAsync(
            new AvailableClassesFilterRequest(BaseToday, BaseToday.AddDays(7)));
        var s1After = afterReserve.Single(c => c.ClassSessionId == session1.Id);
        Assert.True(s1After.HasActiveReservation);
        Assert.Equal(ownRes.Id, s1After.ActiveReservationId);
        Assert.Equal(ReservationStatus.Confirmed, s1After.ActiveReservationStatus);
        Assert.False(s1After.CanReserve);
        Assert.Contains(s1After.IneligibilityReasons, r => r.Code == ReservationErrorCodes.AlreadyReserved);

        // Exceeding AvailableClassesMaxRangeDays (31 days) -> 400 ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            student1Service.GetAvailableClassesAsync(
                new AvailableClassesFilterRequest(BaseToday, BaseToday.AddDays(45))));
    }

    private static ReservationService CreateService(
        SmartGymDbContext db,
        StubGymCalendar calendar,
        ICurrentUserService currentUser)
    {
        return new ReservationService(
            db,
            calendar,
            currentUserService: currentUser,
            reservationsOptions: Options.Create(new ReservationsOptions
            {
                FreeCancellationHoursBeforeStart = 2.0,
                AvailableClassesMaxRangeDays = 31,
                EnableWaitlist = true,
                MaxWaitlistSize = 20
            }),
            gymOptions: Options.Create(new GymOptions()));
    }

    private static async Task<(Person Student1, Person Student2, Activity Activity1, Activity Activity2, ClassSession Session1, ClassSession Session2)> SeedMultiSessionScenarioAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar,
        int session1Capacity = 10,
        bool restrictStudent1ToActivity1Only = false)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var student1 = Person.Create(
            "Camila",
            "Rojas",
            Guid.NewGuid().ToString("N")[..8],
            birthDate: new DateTime(1997, 8, 12),
            emergencyContact: EmergencyContact.Create("Madre", "+5492644123456", "Madre"));
        student1.AddRole(studentRole);

        var student2 = Person.Create(
            "Mateo",
            "Silva",
            Guid.NewGuid().ToString("N")[..8],
            birthDate: new DateTime(1995, 3, 20),
            emergencyContact: EmergencyContact.Create("Padre", "+5492644999888", "Padre"));
        student2.AddRole(studentRole);

        var instructor = Person.Create("Lucas", "Vera", Guid.NewGuid().ToString("N")[..8]);
        var activity1 = new Activity("FUNC", "Funcional", defaultCapacity: 10);
        var activity2 = new Activity("YOGA", "Yoga", defaultCapacity: 10);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Principal", Capacity = 20, IsActive = true };

        db.People.AddRange(student1, student2, instructor);
        db.Activities.AddRange(activity1, activity2);
        db.Rooms.Add(room);

        var ledger = new CreditLedgerService(db, calendar);

        var plan1 = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Funcional",
            DurationDays = 30,
            Price = 15000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = !restrictStudent1ToActivity1Only,
            IsActive = true
        };
        if (restrictStudent1ToActivity1Only)
        {
            plan1.AllowedActivities.Add(new MembershipPlanActivity
            {
                MembershipPlanId = plan1.Id,
                ActivityId = activity1.Id,
                Activity = activity1
            });
        }

        var plan2 = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan Libre",
            DurationDays = 30,
            Price = 15000m,
            Credits = 4,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.AddRange(plan1, plan2);

        var m1 = Membership.Create(student1.Id, plan1, BaseToday, BaseNowUtc, 15000m, 0m, 15000m);
        m1.Activate(BaseToday, BaseNowUtc, Guid.NewGuid());

        var m2 = Membership.Create(student2.Id, plan2, BaseToday, BaseNowUtc, 15000m, 0m, 15000m);
        m2.Activate(BaseToday, BaseNowUtc, Guid.NewGuid());

        db.Memberships.AddRange(m1, m2);
        await ledger.RecordInitialCreditsAsync(m1);
        await ledger.RecordInitialCreditsAsync(m2);

        var s1Start = BaseNowUtc.AddHours(4);
        var session1 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity1.Id, room.Id, s1Start, s1Start.AddHours(1), session1Capacity, instructor.Id, null);
        session1.Open(BaseNowUtc);

        var s2Start = BaseNowUtc.AddHours(6);
        var session2 = ClassSession.CreateFromSchedule(
            Guid.NewGuid(), activity2.Id, room.Id, s2Start, s2Start.AddHours(1), 10, instructor.Id, null);
        session2.Open(BaseNowUtc);

        db.ClassSessions.AddRange(session1, session2);
        await db.SaveChangesAsync();

        return (student1, student2, activity1, activity2, session1, session2);
    }

    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private sealed class StubGymCalendar : IGymCalendar
    {
        public StubGymCalendar(DateOnly today, DateTime? utcNow = null)
        {
            Today = today;
            UtcNow = utcNow ?? today.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        }

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
        public DateOnly Today { get; set; }
        public DateTime UtcNow { get; set; }
        public DateTime ToUtc(DateOnly date, TimeOnly time) =>
            new(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, DateTimeKind.Utc);
        public DateTimeOffset ToLocal(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        public (DateTime FromUtc, DateTime ToUtc) LocalDayRange(DateOnly from, DateOnly to) =>
            (ToUtc(from, TimeOnly.MinValue), ToUtc(to.AddDays(1), TimeOnly.MinValue));
    }

    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public Guid? UserId { get; set; }
        public Guid? PersonId { get; set; }
        public string? Email { get; set; } = "user@smartgym.local";
        public bool IsAuthenticated { get; set; } = true;
        public IReadOnlyList<string> Roles { get; set; } = [SmartGym.Application.Common.Security.Roles.Student];
        public bool IsInRole(string role) => Roles.Contains(role);
    }
}
