using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Reservations.Dtos;
using SmartGym.Application.Modules.Reservations.Services;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Infrastructure.Persistence;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationAttendanceTests
{
    private static readonly DateTime BaseNowUtc = new(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RecordAttendanceAsync_WithinWindow_TransitionsToAttended_IncrementsAttendedCount_AndRecordsHistory()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15); // Within 30 min check-in window before start
        var endsAtUtc = startsAtUtc.AddHours(1);
        var (_, session, reservation, _) = await SeedConfirmedCreditReservationAsync(context, startsAtUtc, endsAtUtc);

        var currentUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Email = "sec@smartgym.local",
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, currentUser);

        var dto = await service.RecordAttendanceAsync(
            new RecordAttendanceRequest(reservation.Id, AttendanceSource.SecretaryDesk),
            currentUser.UserId!.Value);

        Assert.Equal(ReservationStatus.Attended, dto.Status);
        Assert.Equal(BaseNowUtc, dto.AttendedAtUtc);
        Assert.Equal(AttendanceSource.SecretaryDesk, dto.AttendanceSource);

        var persistedReservation = await context.Reservations
            .Include(r => r.StatusChanges)
            .SingleAsync(r => r.Id == reservation.Id);
        Assert.Equal(ReservationStatus.Attended, persistedReservation.Status);
        Assert.Equal(currentUser.UserId, persistedReservation.CheckedInByUserId);
        Assert.Contains(persistedReservation.StatusChanges, sc =>
            sc.PreviousStatus == ReservationStatus.Confirmed &&
            sc.NewStatus == ReservationStatus.Attended &&
            sc.Trigger == ReservationChangeTrigger.Attendance);

        var persistedSession = await context.ClassSessions.SingleAsync(cs => cs.Id == session.Id);
        Assert.Equal(1, persistedSession.AttendedCount);
    }

    [Fact]
    public async Task RecordNoShowAsync_AfterClassEnd_TransitionsToNoShow_RecordsHistory_AndDoesNotRefundCredit()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddHours(-2);
        var endsAtUtc = BaseNowUtc.AddHours(-1); // Class ended 1 hour ago
        var (_, _, reservation, membership) = await SeedConfirmedCreditReservationAsync(context, startsAtUtc, endsAtUtc);

        var currentUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Email = "sec@smartgym.local",
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, currentUser);

        var dto = await service.RecordNoShowAsync(reservation.Id, currentUser.UserId!.Value);

        Assert.Equal(ReservationStatus.NoShow, dto.Status);

        var persistedReservation = await context.Reservations
            .Include(r => r.StatusChanges)
            .SingleAsync(r => r.Id == reservation.Id);
        Assert.Equal(ReservationStatus.NoShow, persistedReservation.Status);
        Assert.Null(persistedReservation.RefundCreditMovementId);
        Assert.Contains(persistedReservation.StatusChanges, sc =>
            sc.PreviousStatus == ReservationStatus.Confirmed &&
            sc.NewStatus == ReservationStatus.NoShow &&
            sc.Trigger == ReservationChangeTrigger.Attendance);

        var persistedMembership = await context.Memberships.SingleAsync(m => m.Id == membership.Id);
        Assert.Equal(7, persistedMembership.AvailableCredits); // Initial 8 - 1 consumed, not refunded
    }

    [Fact]
    public async Task RecordAttendanceAndNoShow_OutsideWindow_RejectsWithAttendanceWindowClosed()
    {
        await using var context = CreateInMemoryDbContext();
        // Class starts in 2 hours (outside 30-min check-in window before start, and not yet ended for No-Show)
        var startsAtUtc = BaseNowUtc.AddHours(2);
        var endsAtUtc = startsAtUtc.AddHours(1);
        var (_, _, reservation, _) = await SeedConfirmedCreditReservationAsync(context, startsAtUtc, endsAtUtc);

        var currentUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, currentUser);

        var exEarlyAttendance = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RecordAttendanceAsync(
                new RecordAttendanceRequest(reservation.Id, AttendanceSource.SecretaryDesk),
                currentUser.UserId!.Value));
        Assert.Equal(ReservationErrorCodes.AttendanceWindowClosed, exEarlyAttendance.Code);

        var exEarlyNoShow = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RecordNoShowAsync(reservation.Id, currentUser.UserId!.Value));
        Assert.Equal(ReservationErrorCodes.AttendanceWindowClosed, exEarlyNoShow.Code);

        // Also test after the 24h registration window after end
        var lateService = CreateReservationService(context, endsAtUtc.AddHours(25), currentUser);

        var exLateAttendance = await Assert.ThrowsAsync<ConflictException>(() =>
            lateService.RecordAttendanceAsync(
                new RecordAttendanceRequest(reservation.Id, AttendanceSource.SecretaryDesk),
                currentUser.UserId!.Value));
        Assert.Equal(ReservationErrorCodes.AttendanceWindowClosed, exLateAttendance.Code);

        var exLateNoShow = await Assert.ThrowsAsync<ConflictException>(() =>
            lateService.RecordNoShowAsync(reservation.Id, currentUser.UserId!.Value));
        Assert.Equal(ReservationErrorCodes.AttendanceWindowClosed, exLateNoShow.Code);
    }

    [Fact]
    public async Task RecordAttendanceAndNoShow_WhenSessionIsVoided_RejectsWithClassNotOpen()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddHours(-1);
        var endsAtUtc = BaseNowUtc.AddMinutes(-5);
        var (_, session, reservation, _) = await SeedConfirmedCreditReservationAsync(context, startsAtUtc, endsAtUtc);

        var adminUserId = Guid.NewGuid();
        session.Suspend(ClassSessionSuspensionReason.Other, "Corte de energía", adminUserId, startsAtUtc.AddMinutes(-10));
        session.ClearDomainEvents();
        await context.SaveChangesAsync();

        var currentUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = adminUserId,
            Roles = [Roles.Administrator]
        };
        var service = CreateReservationService(context, BaseNowUtc, currentUser);

        var exAttendance = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RecordAttendanceAsync(
                new RecordAttendanceRequest(reservation.Id, AttendanceSource.SecretaryDesk),
                adminUserId));
        Assert.Equal(ReservationErrorCodes.ClassNotOpen, exAttendance.Code);

        var exNoShow = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RecordNoShowAsync(reservation.Id, adminUserId));
        Assert.Equal(ReservationErrorCodes.ClassNotOpen, exNoShow.Code);
    }

    [Fact]
    public async Task RecordAttendanceAndNoShow_FromTerminalStates_RejectsWithInvalidStateTransition()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddHours(-1);
        var endsAtUtc = BaseNowUtc.AddMinutes(-5);
        var (_, _, reservation, _) = await SeedConfirmedCreditReservationAsync(context, startsAtUtc, endsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        // Transition to Attended first
        await service.RecordAttendanceAsync(
            new RecordAttendanceRequest(reservation.Id, AttendanceSource.SecretaryDesk),
            staffUser.UserId!.Value);

        // Attempting to mark NoShow or Attended again on terminal state Attended must fail with INVALID_STATE_TRANSITION
        var exNoShowAfterAttended = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RecordNoShowAsync(reservation.Id, staffUser.UserId!.Value));
        Assert.Equal(ReservationErrorCodes.InvalidStateTransition, exNoShowAfterAttended.Code);

        var exAttendanceAgain = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RecordAttendanceAsync(
                new RecordAttendanceRequest(reservation.Id, AttendanceSource.SecretaryDesk),
                staffUser.UserId!.Value));
        Assert.Equal(ReservationErrorCodes.InvalidStateTransition, exAttendanceAgain.Code);
    }

    [Fact]
    public async Task RecordAttendanceAndNoShow_InstructorScope_AllowsAssignedInstructor_AndRejectsOtherInstructorWithNotFound()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(-45);
        var endsAtUtc = BaseNowUtc.AddMinutes(-5);
        var (_, session, reservation, _) = await SeedConfirmedCreditReservationAsync(context, startsAtUtc, endsAtUtc);

        var otherInstructorUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(), // Not assigned to session
            Roles = [Roles.Instructor]
        };
        var otherInstructorService = CreateReservationService(context, BaseNowUtc, otherInstructorUser);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            otherInstructorService.RecordAttendanceAsync(
                new RecordAttendanceRequest(reservation.Id, AttendanceSource.InstructorInRoom),
                otherInstructorUser.UserId!.Value));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            otherInstructorService.RecordNoShowAsync(
                reservation.Id,
                otherInstructorUser.UserId!.Value));

        // Assigned instructor succeeds
        var assignedInstructorUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            PersonId = session.EffectiveInstructorId,
            Roles = [Roles.Instructor]
        };
        var assignedInstructorService = CreateReservationService(context, BaseNowUtc, assignedInstructorUser);

        var dto = await assignedInstructorService.RecordAttendanceAsync(
            new RecordAttendanceRequest(reservation.Id, AttendanceSource.InstructorInRoom),
            assignedInstructorUser.UserId!.Value);

        Assert.Equal(ReservationStatus.Attended, dto.Status);
    }

    private static ReservationService CreateReservationService(
        SmartGymDbContext context,
        DateTime nowUtc,
        ICurrentUserService currentUserService)
    {
        return new ReservationService(
            context,
            new StubGymCalendar(DateOnly.FromDateTime(nowUtc), nowUtc),
            currentUserService: currentUserService,
            reservationsOptions: Options.Create(new ReservationsOptions
            {
                CheckInOpensMinutesBeforeStart = 30,
                AttendanceRegistrationHoursAfterEnd = 24
            }),
            gymOptions: Options.Create(new GymOptions()));
    }

    private static async Task<(Person Student, ClassSession Session, Reservation Reservation, Membership Membership)> SeedConfirmedCreditReservationAsync(
        SmartGymDbContext context,
        DateTime startsAtUtc,
        DateTime endsAtUtc)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        context.Roles.Add(studentRole);

        var student = Person.Create(
            "Ana",
            "Gomez",
            Guid.NewGuid().ToString("N")[..8],
            birthDate: new DateTime(1996, 5, 10),
            emergencyContact: EmergencyContact.Create("Madre", "+5492644123456", "Madre"));
        student.AddRole(studentRole);

        var instructor = Person.Create("Lucas", "Vera", Guid.NewGuid().ToString("N")[..8]);
        var activity = new Activity("SPIN", "Spinning", defaultCapacity: 10);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala 1", Capacity = 20, IsActive = true };

        context.People.AddRange(instructor, student);
        context.Activities.Add(activity);
        context.Rooms.Add(room);

        var sessionDate = DateOnly.FromDateTime(startsAtUtc);
        var calendar = new StubGymCalendar(sessionDate, startsAtUtc);
        var ledger = new CreditLedgerService(context, calendar);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            DurationDays = 30,
            Price = 20000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        context.MembershipPlans.Add(plan);

        var membership = Membership.Create(student.Id, plan, sessionDate.AddDays(-5), startsAtUtc.AddDays(-5), 20000m, 0m, 20000m);
        membership.Activate(sessionDate.AddDays(-5), startsAtUtc.AddDays(-5), Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(membership);
        context.Memberships.Add(membership);

        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            activity.Id,
            room.Id,
            startsAtUtc,
            endsAtUtc,
            capacity: 10,
            instructorId: instructor.Id,
            secretaryId: null);
        session.Open(startsAtUtc.AddDays(-1));
        session.IncrementReservedCount();
        context.ClassSessions.Add(session);

        await context.SaveChangesAsync();

        var consumptionMovement = await ledger.ConsumeCreditForReservationAsync(
            membership,
            session.Id,
            sessionDate,
            "Consumo de crédito por reserva");

        var reservation = Reservation.CreateConfirmed(
            session.Id,
            student.Id,
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.MembershipCredit,
            startsAtUtc.AddDays(-1),
            membershipId: membership.Id,
            creditMovementId: consumptionMovement.Id);

        consumptionMovement.ReservationId = reservation.Id;
        context.Reservations.Add(reservation);
        await context.SaveChangesAsync();

        return (student, session, reservation, membership);
    }

    private static SmartGymDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
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
