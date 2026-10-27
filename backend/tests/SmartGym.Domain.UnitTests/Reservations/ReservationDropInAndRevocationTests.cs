using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationDropInAndRevocationTests
{
    [Fact]
    public void CreateDropInAttended_ShouldInitializeAttendedStateCorrectly()
    {
        var classSessionId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var creditMovementId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var reservation = Reservation.CreateDropInAttended(
            classSessionId: classSessionId,
            studentId: studentId,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: nowUtc,
            staffUserId: staffUserId,
            membershipId: membershipId,
            creditMovementId: creditMovementId,
            singleClassTicketMovementId: null,
            paymentId: null,
            notes: "Ingreso directo en mostrador",
            idempotencyKey: "dropin-123");

        Assert.NotEqual(Guid.Empty, reservation.Id);
        Assert.Equal(classSessionId, reservation.ClassSessionId);
        Assert.Equal(studentId, reservation.StudentId);
        Assert.Equal(ReservationStatus.Attended, reservation.Status);
        Assert.Equal(ReservationOrigin.StaffDesk, reservation.Origin);
        Assert.True(reservation.IsDropIn);
        Assert.Equal(ReservationAccessType.MembershipCredit, reservation.AccessType);
        Assert.Equal(nowUtc, reservation.AttendedAtUtc);
        Assert.Equal(staffUserId, reservation.CheckedInByUserId);
        Assert.Equal(AttendanceSource.DeskDropIn, reservation.AttendanceSource);
        Assert.Equal(staffUserId, reservation.CreatedByUserId);
        Assert.Equal("Ingreso directo en mostrador", reservation.Notes);
        Assert.Equal("dropin-123", reservation.IdempotencyKey);

        Assert.Single(reservation.StatusChanges);
        var change = reservation.StatusChanges.First();
        Assert.Null(change.PreviousStatus);
        Assert.Equal(ReservationStatus.Attended, change.NewStatus);
        Assert.Equal(ReservationChangeTrigger.Attendance, change.Trigger);
        Assert.Equal(staffUserId, change.ChangedByUserId);
    }

    [Fact]
    public void RevokeAttendance_OnDropInReservation_ShouldTransitionToCancelled()
    {
        var classSessionId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var reservation = Reservation.CreateDropInAttended(
            classSessionId: classSessionId,
            studentId: studentId,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: nowUtc,
            staffUserId: staffUserId,
            membershipId: Guid.NewGuid(),
            creditMovementId: Guid.NewGuid());

        var revokeTime = nowUtc.AddMinutes(5);

        reservation.RevokeAttendance(
            reason: AttendanceRevocationReason.StaffError,
            notes: "Error al seleccionar alumno en recepción",
            byUserId: staffUserId,
            nowUtc: revokeTime);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(ReservationCancellationSource.AttendanceRevoked, reservation.CancellationSource);
        Assert.False(reservation.IsLateCancellation);
        Assert.Equal(revokeTime, reservation.CancelledAtUtc);
        Assert.Equal(staffUserId, reservation.CancelledByUserId);
        Assert.Equal(revokeTime, reservation.AttendanceRevokedAtUtc);
        Assert.Equal(staffUserId, reservation.AttendanceRevokedByUserId);
        Assert.Equal(AttendanceRevocationReason.StaffError, reservation.AttendanceRevocationReason);
        Assert.Equal("Error al seleccionar alumno en recepción", reservation.AttendanceRevocationNotes);

        var latestChange = reservation.StatusChanges.Last();
        Assert.Equal(ReservationStatus.Attended, latestChange.PreviousStatus);
        Assert.Equal(ReservationStatus.Cancelled, latestChange.NewStatus);
        Assert.Equal(ReservationChangeTrigger.Attendance, latestChange.Trigger);
    }

    [Fact]
    public void RevokeAttendance_OnRegularReservation_WithStaffError_ShouldRevertToConfirmed()
    {
        var classSessionId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var reservation = Reservation.CreateConfirmed(
            classSessionId: classSessionId,
            studentId: studentId,
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: nowUtc);

        reservation.MarkAttended(AttendanceSource.SecretaryDesk, staffUserId, nowUtc.AddMinutes(10));
        Assert.Equal(ReservationStatus.Attended, reservation.Status);

        var revokeTime = nowUtc.AddMinutes(15);
        reservation.RevokeAttendance(
            reason: AttendanceRevocationReason.StaffError,
            notes: "Se marcó presente por error a otro alumno con reserva previa",
            byUserId: staffUserId,
            nowUtc: revokeTime);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Null(reservation.AttendedAtUtc);
        Assert.Null(reservation.CheckedInByUserId);
        Assert.Null(reservation.AttendanceSource);
        Assert.Equal(revokeTime, reservation.AttendanceRevokedAtUtc);
        Assert.Equal(staffUserId, reservation.AttendanceRevokedByUserId);
        Assert.Equal(AttendanceRevocationReason.StaffError, reservation.AttendanceRevocationReason);
        Assert.Equal("Se marcó presente por error a otro alumno con reserva previa", reservation.AttendanceRevocationNotes);

        var latestChange = reservation.StatusChanges.Last();
        Assert.Equal(ReservationStatus.Attended, latestChange.PreviousStatus);
        Assert.Equal(ReservationStatus.Confirmed, latestChange.NewStatus);
    }

    [Fact]
    public void RevokeAttendance_OnRegularReservation_WithStudentWithdrew_ShouldTransitionToCancelled()
    {
        var classSessionId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        var reservation = Reservation.CreateConfirmed(
            classSessionId: classSessionId,
            studentId: studentId,
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: nowUtc);

        reservation.MarkAttended(AttendanceSource.SecretaryDesk, staffUserId, nowUtc.AddMinutes(10));

        var revokeTime = nowUtc.AddMinutes(12);
        reservation.RevokeAttendance(
            reason: AttendanceRevocationReason.StudentWithdrew,
            notes: "El alumno se retiró antes de iniciar la clase por emergencia",
            byUserId: staffUserId,
            nowUtc: revokeTime);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Equal(ReservationCancellationSource.AttendanceRevoked, reservation.CancellationSource);
        Assert.False(reservation.IsLateCancellation);
        Assert.Equal(AttendanceRevocationReason.StudentWithdrew, reservation.AttendanceRevocationReason);
    }

    [Fact]
    public void RevokeAttendance_WhenNotAttended_ShouldThrowInvalidOperationException()
    {
        var reservation = Reservation.CreateConfirmed(
            classSessionId: Guid.NewGuid(),
            studentId: Guid.NewGuid(),
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: DateTime.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            reservation.RevokeAttendance(AttendanceRevocationReason.StaffError, "Nota", Guid.NewGuid(), DateTime.UtcNow));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RevokeAttendance_WithoutNotes_ShouldThrowArgumentException(string? invalidNotes)
    {
        var reservation = Reservation.CreateDropInAttended(
            classSessionId: Guid.NewGuid(),
            studentId: Guid.NewGuid(),
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: DateTime.UtcNow);

        Assert.Throws<ArgumentException>(() =>
            reservation.RevokeAttendance(AttendanceRevocationReason.StaffError, invalidNotes!, Guid.NewGuid(), DateTime.UtcNow));
    }
}
