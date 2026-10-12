using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Dtos;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
using SmartGym.Application.Modules.Promotions.Services;
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

public class ReservationCreationTests
{
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

    private sealed class ThrowingCreditLedgerService : ICreditLedgerService
    {
        private readonly ICreditLedgerService _inner;

        public ThrowingCreditLedgerService(ICreditLedgerService inner)
        {
            _inner = inner;
        }

        public Task<List<CreditMovementDto>> GetMovementsByStudentAsync(Guid studentId, CancellationToken cancellationToken = default)
            => _inner.GetMovementsByStudentAsync(studentId, cancellationToken);

        public Task<List<CreditMovementDto>> GetMovementsByMembershipAsync(Guid membershipId, CancellationToken cancellationToken = default)
            => _inner.GetMovementsByMembershipAsync(membershipId, cancellationToken);

        public Task<MembershipCreditsSummaryDto> GetMembershipCreditsSummaryAsync(Guid membershipId, CancellationToken cancellationToken = default)
            => _inner.GetMembershipCreditsSummaryAsync(membershipId, cancellationToken);

        public Task RecordInitialCreditsAsync(Membership membership, Guid? performedByUserId = null, CancellationToken cancellationToken = default)
            => _inner.RecordInitialCreditsAsync(membership, performedByUserId, cancellationToken);

        public Task<MembershipCreditMovement> ConsumeCreditForReservationAsync(
            Membership membership,
            Guid classSessionId,
            DateOnly sessionLocalDate,
            string reason,
            Guid? performedByUserId = null,
            Guid? reservationId = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated ledger failure during reservation creation.");
        }

        public Task<MembershipCreditMovement> RefundCreditForReservationAsync(
            Membership membership,
            Guid classSessionId,
            string reason,
            Guid? performedByUserId = null,
            Guid? reservationId = null,
            CancellationToken cancellationToken = default)
            => _inner.RefundCreditForReservationAsync(membership, classSessionId, reason, performedByUserId, reservationId, cancellationToken);

        public Task<MembershipCreditMovement?> ForfeitRemainingCreditsOnCancelAsync(Membership membership, string reason, Guid? performedByUserId = null, CancellationToken cancellationToken = default)
            => _inner.ForfeitRemainingCreditsOnCancelAsync(membership, reason, performedByUserId, cancellationToken);

        public Task<MembershipCreditMovement?> ExpireRemainingCreditsAsync(Membership membership, string reason = "Expiración de créditos por finalización de vigencia", CancellationToken cancellationToken = default)
            => _inner.ExpireRemainingCreditsAsync(membership, reason, cancellationToken);

        public Task<CreditMovementDto> AddCompensatoryCreditAsync(AddCompensatoryCreditRequest request, Guid performedByUserId, CancellationToken cancellationToken = default)
            => _inner.AddCompensatoryCreditAsync(request, performedByUserId, cancellationToken);

        public Task<CreditMovementDto> PurchaseSingleClassTicketAsync(PurchaseSingleClassTicketRequest request, Guid performedByUserId, CancellationToken cancellationToken = default)
            => _inner.PurchaseSingleClassTicketAsync(request, performedByUserId, cancellationToken);
    }

    private static SmartGymDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static async Task<(Person Student, Activity Activity, Room Room, Person Instructor, ClassSession Session, Membership Membership)> SeedEligibleStudentAndSessionAsync(
        SmartGymDbContext db,
        StubGymCalendar calendar,
        DateTime? sessionStartsAtUtc = null,
        int capacity = 10)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        db.Roles.Add(studentRole);

        var student = Person.Create(
            "Valentina",
            "Gómez",
            "38111222",
            birthDate: new DateTime(1996, 5, 20),
            emergencyContact: EmergencyContact.Create("Padre", "+5492644111111", "Padre"));
        student.AddRole(studentRole);

        var instructor = Person.Create("Martín", "Paz", "27000111");
        var activity = new Activity("CROSS", "Cross Training", defaultCapacity: capacity, minAge: 16, maxAge: 60);
        var room = new Room { Id = Guid.NewGuid(), Name = "Box 1", Capacity = capacity, IsActive = true };

        db.People.AddRange(student, instructor);
        db.Activities.Add(activity);
        db.Rooms.Add(room);

        var starts = sessionStartsAtUtc ?? calendar.UtcNow.AddDays(1);
        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            activity.Id,
            room.Id,
            starts,
            starts.AddHours(1),
            capacity,
            instructor.Id,
            null);
        session.Open(calendar.UtcNow);
        db.ClassSessions.Add(session);

        var plan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Plan 8 Clases",
            DurationDays = 60,
            Price = 20000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };
        db.MembershipPlans.Add(plan);

        var membership = Membership.Create(student.Id, plan, calendar.Today, calendar.UtcNow, 20000m, 0m, 20000m);
        membership.Activate(calendar.Today, calendar.UtcNow, Guid.NewGuid());
        var ledger = new CreditLedgerService(db, calendar);
        await ledger.RecordInitialCreditsAsync(membership);
        db.Memberships.Add(membership);

        await db.SaveChangesAsync();
        return (student, activity, room, instructor, session, membership);
    }

    [Fact]
    public async Task CreateReservationAsync_EligibleStudentAndOpenSession_CreatesConfirmedReservationDebitsCreditAndRecordsHistoryAndAudit()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 7, 10));
        var (student, _, _, _, session, membership) = await SeedEligibleStudentAndSessionAsync(db, calendar);
        var userId = Guid.NewGuid();
        var currentUser = new StubCurrentUserService
        {
            UserId = userId,
            PersonId = student.Id,
            Roles = [Roles.Student]
        };

        var service = new ReservationService(
            db,
            calendar,
            currentUserService: currentUser,
            auditService: new AuditService(db));

        var dto = await service.CreateReservationAsync(
            new CreateReservationRequest(session.Id, student.Id),
            userId);

        Assert.Equal(ReservationStatus.Confirmed, dto.Status);
        Assert.Equal(ReservationOrigin.StudentSelfService, dto.Origin);
        Assert.Equal(ReservationAccessType.MembershipCredit, dto.AccessType);
        Assert.Equal(membership.Id, dto.MembershipId);
        Assert.NotNull(dto.CreditMovementId);
        Assert.Equal(userId, dto.CreatedByUserId);

        var savedReservation = await db.Reservations
            .Include(r => r.StatusChanges)
            .SingleAsync(r => r.Id == dto.Id);
        Assert.Single(savedReservation.StatusChanges);
        var change = savedReservation.StatusChanges.Single();
        Assert.Null(change.PreviousStatus);
        Assert.Equal(ReservationStatus.Confirmed, change.NewStatus);
        Assert.Equal(ReservationChangeTrigger.StudentAction, change.Trigger);

        var debit = await db.MembershipCreditMovements.SingleAsync(m => m.Id == dto.CreditMovementId);
        Assert.Equal(dto.Id, debit.ReservationId);
        Assert.Equal(7, (await db.Memberships.SingleAsync(m => m.Id == membership.Id)).AvailableCredits);
        Assert.Equal(1, (await db.ClassSessions.SingleAsync(s => s.Id == session.Id)).ReservedCount);
        Assert.Contains(await db.AuditLogs.ToListAsync(), a => a.EntityId == dto.Id.ToString());
    }

    [Fact]
    public async Task CreateReservationAsync_OutsideBookingWindow_RejectsWithBookingWindowClosedUnlessStaffOverridesWithReason()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 7, 10));
        // Inicia en 40 días (> MaxAdvanceBookingDays = 30)
        var (student, _, _, _, sessionFar, _) = await SeedEligibleStudentAndSessionAsync(
            db,
            calendar,
            sessionStartsAtUtc: calendar.UtcNow.AddDays(40));

        var secretaryUserId = Guid.NewGuid();
        var currentUser = new StubCurrentUserService
        {
            UserId = secretaryUserId,
            PersonId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };

        var options = Options.Create(new ReservationsOptions
        {
            MaxAdvanceBookingDays = 30,
            MinAdvanceBookingMinutes = 60
        });

        var service = new ReservationService(
            db,
            calendar,
            currentUserService: currentUser,
            auditService: new AuditService(db),
            reservationsOptions: options);

        // 1. Sin override -> 409 BOOKING_WINDOW_CLOSED
        var exWindow = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(sessionFar.Id, student.Id), secretaryUserId));
        Assert.Equal(ReservationErrorCodes.BookingWindowClosed, exWindow.Code);

        // 2. Override sin motivo -> 400 ArgumentException
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(sessionFar.Id, student.Id, OverrideBookingWindow: true, PolicyOverrideReason: " "),
                secretaryUserId));

        // 3. Alumno sin permiso intenta OverrideBookingWindow -> 403 ForbiddenException
        currentUser.Roles = [Roles.Student];
        currentUser.PersonId = student.Id;
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(sessionFar.Id, student.Id, OverrideBookingWindow: true, PolicyOverrideReason: "Quiero reservar antes"),
                secretaryUserId));

        // 4. Secretario con permiso y motivo -> se crea con IsPolicyOverride = true y Origin = StaffDesk
        currentUser.Roles = [Roles.Secretary];
        var dto = await service.CreateReservationAsync(
            new CreateReservationRequest(
                sessionFar.Id,
                student.Id,
                OverrideBookingWindow: true,
                PolicyOverrideReason: "Viaje programado autorizado por coordinación"),
            secretaryUserId);

        Assert.True(dto.IsPolicyOverride);
        Assert.Equal("Viaje programado autorizado por coordinación", dto.PolicyOverrideReason);
        Assert.Equal(ReservationOrigin.StaffDesk, dto.Origin);
        Assert.Equal(secretaryUserId, dto.CreatedByUserId);
        Assert.Equal(student.Id, dto.StudentId);
    }

    [Fact]
    public async Task CreateReservationAsync_DuplicateActiveReservation_ThrowsAlreadyReservedConflict()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 7, 10));
        var (student, _, _, _, session, _) = await SeedEligibleStudentAndSessionAsync(db, calendar);
        var userId = Guid.NewGuid();
        var service = new ReservationService(db, calendar);

        await service.CreateReservationAsync(new CreateReservationRequest(session.Id, student.Id), userId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(session.Id, student.Id), userId));

        Assert.Equal(ReservationErrorCodes.AlreadyReserved, ex.Code);
        Assert.Equal(1, await db.Reservations.CountAsync());
        Assert.Equal(7, (await db.Memberships.SingleAsync()).AvailableCredits);
    }

    [Fact]
    public async Task CreateReservationAsync_WhenLedgerThrows_RollsBackReservationAndDoesNotIncrementReservedCount()
    {
        await using var db = CreateDbContext();
        var calendar = new StubGymCalendar(new DateOnly(2026, 7, 10));
        var (student, _, _, _, session, _) = await SeedEligibleStudentAndSessionAsync(db, calendar);
        var failingLedger = new ThrowingCreditLedgerService(new CreditLedgerService(db, calendar));
        var service = new ReservationService(db, calendar, creditLedgerService: failingLedger);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(session.Id, student.Id), Guid.NewGuid()));

        // Ninguna reserva persistida y el cupo de la clase sigue en 0
        Assert.Empty(await db.Reservations.AsNoTracking().ToListAsync());
        var reloadedSession = await db.ClassSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        Assert.Equal(0, reloadedSession.ReservedCount);
    }
}
