using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationTests
{
    [Fact]
    public void Reservation_ShouldInitializeInReservedStatus()
    {
        // Arrange
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            Status = ReservationStatus.Reserved,
            ReservedAtUtc = DateTime.UtcNow
        };

        // Assert
        Assert.Equal(ReservationStatus.Reserved, reservation.Status);
        Assert.False(reservation.IsLateCancellation);
    }

    [Fact]
    public void Reservation_ShouldMarkAttendedByInstructorOrSecretary()
    {
        // Arrange
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            Status = ReservationStatus.Reserved
        };

        var instructorUserId = Guid.NewGuid();

        // Act
        reservation.MarkAttended(AttendanceSource.InstructorInRoom, instructorUserId);

        // Assert
        Assert.Equal(ReservationStatus.Attended, reservation.Status);
        Assert.Equal(AttendanceSource.InstructorInRoom, reservation.AttendanceSource);
        Assert.Equal(instructorUserId, reservation.CheckedInByUserId);
        Assert.NotNull(reservation.AttendedAtUtc);
    }

    [Fact]
    public void Reservation_ShouldHandleEarlyCancellation_WithRefund()
    {
        // Arrange
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            Status = ReservationStatus.Reserved
        };

        var refundMovementId = Guid.NewGuid();

        // Act
        reservation.Cancel("Aviso anticipado", isLateCancellation: false, refundMovementId: refundMovementId);

        // Assert
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.False(reservation.IsLateCancellation);
        Assert.Equal("Aviso anticipado", reservation.CancellationReason);
        Assert.Equal(refundMovementId, reservation.RefundCreditMovementId);
    }

    [Fact]
    public void Reservation_ShouldHandleLateCancellation_WithoutRefund()
    {
        // Arrange
        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            ClassSessionId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            Status = ReservationStatus.Reserved
        };

        // Act
        reservation.Cancel("Cancelación a última hora", isLateCancellation: true, refundMovementId: null);

        // Assert
        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.True(reservation.IsLateCancellation);
        Assert.Null(reservation.RefundCreditMovementId);
    }
}
