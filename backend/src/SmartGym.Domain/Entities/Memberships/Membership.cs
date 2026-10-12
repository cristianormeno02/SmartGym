using SmartGym.Domain.Common;
using SmartGym.Domain.Entities.Identity;
using SmartGym.Domain.Entities.Promotions;
using SmartGym.Domain.Enums;
using SmartGym.Domain.Events.Memberships;

namespace SmartGym.Domain.Entities.Memberships;

public class Membership : BaseEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public Guid StudentId { get; set; }
    public Person Student { get; set; } = null!;

    public Guid MembershipPlanId { get; set; }
    public MembershipPlan MembershipPlan { get; set; } = null!;

    public Guid? RenewedFromMembershipId { get; set; }
    public Membership? RenewedFromMembership { get; set; }

    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public DateTime PurchasedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ActivatedAtUtc { get; set; }

    public int DurationDaysSnapshot { get; set; } = 30;
    public int InitialCredits { get; set; }
    public int ExtraCreditsGranted { get; set; }
    public int TotalCredits { get; set; }
    public int AvailableCredits { get; set; }

    public bool IsUnlimited { get; set; }
    public bool AppliesToAllActivities { get; set; } = true;
    public bool InitialCreditsLoaded { get; set; }

    public decimal OriginalPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PurchasePrice { get; set; }
    public string Currency { get; set; } = "ARS";

    public Guid? PromotionId { get; set; }
    public Promotion? Promotion { get; set; }

    public Guid? PromotionRedemptionId { get; set; }
    public PromotionRedemption? PromotionRedemption { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public User? CancelledByUser { get; set; }
    public string? CancellationReason { get; set; }

    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }

    public DateTime? ExpiringNotificationSentAtUtc { get; set; }
    public DateTime? ExpiredNotificationSentAtUtc { get; set; }

    public uint Version { get; set; }

    public ICollection<MembershipAllowedActivity> AllowedActivities { get; set; } = new List<MembershipAllowedActivity>();
    public ICollection<MembershipStatusChange> StatusChanges { get; set; } = new List<MembershipStatusChange>();
    public ICollection<MembershipSuspension> Suspensions { get; set; } = new List<MembershipSuspension>();
    public ICollection<MembershipCreditMovement> CreditMovements { get; set; } = new List<MembershipCreditMovement>();

    public static Membership Create(
        Guid studentId,
        MembershipPlan plan,
        DateOnly startDate,
        DateTime purchasedAtUtc,
        decimal originalPrice,
        decimal discountAmount,
        decimal purchasePrice,
        string currency = "ARS",
        int extraCreditsGranted = 0,
        Guid? promotionId = null,
        Guid? promotionRedemptionId = null,
        Guid? renewedFromMembershipId = null,
        Guid? createdByUserId = null,
        string? notes = null,
        string? idempotencyKey = null,
        string? initialStatusReason = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (studentId == Guid.Empty)
        {
            throw new ArgumentException("El alumno es obligatorio.", nameof(studentId));
        }

        if (plan.DurationDays < 1)
        {
            throw new ArgumentException("La duración del plan debe ser de al menos 1 día.", nameof(plan));
        }

        if (originalPrice < 0 || discountAmount < 0 || purchasePrice < 0)
        {
            throw new ArgumentException("Los importes comerciales no pueden ser negativos.");
        }

        if (extraCreditsGranted < 0)
        {
            throw new ArgumentException("Los créditos adicionales no pueden ser negativos.", nameof(extraCreditsGranted));
        }

        var normalizedCurrency = string.IsNullOrWhiteSpace(currency)
            ? "ARS"
            : currency.Trim().ToUpperInvariant();

        if (normalizedCurrency.Length != 3)
        {
            throw new ArgumentException("La moneda debe ser un código ISO 4217 de 3 letras.", nameof(currency));
        }

        var durationDays = plan.DurationDays;
        var endDate = startDate.AddDays(durationDays - 1);
        var isUnlimited = plan.IsUnlimited;
        var initialCredits = isUnlimited ? 0 : Math.Max(0, plan.Credits);
        var effectiveExtraCredits = isUnlimited ? 0 : extraCreditsGranted;

        var membershipId = Guid.NewGuid();
        var membership = new Membership
        {
            Id = membershipId,
            StudentId = studentId,
            MembershipPlanId = plan.Id,
            MembershipPlan = plan,
            RenewedFromMembershipId = renewedFromMembershipId,
            Status = MembershipStatus.Pending,
            StartDate = startDate,
            EndDate = endDate,
            PurchasedAtUtc = purchasedAtUtc,
            DurationDaysSnapshot = durationDays,
            InitialCredits = initialCredits,
            ExtraCreditsGranted = effectiveExtraCredits,
            TotalCredits = 0,
            AvailableCredits = 0,
            IsUnlimited = isUnlimited,
            AppliesToAllActivities = plan.AppliesToAllActivities,
            InitialCreditsLoaded = false,
            OriginalPrice = decimal.Round(originalPrice, 2),
            DiscountAmount = decimal.Round(discountAmount, 2),
            PurchasePrice = decimal.Round(purchasePrice, 2),
            Currency = normalizedCurrency,
            PromotionId = promotionId,
            PromotionRedemptionId = promotionRedemptionId,
            CreatedByUserId = createdByUserId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
            CreatedAtUtc = purchasedAtUtc,
            IsActive = true
        };

        if (!plan.AppliesToAllActivities && plan.AllowedActivities != null)
        {
            foreach (var activityId in plan.AllowedActivities.Select(a => a.ActivityId).Distinct())
            {
                membership.AllowedActivities.Add(new MembershipAllowedActivity
                {
                    MembershipId = membershipId,
                    ActivityId = activityId
                });
            }
        }

        membership.StatusChanges.Add(new MembershipStatusChange
        {
            Id = Guid.NewGuid(),
            MembershipId = membershipId,
            PreviousStatus = null,
            NewStatus = MembershipStatus.Pending,
            Reason = string.IsNullOrWhiteSpace(initialStatusReason)
                ? (renewedFromMembershipId.HasValue ? "Renovación registrada en estado pendiente" : "Contratación registrada en estado pendiente")
                : initialStatusReason.Trim(),
            ChangedByUserId = createdByUserId,
            ChangedAtUtc = purchasedAtUtc
        });

        return membership;
    }

    public void Activate(
        DateOnly today,
        DateTime activatedAtUtc,
        Guid? changedByUserId,
        string reason = "Membresía activada",
        string? notes = null)
    {
        if (Status != MembershipStatus.Pending)
        {
            throw new InvalidOperationException($"No se puede activar una membresía en estado {Status}.");
        }

        if (today > EndDate)
        {
            throw new InvalidOperationException("No se puede activar una membresía cuyo período de vigencia ya finalizó.");
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Membresía activada" : reason.Trim();
        var previousStatus = Status;

        Status = MembershipStatus.Active;
        ActivatedAtUtc = activatedAtUtc;
        UpdatedAtUtc = activatedAtUtc;

        RecordStatusChange(previousStatus, MembershipStatus.Active, normalizedReason, notes, changedByUserId, activatedAtUtc);
        AddDomainEvent(new MembershipActivatedEvent(Id, StudentId, changedByUserId, activatedAtUtc));
    }

    public MembershipSuspension Suspend(
        DateOnly today,
        DateOnly? endDate,
        bool extendExpirationDate,
        string reason,
        string? notes,
        Guid suspendedByUserId,
        DateTime suspendedAtUtc,
        int maxSuspensionDays = 90)
    {
        if (Status != MembershipStatus.Active)
        {
            throw new InvalidOperationException($"Sólo se puede suspender una membresía activa (estado actual: {Status}).");
        }

        if (today < StartDate || today > EndDate)
        {
            throw new InvalidOperationException("La membresía debe encontrarse dentro de su período de vigencia para suspenderse.");
        }

        if (Suspensions.Any(s => s.IsActiveSuspension))
        {
            throw new InvalidOperationException("La membresía ya posee una suspensión abierta.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("El motivo de suspensión es obligatorio.", nameof(reason));
        }

        var normalizedReason = reason.Trim();
        if (normalizedReason.Length > 500)
        {
            throw new ArgumentException("El motivo de suspensión no puede superar los 500 caracteres.", nameof(reason));
        }

        if (extendExpirationDate && !endDate.HasValue)
        {
            throw new ArgumentException("La fecha de fin de suspensión es obligatoria cuando se solicita extender el vencimiento.", nameof(endDate));
        }

        if (endDate.HasValue)
        {
            if (endDate.Value < today)
            {
                throw new ArgumentException("La fecha de fin de suspensión no puede ser anterior a la fecha de inicio.", nameof(endDate));
            }

            var maxAllowedEndDate = today.AddDays(Math.Max(1, maxSuspensionDays) - 1);
            if (endDate.Value > maxAllowedEndDate)
            {
                throw new ArgumentException($"La suspensión no puede superar el máximo permitido de {maxSuspensionDays} días.", nameof(endDate));
            }
        }

        var suspension = new MembershipSuspension
        {
            Id = Guid.NewGuid(),
            MembershipId = Id,
            StartDate = today,
            EndDate = endDate,
            ExtendExpirationDate = extendExpirationDate,
            ExtensionDaysApplied = 0,
            Reason = normalizedReason,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            SuspendedByUserId = suspendedByUserId,
            SuspendedAtUtc = suspendedAtUtc,
            IsActiveSuspension = true
        };

        var previousStatus = Status;
        Status = MembershipStatus.Suspended;
        UpdatedAtUtc = suspendedAtUtc;

        Suspensions.Add(suspension);
        RecordStatusChange(previousStatus, MembershipStatus.Suspended, normalizedReason, notes, suspendedByUserId, suspendedAtUtc);
        AddDomainEvent(new MembershipSuspendedEvent(
            Id,
            StudentId,
            suspension.Id,
            suspension.StartDate,
            suspension.EndDate,
            suspension.ExtendExpirationDate,
            normalizedReason,
            suspendedByUserId,
            suspendedAtUtc));

        return suspension;
    }

    public int Reactivate(
        DateOnly reactivationDate,
        DateTime reactivatedAtUtc,
        Guid? reactivatedByUserId,
        string reason = "Membresía reactivada",
        string? notes = null)
    {
        if (Status != MembershipStatus.Suspended)
        {
            throw new InvalidOperationException($"Sólo se puede reactivar una membresía suspendida (estado actual: {Status}).");
        }

        var activeSuspension = Suspensions.FirstOrDefault(s => s.IsActiveSuspension);
        var extendExpiration = activeSuspension?.ExtendExpirationDate == true;

        if (!extendExpiration && reactivationDate > EndDate)
        {
            throw new InvalidOperationException("No se puede reactivar una membresía cuyo período de vigencia ya finalizó.");
        }

        var extensionDaysApplied = 0;
        if (activeSuspension != null)
        {
            if (extendExpiration)
            {
                extensionDaysApplied = Math.Max(0, reactivationDate.DayNumber - activeSuspension.StartDate.DayNumber);
                if (extensionDaysApplied > 0)
                {
                    EndDate = EndDate.AddDays(extensionDaysApplied);
                }
            }

            activeSuspension.IsActiveSuspension = false;
            activeSuspension.ReactivatedAtDate = reactivationDate;
            activeSuspension.ReactivatedByUserId = reactivatedByUserId;
            activeSuspension.ReactivatedAtUtc = reactivatedAtUtc;
            activeSuspension.ExtensionDaysApplied = extensionDaysApplied;
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Membresía reactivada" : reason.Trim();
        var previousStatus = Status;

        Status = MembershipStatus.Active;
        UpdatedAtUtc = reactivatedAtUtc;

        RecordStatusChange(previousStatus, MembershipStatus.Active, normalizedReason, notes, reactivatedByUserId, reactivatedAtUtc);
        return extensionDaysApplied;
    }

    public void Cancel(
        string reason,
        Guid? cancelledByUserId,
        DateTime cancelledAtUtc,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("El motivo de cancelación es obligatorio.", nameof(reason));
        }

        if (Status == MembershipStatus.Cancelled || Status == MembershipStatus.Expired)
        {
            throw new InvalidOperationException($"No se puede cancelar una membresía en estado terminal ({Status}).");
        }

        var normalizedReason = reason.Trim();
        if (normalizedReason.Length > 500)
        {
            throw new ArgumentException("El motivo de cancelación no puede superar los 500 caracteres.", nameof(reason));
        }

        foreach (var openSuspension in Suspensions.Where(s => s.IsActiveSuspension))
        {
            openSuspension.IsActiveSuspension = false;
            openSuspension.ReactivatedByUserId = cancelledByUserId;
            openSuspension.ReactivatedAtUtc = cancelledAtUtc;
        }

        var previousStatus = Status;

        Status = MembershipStatus.Cancelled;
        CancelledAtUtc = cancelledAtUtc;
        CancelledByUserId = cancelledByUserId;
        CancellationReason = normalizedReason;
        UpdatedAtUtc = cancelledAtUtc;

        RecordStatusChange(previousStatus, MembershipStatus.Cancelled, normalizedReason, notes, cancelledByUserId, cancelledAtUtc);
        AddDomainEvent(new MembershipCancelledEvent(Id, StudentId, previousStatus, normalizedReason, cancelledByUserId, cancelledAtUtc));
    }

    public void Expire(
        DateOnly today,
        DateTime expiredAtUtc,
        string reason = "Vencimiento automático por finalización de vigencia")
    {
        if (Status != MembershipStatus.Active && Status != MembershipStatus.Suspended)
        {
            throw new InvalidOperationException($"No se puede vencer una membresía en estado {Status}.");
        }

        var activeSuspension = Suspensions.FirstOrDefault(s => s.IsActiveSuspension);
        if (Status == MembershipStatus.Suspended && activeSuspension?.ExtendExpirationDate == true)
        {
            throw new InvalidOperationException("Una membresía suspendida con extensión de vencimiento pactada no vence mientras dure la suspensión.");
        }

        if (today <= EndDate)
        {
            throw new InvalidOperationException("La membresía aún se encuentra dentro de su período de vigencia.");
        }

        if (activeSuspension != null)
        {
            activeSuspension.IsActiveSuspension = false;
            activeSuspension.ReactivatedAtUtc = expiredAtUtc;
        }

        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? "Vencimiento automático por finalización de vigencia"
            : reason.Trim();

        var previousStatus = Status;
        Status = MembershipStatus.Expired;
        UpdatedAtUtc = expiredAtUtc;

        RecordStatusChange(previousStatus, MembershipStatus.Expired, normalizedReason, null, null, expiredAtUtc);
        AddDomainEvent(new MembershipExpiredEvent(Id, StudentId, expiredAtUtc));
    }

    protected void RecordStatusChange(
        MembershipStatus? previousStatus,
        MembershipStatus newStatus,
        string reason,
        string? notes,
        Guid? changedByUserId,
        DateTime changedAtUtc)
    {
        StatusChanges.Add(new MembershipStatusChange
        {
            Id = Guid.NewGuid(),
            MembershipId = Id,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Reason = reason,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            ChangedByUserId = changedByUserId,
            ChangedAtUtc = changedAtUtc
        });
    }

    public bool IsDepleted => !IsUnlimited && InitialCreditsLoaded && AvailableCredits <= 0;

    public MembershipStatus GetEffectiveStatus(DateOnly today)
    {
        if (Status == MembershipStatus.Active && today > EndDate)
        {
            return MembershipStatus.Expired;
        }

        if (Status == MembershipStatus.Suspended && today > EndDate)
        {
            var activeSuspension = Suspensions?.FirstOrDefault(s => s.IsActiveSuspension);
            if (activeSuspension?.ExtendExpirationDate != true)
            {
                return MembershipStatus.Expired;
            }
        }

        return Status;
    }

    public bool HasUsableOrPendingCredits()
    {
        if (IsUnlimited)
        {
            return true;
        }

        if (InitialCreditsLoaded)
        {
            return AvailableCredits > 0;
        }

        return (InitialCredits + ExtraCreditsGranted) > 0;
    }

    public bool IsValidOn(DateOnly date)
    {
        return Status == MembershipStatus.Active && date >= StartDate && date <= EndDate;
    }

    public bool AllowsActivity(Guid activityId)
    {
        if (AppliesToAllActivities)
        {
            return true;
        }

        // Sólo cuenta el snapshot tomado al contratar; el plan puede haber cambiado después.
        return AllowedActivities != null && AllowedActivities.Any(a => a.ActivityId == activityId);
    }

    public bool HasAvailableCredit()
    {
        if (IsUnlimited)
        {
            return true;
        }

        return AvailableCredits > 0;
    }
}

