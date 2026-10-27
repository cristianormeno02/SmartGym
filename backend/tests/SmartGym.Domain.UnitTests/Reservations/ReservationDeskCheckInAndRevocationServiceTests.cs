using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using SmartGym.Application.Common;
using SmartGym.Application.Common.Exceptions;
using SmartGym.Application.Common.Interfaces;
using SmartGym.Application.Common.Security;
using SmartGym.Application.Modules.Memberships.Services;
using SmartGym.Application.Modules.Operations.Services;
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

public class ReservationDeskCheckInAndRevocationServiceTests
{
    private static readonly DateTime BaseNowUtc = new(2026, 7, 15, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateReservation_WhenActivityDoesNotRequireBooking_RejectsWithBookingNotRequired()
    {
        await using var context = CreateInMemoryDbContext();
        var (student, session) = await SeedSessionWithActivityRequiresBookingAsync(context, requiresBooking: false);

        var currentUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            PersonId = student.Id,
            Roles = [Roles.Student]
        };
        var service = CreateReservationService(context, BaseNowUtc, currentUser);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(
                new CreateReservationRequest(session.Id, student.Id),
                currentUser.UserId!.Value));

        Assert.Equal(ReservationErrorCodes.BookingNotRequired, ex.Code);
    }

    [Fact]
    public async Task JoinWaitlist_WhenActivityDoesNotRequireBooking_RejectsWithBookingNotRequired()
    {
        await using var context = CreateInMemoryDbContext();
        var (student, session) = await SeedSessionWithActivityRequiresBookingAsync(context, requiresBooking: false);

        var currentUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            PersonId = student.Id,
            Roles = [Roles.Student]
        };
        var service = CreateReservationService(context, BaseNowUtc, currentUser);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.JoinWaitlistAsync(
                session.Id,
                new JoinWaitlistRequest(student.Id),
                currentUser.UserId!.Value));

        Assert.Equal(ReservationErrorCodes.BookingNotRequired, ex.Code);
    }

    [Fact]
    public async Task GetAvailableClasses_WhenActivityDoesNotRequireBooking_SetsCanReserveFalseAndReasonBookingNotRequired()
    {
        await using var context = CreateInMemoryDbContext();
        var (student, session) = await SeedSessionWithActivityRequiresBookingAsync(context, requiresBooking: false);

        var currentUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            PersonId = student.Id,
            Roles = [Roles.Student]
        };
        var service = CreateReservationService(context, BaseNowUtc, currentUser);

        var list = await service.GetAvailableClassesAsync(new AvailableClassesFilterRequest(
            From: DateOnly.FromDateTime(session.StartsAtUtc),
            To: DateOnly.FromDateTime(session.StartsAtUtc),
            StudentId: student.Id));

        var classDto = Assert.Single(list);
        Assert.False(classDto.RequiresBooking);
        Assert.False(classDto.CanReserve);
        Assert.False(classDto.CanJoinWaitlist);
        Assert.Contains(classDto.IneligibilityReasons, r => r.Code == ReservationErrorCodes.BookingNotRequired);
    }

    [Fact]
    public async Task GetDeskEligibility_ReturnsCorrectStatusAndOptions()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, passPlan) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var eligibility = await service.GetDeskEligibilityAsync(session.Id, student.Id);

        Assert.Equal(session.Id, eligibility.ClassSessionId);
        Assert.Equal(student.Id, eligibility.StudentId);
        Assert.True(eligibility.IsWithinDropInWindow);
        Assert.True(eligibility.CanCheckInWithMembership);
        Assert.True(eligibility.CanPurchaseSingleClassPass);
        Assert.Contains(eligibility.EligibleMemberships, m => m.MembershipId == membership.Id);
        Assert.Contains(eligibility.SingleClassPassOptions, p => p.PlanId == passPlan.Id);
    }

    [Fact]
    public async Task CheckInDeskDropIn_WithMembershipCredit_DebitsCreditAndCreatesAttendedDropIn()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Email = "sec@smartgym.local",
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var response = await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id
        ), staffUser.UserId!.Value);

        Assert.NotNull(response.Reservation);
        Assert.Equal(ReservationStatus.Attended, response.Reservation.Status);
        Assert.True(response.Reservation.IsDropIn);
        Assert.Equal(AttendanceSource.DeskDropIn, response.Reservation.AttendanceSource);
        Assert.Equal(ReservationOrigin.StaffDesk, response.Reservation.Origin);
        Assert.Null(response.PaymentId);

        var updatedSession = await context.ClassSessions.FindAsync(session.Id);
        Assert.Equal(1, updatedSession!.ReservedCount);
        Assert.Equal(1, updatedSession.AttendedCount);

        var updatedMembership = await context.Memberships.FindAsync(membership.Id);
        Assert.Equal(7, updatedMembership!.AvailableCredits); // Started with 8, 1 debited
    }

    [Fact]
    public async Task CheckInDeskDropIn_WithSingleClassPass_RecordsPaymentAndCreatesAttendedDropIn()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, _, passPlan) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Email = "admin@smartgym.local",
            Roles = [Roles.Administrator]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var response = await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            SingleClassPlanId: passPlan.Id,
            PaymentMethod: PaymentMethod.Cash
        ), staffUser.UserId!.Value);

        Assert.NotNull(response.Reservation);
        Assert.Equal(ReservationStatus.Attended, response.Reservation.Status);
        Assert.True(response.Reservation.IsDropIn);
        Assert.NotNull(response.PaymentId);

        var payment = await context.Payments.FindAsync(response.PaymentId.Value);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Completed, payment.Status);
        Assert.Equal(passPlan.Price, payment.Amount);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
    }

    [Fact]
    public async Task CheckInDeskDropIn_OutsideWindow_RejectsWithDropInWindowClosed()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddHours(2); // In 2 hours (outside 30m window)
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CheckInDeskDropInAsync(new DeskDropInRequest(
                ClassSessionId: session.Id,
                StudentId: student.Id,
                MembershipId: membership.Id
            ), staffUser.UserId!.Value));

        Assert.Equal(ReservationErrorCodes.DropInWindowClosed, ex.Code);
    }

    [Fact]
    public async Task RevokeAttendance_OnDropInWithCredit_RevertsToCancelledAndRestoresCredit()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Email = "sec@smartgym.local",
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var checkIn = await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id
        ), staffUser.UserId!.Value);

        var revocation = await service.RevokeAttendanceAsync(
            checkIn.Reservation.Id,
            new RevokeAttendanceRequest(AttendanceRevocationReason.StudentWithdrew, "El alumno debió retirarse antes de iniciar"),
            staffUser.UserId!.Value);

        Assert.Equal("Cancelled", revocation.ActionTaken);
        Assert.True(revocation.RightsRestored);
        Assert.Equal(ReservationStatus.Cancelled, revocation.Reservation.Status);

        var updatedSession = await context.ClassSessions.FindAsync(session.Id);
        Assert.Equal(0, updatedSession!.ReservedCount);
        Assert.Equal(0, updatedSession.AttendedCount);

        var updatedMembership = await context.Memberships.FindAsync(membership.Id);
        Assert.Equal(8, updatedMembership!.AvailableCredits); // Restored
    }

    [Fact]
    public async Task RevokeAttendance_OnPriorReservationWithStaffError_RevertsToConfirmed()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Email = "sec@smartgym.local",
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        // Crear reserva previa online/mostrador
        var resDto = await service.CreateReservationAsync(new CreateReservationRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id
        ), staffUser.UserId!.Value);

        // Dar presente
        await service.RecordAttendanceAsync(new RecordAttendanceRequest(resDto.Id, AttendanceSource.SecretaryDesk), staffUser.UserId!.Value);

        // Anular presente por error de tipeo del secretario
        var revocation = await service.RevokeAttendanceAsync(
            resDto.Id,
            new RevokeAttendanceRequest(AttendanceRevocationReason.StaffError, "Se marcó el alumno equivocado por error"),
            staffUser.UserId!.Value);

        Assert.Equal("RevertedToConfirmed", revocation.ActionTaken);
        Assert.False(revocation.RightsRestored);
        Assert.Equal(ReservationStatus.Confirmed, revocation.Reservation.Status);

        var updatedSession = await context.ClassSessions.FindAsync(session.Id);
        Assert.Equal(1, updatedSession!.ReservedCount); // Sigue reservada
        Assert.Equal(0, updatedSession.AttendedCount);  // Ya no atendida
    }

    [Fact]
    public async Task CheckInDeskDropIn_Idempotency_ReturnsSameReservationWithoutDoubleDebit()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Email = "sec@smartgym.local",
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var key = "test-key-123";
        var first = await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id,
            IdempotencyKey: key
        ), staffUser.UserId!.Value);

        var second = await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id,
            IdempotencyKey: key
        ), staffUser.UserId!.Value);

        Assert.Equal(first.Reservation.Id, second.Reservation.Id);
        Assert.True(second.WasIdempotentReplay);

        var updatedMembership = await context.Memberships.FindAsync(membership.Id);
        Assert.Equal(7, updatedMembership!.AvailableCredits); // Sólo se debitó una vez
    }

    [Fact]
    public async Task CheckInDeskDropIn_WhenClassFull_RejectsWithClassFull()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        // Llenar el cupo
        for (int i = 0; i < session.Capacity; i++)
        {
            session.IncrementReservedCount();
        }
        await context.SaveChangesAsync();

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CheckInDeskDropInAsync(new DeskDropInRequest(
                ClassSessionId: session.Id,
                StudentId: student.Id,
                MembershipId: membership.Id
            ), staffUser.UserId!.Value));

        Assert.Equal(ReservationErrorCodes.ClassFull, ex.Code);
    }

    [Fact]
    public async Task CheckInDeskDropIn_WhenAlreadyCheckedIn_RejectsWithAlreadyCheckedIn()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id
        ), staffUser.UserId!.Value);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CheckInDeskDropInAsync(new DeskDropInRequest(
                ClassSessionId: session.Id,
                StudentId: student.Id,
                MembershipId: membership.Id
            ), staffUser.UserId!.Value));

        Assert.Equal(ReservationErrorCodes.AlreadyCheckedIn, ex.Code);
    }

    [Fact]
    public async Task CheckInDeskDropIn_WithUnlimitedMembership_RecordsZeroAmountLedgerMovementLinkedToReservation()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        membership.MembershipPlan.IsUnlimited = true;
        membership.IsUnlimited = true;
        await context.SaveChangesAsync();

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var response = await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id
        ), staffUser.UserId!.Value);

        Assert.Equal(ReservationAccessType.UnlimitedMembership, response.Reservation.AccessType);
        Assert.NotNull(response.Reservation.CreditMovementId);

        var movement = await context.MembershipCreditMovements.FindAsync(response.Reservation.CreditMovementId.Value);
        Assert.NotNull(movement);
        Assert.Equal(0, movement.Amount);
        Assert.Equal(response.Reservation.Id, movement.ReservationId);
    }

    [Fact]
    public async Task RevokeAttendance_OnDropInWithSingleClassPass_ReleasesTicketSoItCanBeReusedAndKeepsPaymentCompleted()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, passPlan) = await SeedDropInScenarioAsync(context, startsAtUtc);

        // Agotar membresía para probar reutilización del SingleClassTicket liberado
        membership.AvailableCredits = 0;
        await context.SaveChangesAsync();

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        // 1. Cobrar pase individual y dar presente
        var firstDropIn = await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            SingleClassPlanId: passPlan.Id,
            PaymentMethod: PaymentMethod.Cash
        ), staffUser.UserId!.Value);

        Assert.NotNull(firstDropIn.PaymentId);
        var ticketMovementId = firstDropIn.Reservation.SingleClassTicketMovementId;
        Assert.NotNull(ticketMovementId);

        // 2. Anular presente por error de operador
        var revoked = await service.RevokeAttendanceAsync(
            firstDropIn.Reservation.Id,
            new RevokeAttendanceRequest(AttendanceRevocationReason.StaffError, "Error de fila"),
            staffUser.UserId!.Value);

        Assert.Equal("Cancelled", revoked.ActionTaken);
        Assert.True(revoked.RightsRestored);
        Assert.Equal(firstDropIn.PaymentId, revoked.PaymentId);

        var ticketAfterRevoke = await context.MembershipCreditMovements.FindAsync(ticketMovementId.Value);
        Assert.True(ticketAfterRevoke!.IsActive); // El pase sigue activo y liberado

        var payment = await context.Payments.FindAsync(firstDropIn.PaymentId.Value);
        Assert.Equal(PaymentStatus.Completed, payment!.Status);

        // 3. Reingresar al alumno a la misma clase reutilizando el pase liberado (sin volver a cobrar)
        var secondDropIn = await service.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id
        ), staffUser.UserId!.Value);

        Assert.Equal(ReservationStatus.Attended, secondDropIn.Reservation.Status);
        Assert.Equal(ReservationAccessType.SingleClassTicket, secondDropIn.Reservation.AccessType);
        Assert.Equal(ticketMovementId, secondDropIn.Reservation.SingleClassTicketMovementId);
    }

    [Fact]
    public async Task RevokeAttendance_OnPriorReservationWithStudentWithdrew_CancelsAndRestoresCredit()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var resDto = await service.CreateReservationAsync(new CreateReservationRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id
        ), staffUser.UserId!.Value);

        await service.RecordAttendanceAsync(new RecordAttendanceRequest(resDto.Id, AttendanceSource.SecretaryDesk), staffUser.UserId!.Value);

        var revocation = await service.RevokeAttendanceAsync(
            resDto.Id,
            new RevokeAttendanceRequest(AttendanceRevocationReason.StudentWithdrew, "Se descompuso antes de empezar"),
            staffUser.UserId!.Value);

        Assert.Equal("Cancelled", revocation.ActionTaken);
        Assert.True(revocation.RightsRestored);
        Assert.Equal(ReservationStatus.Cancelled, revocation.Reservation.Status);

        var updatedMembership = await context.Memberships.FindAsync(membership.Id);
        Assert.Equal(8, updatedMembership!.AvailableCredits);
    }

    [Fact]
    public async Task RevokeAttendance_StudentWithdrewAfterClassEnded_RejectsWithWindowClosed()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var serviceDuringWindow = CreateReservationService(context, BaseNowUtc, staffUser);

        var checkIn = await serviceDuringWindow.CheckInDeskDropInAsync(new DeskDropInRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id
        ), staffUser.UserId!.Value);

        // Avanzar el reloj 10 minutos después de finalizada la clase
        var afterClassEndUtc = session.EndsAtUtc.AddMinutes(10);
        var serviceAfterEnd = CreateReservationService(context, afterClassEndUtc, staffUser);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            serviceAfterEnd.RevokeAttendanceAsync(
                checkIn.Reservation.Id,
                new RevokeAttendanceRequest(AttendanceRevocationReason.StudentWithdrew, "Intenta anular tras finalizar"),
                staffUser.UserId!.Value));

        Assert.Equal(ReservationErrorCodes.AttendanceRevocationWindowClosed, ex.Code);
    }

    [Fact]
    public async Task CheckInDeskDropIn_WithIneligibleOrInactivePlan_RejectsWithSingleClassPlanNotAvailable()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        // Plan de 8 créditos sin IsSingleClassPass no es elegible como pase individual
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CheckInDeskDropInAsync(new DeskDropInRequest(
                ClassSessionId: session.Id,
                StudentId: student.Id,
                SingleClassPlanId: membership.MembershipPlanId,
                PaymentMethod: PaymentMethod.Cash
            ), staffUser.UserId!.Value));

        Assert.Equal(ReservationErrorCodes.SingleClassPlanNotAvailable, ex.Code);
    }

    [Fact]
    public async Task CheckInDeskDropIn_WhenCallerIsInstructorOnly_ThrowsForbidden()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddMinutes(15);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var instructorUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Instructor]
        };
        var service = CreateReservationService(context, BaseNowUtc, instructorUser);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CheckInDeskDropInAsync(new DeskDropInRequest(
                ClassSessionId: session.Id,
                StudentId: student.Id,
                MembershipId: membership.Id
            ), instructorUser.UserId!.Value));
    }

    [Fact]
    public async Task ChangingActivityRequiresBookingToFalse_PreservesExistingConfirmedReservations()
    {
        await using var context = CreateInMemoryDbContext();
        var startsAtUtc = BaseNowUtc.AddHours(3);
        var (student, session, membership, _) = await SeedDropInScenarioAsync(context, startsAtUtc);

        var staffUser = new StubCurrentUserService
        {
            IsAuthenticated = true,
            UserId = Guid.NewGuid(),
            Roles = [Roles.Secretary]
        };
        var service = CreateReservationService(context, BaseNowUtc, staffUser);

        var created = await service.CreateReservationAsync(new CreateReservationRequest(
            ClassSessionId: session.Id,
            StudentId: student.Id,
            MembershipId: membership.Id
        ), staffUser.UserId!.Value);

        var activity = await context.Activities.FindAsync(session.ActivityId);
        activity!.SetRequiresBooking(false);
        await context.SaveChangesAsync();

        var existing = await context.Reservations.FindAsync(created.Id);
        Assert.Equal(ReservationStatus.Confirmed, existing!.Status);

        var secondStudent = Person.Create("Ana", "Gomez", "40111222");
        var studentRole = await context.Roles.FindAsync((int)RoleType.Student);
        secondStudent.AddRole(studentRole!);
        context.People.Add(secondStudent);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateReservationAsync(new CreateReservationRequest(
                ClassSessionId: session.Id,
                StudentId: secondStudent.Id
            ), staffUser.UserId!.Value));

        Assert.Equal(ReservationErrorCodes.BookingNotRequired, ex.Code);
    }

    // --- Helpers de Infraestructura y Sembrado ---

    private static SmartGymDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<SmartGymDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new SmartGymDbContext(options);
    }

    private static ReservationService CreateReservationService(
        SmartGymDbContext context,
        DateTime nowUtc,
        ICurrentUserService currentUserService)
    {
        var calendar = new StubGymCalendar(DateOnly.FromDateTime(nowUtc), nowUtc);
        var paymentService = new PaymentService(context, calendar);

        return new ReservationService(
            context,
            calendar,
            paymentService: paymentService,
            currentUserService: currentUserService,
            reservationsOptions: Options.Create(new ReservationsOptions
            {
                CheckInOpensMinutesBeforeStart = 30,
                AttendanceRegistrationHoursAfterEnd = 24
            }),
            gymOptions: Options.Create(new GymOptions()));
    }

    private static async Task<(Person Student, ClassSession Session)> SeedSessionWithActivityRequiresBookingAsync(
        SmartGymDbContext context,
        bool requiresBooking)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        context.Roles.Add(studentRole);

        var student = Person.Create("Marcos", "Perez", "34111222");
        student.AddRole(studentRole);

        var instructor = Person.Create("Instructor", "Test", "30999888");
        var activity = new Activity("TEST", "Actividad Test", defaultCapacity: 15);
        activity.SetRequiresBooking(requiresBooking);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala A", Capacity = 20, IsActive = true };

        context.People.AddRange(instructor, student);
        context.Activities.Add(activity);
        context.Rooms.Add(room);

        var startsAtUtc = BaseNowUtc.AddHours(2);
        var endsAtUtc = startsAtUtc.AddHours(1);
        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            activity.Id,
            room.Id,
            startsAtUtc,
            endsAtUtc,
            capacity: 15,
            instructorId: instructor.Id,
            secretaryId: null);
        session.Open(BaseNowUtc.AddDays(-1));

        context.ClassSessions.Add(session);
        await context.SaveChangesAsync();

        return (student, session);
    }

    private static async Task<(Person Student, ClassSession Session, Membership Membership, MembershipPlan SinglePassPlan)> SeedDropInScenarioAsync(
        SmartGymDbContext context,
        DateTime startsAtUtc)
    {
        var studentRole = new Role { Id = (int)RoleType.Student, Name = RoleType.Student.ToString(), Description = "Student" };
        context.Roles.Add(studentRole);

        var student = Person.Create("Lucia", "Paz", "38555666");
        student.AddRole(studentRole);

        var instructor = Person.Create("Profe", "Uno", "31222333");
        var activity = new Activity("BOX", "Boxeo", defaultCapacity: 10);
        var room = new Room { Id = Guid.NewGuid(), Name = "Sala Boxeo", Capacity = 15, IsActive = true };

        context.People.AddRange(instructor, student);
        context.Activities.Add(activity);
        context.Rooms.Add(room);

        var endsAtUtc = startsAtUtc.AddHours(1);
        var session = ClassSession.CreateFromSchedule(
            Guid.NewGuid(),
            activity.Id,
            room.Id,
            startsAtUtc,
            endsAtUtc,
            capacity: 10,
            instructorId: instructor.Id,
            secretaryId: null);
        session.Open(BaseNowUtc.AddDays(-1));
        context.ClassSessions.Add(session);

        var sessionDate = DateOnly.FromDateTime(startsAtUtc);
        var calendar = new StubGymCalendar(sessionDate, startsAtUtc);
        var ledger = new CreditLedgerService(context, calendar);

        var regularPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pack 8 Clases",
            DurationDays = 30,
            Price = 25000m,
            Credits = 8,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsActive = true
        };

        var singlePassPlan = new MembershipPlan
        {
            Id = Guid.NewGuid(),
            Name = "Pase Diario Boxeo",
            DurationDays = 1,
            Price = 5000m,
            Credits = 1,
            IsUnlimited = false,
            AppliesToAllActivities = true,
            IsSingleClassPass = true,
            IsActive = true
        };

        context.MembershipPlans.AddRange(regularPlan, singlePassPlan);

        var membership = Membership.Create(student.Id, regularPlan, sessionDate.AddDays(-2), startsAtUtc.AddDays(-2), 25000m, 0m, 25000m);
        membership.Activate(sessionDate.AddDays(-2), startsAtUtc.AddDays(-2), Guid.NewGuid());
        await ledger.RecordInitialCreditsAsync(membership);
        context.Memberships.Add(membership);

        await context.SaveChangesAsync();

        return (student, session, membership, singlePassPlan);
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
