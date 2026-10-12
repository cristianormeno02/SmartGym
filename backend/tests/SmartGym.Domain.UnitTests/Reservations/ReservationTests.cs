using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Reservations;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Reservations;
using Xunit;

namespace SmartGym.Domain.UnitTests.Reservations;

public class ReservationTests
{
    private static readonly DateTime NowUtc = new(2026, 6, 10, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ReservationStatus_ShouldPreservePersistedNumericValues()
    {
        Assert.Equal(1, (int)ReservationStatus.Confirmed);
        Assert.Equal(3, (int)ReservationStatus.Cancelled);
        Assert.Equal(4, (int)ReservationStatus.Attended);
        Assert.Equal(5, (int)ReservationStatus.NoShow);
        Assert.Equal(6, (int)ReservationStatus.Waitlisted);
        Assert.Equal(7, (int)ReservationStatus.Pending);
        Assert.False(Enum.IsDefined(typeof(ReservationStatus), 2));
    }

    [Fact]
    public void CreateConfirmed_ShouldInitializeInConfirmedStatus_RecordHistoryAndEmitPostCommitEvent()
    {
        var sessionId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var debitMovementId = Guid.NewGuid();
        var operatorUserId = Guid.NewGuid();

        var reservation = Reservation.CreateConfirmed(
            classSessionId: sessionId,
            studentId: studentId,
            origin: ReservationOrigin.StaffDesk,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: NowUtc,
            createdByUserId: operatorUserId,
            membershipId: membershipId,
            creditMovementId: debitMovementId,
            notes: "  Reserva en mostrador  ",
            idempotencyKey: "  idem-001  ");

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(ReservationOrigin.StaffDesk, reservation.Origin);
        Assert.Equal(ReservationAccessType.MembershipCredit, reservation.AccessType);
        Assert.Equal(membershipId, reservation.MembershipId);
        Assert.Equal(debitMovementId, reservation.CreditMovementId);
        Assert.Equal(operatorUserId, reservation.CreatedByUserId);
        Assert.Equal(NowUtc, reservation.ReservedAtUtc);
        Assert.Equal(NowUtc, reservation.ConfirmedAtUtc);
        Assert.Equal("Reserva en mostrador", reservation.Notes);
        Assert.Equal("idem-001", reservation.IdempotencyKey);

        var change = Assert.Single(reservation.StatusChanges);
        Assert.Null(change.PreviousStatus);
        Assert.Equal(ReservationStatus.Confirmed, change.NewStatus);
        Assert.Equal(ReservationChangeTrigger.StaffAction, change.Trigger);
        Assert.Equal(operatorUserId, change.ChangedByUserId);
        Assert.Equal(NowUtc, change.ChangedAtUtc);

        var evt = Assert.Single(reservation.DomainEvents);
        var confirmedEvt = Assert.IsType<ReservationConfirmedEvent>(evt);
        Assert.IsAssignableFrom<IPostCommitDomainEvent>(confirmedEvt);
        Assert.Equal(reservation.Id, confirmedEvt.ReservationId);
        Assert.Equal(sessionId, confirmedEvt.ClassSessionId);
        Assert.Equal(studentId, confirmedEvt.StudentId);
        Assert.Equal(ReservationAccessType.MembershipCredit, confirmedEvt.AccessType);
    }

    [Fact]
    public void CreateWaitlisted_ShouldInitializeInWaitlistedStatus_RecordHistoryAndEmitEvent()
    {
        var sessionId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var reservation = Reservation.CreateWaitlisted(
            classSessionId: sessionId,
            studentId: studentId,
            position: 2,
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: NowUtc,
            createdByUserId: userId,
            membershipId: Guid.NewGuid());

        Assert.Equal(ReservationStatus.Waitlisted, reservation.Status);
        Assert.Equal(2, reservation.WaitListPosition);
        Assert.Equal(NowUtc, reservation.WaitlistEnteredAtUtc);
        Assert.Null(reservation.ConfirmedAtUtc);
        Assert.Null(reservation.CreditMovementId);

        var change = Assert.Single(reservation.StatusChanges);
        Assert.Null(change.PreviousStatus);
        Assert.Equal(ReservationStatus.Waitlisted, change.NewStatus);
        Assert.Equal(ReservationChangeTrigger.StudentAction, change.Trigger);

        var evt = Assert.Single(reservation.DomainEvents);
        var waitlistedEvt = Assert.IsType<ReservationWaitlistedEvent>(evt);
        Assert.Equal(2, waitlistedEvt.WaitListPosition);
    }

    [Fact]
    public void Confirm_FromWaitlisted_ShouldPromoteToConfirmed_ClearWaitlistPositionAndEmitPromotedEvent()
    {
        var reservation = Reservation.CreateWaitlisted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            position: 1,
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: NowUtc);
        reservation.ClearDomainEvents();

        var promotedAt = NowUtc.AddMinutes(15);
        var creditMovementId = Guid.NewGuid();

        reservation.Confirm(
            nowUtc: promotedAt,
            changedByUserId: null,
            trigger: ReservationChangeTrigger.WaitlistPromotion,
            reason: "Promoción automática desde lista de espera",
            accessType: ReservationAccessType.MembershipCredit,
            creditMovementId: creditMovementId);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Null(reservation.WaitListPosition);
        Assert.Equal(promotedAt, reservation.ConfirmedAtUtc);
        Assert.Equal(promotedAt, reservation.WaitlistPromotedAtUtc);
        Assert.Equal(creditMovementId, reservation.CreditMovementId);
        Assert.Equal(2, reservation.StatusChanges.Count);

        var lastChange = reservation.StatusChanges.Last();
        Assert.Equal(ReservationStatus.Waitlisted, lastChange.PreviousStatus);
        Assert.Equal(ReservationStatus.Confirmed, lastChange.NewStatus);
        Assert.Equal(ReservationChangeTrigger.WaitlistPromotion, lastChange.Trigger);

        var evt = Assert.Single(reservation.DomainEvents);
        Assert.IsType<ReservationPromotedFromWaitlistEvent>(evt);
    }

    [Fact]
    public void OfferSpot_And_AcceptOffer_ShouldTransitionWaitlistedToPendingToConfirmed()
    {
        var reservation = Reservation.CreateWaitlisted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            position: 1,
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: NowUtc);
        reservation.ClearDomainEvents();

        var offerTime = NowUtc.AddMinutes(10);
        var expiresAt = offerTime.AddMinutes(30);

        reservation.OfferSpot(offerTime, expiresAt);

        Assert.Equal(ReservationStatus.Pending, reservation.Status);
        Assert.Null(reservation.WaitListPosition);
        Assert.Equal(offerTime, reservation.WaitlistPromotedAtUtc);
        Assert.Equal(expiresAt, reservation.WaitlistOfferExpiresAtUtc);

        var offerEvt = Assert.Single(reservation.DomainEvents);
        var createdOfferEvt = Assert.IsType<WaitlistOfferCreatedEvent>(offerEvt);
        Assert.Equal(expiresAt, createdOfferEvt.OfferExpiresAtUtc);

        reservation.ClearDomainEvents();
        var acceptTime = offerTime.AddMinutes(5);
        var debitId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        reservation.AcceptOffer(
            nowUtc: acceptTime,
            changedByUserId: actorId,
            trigger: ReservationChangeTrigger.StudentAction,
            reason: "Oferta aceptada por el alumno",
            accessType: ReservationAccessType.MembershipCredit,
            creditMovementId: debitId);

        Assert.Equal(ReservationStatus.Confirmed, reservation.Status);
        Assert.Equal(acceptTime, reservation.ConfirmedAtUtc);
        Assert.Null(reservation.WaitlistOfferExpiresAtUtc);
        Assert.Equal(debitId, reservation.CreditMovementId);
        Assert.Equal(3, reservation.StatusChanges.Count);

        var acceptEvt = Assert.Single(reservation.DomainEvents);
        Assert.IsType<ReservationConfirmedEvent>(acceptEvt);
    }

    [Fact]
    public void AcceptOffer_WhenOfferExpired_ShouldThrowInvalidOperationException()
    {
        var reservation = Reservation.CreateWaitlisted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            position: 1,
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: NowUtc);

        var offerTime = NowUtc.AddMinutes(10);
        var expiresAt = offerTime.AddMinutes(30);
        reservation.OfferSpot(offerTime, expiresAt);

        Assert.Throws<InvalidOperationException>(() =>
            reservation.AcceptOffer(expiresAt.AddSeconds(1)));
    }

    [Fact]
    public void Cancel_EarlyAndLateAndOverride_ShouldRecordMetadataHistoryAndEvents()
    {
        var reservation = Reservation.CreateConfirmed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.MembershipCredit,
            NowUtc);
        reservation.ClearDomainEvents();

        var cancelTime = NowUtc.AddHours(1);
        var refundId = Guid.NewGuid();
        var staffUserId = Guid.NewGuid();

        reservation.Cancel(
            reason: "Cancelación con excepción médica",
            isLateCancellation: false,
            nowUtc: cancelTime,
            refundMovementId: refundId,
            cancelledByUserId: staffUserId,
            source: ReservationCancellationSource.Staff,
            trigger: ReservationChangeTrigger.StaffAction,
            isPolicyOverride: true,
            policyOverrideReason: "Certificado médico presentado");

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.False(reservation.IsLateCancellation);
        Assert.True(reservation.IsPolicyOverride);
        Assert.Equal("Certificado médico presentado", reservation.PolicyOverrideReason);
        Assert.Equal(refundId, reservation.RefundCreditMovementId);
        Assert.Equal(staffUserId, reservation.CancelledByUserId);
        Assert.Equal(ReservationCancellationSource.Staff, reservation.CancellationSource);
        Assert.Equal(cancelTime, reservation.CancelledAtUtc);

        var lastChange = reservation.StatusChanges.Last();
        Assert.Equal(ReservationStatus.Confirmed, lastChange.PreviousStatus);
        Assert.Equal(ReservationStatus.Cancelled, lastChange.NewStatus);
        Assert.Equal(ReservationChangeTrigger.StaffAction, lastChange.Trigger);

        var evt = Assert.Single(reservation.DomainEvents);
        Assert.IsType<ReservationCancelledEvent>(evt);
    }

    [Fact]
    public void Cancel_PendingOfferExpired_ShouldEmitWaitlistOfferExpiredEvent()
    {
        var reservation = Reservation.CreateWaitlisted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            position: 1,
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: NowUtc);

        var offerTime = NowUtc.AddMinutes(5);
        var expiresAt = offerTime.AddMinutes(30);
        reservation.OfferSpot(offerTime, expiresAt);
        reservation.ClearDomainEvents();

        reservation.Cancel(
            reason: "Oferta de cupo vencida",
            isLateCancellation: false,
            nowUtc: expiresAt,
            source: ReservationCancellationSource.WaitlistOfferExpired,
            trigger: ReservationChangeTrigger.WaitlistWorker);

        Assert.Equal(ReservationStatus.Cancelled, reservation.Status);
        Assert.Null(reservation.WaitlistOfferExpiresAtUtc);
        var evt = Assert.Single(reservation.DomainEvents);
        Assert.IsType<WaitlistOfferExpiredEvent>(evt);
    }

    [Fact]
    public void MarkAttended_And_MarkNoShow_FromConfirmed_ShouldRecordStatusChange()
    {
        var attendedReservation = Reservation.CreateConfirmed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.MembershipCredit,
            NowUtc);
        var instructorId = Guid.NewGuid();
        var checkInTime = NowUtc.AddHours(2);

        attendedReservation.MarkAttended(AttendanceSource.InstructorInRoom, instructorId, checkInTime);

        Assert.Equal(ReservationStatus.Attended, attendedReservation.Status);
        Assert.Equal(AttendanceSource.InstructorInRoom, attendedReservation.AttendanceSource);
        Assert.Equal(instructorId, attendedReservation.CheckedInByUserId);
        Assert.Equal(checkInTime, attendedReservation.AttendedAtUtc);
        Assert.Equal(ReservationStatus.Attended, attendedReservation.StatusChanges.Last().NewStatus);

        var noShowReservation = Reservation.CreateConfirmed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.MembershipCredit,
            NowUtc);
        var noShowTime = NowUtc.AddHours(3);

        noShowReservation.MarkNoShow(noShowTime, instructorId);

        Assert.Equal(ReservationStatus.NoShow, noShowReservation.Status);
        Assert.Equal(ReservationStatus.NoShow, noShowReservation.StatusChanges.Last().NewStatus);
        Assert.Equal(instructorId, noShowReservation.StatusChanges.Last().ChangedByUserId);
    }

    [Theory]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Attended)]
    [InlineData(ReservationStatus.NoShow)]
    public void TerminalStates_ShouldRejectAnyFurtherTransition(ReservationStatus terminalStatus)
    {
        var reservation = Reservation.CreateConfirmed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ReservationOrigin.StudentSelfService,
            ReservationAccessType.MembershipCredit,
            NowUtc);

        switch (terminalStatus)
        {
            case ReservationStatus.Cancelled:
                reservation.Cancel("Cancelada", isLateCancellation: false, nowUtc: NowUtc.AddMinutes(5));
                break;
            case ReservationStatus.Attended:
                reservation.MarkAttended(AttendanceSource.SecretaryDesk, Guid.NewGuid(), NowUtc.AddMinutes(5));
                break;
            case ReservationStatus.NoShow:
                reservation.MarkNoShow(NowUtc.AddMinutes(5), Guid.NewGuid());
                break;
        }

        Assert.Throws<InvalidOperationException>(() =>
            reservation.Cancel("Segundo intento", isLateCancellation: false, nowUtc: NowUtc.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() =>
            reservation.Confirm(NowUtc.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() =>
            reservation.OfferSpot(NowUtc.AddMinutes(10), NowUtc.AddMinutes(40)));
        Assert.Throws<InvalidOperationException>(() =>
            reservation.AcceptOffer(NowUtc.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() =>
            reservation.MarkAttended(AttendanceSource.SecretaryDesk, Guid.NewGuid(), NowUtc.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() =>
            reservation.MarkNoShow(NowUtc.AddMinutes(10), Guid.NewGuid()));
    }

    [Fact]
    public void NonConfirmedStates_ShouldRejectAttendanceAndNoShow()
    {
        var waitlisted = Reservation.CreateWaitlisted(
            Guid.NewGuid(),
            Guid.NewGuid(),
            position: 1,
            origin: ReservationOrigin.StudentSelfService,
            accessType: ReservationAccessType.MembershipCredit,
            nowUtc: NowUtc);

        Assert.Throws<InvalidOperationException>(() =>
            waitlisted.MarkAttended(AttendanceSource.SecretaryDesk, Guid.NewGuid(), NowUtc.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() =>
            waitlisted.MarkNoShow(NowUtc.AddMinutes(10), Guid.NewGuid()));

        waitlisted.OfferSpot(NowUtc.AddMinutes(5), NowUtc.AddMinutes(35));
        Assert.Equal(ReservationStatus.Pending, waitlisted.Status);

        Assert.Throws<InvalidOperationException>(() =>
            waitlisted.MarkAttended(AttendanceSource.SecretaryDesk, Guid.NewGuid(), NowUtc.AddMinutes(10)));
        Assert.Throws<InvalidOperationException>(() =>
            waitlisted.MarkNoShow(NowUtc.AddMinutes(10), Guid.NewGuid()));
    }
}
