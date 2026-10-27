using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Activities;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Memberships;
using SmartGym.Domain.Entities.Operations;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Reservations;

namespace SmartGym.Domain.Entities.Reservations;

public class Reservation : BaseEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public Guid ClassSessionId { get; set; }
    public ClassSession ClassSession { get; set; } = null!;

    public Guid StudentId { get; set; }
    public Person Student { get; set; } = null!;

    public ReservationStatus Status { get; set; } = ReservationStatus.Confirmed;
    public ReservationOrigin Origin { get; set; } = ReservationOrigin.StudentSelfService;
    public bool IsDropIn { get; set; } = false;
    public ReservationAccessType AccessType { get; set; } = ReservationAccessType.MembershipCredit;

    public Guid? MembershipId { get; set; }
    public Membership? Membership { get; set; }

    public Guid? CreditMovementId { get; set; }
    public MembershipCreditMovement? CreditMovement { get; set; }

    public Guid? RefundCreditMovementId { get; set; }
    public MembershipCreditMovement? RefundCreditMovement { get; set; }

    public Guid? SingleClassTicketMovementId { get; set; }
    public MembershipCreditMovement? SingleClassTicketMovement { get; set; }

    public Guid? PaymentId { get; set; }
    public Payment? Payment { get; set; }

    public Guid? PromotionRedemptionId { get; set; }
    public PromotionRedemption? PromotionRedemption { get; set; }

    public string? RequestedPromotionCode { get; set; }

    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public int? WaitListPosition { get; set; }
    public DateTime? WaitlistEnteredAtUtc { get; set; }
    public DateTime? WaitlistPromotedAtUtc { get; set; }
    public DateTime? WaitlistOfferExpiresAtUtc { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public User? CancelledByUser { get; set; }
    public string? CancellationReason { get; set; }
    public ReservationCancellationSource? CancellationSource { get; set; }
    public bool IsLateCancellation { get; set; }
    public bool IsPolicyOverride { get; set; }
    public string? PolicyOverrideReason { get; set; }
    public bool AffectedBySessionChange { get; set; }
    public Guid? RescheduledToSessionId { get; set; }
    public ClassSession? RescheduledToSession { get; set; }

    public DateTime? AttendedAtUtc { get; set; }
    public Guid? CheckedInByUserId { get; set; }
    public User? CheckedInByUser { get; set; }
    public AttendanceSource? AttendanceSource { get; set; }

    public DateTime? AttendanceRevokedAtUtc { get; set; }
    public Guid? AttendanceRevokedByUserId { get; set; }
    public User? AttendanceRevokedByUser { get; set; }
    public AttendanceRevocationReason? AttendanceRevocationReason { get; set; }
    public string? AttendanceRevocationNotes { get; set; }

    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }

    public uint Version { get; set; }

    public ICollection<ReservationStatusChange> StatusChanges { get; set; } = new List<ReservationStatusChange>();

    public static Reservation CreateConfirmed(
        Guid classSessionId,
        Guid studentId,
        ReservationOrigin origin,
        ReservationAccessType accessType,
        DateTime nowUtc,
        Guid? createdByUserId = null,
        Guid? membershipId = null,
        Guid? creditMovementId = null,
        Guid? singleClassTicketMovementId = null,
        Guid? paymentId = null,
        Guid? promotionRedemptionId = null,
        string? requestedPromotionCode = null,
        bool isPolicyOverride = false,
        string? policyOverrideReason = null,
        string? notes = null,
        string? idempotencyKey = null,
        string? initialStatusReason = null)
    {
        if (classSessionId == Guid.Empty)
        {
            throw new ArgumentException("La clase es obligatoria.", nameof(classSessionId));
        }

        if (studentId == Guid.Empty)
        {
            throw new ArgumentException("El alumno es obligatorio.", nameof(studentId));
        }

        if (isPolicyOverride && string.IsNullOrWhiteSpace(policyOverrideReason))
        {
            throw new ArgumentException("El motivo de excepción administrativa es obligatorio.", nameof(policyOverrideReason));
        }

        var reservationId = Guid.NewGuid();
        var trigger = origin == ReservationOrigin.StaffDesk
            ? ReservationChangeTrigger.StaffAction
            : ReservationChangeTrigger.StudentAction;

        var reservation = new Reservation
        {
            Id = reservationId,
            ClassSessionId = classSessionId,
            StudentId = studentId,
            Status = ReservationStatus.Confirmed,
            Origin = origin,
            AccessType = accessType,
            MembershipId = membershipId,
            CreditMovementId = creditMovementId,
            SingleClassTicketMovementId = singleClassTicketMovementId,
            PaymentId = paymentId,
            PromotionRedemptionId = promotionRedemptionId,
            RequestedPromotionCode = NormalizeOptional(requestedPromotionCode),
            ReservedAtUtc = nowUtc,
            ConfirmedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = createdByUserId,
            IsPolicyOverride = isPolicyOverride,
            PolicyOverrideReason = NormalizeOptional(policyOverrideReason),
            Notes = NormalizeOptional(notes),
            IdempotencyKey = NormalizeOptional(idempotencyKey),
            IsActive = true
        };

        var reason = string.IsNullOrWhiteSpace(initialStatusReason)
            ? (isPolicyOverride
                ? $"Reserva confirmada con excepción: {reservation.PolicyOverrideReason}"
                : "Reserva confirmada")
            : initialStatusReason.Trim();

        reservation.RecordStatusChange(
            previousStatus: null,
            newStatus: ReservationStatus.Confirmed,
            reason: reason,
            changedByUserId: createdByUserId,
            trigger: trigger,
            changedAtUtc: nowUtc);

        reservation.AddDomainEvent(new ReservationConfirmedEvent(
            reservation.Id,
            reservation.ClassSessionId,
            reservation.StudentId,
            reservation.AccessType,
            nowUtc));

        return reservation;
    }

    public static Reservation CreateDropInAttended(
        Guid classSessionId,
        Guid studentId,
        ReservationAccessType accessType,
        DateTime nowUtc,
        Guid? staffUserId = null,
        Guid? membershipId = null,
        Guid? creditMovementId = null,
        Guid? singleClassTicketMovementId = null,
        Guid? paymentId = null,
        string? notes = null,
        string? idempotencyKey = null)
    {
        if (classSessionId == Guid.Empty)
        {
            throw new ArgumentException("La clase es obligatoria.", nameof(classSessionId));
        }

        if (studentId == Guid.Empty)
        {
            throw new ArgumentException("El alumno es obligatorio.", nameof(studentId));
        }

        var reservationId = Guid.NewGuid();

        var reservation = new Reservation
        {
            Id = reservationId,
            ClassSessionId = classSessionId,
            StudentId = studentId,
            Status = ReservationStatus.Attended,
            Origin = ReservationOrigin.StaffDesk,
            IsDropIn = true,
            AccessType = accessType,
            MembershipId = membershipId,
            CreditMovementId = creditMovementId,
            SingleClassTicketMovementId = singleClassTicketMovementId,
            PaymentId = paymentId,
            ReservedAtUtc = nowUtc,
            ConfirmedAtUtc = nowUtc,
            AttendedAtUtc = nowUtc,
            AttendanceSource = SmartGym.Domain.Enums.AttendanceSource.DeskDropIn,
            CheckedInByUserId = staffUserId,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = staffUserId,
            Notes = NormalizeOptional(notes),
            IdempotencyKey = NormalizeOptional(idempotencyKey),
            IsActive = true
        };

        reservation.RecordStatusChange(
            previousStatus: null,
            newStatus: ReservationStatus.Attended,
            reason: "Check-in directo en mostrador (Drop-in)",
            changedByUserId: staffUserId,
            trigger: ReservationChangeTrigger.Attendance,
            changedAtUtc: nowUtc);

        return reservation;
    }

    public static Reservation CreateWaitlisted(
        Guid classSessionId,
        Guid studentId,
        int position,
        ReservationOrigin origin,
        ReservationAccessType accessType,
        DateTime nowUtc,
        Guid? createdByUserId = null,
        Guid? membershipId = null,
        Guid? singleClassTicketMovementId = null,
        string? requestedPromotionCode = null,
        bool isPolicyOverride = false,
        string? policyOverrideReason = null,
        string? notes = null,
        string? idempotencyKey = null,
        string? initialStatusReason = null)
    {
        if (classSessionId == Guid.Empty)
        {
            throw new ArgumentException("La clase es obligatoria.", nameof(classSessionId));
        }

        if (studentId == Guid.Empty)
        {
            throw new ArgumentException("El alumno es obligatorio.", nameof(studentId));
        }

        if (position < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "La posición en lista de espera debe ser mayor o igual a 1.");
        }

        if (isPolicyOverride && string.IsNullOrWhiteSpace(policyOverrideReason))
        {
            throw new ArgumentException("El motivo de excepción administrativa es obligatorio.", nameof(policyOverrideReason));
        }

        var reservationId = Guid.NewGuid();
        var trigger = origin == ReservationOrigin.StaffDesk
            ? ReservationChangeTrigger.StaffAction
            : ReservationChangeTrigger.StudentAction;

        var reservation = new Reservation
        {
            Id = reservationId,
            ClassSessionId = classSessionId,
            StudentId = studentId,
            Status = ReservationStatus.Waitlisted,
            Origin = origin,
            AccessType = accessType,
            MembershipId = membershipId,
            SingleClassTicketMovementId = singleClassTicketMovementId,
            RequestedPromotionCode = NormalizeOptional(requestedPromotionCode),
            WaitListPosition = position,
            WaitlistEnteredAtUtc = nowUtc,
            ReservedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = createdByUserId,
            IsPolicyOverride = isPolicyOverride,
            PolicyOverrideReason = NormalizeOptional(policyOverrideReason),
            Notes = NormalizeOptional(notes),
            IdempotencyKey = NormalizeOptional(idempotencyKey),
            IsActive = true
        };

        var reason = string.IsNullOrWhiteSpace(initialStatusReason)
            ? $"Ingreso a lista de espera en posición {position}"
            : initialStatusReason.Trim();

        reservation.RecordStatusChange(
            previousStatus: null,
            newStatus: ReservationStatus.Waitlisted,
            reason: reason,
            changedByUserId: createdByUserId,
            trigger: trigger,
            changedAtUtc: nowUtc);

        reservation.AddDomainEvent(new ReservationWaitlistedEvent(
            reservation.Id,
            reservation.ClassSessionId,
            reservation.StudentId,
            position,
            nowUtc));

        return reservation;
    }

    public void Confirm(
        DateTime nowUtc,
        Guid? changedByUserId = null,
        ReservationChangeTrigger trigger = ReservationChangeTrigger.WaitlistPromotion,
        string reason = "Promoción automática desde lista de espera",
        ReservationAccessType? accessType = null,
        Guid? membershipId = null,
        Guid? creditMovementId = null,
        Guid? singleClassTicketMovementId = null,
        Guid? paymentId = null,
        Guid? promotionRedemptionId = null)
    {
        if (Status != ReservationStatus.Waitlisted)
        {
            throw new InvalidOperationException($"No se puede promover directamente a confirmada una reserva en estado {Status}.");
        }

        var previousStatus = Status;
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "Promoción automática desde lista de espera"
            : reason.Trim();

        ApplyConfirmedAccessRight(
            accessType,
            membershipId,
            creditMovementId,
            singleClassTicketMovementId,
            paymentId,
            promotionRedemptionId);

        Status = ReservationStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
        WaitlistPromotedAtUtc = nowUtc;
        WaitListPosition = null;
        WaitlistOfferExpiresAtUtc = null;
        UpdatedAtUtc = nowUtc;

        RecordStatusChange(previousStatus, ReservationStatus.Confirmed, normalizedReason, changedByUserId, trigger, nowUtc);
        AddDomainEvent(new ReservationPromotedFromWaitlistEvent(Id, ClassSessionId, StudentId, AccessType, nowUtc));
    }

    public void OfferSpot(
        DateTime nowUtc,
        DateTime offerExpiresAtUtc,
        Guid? changedByUserId = null,
        ReservationChangeTrigger trigger = ReservationChangeTrigger.WaitlistPromotion,
        string reason = "Oferta de cupo desde lista de espera")
    {
        if (Status != ReservationStatus.Waitlisted)
        {
            throw new InvalidOperationException($"Sólo se puede ofrecer un cupo a una reserva en lista de espera (estado actual: {Status}).");
        }

        if (offerExpiresAtUtc <= nowUtc)
        {
            throw new ArgumentException("El vencimiento de la oferta debe ser posterior al instante actual.", nameof(offerExpiresAtUtc));
        }

        var previousStatus = Status;
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "Oferta de cupo desde lista de espera"
            : reason.Trim();

        Status = ReservationStatus.Pending;
        WaitListPosition = null;
        WaitlistPromotedAtUtc = nowUtc;
        WaitlistOfferExpiresAtUtc = offerExpiresAtUtc;
        UpdatedAtUtc = nowUtc;

        RecordStatusChange(previousStatus, ReservationStatus.Pending, normalizedReason, changedByUserId, trigger, nowUtc);
        AddDomainEvent(new WaitlistOfferCreatedEvent(Id, ClassSessionId, StudentId, offerExpiresAtUtc, nowUtc));
    }

    public void AcceptOffer(
        DateTime nowUtc,
        Guid? changedByUserId = null,
        ReservationChangeTrigger trigger = ReservationChangeTrigger.StudentAction,
        string reason = "Oferta de cupo aceptada",
        ReservationAccessType? accessType = null,
        Guid? membershipId = null,
        Guid? creditMovementId = null,
        Guid? singleClassTicketMovementId = null,
        Guid? paymentId = null,
        Guid? promotionRedemptionId = null)
    {
        if (Status != ReservationStatus.Pending)
        {
            throw new InvalidOperationException($"Sólo se puede aceptar una oferta cuando la reserva está en estado Pending (estado actual: {Status}).");
        }

        if (WaitlistOfferExpiresAtUtc.HasValue && nowUtc > WaitlistOfferExpiresAtUtc.Value)
        {
            throw new InvalidOperationException("La oferta de cupo ha vencido.");
        }

        var previousStatus = Status;
        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "Oferta de cupo aceptada"
            : reason.Trim();

        ApplyConfirmedAccessRight(
            accessType,
            membershipId,
            creditMovementId,
            singleClassTicketMovementId,
            paymentId,
            promotionRedemptionId);

        Status = ReservationStatus.Confirmed;
        ConfirmedAtUtc = nowUtc;
        WaitlistOfferExpiresAtUtc = null;
        UpdatedAtUtc = nowUtc;

        RecordStatusChange(previousStatus, ReservationStatus.Confirmed, normalizedReason, changedByUserId, trigger, nowUtc);
        AddDomainEvent(new ReservationConfirmedEvent(Id, ClassSessionId, StudentId, AccessType, nowUtc));
    }

    public void Cancel(
        string reason,
        bool isLateCancellation,
        DateTime? nowUtc = null,
        Guid? refundMovementId = null,
        Guid? cancelledByUserId = null,
        ReservationCancellationSource source = ReservationCancellationSource.Student,
        ReservationChangeTrigger trigger = ReservationChangeTrigger.StudentAction,
        bool isPolicyOverride = false,
        string? policyOverrideReason = null,
        bool affectedBySessionChange = false,
        Guid? rescheduledToSessionId = null)
    {
        if (Status is ReservationStatus.Cancelled or ReservationStatus.Attended or ReservationStatus.NoShow)
        {
            throw new InvalidOperationException($"No se puede cancelar una reserva en estado terminal {Status}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("El motivo de cancelación es obligatorio.", nameof(reason));
        }

        if (isPolicyOverride && string.IsNullOrWhiteSpace(policyOverrideReason))
        {
            throw new ArgumentException("El motivo de excepción administrativa es obligatorio.", nameof(policyOverrideReason));
        }

        var effectiveNowUtc = nowUtc ?? DateTime.UtcNow;
        var previousStatus = Status;
        var normalizedReason = reason.Trim();

        Status = ReservationStatus.Cancelled;
        CancellationReason = normalizedReason;
        CancellationSource = source;
        IsLateCancellation = isLateCancellation;
        RefundCreditMovementId = refundMovementId;
        CancelledByUserId = cancelledByUserId;
        CancelledAtUtc = effectiveNowUtc;
        IsPolicyOverride = isPolicyOverride;
        PolicyOverrideReason = NormalizeOptional(policyOverrideReason);
        AffectedBySessionChange = affectedBySessionChange;
        RescheduledToSessionId = rescheduledToSessionId;
        WaitListPosition = null;
        WaitlistOfferExpiresAtUtc = null;
        UpdatedAtUtc = effectiveNowUtc;

        RecordStatusChange(previousStatus, ReservationStatus.Cancelled, normalizedReason, cancelledByUserId, trigger, effectiveNowUtc);

        if (previousStatus == ReservationStatus.Pending && source == ReservationCancellationSource.WaitlistOfferExpired)
        {
            AddDomainEvent(new WaitlistOfferExpiredEvent(Id, ClassSessionId, StudentId, effectiveNowUtc));
        }
        else
        {
            AddDomainEvent(new ReservationCancelledEvent(
                Id,
                ClassSessionId,
                StudentId,
                previousStatus,
                source,
                normalizedReason,
                isLateCancellation,
                effectiveNowUtc));
        }
    }

    public void MarkAttended(
        AttendanceSource source,
        Guid? checkedInByUserId,
        DateTime? nowUtc = null,
        string reason = "Asistencia registrada")
    {
        if (Status != ReservationStatus.Confirmed)
        {
            throw new InvalidOperationException($"Sólo se puede registrar asistencia sobre una reserva confirmada (estado actual: {Status}).");
        }

        var effectiveNowUtc = nowUtc ?? DateTime.UtcNow;
        var previousStatus = Status;
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Asistencia registrada" : reason.Trim();

        Status = ReservationStatus.Attended;
        AttendanceSource = source;
        CheckedInByUserId = checkedInByUserId;
        AttendedAtUtc = effectiveNowUtc;
        UpdatedAtUtc = effectiveNowUtc;

        RecordStatusChange(
            previousStatus,
            ReservationStatus.Attended,
            normalizedReason,
            checkedInByUserId,
            ReservationChangeTrigger.Attendance,
            effectiveNowUtc);
    }

    public void MarkNoShow(
        DateTime? nowUtc = null,
        Guid? changedByUserId = null,
        string reason = "Ausencia (No-Show) registrada")
    {
        if (Status != ReservationStatus.Confirmed)
        {
            throw new InvalidOperationException($"Sólo se puede registrar ausencia sobre una reserva confirmada (estado actual: {Status}).");
        }

        var effectiveNowUtc = nowUtc ?? DateTime.UtcNow;
        var previousStatus = Status;
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Ausencia (No-Show) registrada" : reason.Trim();

        Status = ReservationStatus.NoShow;
        UpdatedAtUtc = effectiveNowUtc;

        RecordStatusChange(
            previousStatus,
            ReservationStatus.NoShow,
            normalizedReason,
            changedByUserId,
            ReservationChangeTrigger.Attendance,
            effectiveNowUtc);
    }

    public void RevokeAttendance(
        AttendanceRevocationReason reason,
        string notes,
        Guid? byUserId,
        DateTime nowUtc)
    {
        if (Status != ReservationStatus.Attended)
        {
            throw new InvalidOperationException($"Sólo se puede anular la asistencia sobre una reserva en estado Asistida (estado actual: {Status}).");
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new ArgumentException("El motivo u observación de la anulación es obligatorio.", nameof(notes));
        }

        var normalizedNotes = notes.Trim();
        if (normalizedNotes.Length > 500)
        {
            throw new ArgumentException("La observación no puede superar los 500 caracteres.", nameof(notes));
        }

        AttendanceRevokedAtUtc = nowUtc;
        AttendanceRevokedByUserId = byUserId;
        AttendanceRevocationReason = reason;
        AttendanceRevocationNotes = normalizedNotes;
        UpdatedAtUtc = nowUtc;

        var previousStatus = Status;

        if (!IsDropIn && reason == SmartGym.Domain.Enums.AttendanceRevocationReason.StaffError)
        {
            // Reserva regular marcada por error de personal -> revierte a Confirmed
            Status = ReservationStatus.Confirmed;
            AttendedAtUtc = null;
            CheckedInByUserId = null;
            AttendanceSource = null;

            RecordStatusChange(
                previousStatus: previousStatus,
                newStatus: ReservationStatus.Confirmed,
                reason: $"Asistencia anulada por error de personal: {normalizedNotes}",
                changedByUserId: byUserId,
                trigger: ReservationChangeTrigger.Attendance,
                changedAtUtc: nowUtc);
        }
        else
        {
            // Drop-in o Alumno se retiró -> pasa a Cancelled con CancellationSource = AttendanceRevoked
            Status = ReservationStatus.Cancelled;
            CancelledAtUtc = nowUtc;
            CancelledByUserId = byUserId;
            CancellationSource = ReservationCancellationSource.AttendanceRevoked;
            CancellationReason = normalizedNotes;
            IsLateCancellation = false;

            RecordStatusChange(
                previousStatus: previousStatus,
                newStatus: ReservationStatus.Cancelled,
                reason: $"Asistencia anulada ({reason}): {normalizedNotes}",
                changedByUserId: byUserId,
                trigger: ReservationChangeTrigger.Attendance,
                changedAtUtc: nowUtc);

            AddDomainEvent(new ReservationCancelledEvent(
                Id,
                ClassSessionId,
                StudentId,
                previousStatus,
                ReservationCancellationSource.AttendanceRevoked,
                normalizedNotes,
                false,
                nowUtc));
        }
    }

    private void ApplyConfirmedAccessRight(
        ReservationAccessType? accessType,
        Guid? membershipId,
        Guid? creditMovementId,
        Guid? singleClassTicketMovementId,
        Guid? paymentId,
        Guid? promotionRedemptionId)
    {
        if (accessType.HasValue)
        {
            AccessType = accessType.Value;
        }

        if (membershipId.HasValue)
        {
            MembershipId = membershipId.Value;
        }

        if (creditMovementId.HasValue)
        {
            CreditMovementId = creditMovementId.Value;
        }

        if (singleClassTicketMovementId.HasValue)
        {
            SingleClassTicketMovementId = singleClassTicketMovementId.Value;
        }

        if (paymentId.HasValue)
        {
            PaymentId = paymentId.Value;
        }

        if (promotionRedemptionId.HasValue)
        {
            PromotionRedemptionId = promotionRedemptionId.Value;
        }
    }

    private void RecordStatusChange(
        ReservationStatus? previousStatus,
        ReservationStatus newStatus,
        string reason,
        Guid? changedByUserId,
        ReservationChangeTrigger trigger,
        DateTime changedAtUtc)
    {
        StatusChanges.Add(new ReservationStatusChange
        {
            Id = Guid.NewGuid(),
            ReservationId = Id,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Reason = reason,
            ChangedByUserId = changedByUserId,
            Trigger = trigger,
            ChangedAtUtc = changedAtUtc
        });
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
